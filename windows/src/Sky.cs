using System;
using System.Globalization;

namespace Sunwall
{
    public struct SunPos
    {
        public double Azimuth;    // degrees clockwise from north
        public double Elevation;  // degrees above horizon
    }

    // NOAA-style approximation, good to well under a degree. A direct port of the
    // Swift version so both platforms agree on where the sun is.
    public static class Sky
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static double Rad(double d) { return d * Math.PI / 180.0; }
        public static double Deg(double r) { return r * 180.0 / Math.PI; }

        public static double Clamp(double x, double a, double b)
        {
            if (x < a) return a;
            if (x > b) return b;
            return x;
        }

        public static double Clamp01(double x) { return Clamp(x, 0, 1); }

        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static double SmoothStep(double e0, double e1, double x)
        {
            double t = Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        // Same as Swift's truncatingRemainder: the sign follows the dividend.
        public static double TruncRem(double x, double y) { return x - Math.Truncate(x / y) * y; }

        static double Wrap360(double x)
        {
            double v = TruncRem(x, 360.0);
            if (v < 0) v += 360.0;
            return v;
        }

        public static double UnixSeconds(DateTime local)
        {
            DateTime utc = local.ToUniversalTime();
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static SunPos Position(DateTime local, double lat, double lon)
        {
            double jd = UnixSeconds(local) / 86400.0 + 2440587.5;
            double n = jd - 2451545.0;
            double L = TruncRem(280.460 + 0.9856474 * n, 360.0);
            double g = Rad(TruncRem(357.528 + 0.9856003 * n, 360.0));
            double lambda = Rad(L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
            double eps = Rad(23.439 - 0.0000004 * n);
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
            double gmst = TruncRem(280.46061837 + 360.98564736629 * n, 360.0);
            double ha = Rad(gmst + lon) - ra;
            double phi = Rad(lat);
            double sinEl = Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(ha);
            double el = Math.Asin(Clamp(sinEl, -1, 1));
            double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(phi) - Math.Tan(dec) * Math.Cos(phi)) + Math.PI;

            SunPos s;
            s.Azimuth = Wrap360(Deg(az));
            s.Elevation = Deg(el);
            return s;
        }

        // Minute-by-minute scan for the horizon crossings. 1441 sun positions, so the
        // cached version below is what the renderer and the tray menu actually use.
        public static void Times(DateTime localDay, double lat, double lon,
                                 out DateTime? rise, out DateTime? set)
        {
            DateTime start = localDay.Date;
            rise = null;
            set = null;
            double prev = Position(start, lat, lon).Elevation;
            for (int m = 1; m <= 1440; m++)
            {
                DateTime t = start.AddMinutes(m);
                double e = Position(t, lat, lon).Elevation;
                if (!rise.HasValue && prev < -0.833 && e >= -0.833) rise = t;
                if (!set.HasValue && prev >= -0.833 && e < -0.833) set = t;
                prev = e;
            }
        }

        static string timesKey = "";
        static DateTime? timesRise;
        static DateTime? timesSet;

        public static void TimesCached(DateTime day, double lat, double lon,
                                        out DateTime? rise, out DateTime? set)
        {
            string key = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                       + "|" + lat.ToString("R", CultureInfo.InvariantCulture)
                       + "|" + lon.ToString("R", CultureInfo.InvariantCulture);
            if (key != timesKey)
            {
                Times(day, lat, lon, out timesRise, out timesSet);
                timesKey = key;
            }
            rise = timesRise;
            set = timesSet;
        }
    }
}
