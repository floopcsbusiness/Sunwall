using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Sunwall
{
    // The whole picture for one monitor, built the way the CoreGraphics version builds
    // it: vector fills for the room, a blurred light patch masked out of the shadow.
    public static class WallRenderer
    {
        public static void Render(Bitmap bmp, Model m)
        {
            int W = bmp.Width, H = bmp.Height;
            double w = W, h = H;
            RectangleF full = new RectangleF(0, 0, W, H);

            using (Graphics clear = Graphics.FromImage(bmp)) clear.Clear(Color.Transparent);

            DateTime date = m.Date;
            SunPos sun = Sky.Position(date, m.Lat, m.Lon);
            double el = sun.Elevation;
            double cloud = m.Cloud;

            DateTime? rise, set;
            Sky.TimesCached(date, m.Lat, m.Lon, out rise, out set);

            // Progress through the day, 0 at sunrise, 1 at sunset. Drives the patch left to right.
            double p;
            if (rise.HasValue && set.HasValue && set.Value > rise.Value)
            {
                p = Sky.Clamp((date - rise.Value).TotalSeconds / (set.Value - rise.Value).TotalSeconds, 0, 1);
            }
            else
            {
                p = Sky.Clamp((sun.Azimuth - 90) / 180.0, 0, 1);
            }

            double day = Sky.SmoothStep(-8, 6, el);
            double elevN = Sky.Clamp(el / 60.0, 0, 1);
            double warm = 1 - Sky.SmoothStep(0, 25, el);
            bool replace = m.ReplaceMode;

            double direct = Sky.SmoothStep(-0.5, 8, el) * (1 - 0.88 * cloud);
            double shadowA = replace ? 0 : m.ShadowDepth * day * (1 - 0.8 * cloud);
            bool shaded = shadowA > 0.01;

            RGB lowC = new RGB(1.0, 0.55, 0.28);
            RGB midC = new RGB(1.0, 0.82, 0.60);
            RGB highC = new RGB(1.0, 0.95, 0.86);
            RGB lightC = lowC.Mixed(midC, Sky.SmoothStep(0, 12, el))
                             .Mixed(highC, Sky.SmoothStep(12, 45, el))
                             .Mixed(new RGB(1, 1, 1), Sky.Clamp((m.Brightness - 1) * 0.5, 0, 1));

            double relAz = Sky.Rad((p - 0.5) * 2 * 68);
            double xc = w * (0.5 + Math.Sin(relAz) * 0.38);
            double yc = h * (1 - (0.30 + 0.32 * elevN));
            double patchW = w * 0.46;
            double patchH = h * 0.60;
            double stretch = 1 + 0.9 * (1 - elevN);
            double shear = Math.Tan(relAz) * 0.45;

            using (Graphics g = Canvas.Open(bmp))
            {
                // Full wallpaper mode: paint our own wall over the whole screen.
                if (replace)
                {
                    RGB nightWall = new RGB(0.035, 0.045, 0.085);
                    RGB dayWall = new RGB(0.58, 0.54, 0.49);
                    RGB ambientDay = dayWall.Mixed(new RGB(0.60, 0.42, 0.36), warm * 0.55);
                    RGB wall = nightWall.Mixed(ambientDay, day);
                    wall = wall.Mixed(new RGB(0.45, 0.45, 0.47), cloud * 0.35 * day);
                    wall = wall.Scaled(1 - 0.10 * cloud * day);
                    using (LinearGradientBrush b = new LinearGradientBrush(
                               new PointF(0, (float)h), new PointF(0, 0),
                               wall.Scaled(0.86).ToColor(1), wall.Scaled(1.08).ToColor(1)))
                    {
                        b.WrapMode = WrapMode.TileFlipXY;
                        g.FillRectangle(b, full);
                    }
                }

                // Night and dusk tint
                double night = 1 - day;
                if (!replace && night > 0.01)
                {
                    Fill(g, full, new RGB(0.03, 0.05, 0.13), 0.62 * night);
                }

                // Golden hour tint
                double golden = warm * day * (1 - cloud * 0.7);
                if (!replace && golden > 0.01)
                {
                    Fill(g, full, new RGB(1.0, 0.55, 0.25), 0.10 * golden);
                }

                // Room shadow: dims the wallpaper everywhere, then the sunlight patch
                // is cut out of it by the compositing step below.
                if (shaded)
                {
                    Fill(g, full, new RGB(0.05, 0.04, 0.08), shadowA);
                }

                // Warm glow around the patch
                if (direct > 0.01)
                {
                    double ga = Sky.Clamp01((replace ? 0.22 : 0.14) * direct * m.Brightness);
                    DrawRadialGlow(g, xc, yc, w * 0.55, lightC.ToColor(ga), lightC.ToColor(0));
                }
            }

            if (direct > 0.01)
            {
                double cutK = Sky.Clamp01(direct * m.Brightness);
                double overK = Sky.Clamp(direct * (replace ? 0.62 : (shaded ? 0.22 : 0.34)) * m.Brightness, 0, 0.95);
                using (Bitmap small = BuildLight(w, h, m, elevN, cloud, lightC,
                                                 xc, yc, patchW, patchH, stretch, shear))
                {
                    Canvas.CompositeLight(bmp, small, shaded, cutK, overK);
                }
            }

            // Vignette (full wallpaper mode only)
            if (replace)
            {
                using (Graphics g = Canvas.Open(bmp))
                {
                    float rad = (float)(Math.Sqrt(w * w + h * h) * 0.6);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddEllipse((float)(w / 2) - rad, (float)(h / 2) - rad, rad * 2, rad * 2);
                        using (PathGradientBrush pb = new PathGradientBrush(path))
                        {
                            pb.CenterPoint = new PointF((float)(w / 2), (float)(h / 2));
                            ColorBlend cb = new ColorBlend(3);
                            cb.Colors = new Color[]
                            {
                                Color.FromArgb(0, 0, 0, 0),
                                Color.FromArgb(0, 0, 0, 0),
                                Color.FromArgb(76, 0, 0, 0)
                            };
                            cb.Positions = new float[] { 0f, 0.55f, 1f };
                            pb.InterpolationColors = cb;
                            g.FillPath(pb, path);
                        }
                    }
                }
            }
        }

        static void Fill(Graphics g, RectangleF full, RGB c, double alpha)
        {
            if (alpha <= 0) return;
            using (SolidBrush b = new SolidBrush(c.ToColor(alpha))) g.FillRectangle(b, full);
        }

        static void DrawRadialGlow(Graphics g, double cx, double cy, double radius, Color inner, Color outer)
        {
            if (radius <= 0) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse((float)(cx - radius), (float)(cy - radius),
                                (float)(radius * 2), (float)(radius * 2));
                using (PathGradientBrush pb = new PathGradientBrush(path))
                {
                    pb.CenterPoint = new PointF((float)cx, (float)cy);
                    ColorBlend cb = new ColorBlend(2);
                    cb.Colors = new Color[] { inner, outer };
                    cb.Positions = new float[] { 0f, 1f };
                    pb.InterpolationColors = cb;
                    g.FillPath(pb, path);
                }
            }
        }

        // The light shape, at half resolution so the blur is cheap, then blurred.
        static Bitmap BuildLight(double w, double h, Model m, double elevN, double cloud, RGB lightC,
                                 double xc, double yc, double patchW, double patchH,
                                 double stretch, double shear)
        {
            double ps = 0.5;
            int sw = Math.Max(16, (int)(w * ps));
            int sh = Math.Max(16, (int)(h * ps));
            Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);

            using (Graphics g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.TranslateTransform(0, sh);
                g.ScaleTransform(1, -1);
                g.ScaleTransform((float)ps, (float)ps);
                g.TranslateTransform((float)xc, (float)yc);
                g.RotateTransform(-5f);
                using (Matrix shearM = new Matrix(1f, 0f, (float)shear, (float)stretch, 0f, 0f))
                {
                    g.MultiplyTransform(shearM);
                }
                Presets.Draw(g, m.Preset, patchW, patchH, elevN, lightC);
            }

            int[] px = Canvas.ReadPixels(small);
            double sigma = (3 + 14 * cloud + 5 * (1 - elevN)) * ps;
            Canvas.Blur(px, sw, sh, sigma);
            Canvas.WritePixels(small, px);
            return small;
        }
    }
}
