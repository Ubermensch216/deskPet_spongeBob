using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>캐릭터 실루엣을 따라 번지는 네온 빛. 몸을 그린 비트맵에서 알파 윤곽을 뽑아 색을 입히고 흐리게 번지게 한다.</summary>
    internal static class NeonGlow
    {
        /// <summary>빛이 번질 여백(그림 단위 배율 기준). Pet.Render 가 비트맵을 이만큼 더 크게 잡는다.</summary>
        internal const float Margin = 42f;

        private static readonly Color[] cycle =
        {
            Color.FromArgb(255, 30, 200),    // 핫핑크
            Color.FromArgb(150, 70, 255),    // 일렉트릭 바이올렛
            Color.FromArgb(0, 225, 255),     // 사이언
            Color.FromArgb(255, 30, 200),
        };
        private const double CycleSeconds = 7.5;

        /// <summary>시간에 따라 핑크 → 보라 → 사이언 → 핑크로 천천히 도는 형광색.</summary>
        internal static Color ColorAt(double seconds)
        {
            double p = (((seconds / CycleSeconds) % 1) + 1) % 1 * (cycle.Length - 1);
            int i = Math.Min(cycle.Length - 2, (int)p);
            return Mix(cycle[i], cycle[i + 1], (float)(p - i));
        }

        internal static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// 몸 비트맵(body)의 실루엣을 따라 번지는 네온 빛을 몸 아래에 깐 같은 크기의 새 비트맵을 만든다.
        /// 큰 그림을 GDI+ 로 흐리게 하면 느리므로, 몸의 알파만 1/2·1/4 크기 배열로 뽑아 거기서 번지게 하고 한 번의 픽셀 계산으로 합친다.
        /// </summary>
        internal static Bitmap Compose(Bitmap body, Color neon, float pulse, float unit)
        {
            pulse = Math.Max(0, Math.Min(1, pulse));
            Color hot = Mix(neon, Color.White, .4f);
            int width = body.Width, height = body.Height;
            int w2 = Math.Max(2, width / 2), h2 = Math.Max(2, height / 2), w4 = Math.Max(2, w2 / 2), h4 = Math.Max(2, h2 / 2);
            float[] half = AlphaHalf(body, w2, h2), quarter = Shrink(half, w2, h2, w4, h4);
            float[] wide = BlurLayer(quarter, w4, h4, Math.Max(1, (int)Math.Round(36 * unit / 4 / 3)), 3);    // 넓고 부드러운 후광
            float[] near = BlurLayer(quarter, w4, h4, Math.Max(1, (int)Math.Round(13 * unit / 4 / 3)), 3);    // 가까이 짙게 번진 빛
            float[] rim = BlurLayer(half, w2, h2, Math.Max(1, (int)Math.Round(3.2f * unit / 2 / 2)), 2);      // 실루엣을 또렷하게 두르는 형광 테두리
            float wideStrength = .7f + .3f * pulse;

            var bodyBytes = new byte[width * 4 * height];
            var data = body.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < height; y++) System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bodyBytes, y * width * 4, width * 4); }
            finally { body.UnlockBits(data); }

            // 가로 방향 표본 위치(두 해상도)를 미리 계산
            int[] x4a = new int[width], x4b = new int[width], x2a = new int[width], x2b = new int[width]; float[] f4 = new float[width], f2 = new float[width];
            for (int x = 0; x < width; x++) { Tap(x, 4, w4, out x4a[x], out x4b[x], out f4[x]); Tap(x, 2, w2, out x2a[x], out x2b[x], out f2[x]); }

            var pixels = new byte[bodyBytes.Length];
            for (int y = 0; y < height; y++)
            {
                int y4a, y4b, y2a, y2b; float g4, g2;
                Tap(y, 4, h4, out y4a, out y4b, out g4); Tap(y, 2, h2, out y2a, out y2b, out g2);
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    float wv = Sample(wide, w4, x4a[x], x4b[x], f4[x], y4a, y4b, g4), nv = Sample(near, w4, x4a[x], x4b[x], f4[x], y4a, y4b, g4), rv = Sample(rim, w2, x2a[x], x2b[x], f2[x], y2a, y2b, g2);
                    float aw = Math.Min(1f, wv * 3f) * wideStrength, an = Math.Min(1f, nv * 3.2f) * .95f, ar = Math.Min(1f, rv * 4f);
                    float ab = bodyBytes[i + 3] / 255f;
                    if (ab >= .999f && ar <= 0) { pixels[i] = bodyBytes[i]; pixels[i + 1] = bodyBytes[i + 1]; pixels[i + 2] = bodyBytes[i + 2]; pixels[i + 3] = 255; continue; }
                    // 넓은 빛 → 가까운 빛(같은 색) → 테두리 → 몸 순으로 위에 겹친다(미리 곱한 색)
                    float ga = aw + an * (1 - aw);
                    float pr = neon.R * ga, pg = neon.G * ga, pb = neon.B * ga;
                    pr = hot.R * ar + pr * (1 - ar); pg = hot.G * ar + pg * (1 - ar); pb = hot.B * ar + pb * (1 - ar); ga = ar + ga * (1 - ar);
                    pr = bodyBytes[i + 2] * ab + pr * (1 - ab); pg = bodyBytes[i + 1] * ab + pg * (1 - ab); pb = bodyBytes[i] * ab + pb * (1 - ab); ga = ab + ga * (1 - ab);
                    if (ga <= 0) continue;
                    pixels[i] = (byte)Math.Min(255, pb / ga + .5f); pixels[i + 1] = (byte)Math.Min(255, pg / ga + .5f); pixels[i + 2] = (byte)Math.Min(255, pr / ga + .5f);
                    pixels[i + 3] = (byte)Math.Min(255, ga * 255 + .5f);
                }
            }
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var wd = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < height; y++) System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, IntPtr.Add(wd.Scan0, y * wd.Stride), width * 4); }
            finally { result.UnlockBits(wd); }
            return result;
        }

        /// <summary>큰 그림의 한 픽셀 p 가 1/factor 크기 배열(길이 n)에서 걸치는 두 칸과 그 사이 비율.</summary>
        private static void Tap(int p, int factor, int n, out int a, out int b, out float f)
        {
            float s = (p + .5f) / factor - .5f; if (s < 0) s = 0; if (s > n - 1) s = n - 1;
            a = (int)s; b = Math.Min(n - 1, a + 1); f = s - a;
        }

        private static float Sample(float[] arr, int w, int xa, int xb, float fx, int ya, int yb, float fy)
        {
            float top = arr[ya * w + xa] + (arr[ya * w + xb] - arr[ya * w + xa]) * fx, bottom = arr[yb * w + xa] + (arr[ya * w + xb] - arr[ya * w + xa]) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>몸 비트맵의 알파를 2×2 칸씩 평균 내어 (w × h) 0~1 배열로.</summary>
        private static float[] AlphaHalf(Bitmap body, int w, int h)
        {
            var result = new float[w * h];
            var data = body.LockBits(new Rectangle(0, 0, body.Width, body.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * body.Height]; System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = y * 2 * data.Stride + x * 8 + 3;
                        result[y * w + x] = (bytes[o] + bytes[o + 4] + bytes[o + data.Stride] + bytes[o + data.Stride + 4]) / 1020f;
                    }
            }
            finally { body.UnlockBits(data); }
            return result;
        }

        private static float[] Shrink(float[] src, int w, int h, int nw, int nh)
        {
            var result = new float[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int sx = Math.Min(w - 2, x * 2), sy = Math.Min(h - 2, y * 2); if (sx < 0 || sy < 0) { result[y * nw + x] = src[0]; continue; }
                    result[y * nw + x] = (src[sy * w + sx] + src[sy * w + sx + 1] + src[(sy + 1) * w + sx] + src[(sy + 1) * w + sx + 1]) / 4f;
                }
            return result;
        }

        /// <summary>알파 배열을 상자 흐림 여러 번(가우시안 비슷)으로 번지게 한 새 배열.</summary>
        private static float[] BlurLayer(float[] alpha, int w, int h, int radius, int passes)
        {
            float[] a = alpha;
            for (int i = 0; i < passes; i++) { a = BoxBlur(a, w, h, radius, true); a = BoxBlur(a, w, h, radius, false); }
            return a;
        }

        /// <summary>한 방향 상자 흐림(창 바깥은 0). 결과는 새 배열로 돌려주고, 입력은 건드리지 않는다.</summary>
        private static float[] BoxBlur(float[] src, int w, int h, int r, bool horizontal)
        {
            var dst = new float[w * h]; float scale = 1f / (2 * r + 1);
            int lines = horizontal ? h : w, length = horizontal ? w : h, step = horizontal ? 1 : w, lineStep = horizontal ? w : 1;
            for (int line = 0; line < lines; line++)
            {
                int baseIndex = line * lineStep; float sum = 0;
                for (int i = 0; i < Math.Min(r, length); i++) sum += src[baseIndex + i * step];
                for (int i = 0; i < length; i++)
                {
                    if (i + r < length) sum += src[baseIndex + (i + r) * step];
                    if (i - r - 1 >= 0) sum -= src[baseIndex + (i - r - 1) * step];
                    dst[baseIndex + i * step] = sum * scale;
                }
            }
            return dst;
        }
    }

    /// <summary>
    /// 알림 친구: 평소의 2배 크기로 화면 한가운데에 서서, 실루엣에 형광 네온 빛을 두르고,
    /// 큰 글씨 말풍선으로 알림 내용을 알려 준다. 친구나 말풍선을 누르면 닫힌다.
    /// </summary>
    internal sealed class AlertPetWindow : Form
    {
        internal const float Zoom = 2f;
        private const int MaxMessages = 4;
        private static readonly float[] FontSizes = { 36, 33, 30, 27, 24, 22, 20, 18, 16 };

        private readonly App app;
        private readonly CharacterArt art;
        private readonly Pet pet;
        private readonly Rectangle area;
        private readonly List<string> messages = new List<string>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Font font; private float bubbleWidth; private string shownText;
        private double lastT, nextHop = 7, lastTop;

        internal bool HasPresented { get; private set; }
        internal string Message { get { return shownText ?? ""; } }
        internal Rectangle Area { get { return area; } }
        /// <summary>마지막으로 올린 그림의 화면 위치(왼쪽 위).</summary>
        internal Point Placed { get; private set; }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST; return p; }
        }

        internal AlertPetWindow(App app, int character)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Text = "비키니 친구들 · 알림"; Size = new Size(1, 1); Location = new Point(-32000, -32000);
            area = Screen.FromPoint(Cursor.Position).WorkingArea;
            art = CharacterArt.Zoomed(character, Zoom);
            // 발바닥 위치: 몸통의 한가운데가 화면 한가운데에 오도록
            pet = new Pet(app, art, area.Left + area.Width / 2.0, area.Top + area.Height / 2.0 + art.H / 2.0);
            pet.StartAlert(app);
            timer.Tick += delegate { Tick(); };
        }

        /// <summary>
        /// 알림 내용을 말풍선에 더한다. 이미 떠 있으면 아래에 이어 붙인다(최대 4개).
        /// 이미 말한 내용이 포함된 알림이면 건너뛰고, 이미 말한 내용을 포함하는 더 긴 알림(정각 + 점심 문구)이면 그 줄을 합친다.
        /// </summary>
        internal void AddMessage(string text)
        {
            text = (text ?? "").Replace("\r", "").Trim(); if (text.Length == 0) return;
            if (messages.Exists(m => m.IndexOf(text, StringComparison.Ordinal) >= 0)) return;
            messages.RemoveAll(m => text.IndexOf(m, StringComparison.Ordinal) >= 0);
            messages.Add(text);
            while (messages.Count > MaxMessages) messages.RemoveAt(0);
            Relayout();
        }

        /// <summary>창을 띄우고 움직이기 시작한다.</summary>
        internal void Begin()
        {
            Show(); Draw(0); timer.Start();
        }

        /// <summary>글자 크기를 정한다: 큰 글씨로 시작해, 몸 위쪽 빈 공간에 말풍선이 들어갈 때까지 줄인다.</summary>
        private void Relayout()
        {
            string text = string.Join("\n\n", messages.ToArray());
            float u = art.Unit, scale = CharacterArt.Scale;
            bubbleWidth = Math.Min(area.Width * .55f, 760f * scale);
            // 말풍선 윗면 = 화면 가운데 − 몸 반 높이 − (꼬리·간격 10u) − 말풍선 높이. 폴짝 뛰는 높이(약 11u)와 여유를 빼 둔다.
            float room = Math.Max(60f, area.Height / 2f - art.H / 2f - 10 * u - 14 * u - 6);
            Font chosen = null;
            using (var bitmap = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < FontSizes.Length; i++)
                {
                    Font candidate = MakeFont(FontSizes[i] * scale);
                    SizeF size = g.MeasureString(text, candidate, (int)bubbleWidth, StringFormat.GenericTypographic);
                    float height = (float)Math.Ceiling(size.Height + 7 * u * 1.4f + 2 * u);
                    if (height <= room || i == FontSizes.Length - 1) { chosen = candidate; break; }
                    candidate.Dispose();
                }
            }
            if (font != null) font.Dispose();
            font = chosen; shownText = text; pet.Bubble = text;
            if (IsHandleCreated) Draw(clock.Elapsed.TotalSeconds);
        }

        private static Font MakeFont(float pixels)
        {
            try { return new Font("Malgun Gothic", pixels, FontStyle.Bold, GraphicsUnit.Pixel); }
            catch { return new Font(FontFamily.GenericSansSerif, pixels, FontStyle.Bold, GraphicsUnit.Pixel); }
        }

        private void Tick()
        {
            if (IsDisposed || !IsHandleCreated) return;
            double t = clock.Elapsed.TotalSeconds, dt = Math.Max(0, Math.Min(.1, t - lastT)); lastT = t;
            pet.AdvanceAlert(dt);
            if (app.Now >= nextHop) { pet.AlertHop(app); nextHop = app.Now + 7; }
            Draw(t);
            if (t - lastTop > 2) { lastTop = t; Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 19); }
        }

        /// <summary>지금 모습(자세·네온·말풍선)을 그려 창에 올린다.</summary>
        internal void Draw(double t)
        {
            if (!IsHandleCreated || font == null) return;
            Pet.Look look = pet.GetLook(app.Now);
            look.AffinityLabel = null; look.HopY *= Zoom;
            look.BubbleFont = font; look.BubbleWidth = bubbleWidth; look.CloseMark = true;
            look.Neon = NeonGlow.ColorAt(t); look.NeonPulse = (float)(.5 + .5 * Math.Sin(t * Math.PI * 2 / 1.5));
            PointF origin;
            using (Bitmap bitmap = Pet.Render(look, out origin))
            {
                int x = (int)Math.Round(pet.X - origin.X), y = (int)Math.Round(pet.Y + look.HopY - origin.Y);
                try { LayerPixels.Present(Handle, bitmap, x, y, 255); HasPresented = true; Placed = new Point(x, y); }
                catch (Exception e) { FeatureLog.Write(e); }
            }
        }

        /// <summary>지금 화면에 그려진 모습을 그림 파일용 비트맵으로 돌려준다(점검용).</summary>
        internal Bitmap Snapshot(double t, out PointF origin)
        {
            Pet.Look look = pet.GetLook(app.Now);
            look.AffinityLabel = null; look.HopY *= Zoom;
            look.BubbleFont = font; look.BubbleWidth = bubbleWidth; look.CloseMark = true;
            look.Neon = NeonGlow.ColorAt(t); look.NeonPulse = .5f;
            return Pet.Render(look, out origin);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (font != null) { font.Dispose(); font = null; } }
            base.Dispose(disposing);
        }
    }
}