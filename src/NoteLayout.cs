using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BikiniFriends
{
    /// <summary>메모 색 8종. 메모에는 색 이름(키)만 저장하고, 실제 색은 여기서 정한다.</summary>
    internal static class NoteColors
    {
        internal const string Yellow = "yellow", Amber = "amber", Green = "green", Teal = "teal", Blue = "blue", Purple = "purple", Pink = "pink", Gray = "gray";
        internal const string Default = Yellow;
        internal static readonly string[] All = { Yellow, Amber, Green, Teal, Blue, Purple, Pink, Gray };
        internal static bool IsKnown(string key) { return key != null && Array.IndexOf(All, key) >= 0; }
        /// <summary>모르는 색(다른 버전이 쓴 값)은 기본색으로 — 불러오기가 깨지지 않게.</summary>
        internal static string Normalize(string key) { return IsKnown(key) ? key : Default; }

        internal static string Label(string key)
        {
            switch (key)
            {
                case Amber: return "호박"; case Green: return "초록"; case Teal: return "청록"; case Blue: return "파랑";
                case Purple: return "보라"; case Pink: return "분홍"; case Gray: return "회색"; default: return "노랑";
            }
        }

        internal static readonly Color Ink = Color.FromArgb(0x2B, 0x27, 0x1D);
        internal static Color Background(string key)
        {
            switch (Normalize(key))
            {
                case Amber: return Color.FromArgb(0xFB, 0xE3, 0xC4); case Green: return Color.FromArgb(0xDF, 0xEF, 0xD8);
                case Teal: return Color.FromArgb(0xD5, 0xEC, 0xE8); case Blue: return Color.FromArgb(0xDA, 0xE6, 0xF5);
                case Purple: return Color.FromArgb(0xE4, 0xDE, 0xF2); case Pink: return Color.FromArgb(0xF6, 0xDE, 0xE6);
                case Gray: return Color.FromArgb(0xE9, 0xE8, 0xE4); default: return Color.FromArgb(0xFF, 0xF4, 0xCC);
            }
        }
        internal static Color Border(string key)
        {
            switch (Normalize(key))
            {
                case Amber: return Color.FromArgb(0xE2, 0xC5, 0x9C); case Green: return Color.FromArgb(0xC2, 0xD9, 0xB8);
                case Teal: return Color.FromArgb(0xB5, 0xD6, 0xD0); case Blue: return Color.FromArgb(0xBB, 0xCD, 0xE4);
                case Purple: return Color.FromArgb(0xC7, 0xBE, 0xDE); case Pink: return Color.FromArgb(0xE1, 0xC0, 0xCC);
                case Gray: return Color.FromArgb(0xCF, 0xCE, 0xC9); default: return Color.FromArgb(0xE6, 0xD8, 0xA4);
            }
        }
        /// <summary>목록 옆에 칠하는 진한 색 막대.</summary>
        internal static Color Swatch(string key)
        {
            switch (Normalize(key))
            {
                case Amber: return Color.FromArgb(0xF2, 0x99, 0x4A); case Green: return Color.FromArgb(0x6F, 0xCF, 0x97);
                case Teal: return Color.FromArgb(0x4E, 0xCD, 0xC4); case Blue: return Color.FromArgb(0x5D, 0xAD, 0xE2);
                case Purple: return Color.FromArgb(0xBB, 0x6B, 0xD9); case Pink: return Color.FromArgb(0xF0, 0x62, 0x92);
                case Gray: return Color.FromArgb(0xA0, 0xA4, 0xB0); default: return Color.FromArgb(0xF2, 0xC9, 0x4C);
            }
        }
    }

    internal enum NoteSizePreset { Small = 0, Medium = 1, Large = 2, Custom = 3 }

    /// <summary>창 위치와 크기, 마지막에 있던 모니터.</summary>
    internal struct NoteGeometry
    {
        internal const int SmallW = 240, SmallH = 180, MediumW = 320, MediumH = 260, LargeW = 420, LargeH = 360;
        internal const int MinW = 180, MinH = 120;
        internal int X, Y, Width, Height; internal string MonitorKey; internal NoteSizePreset Preset;

        internal static Size SizeOf(NoteSizePreset p)
        {
            switch (p) { case NoteSizePreset.Small: return new Size(SmallW, SmallH); case NoteSizePreset.Large: return new Size(LargeW, LargeH); default: return new Size(MediumW, MediumH); }
        }
        internal static NoteGeometry ForPreset(NoteSizePreset p, int x, int y, string monitor)
        { var s = SizeOf(p); return new NoteGeometry { X = x, Y = y, Width = s.Width, Height = s.Height, MonitorKey = monitor ?? "", Preset = p }; }

        /// <summary>손으로 크기를 바꾸면 Custom, 규격과 딱 맞으면 그 규격.</summary>
        internal NoteGeometry WithSize(int w, int h)
        {
            var g = this; g.Width = w; g.Height = h; g.Preset = NoteSizePreset.Custom;
            foreach (var p in new[] { NoteSizePreset.Small, NoteSizePreset.Medium, NoteSizePreset.Large }) { var s = SizeOf(p); if (s.Width == w && s.Height == h) { g.Preset = p; break; } }
            return g;
        }
    }

    /// <summary>모니터의 작업 영역(작업표시줄 제외).</summary>
    internal struct WorkArea
    {
        internal string DeviceName; internal int Left, Top, Right, Bottom;
        internal int Width { get { return Right - Left; } }
        internal int Height { get { return Bottom - Top; } }
        internal bool Contains(int x, int y) { return x >= Left && x < Right && y >= Top && y < Bottom; }
    }

    /// <summary>메모 창이 놓일 자리를 정한다. 창이 없어진 모니터에 있던 메모도 손이 닿는 곳으로 끌어온다.</summary>
    internal static class NotePlacement
    {
        internal const int MinimumVisibleEdge = 48;
        private const int CascadeStep = 28, CascadeWrap = 8;

        internal static NoteGeometry ClampToVisibleArea(NoteGeometry g, IList<WorkArea> monitors)
        {
            if (monitors == null || monitors.Count == 0) return g;
            WorkArea target = monitors[0]; bool found = false;
            foreach (var m in monitors) if (string.Equals(m.DeviceName, g.MonitorKey, StringComparison.OrdinalIgnoreCase)) { target = m; found = true; break; }
            if (!found) foreach (var m in monitors) if (m.Contains(g.X, g.Y)) { target = m; break; }
            int w = Math.Max(NoteGeometry.MinW, Math.Min(g.Width, Math.Max(NoteGeometry.MinW, target.Width)));
            int h = Math.Max(NoteGeometry.MinH, Math.Min(g.Height, Math.Max(NoteGeometry.MinH, target.Height)));
            int minX = target.Left - w + MinimumVisibleEdge, maxX = target.Right - MinimumVisibleEdge, minY = target.Top, maxY = target.Bottom - MinimumVisibleEdge;
            g.X = Math.Max(minX, Math.Min(g.X, Math.Max(minX, maxX)));
            g.Y = Math.Max(minY, Math.Min(g.Y, Math.Max(minY, maxY)));
            g.Width = w; g.Height = h; g.MonitorKey = target.DeviceName;
            return g;
        }

        /// <summary>새 메모는 화면 가운데 근처에, 연달아 만들면 조금씩 비껴 놓는다.</summary>
        internal static NoteGeometry PlaceNew(NoteSizePreset preset, WorkArea monitor, int cascadeIndex, IList<WorkArea> monitors = null)
        {
            var size = NoteGeometry.SizeOf(preset); int offset = Math.Abs(cascadeIndex % CascadeWrap) * CascadeStep;
            var g = new NoteGeometry
            {
                X = monitor.Left + (monitor.Width - size.Width) / 2 + offset, Y = monitor.Top + (monitor.Height - size.Height) / 2 + offset,
                Width = size.Width, Height = size.Height, MonitorKey = monitor.DeviceName, Preset = preset,
            };
            return ClampToVisibleArea(g, monitors ?? new List<WorkArea> { monitor });
        }

        internal static List<WorkArea> CurrentMonitors()
        {
            return System.Windows.Forms.Screen.AllScreens.Select(s => new WorkArea { DeviceName = s.DeviceName, Left = s.WorkingArea.Left, Top = s.WorkingArea.Top, Right = s.WorkingArea.Right, Bottom = s.WorkingArea.Bottom }).ToList();
        }
        internal static WorkArea Primary()
        {
            var s = System.Windows.Forms.Screen.PrimaryScreen;
            return new WorkArea { DeviceName = s.DeviceName, Left = s.WorkingArea.Left, Top = s.WorkingArea.Top, Right = s.WorkingArea.Right, Bottom = s.WorkingArea.Bottom };
        }
    }
}
