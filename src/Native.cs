using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BikiniFriends
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfo
        {
            public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize;
            public int XPels, YPels; public uint Used, Important, Color;
        }

        public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
        public const int ULW_ALPHA = 2;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 16, SWP_NOOWNERZORDER = 512;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        [DllImport("gdi32.dll", SetLastError = true)] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        // 마지막 입력(키보드·마우스) 이후 지난 시간(초). 입력 감지에도 쓴다.
        public static uint LastInputTick()
        {
            try { var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) }; return GetLastInputInfo(ref i) ? i.dwTime : 0; }
            catch { return 0; }
        }
        public static double IdleSeconds()
        {
            try
            {
                var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref i)) return 0;
                return (double)((uint)Environment.TickCount - i.dwTime) / 1000.0;
            }
            catch { return 0; }
        }
        public static bool ScreenSaverRunning()
        {
            try { bool v = false; SystemParametersInfo(114, 0, ref v, 0); return v; } catch { return false; }
        }
        public static float SystemDpiScale()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero); int dpi = GetDeviceCaps(dc, 88); ReleaseDC(IntPtr.Zero, dc);
                return dpi <= 0 ? 1f : dpi / 96f;
            }
            catch { return 1f; }
        }
    }

    internal static class FeatureLog
    {
        internal static void Write(Exception e)
        {
            try
            {
                Directory.CreateDirectory(Paths.Dir);
                File.AppendAllText(Path.Combine(Paths.Dir, "비키니친구들_오류기록.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + e + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class Paths
    {
        internal static string StorageOverride;
        internal static string Dir { get { return StorageOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "비키니친구들"); } }
    }

    /// <summary>열린 창(걸어 올라갈 수 있는 면)과 마우스·전체화면 상태.</summary>
    internal sealed class SurfaceWindow
    {
        internal IntPtr Handle; internal Rectangle Bounds;
        internal SurfaceWindow(IntPtr h, Rectangle r) { Handle = h; Bounds = r; }
    }

    internal static class DesktopSurfaces
    {
        internal static Func<List<SurfaceWindow>> TestProvider;
        internal static Func<Point> TestMouse;
        internal static List<SurfaceWindow> Cached = new List<SurfaceWindow>();
        private static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        internal static Point Mouse { get { if (TestMouse != null) return TestMouse(); try { return Cursor.Position; } catch { return new Point(-10000, -10000); } } }
        internal static bool MousePressed { get { try { return Control.MouseButtons != MouseButtons.None; } catch { return false; } } }

        internal static void Refresh()
        {
            if (TestProvider != null) { Cached = TestProvider(); return; }
            var result = new List<SurfaceWindow>();
            try
            {
                EnumWindows(delegate (IntPtr h, IntPtr unused)
                {
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    if (pid == ownPid || !IsWindowVisible(h) || IsIconic(h)) return true;
                    var cls = new StringBuilder(128); GetClassName(h, cls, cls.Capacity);
                    string c = cls.ToString();
                    if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c.Contains("tooltips")) return true;
                    try { int cloaked; if (DwmGetWindowAttributeInt(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true; } catch { }
                    Rectangle r;
                    if (!TryBounds(h, out r) || r.Width < 160 || r.Height < 90) return true;
                    foreach (double part in new[] { .18, .5, .82 })
                    {
                        IntPtr hit = WindowFromPoint(new WPoint(r.Left + (int)(r.Width * part), r.Top + 5));
                        if (hit == h || GetAncestor(hit, 2) == h) { result.Add(new SurfaceWindow(h, r)); break; }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            Cached = result;
        }

        internal static bool TryBounds(IntPtr h, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (TestProvider != null)
            {
                var t = TestProvider().FirstOrDefault(w => w.Handle == h);
                if (t == null) return false; bounds = t.Bounds; return true;
            }
            if (!IsWindowVisible(h) || IsIconic(h)) return false;
            WRect r;
            try { if (DwmGetWindowAttributeRect(h, 9, out r, Marshal.SizeOf(typeof(WRect))) == 0) { bounds = r.ToRectangle(); return true; } } catch { }
            if (!GetWindowRect(h, out r)) return false;
            bounds = r.ToRectangle(); return true;
        }

        internal static bool TopVisible(IntPtr h, double x, int top)
        {
            if (TestProvider != null) return true;
            try { var hit = WindowFromPoint(new WPoint((int)x, top + 5)); return hit == h || GetAncestor(hit, 2) == h; } catch { return false; }
        }

        internal static bool FullscreenActive()
        {
            try
            {
                IntPtr h = GetForegroundWindow(); uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid == ownPid || IsZoomed(h)) return false;
                var cls = new StringBuilder(128); GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW") return false;
                Rectangle r; if (!TryBounds(h, out r)) return false;
                Rectangle s = Screen.FromRectangle(r).Bounds;
                return r.Left <= s.Left + 2 && r.Top <= s.Top + 2 && r.Right >= s.Right - 2 && r.Bottom >= s.Bottom - 2;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct WRect { public int L, T, R, B; public Rectangle ToRectangle() { return Rectangle.FromLTRB(L, T, R, B); } }
        [StructLayout(LayoutKind.Sequential)] private struct WPoint { public int X, Y; internal WPoint(int x, int y) { X = x; Y = y; } }
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out WRect r);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flag);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(WPoint point);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeInt(IntPtr h, int a, out int v, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeRect(IntPtr h, int a, out WRect r, int size);
    }

    /// <summary>알림 가장자리 빛·정각 카드 등이 쓰는 레이어드 비트맵 변환.</summary>
    internal static class LayerPixels
    {
        internal static byte[] Premultiplied(Bitmap bitmap)
        {
            int rowSize = bitmap.Width * 4; var bytes = new byte[rowSize * bitmap.Height];
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * rowSize, rowSize); }
            finally { bitmap.UnlockBits(data); }
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int a = bytes[i + 3];
                bytes[i] = (byte)((bytes[i] * a + 127) / 255); bytes[i + 1] = (byte)((bytes[i + 1] * a + 127) / 255); bytes[i + 2] = (byte)((bytes[i + 2] * a + 127) / 255);
            }
            return bytes;
        }
    }
}
