using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Sunwall
{
    // One monitor's worth of wallpaper. The window lives as a child of Explorer's
    // wallpaper window, so it draws above the desktop picture and below the icons.
    public sealed class WallWindow
    {
        public IntPtr Handle = IntPtr.Zero;
        public Rectangle Bounds;
        public Bitmap Surface;
        public bool TopLevel;
        public bool LastPaintOk;
        public int LastError;

        public void Render(Model m)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            EnsureSurface();
            if (Surface == null) return;
            WallRenderer.Render(Surface, m);
            // Other windows come and go; keep an overlay window underneath them.
            if (TopLevel) Native.SendToBottom(Handle, Bounds);
            Paint(m.Opacity);
        }

        public void Fill(Color colour)
        {
            EnsureSurface();
            if (Surface == null) return;
            using (Graphics g = Graphics.FromImage(Surface)) g.Clear(colour);
            Paint(1.0);
        }

        void EnsureSurface()
        {
            if (Surface != null && Surface.Width == Bounds.Width && Surface.Height == Bounds.Height) return;
            if (Surface != null) Surface.Dispose();
            Surface = new Bitmap(Bounds.Width, Bounds.Height, PixelFormat.Format32bppPArgb);
        }

        public void Paint(double opacity)
        {
            if (Surface == null || Handle == IntPtr.Zero)
            {
                LastPaintOk = false;
                LastError = 0;
                return;
            }
            // A top-level window gets its position spelled out every time. A child of the
            // desktop keeps the parent-relative position it was given.
            int error;
            if (TopLevel)
                LastPaintOk = Native.BlitLayeredAt(Handle, Surface, opacity, Bounds.Left, Bounds.Top, out error);
            else
                LastPaintOk = Native.BlitLayered(Handle, Surface, opacity, out error);
            LastError = error;
        }
    }

    public sealed class DesktopHost
    {
        readonly List<WallWindow> windows = new List<WallWindow>();

        public IntPtr Parent = IntPtr.Zero;
        public string ParentClass = "(none)";
        public bool TopLevel;

        public int Count { get { return windows.Count; } }

        public bool Valid
        {
            get
            {
                if (windows.Count == 0) return false;
                return Native.IsWindow(windows[0].Handle);
            }
        }

        // True when the last paint did not reach the screen. That is the signal that the
        // child-of-the-desktop approach is not working on this machine.
        public bool PaintFailed
        {
            get
            {
                if (windows.Count == 0) return true;
                for (int i = 0; i < windows.Count; i++)
                {
                    if (!windows[i].LastPaintOk) return true;
                }
                return false;
            }
        }

        public void Build()
        {
            BuildWith(Native.FindWallpaperParent(), false);
        }

        // Fallback: an ordinary layered window kept at the bottom of the z-order. It sits
        // over the wallpaper the same way, without depending on Explorer's window layout.
        public void BuildTopLevel()
        {
            BuildWith(IntPtr.Zero, true);
        }

        void BuildWith(IntPtr parent, bool topLevel)
        {
            Destroy();

            TopLevel = topLevel || parent == IntPtr.Zero;
            Parent = TopLevel ? IntPtr.Zero : parent;
            ParentClass = Parent == IntPtr.Zero ? "(none)" : Native.ClassName(Parent);

            List<Rectangle> monitors = Native.Monitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                WallWindow w = new WallWindow();
                w.Bounds = monitors[i];
                w.TopLevel = TopLevel;
                w.Handle = Native.CreateWallWindow(TopLevel ? IntPtr.Zero : Parent, monitors[i], TopLevel);
                windows.Add(w);
            }

            Log.Write("walls: mode=" + (TopLevel ? "top-level overlay" : "child of " + ParentClass)
                + ", parent=0x" + Parent.ToInt64().ToString("X")
                + ", monitors=" + monitors.Count);
        }

        public void Render(Model m)
        {
            for (int i = 0; i < windows.Count; i++) windows[i].Render(m);
            for (int i = 0; i < windows.Count; i++)
            {
                WallWindow w = windows[i];
                if (!w.LastPaintOk)
                {
                    Log.Write("paint failed on window 0x" + w.Handle.ToInt64().ToString("X")
                        + " error=" + w.LastError);
                }
            }
        }

        public void Repaint(double opacity)
        {
            for (int i = 0; i < windows.Count; i++) windows[i].Paint(opacity);
        }

        public void Fill(Color colour)
        {
            for (int i = 0; i < windows.Count; i++) windows[i].Fill(colour);
        }

        public void Destroy()
        {
            for (int i = 0; i < windows.Count; i++)
            {
                WallWindow w = windows[i];
                if (w.Handle != IntPtr.Zero) Native.DestroyWindow(w.Handle);
                w.Handle = IntPtr.Zero;
                if (w.Surface != null) { w.Surface.Dispose(); w.Surface = null; }
            }
            windows.Clear();
        }

        public string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("mode: " + (TopLevel ? "top-level overlay" : "child of desktop"));
            sb.AppendLine("parent: 0x" + Parent.ToInt64().ToString("X") + " " + ParentClass);
            sb.AppendLine("windows: " + windows.Count);
            for (int i = 0; i < windows.Count; i++)
            {
                WallWindow w = windows[i];
                sb.AppendLine("  monitor " + i
                    + "  " + w.Bounds.Width + "x" + w.Bounds.Height
                    + " at " + w.Bounds.Left + "," + w.Bounds.Top
                    + "  hwnd=0x" + w.Handle.ToInt64().ToString("X")
                    + "  visible=" + (w.Handle != IntPtr.Zero && Native.IsWindowVisible(w.Handle))
                    + "  paint=" + (w.LastPaintOk ? "ok" : "failed (" + w.LastError + ")"));
            }
            return sb.ToString();
        }
    }

    public static class Native
    {
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;

        const int GWL_STYLE = -16;
        const int WS_CHILD_I = 0x40000000;
        const int WS_POPUP_I = unchecked((int)0x80000000);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;
        const uint WS_EX_LAYERED = 0x00080000;
        const uint WS_EX_TRANSPARENT = 0x00000020;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint SMTO_NORMAL = 0;
        const int ULW_ALPHA = 2;
        const byte AC_SRC_OVER = 0;
        const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int w, int h,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
        static extern bool UpdateLayeredWindowNoPos(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
            ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
        static extern bool UpdateLayeredWindowPos(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
            ref BLENDFUNCTION pblend, uint dwFlags);

        public static void SendToBottom(IntPtr hwnd, Rectangle bounds)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_BOTTOM, bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, SWP_NOACTIVATE);
        }

        // Held in static fields so the GC cannot collect the thunks while Windows
        // still holds the pointers.
        static WndProcDelegate wndProc;
        static bool classRegistered;

        static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        static void EnsureClass()
        {
            if (classRegistered) return;
            wndProc = StaticWndProc;
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc);
            wc.hInstance = GetModuleHandle(null);
            wc.lpszClassName = "SunwallWallpaper";
            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                Log.Write("RegisterClassEx failed, win32 error " + Marshal.GetLastWin32Error());
            }
            classRegistered = true;
        }

        public static string ClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(none)";
            StringBuilder sb = new StringBuilder(256);
            int n = GetClassName(hwnd, sb, sb.Capacity);
            if (n <= 0) return "(unknown)";
            return sb.ToString();
        }

        public static List<Rectangle> Monitors()
        {
            List<Rectangle> list = new List<Rectangle>();
            MonitorEnumProc proc = delegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT r, IntPtr data)
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    list.Add(Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top,
                                                mi.rcMonitor.Right, mi.rcMonitor.Bottom));
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            if (list.Count == 0)
            {
                list.Add(new Rectangle(0, 0, Math.Max(1, GetSystemMetrics(0)), Math.Max(1, GetSystemMetrics(1))));
            }
            return list;
        }

        // The window that draws the desktop picture itself: the WorkerW that follows the
        // one holding the icons. Falls back to Progman when Explorer behaves differently.
        public static IntPtr FindWallpaperParent()
        {
            IntPtr progman = FindWindow("Progman", null);
            Log.Write("progman=0x" + progman.ToInt64().ToString("X") + " " + ClassName(progman));
            if (progman != IntPtr.Zero)
            {
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out result);
            }

            IntPtr workerw = IntPtr.Zero;
            EnumWindowsProc cb = delegate(IntPtr hwnd, IntPtr lparam)
            {
                IntPtr defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    IntPtr next = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (next != IntPtr.Zero) workerw = next;
                }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            if (workerw != IntPtr.Zero)
            {
                Log.Write("workerw=0x" + workerw.ToInt64().ToString("X") + " " + ClassName(workerw));
                return workerw;
            }
            Log.Write("no WorkerW found, falling back to Progman");
            return progman;
        }

        public static IntPtr CreateWallWindow(IntPtr parent, Rectangle bounds, bool topLevel)
        {
            EnsureClass();
            IntPtr hwnd = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                "SunwallWallpaper", "Sunwall",
                WS_POPUP | WS_VISIBLE,
                0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height),
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                Log.Write("CreateWindowEx failed, win32 error " + Marshal.GetLastWin32Error());
                return IntPtr.Zero;
            }

            if (!topLevel && parent != IntPtr.Zero)
            {
                SetParent(hwnd, parent);
                // SetParent does not adjust the style; a popup that is now a child is
                // inconsistent enough that some Windows versions refuse to draw it.
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style = (style & ~WS_POPUP_I) | WS_CHILD_I;
                SetWindowLong(hwnd, GWL_STYLE, style);
            }

            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            if (topLevel)
            {
                // Bottom of the z-order: above the wallpaper, below everything else.
                SetWindowPos(hwnd, HWND_BOTTOM, bounds.Left, bounds.Top,
                    bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else
            {
                // Child coordinates are relative to the wallpaper window, which spans the
                // virtual desktop.
                SetWindowPos(hwnd, IntPtr.Zero, bounds.Left - vx, bounds.Top - vy,
                    bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            return hwnd;
        }

        // Per-pixel alpha is what lets the wallpaper show through everything the app
        // does not paint, exactly like the transparent CoreGraphics layer.
        public static bool BlitLayered(IntPtr hwnd, Bitmap bmp, double opacity, out int error)
        {
            return Blit(hwnd, bmp, opacity, false, 0, 0, out error);
        }

        public static bool BlitLayeredAt(IntPtr hwnd, Bitmap bmp, double opacity, int x, int y, out int error)
        {
            return Blit(hwnd, bmp, opacity, true, x, y, out error);
        }

        static bool Blit(IntPtr hwnd, Bitmap bmp, double opacity, bool usePosition, int x, int y, out int error)
        {
            error = 0;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBmp);
            bool ok = false;
            try
            {
                SIZE size;
                size.cx = bmp.Width;
                size.cy = bmp.Height;
                POINT src;
                src.X = 0;
                src.Y = 0;
                BLENDFUNCTION blend;
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                double a = opacity;
                if (a < 0) a = 0;
                if (a > 1) a = 1;
                blend.SourceConstantAlpha = (byte)(a * 255.0 + 0.5);
                blend.AlphaFormat = AC_SRC_ALPHA;
                if (usePosition)
                {
                    POINT dst;
                    dst.X = x;
                    dst.Y = y;
                    ok = UpdateLayeredWindowPos(hwnd, screenDc, ref dst, ref size, memDc, ref src,
                        0, ref blend, ULW_ALPHA);
                }
                else
                {
                    ok = UpdateLayeredWindowNoPos(hwnd, screenDc, IntPtr.Zero, ref size, memDc, ref src,
                        0, ref blend, ULW_ALPHA);
                }
                error = Marshal.GetLastWin32Error();
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
            return ok;
        }
    }
}
