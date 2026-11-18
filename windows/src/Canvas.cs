using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Sunwall
{
    // Bitmap helpers. GDI+ draws the vector work; the blur and the light compositing
    // are done on the pixels, which is what the CoreGraphics version leans on.
    public static class Canvas
    {
        // Every bitmap is drawn in "math" coordinates: origin bottom left, y up, the
        // same convention a CoreGraphics context starts with. One flip transform at the
        // start of each Graphics makes the ported drawing code line up 1:1.
        public static Graphics Open(Bitmap bmp)
        {
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TranslateTransform(0, bmp.Height);
            g.ScaleTransform(1, -1);
            return g;
        }

        public static int[] ReadPixels(Bitmap bmp)
        {
            int[] px = new int[bmp.Width * bmp.Height];
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(d.Scan0, px, 0, px.Length); }
            finally { bmp.UnlockBits(d); }
            return px;
        }

        public static void WritePixels(Bitmap bmp, int[] px)
        {
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(px, 0, d.Scan0, px.Length); }
            finally { bmp.UnlockBits(d); }
        }

        public static int Pack(int a, int r, int g, int b)
        {
            if (a < 0) a = 0; if (a > 255) a = 255;
            if (r < 0) r = 0; if (r > 255) r = 255;
            if (g < 0) g = 0; if (g > 255) g = 255;
            if (b < 0) b = 0; if (b > 255) b = 255;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        // Three box passes are close enough to a Gaussian, which GDI+ has no direct
        // equivalent of. Everything is premultiplied, so averaging is correct.
        public static void Blur(int[] px, int w, int h, double sigma)
        {
            int r = (int)Math.Round(sigma * 0.9);
            if (r < 1 || w < 3 || h < 3) return;
            int[] tmp = new int[px.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(px, tmp, w, h, r);
                BoxV(tmp, px, w, h, r);
            }
        }

        static int Edge(int i, int n)
        {
            if (i < 0) return 0;
            if (i >= n) return n - 1;
            return i;
        }

        static void BoxH(int[] src, int[] dst, int w, int h, int r)
        {
            int win = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                long sa = 0, sr = 0, sg = 0, sb = 0;
                for (int i = -r; i <= r; i++)
                {
                    int v = src[row + Edge(i, w)];
                    sa += (v >> 24) & 255; sr += (v >> 16) & 255; sg += (v >> 8) & 255; sb += v & 255;
                }
                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = Pack((int)(sa / win), (int)(sr / win), (int)(sg / win), (int)(sb / win));
                    int outv = src[row + Edge(x - r, w)];
                    int inv = src[row + Edge(x + r + 1, w)];
                    sa += ((inv >> 24) & 255) - ((outv >> 24) & 255);
                    sr += ((inv >> 16) & 255) - ((outv >> 16) & 255);
                    sg += ((inv >> 8) & 255) - ((outv >> 8) & 255);
                    sb += (inv & 255) - (outv & 255);
                }
            }
        }

        static void BoxV(int[] src, int[] dst, int w, int h, int r)
        {
            int win = 2 * r + 1;
            for (int x = 0; x < w; x++)
            {
                long sa = 0, sr = 0, sg = 0, sb = 0;
                for (int i = -r; i <= r; i++)
                {
                    int v = src[Edge(i, h) * w + x];
                    sa += (v >> 24) & 255; sr += (v >> 16) & 255; sg += (v >> 8) & 255; sb += v & 255;
                }
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = Pack((int)(sa / win), (int)(sr / win), (int)(sg / win), (int)(sb / win));
                    int outv = src[Edge(y - r, h) * w + x];
                    int inv = src[Edge(y + r + 1, h) * w + x];
                    sa += ((inv >> 24) & 255) - ((outv >> 24) & 255);
                    sr += ((inv >> 16) & 255) - ((outv >> 16) & 255);
                    sg += ((inv >> 8) & 255) - ((outv >> 8) & 255);
                    sb += (inv & 255) - (outv & 255);
                }
            }
        }

        static int Sample(int[] p, int w, int h, double x, double y)
        {
            if (x < 0) x = 0; if (y < 0) y = 0;
            if (x > w - 1) x = w - 1;
            if (y > h - 1) y = h - 1;
            int x0 = (int)x, y0 = (int)y;
            int x1 = x0 + 1 < w ? x0 + 1 : x0;
            int y1 = y0 + 1 < h ? y0 + 1 : y0;
            double fx = x - x0, fy = y - y0;
            int p00 = p[y0 * w + x0], p10 = p[y0 * w + x1];
            int p01 = p[y1 * w + x0], p11 = p[y1 * w + x1];

            int a = Bilinear(p00 >> 24, p10 >> 24, p01 >> 24, p11 >> 24, fx, fy);
            int r = Bilinear(p00 >> 16, p10 >> 16, p01 >> 16, p11 >> 16, fx, fy);
            int g = Bilinear(p00 >> 8, p10 >> 8, p01 >> 8, p11 >> 8, fx, fy);
            int b = Bilinear(p00, p10, p01, p11, fx, fy);
            return Pack(a, r, g, b);
        }

        static int Bilinear(int c00, int c10, int c01, int c11, double fx, double fy)
        {
            double a = (c00 & 255) * (1 - fx) + (c10 & 255) * fx;
            double b = (c01 & 255) * (1 - fx) + (c11 & 255) * fx;
            return (int)(a * (1 - fy) + b * fy + 0.5);
        }

        // Draws the blurred light into the wallpaper layer: first cutting it out of the
        // shadow (destination-out), then laying the warm light over the top.
        public static void CompositeLight(Bitmap dst, Bitmap small, bool cut, double kCut, double kOver)
        {
            int dw = dst.Width, dh = dst.Height;
            int sw = small.Width, sh = small.Height;
            int[] dp = ReadPixels(dst);
            int[] sp = ReadPixels(small);

            for (int y = 0; y < dh; y++)
            {
                double sy = (y + 0.5) * sh / dh - 0.5;
                int row = y * dw;
                for (int x = 0; x < dw; x++)
                {
                    double sx = (x + 0.5) * sw / dw - 0.5;
                    int lp = Sample(sp, sw, sh, sx, sy);
                    int la = (lp >> 24) & 255;
                    if (la == 0) continue;

                    int di = row + x;
                    int d = dp[di];
                    int da = (d >> 24) & 255, dr = (d >> 16) & 255, dg = (d >> 8) & 255, db = d & 255;

                    if (cut && kCut > 0)
                    {
                        double f = 1.0 - (la / 255.0) * kCut;
                        if (f < 0) f = 0;
                        da = (int)(da * f); dr = (int)(dr * f); dg = (int)(dg * f); db = (int)(db * f);
                    }

                    if (kOver > 0)
                    {
                        int sa = (int)(la * kOver + 0.5);
                        int sr = (int)(((lp >> 16) & 255) * kOver + 0.5);
                        int sg = (int)(((lp >> 8) & 255) * kOver + 0.5);
                        int sb = (int)((lp & 255) * kOver + 0.5);
                        int inv = 255 - sa;
                        da = sa + da * inv / 255;
                        dr = sr + dr * inv / 255;
                        dg = sg + dg * inv / 255;
                        db = sb + db * inv / 255;
                    }

                    dp[di] = Pack(da, dr, dg, db);
                }
            }
            WritePixels(dst, dp);
        }
    }
}
