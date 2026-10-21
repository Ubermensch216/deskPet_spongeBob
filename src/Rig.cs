using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    internal enum EyeStyle { Open, Happy, Sleep, Spiral }
    internal enum MouthStyle { Neutral, Smile, Open, Scared, Frown, Pout }
    internal enum HandStyle { Open, Fist }
    /// <summary>앞손(화면에서 보는 방향 쪽 손)에 쥐는 소품.</summary>
    internal enum Prop { None, Net, Wand, Spatula, Clarinet, Lasso, Cash, Blueprint, Brush }

    /// <summary>
    /// 한 순간의 자세. 길이는 모두 "키 300" 기준 단위(캐릭터마다 같은 단위)이고 각도는 도(시계 방향 +).
    /// 팔 각도(ArmLA…)는 캐릭터의 기본 팔 자세에 "더하는" 값이라 0 이 기본, 그림은 왼쪽을 보는 모습이 기준이다.
    /// L(왼쪽)은 보는 방향 쪽의 앞쪽 팔·다리다.
    /// </summary>
    internal sealed class Pose
    {
        // 몸
        public float BodyX, BodyY, Lean;       // 상체 이동(+Y 는 아래)·기울임(엉덩이 축)
        public float Breath;                   // 숨쉬기 (-1..1)
        public bool Sit;                       // 앉은 자세
        public float Turn;                     // 0 정면 … 1 보는 쪽으로 얼굴을 많이 돌림
        public float Squash;                   // 몸이 납작해지는 정도(가로로 퍼짐), 0..1
        // 머리
        public float HeadX, HeadY, HeadTilt;
        public float EarL, EarR;               // 귀·안테나·눈자루 각도
        // 눈
        public EyeStyle Eyes;
        public float OpenL = 1, OpenR = 1;
        public float Gx, Gy;                   // 시선 (-1..1)
        public float PupilR = 1, EyeScale = 1;
        public float Brow;                     // + 화남, - 슬픔
        public float SpiralT;
        // 입
        public MouthStyle Mouth;
        public float MouthOpen, Tongue;
        // 팔: 기본 자세에 더하는 각도
        public float ArmLA, ArmLB, ArmRA, ArmRB;
        public HandStyle HandL, HandR;
        public PointF? HandLTo, HandRTo;       // 가슴 기준 상대 좌표(손목이 갈 곳)
        public bool Clasp, ArmsFront;
        public float Claw;                     // 집게발(크랩스)·손가락 벌림 0..1
        // 다리
        public float FootLX, FootLY, FootRX, FootRY, FootLRot, FootRRot, Knee;
        // 꼬리·장식 부위(샌디 꼬리 등)
        public float Tail, TailCurl, TailWaveAmp, TailWavePhase;
        // 표정 장식
        public float Tears = -1, Anger, Sweat = -1;
        public bool Goggles;
        public float Food = -1;                // >=0 이면 간식 (깨문 정도 0..1)
        public float Flush;
        // 소품
        public Prop Prop; public float PropRot, PropT;
        public bool EyesDollar;                // 눈이 $ 로 (집게사장)

        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        public long Hash()
        {
            unchecked
            {
                long h = 1469598103934665603L;
                Action<float, float> add = (v, q) => { h = (h ^ (long)Math.Round(v * q)) * 1099511628211L; };
                add(BodyX, 2); add(BodyY, 2); add(Lean, 4); add(Breath, 20); add(Sit ? 1 : 0, 1); add(Turn, 8); add(Squash, 10);
                add(HeadX, 2); add(HeadY, 2); add(HeadTilt, 4); add(EarL, 3); add(EarR, 3);
                add((int)Eyes, 1); add(OpenL, 12); add(OpenR, 12); add(Gx, 10); add(Gy, 10);
                add(PupilR, 10); add(EyeScale, 20); add(Brow, 10); add(SpiralT, 5); add((int)Mouth, 1); add(MouthOpen, 12);
                add(Tongue, 8); add(ArmLA, 3); add(ArmLB, 3); add(ArmRA, 3); add(ArmRB, 3); add((int)HandL, 1); add((int)HandR, 1);
                add(HandLTo.HasValue ? HandLTo.Value.X : -999, 2); add(HandLTo.HasValue ? HandLTo.Value.Y : -999, 2);
                add(HandRTo.HasValue ? HandRTo.Value.X : -999, 2); add(HandRTo.HasValue ? HandRTo.Value.Y : -999, 2);
                add(Clasp ? 1 : 0, 1); add(ArmsFront ? 1 : 0, 1); add(Claw, 10);
                add(FootLX, 2); add(FootLY, 2); add(FootRX, 2); add(FootRY, 2); add(FootLRot, 3); add(FootRRot, 3); add(Knee, 3);
                add(Tail, 3); add(TailCurl, 3); add(TailWaveAmp, 3); add(TailWavePhase, 6);
                add(Tears, 8); add(Anger, 8); add(Sweat, 8); add(Goggles ? 1 : 0, 1); add(Food, 20); add(Flush, 8);
                add((int)Prop, 1); add(PropRot, 3); add(PropT, 8); add(EyesDollar ? 1 : 0, 1);
                return h;
            }
        }
    }

    /// <summary>다각형 계산 보조.</summary>
    internal static class Geo
    {
        public static PointF P(float x, float y) { return new PointF(x, y); }

        private static PointF CR(PointF p0, PointF p1, PointF p2, PointF p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return new PointF(
                .5f * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                .5f * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3));
        }

        /// <summary>제어점을 지나는 부드러운 곡선(Catmull-Rom)을 촘촘한 꺾은선으로 바꾼다.</summary>
        public static PointF[] Smooth(PointF[] c, bool closed, int sub = 7)
        {
            int n = c.Length; var res = new List<PointF>();
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                PointF p0 = c[closed ? (i - 1 + n) % n : Math.Max(i - 1, 0)], p1 = c[i];
                PointF p2 = c[closed ? (i + 1) % n : i + 1], p3 = c[closed ? (i + 2) % n : Math.Min(i + 2, n - 1)];
                for (int s = 0; s < sub; s++) res.Add(CR(p0, p1, p2, p3, s / (float)sub));
            }
            if (!closed) res.Add(c[n - 1]);
            return res.ToArray();
        }

        public static PointF[] Ellipse(float cx, float cy, float rx, float ry, int n = 24)
        {
            var r = new PointF[n];
            for (int i = 0; i < n; i++) { double a = i * Math.PI * 2 / n; r[i] = new PointF(cx + (float)Math.Cos(a) * rx, cy + (float)Math.Sin(a) * ry); }
            return r;
        }

        public static PointF[] Map(PointF[] p, Func<PointF, PointF> f)
        {
            var r = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = f(p[i]);
            return r;
        }

        /// <summary>a*x + b*y + c &gt;= 0 인 쪽만 남긴다(Sutherland-Hodgman).</summary>
        public static PointF[] ClipHalf(PointF[] poly, float a, float b, float c)
        {
            var res = new List<PointF>();
            for (int i = 0; i < poly.Length; i++)
            {
                PointF cur = poly[i], nxt = poly[(i + 1) % poly.Length];
                float dc = a * cur.X + b * cur.Y + c, dn = a * nxt.X + b * nxt.Y + c;
                if (dc >= 0) res.Add(cur);
                if ((dc >= 0) != (dn >= 0))
                {
                    float t = dc / (dc - dn);
                    res.Add(new PointF(cur.X + (nxt.X - cur.X) * t, cur.Y + (nxt.Y - cur.Y) * t));
                }
            }
            return res.ToArray();
        }

        /// <summary>뼈대(spine)를 따라 굵기(w)를 준 몸통 윤곽. 끝은 둥글게 닫을 수 있다.</summary>
        public static PointF[] Limb(PointF[] spine, float[] w, bool roundEnd, int sub = 6)
        {
            PointF[] s = Smooth(spine, false, sub);
            int m = s.Length;
            var left = new PointF[m]; var right = new PointF[m];
            for (int i = 0; i < m; i++)
            {
                PointF a = s[Math.Max(i - 1, 0)], b = s[Math.Min(i + 1, m - 1)];
                float tx = b.X - a.X, ty = b.Y - a.Y, len = (float)Math.Sqrt(tx * tx + ty * ty);
                if (len < 1e-4f) { tx = 1; ty = 0; len = 1; }
                tx /= len; ty /= len;
                float u = i / (float)(m - 1) * (w.Length - 1); int i0 = Math.Min((int)u, w.Length - 2); float f = u - i0;
                float wd = (w.Length == 1 ? w[0] : w[i0] + (w[i0 + 1] - w[i0]) * f) / 2;
                left[i] = new PointF(s[i].X - ty * wd, s[i].Y + tx * wd);
                right[i] = new PointF(s[i].X + ty * wd, s[i].Y - tx * wd);
            }
            var res = new List<PointF>(left);
            if (roundEnd)
            {
                PointF a = s[m - 2], b = s[m - 1];
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X); float wd = (w[w.Length - 1]) / 2;
                for (int i = 1; i < 8; i++)
                {
                    double q = ang + Math.PI / 2 - Math.PI * i / 8.0;
                    res.Add(new PointF(b.X + (float)Math.Cos(q) * wd, b.Y + (float)Math.Sin(q) * wd));
                }
            }
            for (int i = m - 1; i >= 0; i--) res.Add(right[i]);
            return res.ToArray();
        }

        /// <summary>네 모서리를 이은 사각형의 가장자리를 물결(스펀지 가장자리)처럼 흔든다.</summary>
        public static PointF[] Wavy(PointF[] corners, float amp, float step, float phase = 0)
        {
            var res = new List<PointF>();
            int n = corners.Length;
            for (int i = 0; i < n; i++)
            {
                PointF a = corners[i], b = corners[(i + 1) % n];
                float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                int cnt = Math.Max(2, (int)(len / step));
                float nx = -dy / len, ny = dx / len;
                for (int j = 0; j < cnt; j++)
                {
                    float t = j / (float)cnt;
                    float off = (j == 0) ? 0 : amp * (float)Math.Sin((i * 7 + j * 2.3 + phase) * 1.7) * (float)Math.Sin(Math.PI * t * .999f + .05f);
                    res.Add(new PointF(a.X + dx * t + nx * off, a.Y + dy * t + ny * off));
                }
            }
            return res.ToArray();
        }
    }

    /// <summary>그리기 도우미: 색 붓·펜 캐시, 팔 IK, 눈·입·손·눈물 같은 공통 부품.</summary>
    internal static class RU
    {
        /// <summary>오른쪽을 볼 때 좌우를 뒤집어 그리는 중인가(글자는 다시 뒤집어야 한다).</summary>
        internal static bool Mirrored;

        private static readonly Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        internal static SolidBrush B(int r, int g, int b, int a = 255)
        {
            int key = (a << 24) | (r << 16) | (g << 8) | b; SolidBrush br;
            if (!brushes.TryGetValue(key, out br)) brushes[key] = br = new SolidBrush(Color.FromArgb(a, r, g, b));
            return br;
        }
        internal static SolidBrush B(Color c) { return B(c.R, c.G, c.B, c.A); }

        private static readonly Dictionary<long, Pen> pens = new Dictionary<long, Pen>();
        internal static Pen Pn(Color c, float w)
        {
            long key = ((long)(uint)c.ToArgb() << 20) | (long)Math.Min(1048575, (int)(w * 20));
            Pen p;
            if (!pens.TryGetValue(key, out p)) pens[key] = p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            return p;
        }
        internal static Pen Pn(int r, int g, int b, float w, int a = 255) { return Pn(Color.FromArgb(a, r, g, b), w); }

        internal static readonly Color InkColor = Color.FromArgb(52, 40, 34);
        internal static readonly SolidBrush White = B(250, 250, 250), Black = B(30, 28, 32), Pink = B(240, 130, 150), MouthIn = B(150, 36, 52);

        internal static PointF P(float x, float y) { return new PointF(x, y); }

        /// <summary>g 를 (cx,cy) 중심으로 돌려 그린 뒤 되돌린다.</summary>
        internal static GraphicsState Rot(Graphics g, float cx, float cy, float deg)
        {
            var st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(deg); g.TranslateTransform(-cx, -cy); return st;
        }

        internal static void FillOutline(Graphics g, PointF[] pts, Brush fill, Pen ink)
        { g.FillPolygon(fill, pts); if (ink != null) g.DrawPolygon(ink, pts); }

        internal static void Ell(Graphics g, Brush fill, float cx, float cy, float rx, float ry, Pen ink)
        {
            if (fill != null) g.FillEllipse(fill, cx - rx, cy - ry, rx * 2, ry * 2);
            if (ink != null) g.DrawEllipse(ink, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        internal static void LimbFill(Graphics g, PointF[] spine, float[] w, bool round, Brush fill, Pen ink)
        {
            PointF[] o = Geo.Limb(spine, w, round, 6);
            g.FillPolygon(fill, o); if (ink != null) g.DrawPolygon(ink, o);
        }

        /// <summary>글자를 좌우 뒤집힘 없이 (cx,cy) 가운데에 그린다.</summary>
        internal static void Text(Graphics g, string s, float cx, float cy, float size, Brush b)
        {
            var st = g.Save();
            g.TranslateTransform(cx, cy); if (Mirrored) g.ScaleTransform(-1, 1);
            using (var f = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(s, f, b, 0, 0, fmt);
            g.Restore(st);
        }

        // ───── 팔 ─────
        /// <summary>기본 자세 + 가슴 기준 손 위치 지정(IK)을 반영한 팔 관절 위치.</summary>
        internal static void Arm(PointF S, float A, float B, float L1, float L2, int outSign, PointF? to, out PointF E, out PointF W, out PointF d)
        {
            if (to.HasValue) IK(S, to.Value, L1, L2, outSign, out A, out B);
            E = new PointF(S.X + outSign * (float)Math.Sin(A * Math.PI / 180) * L1, S.Y + (float)Math.Cos(A * Math.PI / 180) * L1);
            d = new PointF(outSign * (float)Math.Sin(B * Math.PI / 180), (float)Math.Cos(B * Math.PI / 180));
            W = new PointF(E.X + d.X * L2, E.Y + d.Y * L2);
        }

        private static void IK(PointF S, PointF T, float L1, float L2, int outSign, out float A, out float B)
        {
            float dx = T.X - S.X, dy = T.Y - S.Y, dist = (float)Math.Sqrt(dx * dx + dy * dy);
            dist = Math.Max(Math.Abs(L1 - L2) + .5f, Math.Min(L1 + L2 - .5f, dist));
            double th = Math.Atan2(dy, dx), alpha = Math.Acos(Math.Max(-1, Math.Min(1, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist))));
            PointF best = PointF.Empty; double bestScore = double.MinValue;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                double a = th + sg * alpha;
                PointF e = new PointF(S.X + (float)Math.Cos(a) * L1, S.Y + (float)Math.Sin(a) * L1);
                double score = (e.X - S.X) * outSign + (e.Y - S.Y) * .6;   // 바깥·아래로 꺾이는 쪽
                if (score > bestScore) { bestScore = score; best = e; }
            }
            float len = (float)Math.Max(1e-3, Math.Sqrt(dx * dx + dy * dy));
            PointF wr = new PointF(S.X + dx * dist / len, S.Y + dy * dist / len);
            PointF v1 = new PointF(best.X - S.X, best.Y - S.Y), v2 = new PointF(wr.X - best.X, wr.Y - best.Y);
            A = (float)(Math.Atan2(outSign * v1.X, v1.Y) * 180 / Math.PI);
            B = (float)(Math.Atan2(outSign * v2.X, v2.Y) * 180 / Math.PI);
        }

        /// <summary>손: 손목에서 d 방향으로 뻗는다. size = 손 길이.</summary>
        internal static void Hand(Graphics g, PointF wrist, PointF d, bool fist, float open, Brush skin, Pen ink, float size, int fingers, bool thumb)
        {
            float ang = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI);
            var st = g.Save();
            g.TranslateTransform(wrist.X, wrist.Y); g.RotateTransform(ang);
            if (fist)
            {
                RU.Ell(g, skin, size * .45f, 0, size * .5f, size * .46f, ink);
                var kn = Pn(RU.InkColor, ink.Width * .6f);
                for (int i = -1; i <= 1; i++) g.DrawLine(kn, size * .6f, i * size * .24f, size * .88f, i * size * .24f);
            }
            else
            {
                float spread = 18 + open * 22;
                for (int i = 0; i < fingers; i++)
                {
                    float f = fingers == 1 ? 0 : (i / (float)(fingers - 1) - .5f) * 2;
                    float a = f * spread * (float)Math.PI / 180, len = size * (.62f + (1 - Math.Abs(f)) * .12f);
                    var spine = new[] { new PointF(size * .35f, f * size * .18f), new PointF(size * .35f + (float)Math.Cos(a) * len, f * size * .18f + (float)Math.Sin(a) * len) };
                    RU.LimbFill(g, spine, new[] { size * .24f, size * .2f }, true, skin, ink);
                }
                if (thumb)
                {
                    var tsp = new[] { new PointF(size * .2f, -size * .3f), new PointF(size * .5f, -size * (.62f + open * .2f)) };
                    RU.LimbFill(g, tsp, new[] { size * .28f, size * .22f }, true, skin, ink);
                }
                RU.Ell(g, skin, size * .32f, 0, size * .38f, size * .36f, null);
            }
            g.Restore(st);
        }

        // ───── 눈 ─────
        /// <summary>둥근 눈 하나. 눈꺼풀(깜빡임)·화남/슬픔 눈썹 자름·홍채·눈동자·소용돌이를 처리한다.</summary>
        internal static void Eye(Graphics g, Pose p, PointF c, float rx, float ry, float open, float innerSign, Color iris, float irisFrac, float pupilFrac, Pen ink, bool lashes = false, Brush sclera = null)
        {
            float es = p.EyeScale;
            rx *= es; ry *= es;
            if (p.Eyes == EyeStyle.Spiral)
            {
                g.FillEllipse(sclera ?? White, c.X - rx * 1.1f, c.Y - ry * 1.05f, rx * 2.2f, ry * 2.1f);
                Spiral(g, c, Math.Min(rx, ry) * .9f, p.SpiralT, innerSign < 0);
                return;
            }
            if (p.Eyes != EyeStyle.Open || open < .14f)
            {
                var lid = Pn(RU.InkColor, Math.Max(ink.Width * 1.15f, 3.4f));
                if (p.Eyes == EyeStyle.Happy) g.DrawArc(lid, c.X - rx * .9f, c.Y - ry * .3f, rx * 1.8f, ry * 1.1f, 200, 140);
                else g.DrawArc(lid, c.X - rx * .9f, c.Y - ry * .7f, rx * 1.8f, ry * 1.1f, 25, 130);
                if (lashes) Lashes(g, c, rx, ry, ink, true);
                return;
            }
            PointF[] eye = Geo.Ellipse(c.X, c.Y, rx, ry * open, 32);
            if (Math.Abs(p.Brow) > .02f)
            {
                float slope = p.Brow * .85f, y0 = c.Y - ry * open * .55f;
                eye = Geo.ClipHalf(eye, -slope * innerSign, 1, -y0 + slope * innerSign * c.X);
                if (eye.Length < 3) return;
            }
            g.FillPolygon(sclera ?? White, eye);
            var clip = g.Save();
            using (var path = new GraphicsPath()) { path.AddPolygon(eye); g.SetClip(path, CombineMode.Intersect); }
            float ir = irisFrac * Math.Min(rx, ry) * p.PupilR, pr = pupilFrac * Math.Min(rx, ry) * p.PupilR;
            float maxX = Math.Max(0, rx - Math.Max(ir, pr) - 1), maxY = Math.Max(0, ry * open - Math.Max(ir, pr) - 1);
            float px = c.X + p.Gx * maxX, py = c.Y + p.Gy * maxY * .9f;
            if (irisFrac > 0) g.FillEllipse(B(iris), px - ir, py - ir, ir * 2, ir * 2);
            g.FillEllipse(Black, px - pr, py - pr, pr * 2, pr * 2);
            g.FillEllipse(White, px - pr * .1f - ir * .35f, py - ir * .55f, Math.Max(2f, ir * .42f), Math.Max(2f, ir * .42f));
            g.Restore(clip);
            g.DrawPolygon(ink, eye);
            if (lashes) Lashes(g, c, rx, ry * open, ink, false);
        }

        private static void Lashes(Graphics g, PointF c, float rx, float ry, Pen ink, bool closed)
        {
            var pn = Pn(RU.InkColor, Math.Max(2.6f, ink.Width * .85f));
            float top = closed ? c.Y - ry * .1f : c.Y - ry;
            for (int i = -1; i <= 1; i++)
            {
                float x = c.X + i * rx * .5f, a = i * .35f;
                g.DrawLine(pn, x, top + 1, x + (float)Math.Sin(a) * 16, top - 15 + Math.Abs(i) * 3);
            }
        }

        internal static void Spiral(Graphics g, PointF c, float rad, float phase, bool reverse)
        {
            var pen = Pn(30, 28, 32, 4.2f);
            var pts = new List<PointF>();
            for (int i = 0; i <= 44; i++)
            {
                double a = i * .42 + phase * (reverse ? -1 : 1), r = 2 + i * rad / 44;
                pts.Add(new PointF(c.X + (float)(Math.Cos(a) * r), c.Y + (float)(Math.Sin(a) * r)));
            }
            g.DrawLines(pen, pts.ToArray());
        }

        // ───── 입 ─────
        /// <summary>정면 입. w = 입 가로 길이. buck = 앞니 두 개(스펀지밥·샌디).</summary>
        internal static void Mouth(Graphics g, Pose p, PointF c, float w, Pen ink, bool buck, bool upperTeeth, Color? inside = null)
        {
            var line = Pn(RU.InkColor, Math.Max(3.4f, ink.Width * .95f));
            float open = p.MouthOpen;
            Brush inner = inside.HasValue ? B(inside.Value) : MouthIn;
            switch (p.Mouth)
            {
                case MouthStyle.Neutral:
                    g.DrawArc(line, c.X - w * .24f, c.Y - w * .06f, w * .48f, w * .16f, 200, 140);
                    if (buck) Teeth(g, c.X, c.Y + w * .02f, w * .26f, w * .1f, ink);
                    break;
                case MouthStyle.Frown:
                    g.DrawArc(line, c.X - w * .3f, c.Y - w * .02f, w * .6f, w * .34f, 200, 140);
                    break;
                case MouthStyle.Pout:
                    g.DrawEllipse(line, c.X - w * .1f, c.Y - w * .03f, w * .2f, w * .14f);
                    break;
                case MouthStyle.Smile:
                    g.DrawArc(line, c.X - w * .5f, c.Y - w * .3f, w, w * .5f, 15, 150);
                    g.DrawLine(line, c.X - w * .5f + 2, c.Y - w * .02f, c.X - w * .5f - 4, c.Y - w * .1f);
                    g.DrawLine(line, c.X + w * .5f - 2, c.Y - w * .02f, c.X + w * .5f + 4, c.Y - w * .1f);
                    if (buck) Teeth(g, c.X, c.Y + w * .09f, w * .3f, w * .15f, ink);
                    break;
                case MouthStyle.Scared:
                {
                    float rw = w * (.2f + open * .08f), rh = w * (.12f + open * .16f);
                    g.FillEllipse(inner, c.X - rw, c.Y - w * .02f, rw * 2, rh * 2);
                    g.DrawEllipse(line, c.X - rw, c.Y - w * .02f, rw * 2, rh * 2);
                    if (buck) Teeth(g, c.X, c.Y - w * .02f, w * .26f, w * .1f, ink);
                    break;
                }
                case MouthStyle.Open:
                {
                    float hw = w * (.42f + .08f * open), h = w * (.14f + .5f * open);
                    var path = new GraphicsPath();
                    path.AddBezier(c.X - hw, c.Y - w * .06f, c.X - hw * .4f, c.Y + w * .03f, c.X + hw * .4f, c.Y + w * .03f, c.X + hw, c.Y - w * .06f);
                    path.AddBezier(c.X + hw, c.Y - w * .06f, c.X + hw * .9f, c.Y + h * 1.25f, c.X - hw * .9f, c.Y + h * 1.25f, c.X - hw, c.Y - w * .06f);
                    path.CloseFigure();
                    g.FillPath(inner, path);
                    var st = g.Save(); g.SetClip(path, CombineMode.Intersect);
                    float tl = Math.Min(h * .8f, w * (.2f + p.Tongue * .1f));
                    g.FillEllipse(Pink, c.X - hw * .55f, c.Y + h * .95f - tl, hw * 1.1f, tl * 1.5f);
                    if (upperTeeth) g.FillRectangle(White, c.X - hw, c.Y - w * .07f, hw * 2, w * .1f);
                    if (buck) Teeth(g, c.X, c.Y - w * .02f, w * .3f, w * .16f, ink);
                    g.Restore(st);
                    g.DrawPath(line, path); path.Dispose();
                    break;
                }
            }
            if (p.Tongue > .02f && p.Mouth != MouthStyle.Open)
            {
                float tl = w * (.14f + p.Tongue * .26f), tw = w * .18f;
                g.FillEllipse(Pink, c.X - tw / 2 + 2, c.Y + w * .02f, tw, tl);
                g.DrawEllipse(line, c.X - tw / 2 + 2, c.Y + w * .02f, tw, tl);
            }
        }

        private static void Teeth(Graphics g, float cx, float y, float tw, float th, Pen ink)
        {
            float w = tw / 2 - 1;
            var pn = Pn(RU.InkColor, Math.Max(2.2f, ink.Width * .7f));
            g.FillRectangle(White, cx - w - 1, y, w, th); g.FillRectangle(White, cx + 1, y, w, th);
            g.DrawRectangle(pn, cx - w - 1, y, w, th); g.DrawRectangle(pn, cx + 1, y, w, th);
        }

        // ───── 장식 ─────
        internal static void Tears(Graphics g, PointF eyeL, PointF eyeR, float t, float size, Color drop, bool streams)
        {
            for (int side = 0; side < 2; side++)
            {
                PointF e = side == 0 ? eyeL : eyeR; float dir = side == 0 ? -1 : 1;
                if (streams)
                {
                    var pen = Pn(Color.FromArgb(200, drop.R, drop.G, drop.B), size * 1.6f);
                    g.DrawLine(pen, e.X + dir * size * .4f, e.Y + size * 1.2f, e.X + dir * size * 2.4f, e.Y + size * (7 + 2 * (float)Math.Sin(t * 9 + side)));
                }
                for (int i = 0; i < 2; i++)
                {
                    float q = (t * 1.5f + i * .5f + side * .27f) % 1f;
                    float x = e.X + dir * size * (.8f + q * 2.2f), y = e.Y + size * (1.5f + q * 9f), r = size * (1.0f - .38f * q);
                    int a = (int)(235 * (1 - q * q));
                    g.FillEllipse(B(drop.R, drop.G, drop.B, a), x - r, y - r * 1.3f, r * 2, r * 2.6f);
                    g.DrawEllipse(Pn(Color.FromArgb(a, 60, 110, 180), Math.Max(2.4f, size * .3f)), x - r, y - r * 1.3f, r * 2, r * 2.6f);
                }
            }
        }

        internal static void Sweat(Graphics g, PointF at, float t, float s)
        {
            float q = t % 1.2f / 1.2f, x = at.X + 6 * q, y = at.Y + q * 40; int a = (int)(235 * (1 - q * q * q));
            var pts = new[] { new PointF(x, y - 2 * s), new PointF(x + s, y), new PointF(x, y + s * 1.2f), new PointF(x - s, y) };
            var path = new GraphicsPath(); path.AddCurve(pts, 0f); path.CloseFigure();
            g.FillPath(B(170, 214, 255, a), path); g.DrawPath(Pn(Color.FromArgb(a, 70, 120, 190), 3.2f), path); path.Dispose();
        }

        /// <summary>만화식 💢.</summary>
        internal static void Anger(Graphics g, PointF c, float s, float t)
        {
            float pulse = 1 + .12f * (float)Math.Sin(t * 40), z = s * pulse * Math.Min(1f, t * 1.2f + .2f);
            var pen = Pn(226, 56, 44, Math.Max(5f, s * .38f));
            var st = g.Save(); g.TranslateTransform(c.X, c.Y);
            g.DrawArc(pen, -z - 5, -z - 5, z + 2, z + 2, 100, 80); g.DrawArc(pen, 3, -z - 5, z + 2, z + 2, 280, 80);
            g.DrawArc(pen, -z - 5, 3, z + 2, z + 2, 80, -80); g.DrawArc(pen, 3, 3, z + 2, z + 2, 260, -80);
            g.Restore(st);
        }

        internal static void GogglesFace(Graphics g, float cx, float cy, float spacing, float r, float strapHalf)
        {
            g.DrawLine(Pn(60, 52, 56, 9f), cx - strapHalf, cy, cx + strapHalf, cy);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = cx + s * spacing;
                g.FillEllipse(B(160, 214, 236, 205), x - r, cy - r * .9f, r * 2, r * 1.8f);
                g.DrawEllipse(Pn(60, 52, 56, 5.5f), x - r, cy - r * .9f, r * 2, r * 1.8f);
                g.DrawArc(Pn(255, 255, 255, 4f, 230), x - r * .7f, cy - r * .65f, r * 1.0f, r * .9f, 190, 70);
            }
        }
    }

    /// <summary>캐릭터 하나를 그리는 리그. 좌표는 키 300 단위, 발 가운데가 원점, 왼쪽(−x)을 보는 모습.</summary>
    internal abstract class CharRig
    {
        /// <summary>머리 꼭대기 y(음수). 말풍선·장식의 기준.</summary>
        public float HeadTop = -290;
        /// <summary>가슴 높이 y(음수). 손을 모으는 곳·선물의 기준.</summary>
        public float ChestY = -170;
        /// <summary>앉았을 때 몸이 내려오는 양.</summary>
        public float SitDrop = 50;
        /// <summary>아이콘에 쓸 머리 영역(중심, 반 크기).</summary>
        public PointF IconCenter = new PointF(0, -220); public float IconHalf = 90;
        /// <summary>얼굴 중심(안경·장식 기준).</summary>
        public PointF Face = new PointF(0, -230);
        /// <summary>팔의 기본 자세(위팔·아래팔 각도). 포즈의 팔 각도는 이 값에 더해진다.</summary>
        public float RestA, RestB;

        public abstract void Draw(Graphics g, Pose p, Pen ink);

        protected static PointF P(float x, float y) { return new PointF(x, y); }
        /// <summary>가슴 기준 상대 좌표를 그림 좌표로.</summary>
        protected PointF? Abs(PointF? rel) { return rel.HasValue ? new PointF(rel.Value.X, rel.Value.Y + ChestY) : (PointF?)null; }
    }

    internal static class Rig
    {
        internal const float RefH = 300f;
        private static CharRig[] rigs;

        internal static CharRig Of(int id)
        {
            if (rigs == null) rigs = new CharRig[] { new SpongeRig(), new PatrickRig(), new SquidRig(), new SandyRig(), new KrabsRig(), new PlanktonRig() };
            return rigs[((id % rigs.Length) + rigs.Length) % rigs.Length];
        }

        /// <summary>g 의 원점이 발 가운데(픽셀)라고 가정하고 그린다. k = 단위 1이 화면 몇 픽셀인지.</summary>
        internal static void Draw(Graphics g, int id, Pose p, float k, bool faceRight)
        {
            var saved = g.Save();
            g.ScaleTransform(faceRight ? -k : k, k);
            RU.Mirrored = faceRight;
            float ow = Math.Max(3.0f, 1.25f / k);
            var ink = new Pen(RU.InkColor, ow) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            try { Of(id).Draw(g, p, ink); }
            finally { ink.Dispose(); RU.Mirrored = false; }
            g.Restore(saved);
        }
    }
}
