using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sunwall
{
    public struct RGB
    {
        public double R, G, B;

        public RGB(double r, double g, double b) { R = r; G = g; B = b; }

        public RGB Mixed(RGB o, double t)
        {
            return new RGB(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
        }

        public RGB Scaled(double k) { return new RGB(R * k, G * k, B * k); }

        public Color ToColor(double alpha)
        {
            return Color.FromArgb(Channel(alpha), Channel(R), Channel(G), Channel(B));
        }

        static int Channel(double v)
        {
            double x = v * 255.0 + 0.5;
            if (x < 0) return 0;
            if (x > 255) return 255;
            return (int)x;
        }
    }

    // The same 64-bit generator and the same 53-bit reduction the Swift build uses,
    // so the random speckle in a preset lands in exactly the same places.
    public sealed class Rng
    {
        ulong state;

        public Rng(ulong seed) { state = seed; }

        public ulong Next()
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            return state;
        }

        public double NextDouble() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }

        public double Range(double a, double b) { return a + (b - a) * NextDouble(); }
    }

    public static class Presets
    {
        static int Channel(double v)
        {
            double x = v * 255.0 + 0.5;
            if (x < 0) return 0;
            if (x > 255) return 255;
            return (int)x;
        }

        public static Color HueColor(double h, double s, double b, double a)
        {
            double hh = (h - Math.Floor(h)) * 6.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double p = b * (1 - s), q = b * (1 - s * f), t = b * (1 - s * (1 - f));
            double r, g, bl;
            switch (i % 6)
            {
                case 1: r = q; g = b; bl = p; break;
                case 2: r = p; g = b; bl = t; break;
                case 3: r = p; g = q; bl = b; break;
                case 4: r = t; g = p; bl = b; break;
                case 5: r = b; g = p; bl = q; break;
                default: r = b; g = t; bl = p; break;
            }
            return Color.FromArgb(Channel(a), Channel(r), Channel(g), Channel(bl));
        }

        public static void FillGrid(Graphics g, double bw, double bh, int cols, int rows, double gap, Color c)
        {
            double pw = (bw - gap * (cols - 1)) / cols;
            double ph = (bh - gap * (rows - 1)) / rows;
            using (SolidBrush b = new SolidBrush(c))
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int q = 0; q < cols; q++)
                    {
                        g.FillRectangle(b,
                            (float)(-bw / 2 + q * (pw + gap)),
                            (float)(-bh / 2 + r * (ph + gap)),
                            (float)pw, (float)ph);
                    }
                }
            }
        }

        static void AddArcPoints(GraphicsPath path, double cx, double cy, double r, double a0, double a1, int steps)
        {
            PointF[] pts = new PointF[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double a = a0 + (a1 - a0) * i / steps;
                pts[i] = new PointF((float)(cx + Math.Cos(a) * r), (float)(cy + Math.Sin(a) * r));
            }
            path.AddLines(pts);
        }

        public static void ClearLeaf(Graphics g, double x, double y, double length, double width, double angleRad)
        {
            GraphicsState st = g.Save();
            CompositingMode cm = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.TranslateTransform((float)x, (float)y);
            g.RotateTransform((float)(angleRad * 180.0 / Math.PI));
            using (SolidBrush b = new SolidBrush(Color.Transparent))
            {
                g.FillEllipse(b, (float)(-length / 2), (float)(-width / 2), (float)length, (float)width);
            }
            g.CompositingMode = cm;
            g.Restore(st);
        }

        public static void ClearBranch(Graphics g,
                                       double x0, double y0, double x1, double y1, double x2, double y2,
                                       double leafLen, int count)
        {
            CompositingMode cm = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (Pen p = new Pen(Color.Transparent, (float)(leafLen * 0.07)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                PointF[] pts = new PointF[33];
                for (int i = 0; i < 33; i++)
                {
                    double t = i / 32.0, mt = 1 - t;
                    pts[i] = new PointF(
                        (float)(mt * mt * x0 + 2 * mt * t * x1 + t * t * x2),
                        (float)(mt * mt * y0 + 2 * mt * t * y1 + t * t * y2));
                }
                g.DrawLines(p, pts);
            }
            g.CompositingMode = cm;

            for (int i = 1; i <= count; i++)
            {
                double t = (double)i / (count + 1), mt = 1 - t;
                double x = mt * mt * x0 + 2 * mt * t * x1 + t * t * x2;
                double y = mt * mt * y0 + 2 * mt * t * y1 + t * t * y2;
                double dx = 2 * mt * (x1 - x0) + 2 * t * (x2 - x1);
                double dy = 2 * mt * (y1 - y0) + 2 * t * (y2 - y1);
                double ang = Math.Atan2(dy, dx);
                double side = (i % 2 == 0) ? 0.8 : -0.8;
                ClearLeaf(g,
                          x + Math.Cos(ang + side) * leafLen * 0.5,
                          y + Math.Sin(ang + side) * leafLen * 0.5,
                          leafLen, leafLen * 0.38, ang + side);
            }
        }

        // Draws the light shape. Coordinates are relative to the patch centre, in the
        // bottom-left-origin space Canvas.Open sets up.
        public static void Draw(Graphics g, Preset preset, double bw, double bh, double elevN, RGB light)
        {
            Rng rng = new Rng(42);

            switch (preset)
            {
                case Preset.Olive:
                    FillGrid(g, bw, bh, 2, 2, bw * 0.045, light.ToColor(1));
                    ClearBranch(g, bw * 0.5, -bh * 0.5, bw * 0.05, -bh * 0.05, -bw * 0.15, bh * 0.4, bw * 0.16, 11);
                    break;

                case Preset.Blinds:
                    {
                        int n = 15;
                        double slat = bh / n;
                        double open = 0.30 + 0.40 * elevN;
                        using (SolidBrush b = new SolidBrush(light.ToColor(1)))
                        {
                            for (int i = 0; i < n; i++)
                            {
                                g.FillRectangle(b, (float)(-bw / 2), (float)(-bh / 2 + i * slat),
                                                (float)bw, (float)(slat * open));
                            }
                        }
                    }
                    break;

                case Preset.Prism:
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.StartFigure();
                            path.AddLine((float)(-bw * 0.30), (float)(-bh * 0.50), (float)(bw * 0.42), (float)(-bh * 0.30));
                            path.AddLine((float)(bw * 0.42), (float)(-bh * 0.30), (float)(bw * 0.28), (float)(bh * 0.50));
                            path.AddLine((float)(bw * 0.28), (float)(bh * 0.50), (float)(-bw * 0.44), (float)(bh * 0.30));
                            path.CloseFigure();
                            using (SolidBrush b = new SolidBrush(light.ToColor(1))) g.FillPath(b, path);
                        }

                        GraphicsState st = g.Save();
                        g.TranslateTransform((float)(-bw * 0.52), (float)(-bh * 0.18));
                        g.RotateTransform((float)(0.5 * 180.0 / Math.PI));
                        for (int i = 0; i < 7; i++)
                        {
                            using (SolidBrush b = new SolidBrush(HueColor(i / 7.0 * 0.8, 0.85, 1.0, 0.9)))
                            {
                                g.FillRectangle(b, (float)(i * bw * 0.018), 0f,
                                                (float)(bw * 0.018), (float)(bh * 0.20));
                            }
                        }
                        g.Restore(st);

                        CompositingMode cm = g.CompositingMode;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (Pen p = new Pen(Color.Transparent, (float)(bw * 0.012)))
                        {
                            p.StartCap = LineCap.Round;
                            p.EndCap = LineCap.Round;
                            g.DrawLine(p, 0f, (float)(bh * 0.5), (float)(bw * 0.2), (float)(-bh * 0.1));
                        }
                        g.CompositingMode = cm;
                    }
                    break;

                case Preset.Suncatcher:
                    {
                        FillGrid(g, bw, bh, 2, 2, bw * 0.05, light.ToColor(1));
                        for (int i = 0; i < 16; i++)
                        {
                            double x = rng.Range(bw * 0.45, bw * 0.85);
                            double y = rng.Range(-bh * 0.5, bh * 0.5);
                            double r = rng.Range(0.01, 0.03) * bw;
                            using (SolidBrush b = new SolidBrush(HueColor(rng.Range(0, 1), 0.45, 1.0, 0.8)))
                            {
                                g.FillEllipse(b, (float)(x - r), (float)(y - r), (float)(r * 2), (float)(r * 2));
                            }
                        }
                    }
                    break;

                case Preset.Arch:
                    {
                        double hw = bw * 0.34;
                        double archY = bh * 0.15;
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.StartFigure();
                            path.AddLine((float)(-hw), (float)(-bh * 0.5), (float)hw, (float)(-bh * 0.5));
                            path.AddLine((float)hw, (float)(-bh * 0.5), (float)hw, (float)archY);
                            AddArcPoints(path, 0, archY, hw, 0, Math.PI, 48);
                            path.CloseFigure();
                            using (SolidBrush b = new SolidBrush(light.ToColor(1))) g.FillPath(b, path);
                        }

                        CompositingMode cm = g.CompositingMode;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (SolidBrush cb = new SolidBrush(Color.Transparent))
                        {
                            g.FillRectangle(cb, (float)(-bw * 0.012), (float)(-bh * 0.5),
                                            (float)(bw * 0.024), (float)(bh * 0.5 + archY + hw));
                            g.FillRectangle(cb, (float)(-hw), (float)(-bh * 0.02),
                                            (float)(hw * 2), (float)(bw * 0.024));
                        }
                        g.CompositingMode = cm;
                    }
                    break;

                case Preset.Komorebi:
                    {
                        for (int i = 0; i < 170; i++)
                        {
                            double x = rng.Range(-bw * 0.65, bw * 0.65);
                            double y = rng.Range(-bh * 0.65, bh * 0.65);
                            double a = rng.Range(0.04, 0.13) * bw;
                            double b2 = a * rng.Range(0.55, 1.0);
                            double rot = rng.Range(0, Math.PI);
                            double alpha = rng.Range(0.55, 1.0);
                            GraphicsState st = g.Save();
                            g.TranslateTransform((float)x, (float)y);
                            g.RotateTransform((float)(rot * 180.0 / Math.PI));
                            using (SolidBrush br = new SolidBrush(light.ToColor(alpha)))
                            {
                                g.FillEllipse(br, (float)(-a / 2), (float)(-b2 / 2), (float)a, (float)b2);
                            }
                            g.Restore(st);
                        }
                    }
                    break;

                case Preset.Tidal:
                    {
                        int rows = 34;
                        using (Pen pen = new Pen(light.ToColor(0.75), (float)(bh * 0.010)))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;
                            for (int i = 0; i < rows; i++)
                            {
                                double y0 = -bh * 0.7 + ((double)i) * (bh * 1.4) / ((double)rows);
                                double ph = rng.Range(0, 6.28);
                                double amp = bh * rng.Range(0.012, 0.035);
                                double fq = rng.Range(7, 14) / bw;
                                double x = -bw * 0.7;
                                List<PointF> pts = new List<PointF>();
                                pts.Add(new PointF((float)x, (float)(y0 + Math.Sin(x * fq + ph) * amp)));
                                while (x < bw * 0.7)
                                {
                                    x += bw * 0.03;
                                    double y = y0 + Math.Sin(x * fq + ph) * amp
                                                 + Math.Sin(x * fq * 2.3 + ph * 1.7) * amp * 0.5;
                                    pts.Add(new PointF((float)x, (float)y));
                                }
                                g.DrawLines(pen, pts.ToArray());
                            }
                        }
                    }
                    break;

                case Preset.Rainlight:
                    {
                        FillGrid(g, bw, bh, 2, 3, bw * 0.04, light.ToColor(1));
                        CompositingMode cm = g.CompositingMode;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (SolidBrush b = new SolidBrush(Color.Transparent))
                        {
                            for (int i = 0; i < 80; i++)
                            {
                                double x = rng.Range(-bw / 2, bw / 2);
                                double y = rng.Range(-bh / 2, bh / 2);
                                double r = rng.Range(0.006, 0.016) * bw;
                                g.FillEllipse(b, (float)(x - r), (float)(y - r * 1.8),
                                              (float)(r * 2), (float)(r * 3.6));
                            }
                            for (int i = 0; i < 10; i++)
                            {
                                double x = rng.Range(-bw / 2, bw / 2);
                                double y = rng.Range(-bh / 2, bh / 2);
                                double len = rng.Range(0.1, 0.3) * bh;
                                g.FillRectangle(b, (float)x, (float)y, (float)(bw * 0.006), (float)len);
                            }
                        }
                        g.CompositingMode = cm;
                    }
                    break;

                case Preset.Copper:
                    {
                        RGB copper = light.Mixed(new RGB(1.0, 0.70, 0.42), 0.6);
                        for (int i = 0; i < 10; i++)
                        {
                            double x = rng.Range(-bw * 0.3, bw * 0.3);
                            double y = rng.Range(-bh * 0.5, bh * 0.5);
                            double l = rng.Range(0.5, 1.0) * bw;
                            double t = rng.Range(0.025, 0.06) * bh;
                            using (SolidBrush b = new SolidBrush(copper.ToColor(rng.Range(0.5, 0.9))))
                            {
                                g.FillEllipse(b, (float)(x - l / 2), (float)(y - t / 2), (float)l, (float)t);
                            }
                        }
                    }
                    break;

                case Preset.Opaline:
                    {
                        RGB[] palette = new RGB[]
                        {
                            new RGB(0.55, 0.75, 1.0),
                            new RGB(1.0, 0.92, 0.55),
                            new RGB(1.0, 1.0, 1.0),
                            new RGB(0.95, 0.65, 0.80),
                            new RGB(0.70, 0.95, 0.85)
                        };
                        int cols = 3;
                        int rows = 3;
                        double gap = bw * 0.03;
                        double pw = (bw - gap * (cols - 1)) / cols;
                        double ph = (bh - gap * (rows - 1)) / rows;
                        int idx = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int q = 0; q < cols; q++)
                            {
                                using (SolidBrush b = new SolidBrush(palette[(idx * 2 + r) % palette.Length].ToColor(0.9)))
                                {
                                    g.FillRectangle(b,
                                        (float)(-bw / 2 + q * (pw + gap)),
                                        (float)(-bh / 2 + r * (ph + gap)),
                                        (float)pw, (float)ph);
                                }
                                idx++;
                            }
                        }
                    }
                    break;

                case Preset.Jali:
                    {
                        int cols = 11;
                        int rowsN = 16;
                        double pitchX = bw * 1.3 / cols;
                        double pitchY = bh * 1.3 / rowsN;
                        using (SolidBrush b = new SolidBrush(light.ToColor(1)))
                        {
                            for (int r = 0; r < rowsN; r++)
                            {
                                for (int q = 0; q < cols; q++)
                                {
                                    double off = (r % 2 == 0) ? 0.0 : 0.5;
                                    double cx = -bw * 0.65 + (q + off + 0.5) * pitchX;
                                    double cy = -bh * 0.65 + (r + 0.5) * pitchY;
                                    using (GraphicsPath path = new GraphicsPath())
                                    {
                                        path.StartFigure();
                                        path.AddLine((float)cx, (float)(cy + pitchY * 0.42),
                                                     (float)(cx + pitchX * 0.30), (float)cy);
                                        path.AddLine((float)(cx + pitchX * 0.30), (float)cy,
                                                     (float)cx, (float)(cy - pitchY * 0.42));
                                        path.AddLine((float)cx, (float)(cy - pitchY * 0.42),
                                                     (float)(cx - pitchX * 0.30), (float)cy);
                                        path.CloseFigure();
                                        g.FillPath(b, path);
                                    }
                                }
                            }
                        }
                    }
                    break;

                case Preset.Shoji:
                    FillGrid(g, bw * 0.8, bh, 3, 5, bw * 0.022, light.ToColor(0.88));
                    break;
            }
        }
    }
}
