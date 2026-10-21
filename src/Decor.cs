using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    [Flags]
    internal enum Decor
    {
        None = 0, Rotor = 1, Stars = 8, Gift = 16, Notes = 64, Speed = 256, Sleepy = 512, Dots = 2048, Hobby = 4096
    }

    /// <summary>몸에 붙어 따라다니는 장식: 낙하 도구·선물·대표 동작 효과·생각 풍선·잠옷 등. 그림은 리그 단위로 그린다.</summary>
    internal static class DecorArt
    {
        private static PointF P(float x, float y) { return new PointF(x, y); }

        /// <summary>장식이 몸 상자 바깥으로 나가는 범위 (스프라이트 좌표, 발 가운데 원점).</summary>
        internal static RectangleF Extent(Pet.Look L, CharacterArt art)
        {
            float k = art.K, ht = art.Rig.HeadTop, l = 0, t = 0, r = 0;
            Action<float, float, float> add = (lu, ru, tu) => { l = Math.Min(l, lu * k); r = Math.Max(r, ru * k); t = Math.Min(t, tu * k); };
            if ((L.Decor & Decor.Rotor) != 0) add(-140, 140, ht - art.Info.RotorU - 24);
            if ((L.Decor & Decor.Dots) != 0) add(-30, 250, ht - 130);
            if ((L.Decor & Decor.Stars) != 0) add(-110, 110, ht - 28);
            if ((L.Decor & Decor.Gift) != 0) add(-90, 90, art.Rig.ChestY - 70);
            if ((L.Decor & Decor.Notes) != 0) add(-170, 170, ht - 40);
            if ((L.Decor & Decor.Sleepy) != 0) add(-110, 110, ht - 60);
            if ((L.Decor & Decor.Speed) != 0) { float hw = art.W * .5f + 28 * art.K; l = Math.Min(l, -hw - 20 * k); r = Math.Max(r, hw + 20 * k); }
            if ((L.Decor & Decor.Hobby) != 0) add(-240, 240, art.Id == 1 ? -430 : ht - 120);
            return new RectangleF(l, t, r - l, 0);
        }

        internal static void Draw(Graphics g, Pet.Look L, CharacterArt art)
        {
            if (L.Decor == Decor.None) return;
            float k = art.K, t = L.DecorPhase, sgn = L.FaceRight ? 1 : -1;
            var rig = art.Rig; int id = art.Id; float ht = rig.HeadTop;
            var st = g.Save(); g.ScaleTransform(k, k);
            float ow = Math.Max(2.6f, 1.2f / k);
            using (var ink = new Pen(RU.InkColor, ow) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                RU.Mirrored = false;
                if ((L.Decor & Decor.Sleepy) != 0) Sleepwear(g, id, rig, t, ink);
                if ((L.Decor & Decor.Speed) != 0) Speed(g, art, L.FaceRight, t);
                if ((L.Decor & Decor.Rotor) != 0) Glider(g, id, rig, t, ink);
                if ((L.Decor & Decor.Dots) != 0) Thought(g, id, rig, t, ink);
                if ((L.Decor & Decor.Stars) != 0)
                    for (int i = 0; i < 3; i++)
                    {
                        double a = t * 3 + i * Math.PI * 2 / 3;
                        float x = (float)Math.Cos(a) * 70, y = ht - 14 + (float)Math.Sin(a) * 14;
                        Star(g, x, y, 9, RU.B(250, 214, 80), ink);
                    }
                if ((L.Decor & Decor.Gift) != 0) Gift(g, id, rig, t, ink);
                if ((L.Decor & Decor.Notes) != 0)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * (112 + 10), y = -230 + (float)Math.Sin(t * 5 + side) * 12;
                        Note(g, x, y, 1.1f, RU.B(176, 111, 160), 255);
                    }
                if ((L.Decor & Decor.Hobby) != 0) Hobby(g, id, rig, L, t, sgn, ink);
            }
            g.Restore(st);
        }

        // ───────── 달릴 때 ─────────
        private static void Speed(Graphics g, CharacterArt art, bool faceRight, float t)
        {
            float side = faceRight ? -1 : 1, k = art.K, hw = art.W * .5f / k;
            for (int n = 0; n < 4; n++)
            {
                float p = (float)((t * 3.2 + n * .25) % 1), px = side * (hw * .8f + 6 + p * 40 + n * 6), py = -4 - p * 16;
                g.FillEllipse(RU.B(232, 224, 212, (int)(150 * (1 - p))), px - (4 + p * 7), py - (3.5f + p * 5), 8 + p * 14, 7 + p * 10);
            }
            for (int i = 0; i < 3; i++)
            {
                float y = -300 * (.22f + i * .26f), off = (float)((t * 210 + i * 27) % 36), x0 = side * (hw + 8 + off);
                g.DrawLine(RU.Pn(120, 120, 135, 5f, 190), x0, y, x0 + side * (24 + i * 9), y);
            }
        }

        // ───────── 낙하 도구 ─────────
        private static void Glider(Graphics g, int id, CharRig rig, float t, Pen ink)
        {
            float ht = rig.HeadTop;
            switch (id)
            {
                case 0:   // 스폰지밥 ─ 분홍 해파리가 머리 위에서 몸을 떠받친다
                {
                    float pulse = 1 + .1f * (float)Math.Sin(t * 6), cy = ht - 54;
                    var tent = RU.Pn(246, 150, 200, 4f, 200);
                    for (int i = -2; i <= 2; i++)
                    {
                        var pts = new List<PointF>();
                        for (int j = 0; j <= 8; j++) pts.Add(P(i * 26 + (float)Math.Sin(t * 5 + j * .8 + i) * 5 * j / 8f, cy + 6 + j * ((ht + 8 - cy - 6) / 8f)));
                        g.DrawCurve(tent, pts.ToArray());
                    }
                    Jelly(g, 0, cy, 1.4f, pulse);
                    break;
                }
                case 1:   // 뚱이 ─ 알록달록 프로펠러 모자
                {
                    float hy = ht - 4;
                    PointF[] cap = Geo.Smooth(new[] { P(-34, hy + 10), P(-26, hy - 12), P(0, hy - 20), P(26, hy - 12), P(34, hy + 10) }, true, 5);
                    var cols = new[] { Color.FromArgb(226, 60, 60), Color.FromArgb(80, 150, 230), Color.FromArgb(250, 210, 60), Color.FromArgb(100, 200, 100) };
                    for (int i = 0; i < 4; i++)
                    {
                        float x0 = -32 + i * 16;
                        g.FillPolygon(RU.B(cols[i]), new[] { P(x0, hy + 10), P(x0 + 16, hy + 10), P(x0 + 8 + (i - 1.5f) * -2, hy - 18) });
                    }
                    g.DrawPolygon(ink, cap);
                    float topY = hy - 22, hubY = topY - 40;
                    g.DrawLine(RU.Pn(70, 62, 66, 7f), 0, topY, 0, hubY); g.DrawLine(RU.Pn(150, 150, 162, 3f), 0, topY, 0, hubY);
                    float spread = (float)Math.Max(.12, Math.Abs(Math.Cos(t * 38))), rad = 96;
                    g.FillEllipse(RU.B(255, 236, 160, 40), -rad, hubY - rad * .16f, rad * 2, rad * .32f);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        g.DrawLine(RU.Pn(90, 80, 70, 10f, 160), 0, hubY, i * rad * spread, hubY);
                        g.DrawLine(RU.Pn(i < 0 ? Color.FromArgb(250, 210, 60) : Color.FromArgb(226, 60, 60), 7f), 0, hubY, i * rad * spread, hubY);
                    }
                    g.FillEllipse(RU.B(165, 165, 176), -6, hubY - 6, 12, 12);
                    break;
                }
                case 2:   // 징징이 ─ 시무룩한 검정 우산
                {
                    float cy = ht - 58, sway = (float)Math.Sin(t * 2.4) * 2;
                    var st = RU.Rot(g, 0, ht, sway);
                    g.DrawLine(RU.Pn(70, 56, 44, 5f), 0, cy, 0, ht + 6);
                    PointF[] can = Geo.Smooth(new[] { P(-120, cy + 6), P(-96, cy - 30), P(-50, cy - 50), P(0, cy - 56), P(50, cy - 50), P(96, cy - 30), P(120, cy + 6) }, false, 6);
                    var poly = new List<PointF>(can);
                    for (int i = 6; i >= -6; i--) poly.Add(P(i * 20, cy + 6 + ((i & 1) == 0 ? 0 : 9)));
                    g.FillPolygon(RU.B(58, 70, 88), poly.ToArray()); g.DrawPolygon(ink, poly.ToArray());
                    var rib = RU.Pn(30, 38, 52, 2.4f);
                    for (int i = -2; i <= 2; i++) g.DrawLine(rib, i * 42, cy + 4, i * 12, cy - 54);
                    g.DrawLine(RU.Pn(60, 56, 56, 4f), 0, cy - 56, 0, cy - 68);
                    g.Restore(st);
                    break;
                }
                case 3:   // 다람이 ─ 머리 위에서 도는 올가미
                {
                    float cy = ht - 52, w = 20 + 92 * (float)Math.Abs(Math.Cos(t * 12));
                    g.DrawLine(RU.Pn(214, 170, 100, 5f), 34, ht + 18, 26, cy);
                    g.DrawEllipse(RU.Pn(214, 170, 100, 5.2f), 26 - w, cy - 16, w * 2, 32);
                    g.DrawEllipse(RU.Pn(214, 170, 100, 2.6f, 130), 26 - w * .82f, cy - 11, w * 1.64f, 22);
                    g.FillEllipse(RU.B(176, 126, 60), 20, cy - 6, 12, 12);
                    break;
                }
                case 4:   // 집게사장 ─ 달러 지폐 낙하산
                {
                    float cy = ht - 54;
                    var poly = new List<PointF>(Geo.Smooth(new[] { P(-128, cy + 8), P(-100, cy - 32), P(-50, cy - 54), P(0, cy - 60), P(50, cy - 54), P(100, cy - 32), P(128, cy + 8) }, false, 6));
                    for (int i = 6; i >= -6; i--) poly.Add(P(i * 21.3f, cy + 8 + ((i & 1) == 0 ? 0 : 9)));
                    g.FillPolygon(RU.B(150, 208, 130), poly.ToArray()); g.DrawPolygon(ink, poly.ToArray());
                    g.DrawEllipse(RU.Pn(84, 144, 72, 3f), -34, cy - 50, 68, 46);
                    RU.Text(g, "$", 0, cy - 28, 40, RU.B(70, 124, 60));
                    var s = RU.Pn(150, 130, 90, 2.2f);
                    g.DrawLine(s, -124, cy + 10, -74, -158); g.DrawLine(s, -60, cy + 10, -70, -160); g.DrawLine(s, 60, cy + 10, 70, -160); g.DrawLine(s, 124, cy + 10, 74, -158);
                    break;
                }
                default:  // 플랑크톤 ─ 음모의 자주색 프로펠러
                {
                    float topY = ht - 4, hubY = topY - 52;
                    g.DrawLine(RU.Pn(60, 50, 70, 7f), 0, topY, 0, hubY);
                    float spread = (float)Math.Max(.12, Math.Abs(Math.Cos(t * 42))), rad = 90;
                    g.FillEllipse(RU.B(190, 120, 230, 50), -rad, hubY - rad * .16f, rad * 2, rad * .32f);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        g.DrawLine(RU.Pn(40, 20, 50, 10f, 170), 0, hubY, i * rad * spread, hubY);
                        g.DrawLine(RU.Pn(150, 60, 190, 6.4f), 0, hubY, i * rad * spread, hubY);
                    }
                    g.FillEllipse(RU.B(226, 54, 44), -7, hubY - 7, 14, 14);
                    break;
                }
            }
        }

        private static void Jelly(Graphics g, float cx, float cy, float size, float pulse)
        {
            float w = 54 * size, h = 34 * size * pulse;
            using (var path = new GraphicsPath())
            {
                path.AddArc(cx - w, cy - h, w * 2, h * 2, 180, 180);
                path.AddCurve(new[] { P(cx + w, cy), P(cx + w * .5f, cy + 7 * size), P(cx, cy), P(cx - w * .5f, cy + 7 * size), P(cx - w, cy) });
                path.CloseFigure();
                g.FillPath(RU.B(250, 150, 206, 215), path); g.DrawPath(RU.Pn(200, 90, 150, 2.8f), path);
            }
            for (int i = -1; i <= 1; i++) g.FillEllipse(RU.B(232, 110, 176, 200), cx + i * w * .45f - 4.5f * size, cy - h * .55f, 9 * size, 7 * size);
            g.DrawArc(RU.Pn(255, 255, 255, 3f, 200), cx - w * .7f, cy - h * .8f, w, h, 200, 50);
        }

        // ───────── 선물(친밀도 100 단위) ─────────
        private static void Gift(Graphics g, int id, CharRig rig, float t, Pen ink)
        {
            float cy = rig.ChestY + 4;
            var thin = RU.Pn(RU.InkColor, Math.Max(2.4f, ink.Width * .8f));
            float bob = (float)Math.Sin(t * 3.2) * 2;
            var st = g.Save(); g.TranslateTransform(-2, cy + bob);
            switch (id)
            {
                case 0:   // 스폰지밥: 바닷속 꽃다발
                    Bouquet(g, ink, new[] { Color.FromArgb(245, 150, 180), Color.FromArgb(180, 160, 240), Color.FromArgb(120, 200, 230) });
                    break;
                case 1:   // 뚱이: 커다란 아이스크림
                    g.ScaleTransform(1.5f, 1.5f); g.TranslateTransform(0, -10); Props.DrawFoodItem(g, 1, ink);
                    break;
                case 2:   // 징징이: 자화상 액자
                {
                    RU.FillOutline(g, new[] { P(-30, -34), P(30, -34), P(30, 26), P(-30, 26) }, RU.B(168, 116, 60), thin);
                    RU.FillOutline(g, new[] { P(-24, -28), P(24, -28), P(24, 20), P(-24, 20) }, RU.B(238, 232, 214), thin);
                    RU.Ell(g, RU.B(170, 212, 204), -2, -10, 12, 12, thin);
                    g.FillRectangle(RU.B(170, 212, 204), -8, 0, 12, 18);
                    RU.LimbFill(g, new[] { P(-6, -10), P(-12, 2) }, new[] { 6f, 5f }, true, RU.B(170, 212, 204), thin);
                    break;
                }
                case 3:   // 다람이: 도토리 꽃다발
                {
                    var leaf = RU.B(110, 170, 80);
                    for (int i = -2; i <= 2; i++) { var s = g.Save(); g.RotateTransform(i * 22); g.FillEllipse(leaf, -8, -50, 16, 34); g.DrawEllipse(thin, -8, -50, 16, 34); g.Restore(s); }
                    for (int i = -1; i <= 1; i++) { var s = g.Save(); g.TranslateTransform(i * 20, -34 + Math.Abs(i) * 6); Props.DrawFoodItem(g, 3, ink); g.Restore(s); }
                    PointF[] wrap = { P(-22, -8), P(22, -8), P(0, 30) };
                    RU.FillOutline(g, wrap, RU.B(255, 236, 214), thin);
                    break;
                }
                case 4:   // 집게사장: 번쩍이는 금화
                {
                    g.ScaleTransform(1.9f, 1.9f); g.TranslateTransform(0, -8); Props.DrawFoodItem(g, 4, ink);
                    float s = 1 + .25f * (float)Math.Sin(t * 8);
                    Star(g, 26, -26, 7 * s, RU.B(255, 250, 200), thin); Star(g, -28, -18, 5 * (2 - s), RU.B(255, 250, 200), thin);
                    break;
                }
                default:  // 플랑크톤: 리본 맨 두루마리(비밀 레시피?)
                {
                    PointF[] sheet = { P(-26, -30), P(26, -34), P(28, 24), P(-24, 28) };
                    RU.FillOutline(g, sheet, RU.B(246, 232, 190), thin);
                    g.DrawLine(RU.Pn(130, 100, 60, 1.8f), -16, -20, 16, -22); g.DrawLine(RU.Pn(130, 100, 60, 1.8f), -16, -10, 14, -12); g.DrawLine(RU.Pn(130, 100, 60, 1.8f), -16, 0, 8, -2);
                    g.DrawLine(RU.Pn(214, 52, 56, 5f), -28, -4, 30, -6);
                    g.FillEllipse(RU.B(214, 52, 56), -6, -12, 12, 12);
                    break;
                }
            }
            g.Restore(st);
        }

        private static void Bouquet(Graphics g, Pen ink, Color[] cols)
        {
            var thin = RU.Pn(RU.InkColor, Math.Max(2.4f, ink.Width * .8f));
            var stem = RU.Pn(90, 141, 97, 3.4f);
            for (int j = -1; j <= 1; j++) g.DrawLine(stem, -3, 28, j * 22, -16 - (j == 0 ? 14 : 0));
            PointF[] wrap = { P(-30, 6), P(26, 6), P(-2, 44) };
            RU.FillOutline(g, wrap, RU.B(255, 224, 231), thin);
            for (int j = -1; j <= 1; j++)
            {
                float fx = j * 22, fy = -18 - (j == 0 ? 14 : 0);
                for (int i = 0; i < 5; i++)
                {
                    double a = i * Math.PI * 2 / 5 - Math.PI / 2;
                    float px = fx + (float)Math.Cos(a) * 9.5f, py = fy + (float)Math.Sin(a) * 9.5f;
                    g.FillEllipse(RU.B(cols[j + 1]), px - 8, py - 8, 16, 16); g.DrawEllipse(thin, px - 8, py - 8, 16, 16);
                }
                g.FillEllipse(RU.B(255, 218, 117), fx - 7, fy - 7, 14, 14); g.DrawEllipse(thin, fx - 7, fy - 7, 14, 14);
            }
        }

        // ───────── 생각 풍선(멍때리기) ─────────
        private static void Thought(Graphics g, int id, CharRig rig, float t, Pen ink)
        {
            float ht = rig.HeadTop;
            float[] radius = { 5f, 7.5f, 11f }, phase = { 0f, .75f, 1.5f };
            PointF[] at = { P(56, ht + 38), P(80, ht + 20), P(106, ht + 2) };
            var edge = RU.Pn(110, 104, 120, 2.2f);
            for (int i = 0; i < 3; i++)
            {
                if (t < phase[i]) continue;
                float cycle = (((t - phase[i]) % 3.6f) + 3.6f) % 3.6f;
                int alpha = (int)(255 * Math.Max(0, Math.Min(1, Math.Sin(cycle / 3.6f * Math.PI))));
                if (alpha <= 0) continue;
                float r = radius[i];
                g.FillEllipse(RU.B(255, 255, 255, alpha), at[i].X - r, at[i].Y - r, r * 2, r * 2); g.DrawEllipse(RU.Pn(Color.FromArgb(alpha, 110, 104, 120), 2.2f), at[i].X - r, at[i].Y - r, r * 2, r * 2);
            }
            // 구름 + 안의 그림: 점이 다 나오는 1.7초 뒤부터
            float c = (t % 3.6f); if (c < 1.7f) return;
            float a2 = (float)Math.Min(1, (c - 1.7f) * 3) * (float)Math.Min(1, (3.6f - c) * 3);
            if (a2 <= 0.02f) return;
            int al = (int)(255 * a2);
            PointF center = P(166, ht - 58);
            var cloud = new[] { new[] { -42f, 6f, 30f, 24f }, new[] { -14f, -16f, 34f, 28f }, new[] { 20f, -12f, 32f, 26f }, new[] { 38f, 8f, 28f, 22f }, new[] { 0f, 14f, 44f, 24f } };
            foreach (var cl in cloud) g.FillEllipse(RU.B(255, 255, 255, al), center.X + cl[0] - cl[2], center.Y + cl[1] - cl[3], cl[2] * 2, cl[3] * 2);
            foreach (var cl in cloud) g.DrawArc(RU.Pn(Color.FromArgb(al, 110, 104, 120), 2.4f), center.X + cl[0] - cl[2], center.Y + cl[1] - cl[3], cl[2] * 2, cl[3] * 2, 0, 360);
            foreach (var cl in cloud) g.FillEllipse(RU.B(255, 255, 255, al), center.X + cl[0] - cl[2] + 3, center.Y + cl[1] - cl[3] + 3, cl[2] * 2 - 6, cl[3] * 2 - 6);
            var st = g.Save(); g.TranslateTransform(center.X, center.Y + 4);
            float wob = (float)Math.Sin(t * 3) * 3; g.TranslateTransform(0, wob);
            switch (id)
            {
                case 0: Jelly(g, 0, 4, .9f, 1 + .1f * (float)Math.Sin(t * 6)); break;
                case 1: g.TranslateTransform(0, -4); Props.DrawFoodItem(g, 1, ink); break;
                case 2: Note(g, -14, 10, 1.4f, RU.B(60, 80, 110), 255); Note(g, 16, -2, 1.1f, RU.B(60, 80, 110), 255); break;
                case 3: g.ScaleTransform(1.2f, 1.2f); Props.DrawFoodItem(g, 3, ink); break;
                case 4: g.ScaleTransform(1.3f, 1.3f); Props.DrawFoodItem(g, 4, ink); break;
                default: g.ScaleTransform(1.1f, 1.1f); Props.DrawFoodItem(g, 0, ink); break;
            }
            g.Restore(st);
        }

        // ───────── 잠 ─────────
        private static void Sleepwear(Graphics g, int id, CharRig rig, float t, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, Math.Max(2.4f, ink.Width * .8f));
            switch (id)
            {
                case 0:   // 스폰지밥: 나이트캡
                    Nightcap(g, -78, 84, -296, RU.B(168, 200, 250), thin, 1);
                    break;
                case 1:   // 뚱이: 코에서 부푸는 콧방울
                {
                    float r = 8 + 10 * (.5f + .5f * (float)Math.Sin(t * 1.7));
                    float cx = -44, cy = -214 + 4;
                    g.FillEllipse(RU.B(200, 232, 255, 140), cx - r, cy - r, r * 2, r * 2);
                    g.DrawEllipse(RU.Pn(120, 170, 230, 2.4f), cx - r, cy - r, r * 2, r * 2);
                    g.DrawArc(RU.Pn(255, 255, 255, 2.6f, 230), cx - r * .6f, cy - r * .6f, r * 1.1f, r * 1.1f, 200, 60);
                    break;
                }
                case 2:   // 징징이: 보랏빛 나이트캡
                    Nightcap(g, -50, 56, -301, RU.B(150, 124, 204), thin, 1);
                    break;
                case 3:   // 다람이: 헬멧 속으로 올라가는 방울
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float q = (t * .5f + i * .33f) % 1f, x = -14 + i * 14 + (float)Math.Sin(q * 6 + i) * 4, y = -216 - q * 52, r = 3 + (1 - q) * 3;
                        g.DrawEllipse(RU.Pn(255, 255, 255, 2.2f, (int)(230 * (1 - q * q))), x - r, y - r, r * 2, r * 2);
                    }
                    break;
                }
                case 4:   // 집게사장: 돈주머니를 끌어안고 잔다
                {
                    PointF[] sack = Geo.Smooth(new[] { P(70, -4), P(60, -34), P(74, -58), P(100, -58), P(116, -34), P(108, -4) }, true, 5);
                    RU.FillOutline(g, sack, RU.B(176, 140, 80), thin);
                    g.DrawLine(RU.Pn(120, 90, 40, 5f), 78, -56, 100, -56);
                    RU.Text(g, "$", 88, -30, 30, RU.B(244, 220, 120));
                    break;
                }
                default:  // 플랑크톤: 빨간 나이트캡
                    Nightcap(g, -44, 44, -190, RU.B(220, 90, 96), thin, 1);
                    break;
            }
        }

        private static void Nightcap(Graphics g, float x0, float x1, float topY, Brush fill, Pen thin, int dir)
        {
            float sway = 0;
            PointF[] cap = Geo.Smooth(new[] { P(x0, topY + 6), P(x0 + 20, topY - 14), P(x1 - 26, topY - 28 + sway), P(x1 + 18, topY + 4), P(x1 + 36, topY + 34), P(x1 + 14, topY + 40), P(x1 - 14, topY + 8), P(x1, topY + 12), P(x0, topY + 14) }, true, 5);
            RU.FillOutline(g, cap, fill, thin);
            g.DrawLine(RU.Pn(255, 255, 255, 7f), x0 - 2, topY + 10, x1 + 2, topY + 10);
            RU.Ell(g, RU.White, x1 + 34, topY + 44, 9, 9, thin);
        }

        // ───────── 대표 동작 효과 ─────────
        private static void Hobby(Graphics g, int id, CharRig rig, Pet.Look L, float t, float sgn, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, Math.Max(2.4f, ink.Width * .8f));
            switch (id)
            {
                case 0:
                    if (L.Variant == 0)
                    {
                        // 해파리가 둥실둥실, 그물이 지나가면 반짝!
                        float jx = sgn * (118 + 46 * (float)Math.Sin(t * 1.3)), jy = -190 + 40 * (float)Math.Sin(t * 2.1 + 1);
                        float catchT = t % 2.6f;
                        if (catchT < .35f) Star(g, jx, jy - 8, 14 * (float)Math.Sin(catchT / .35f * Math.PI), RU.B(255, 246, 170), thin);
                        else Jelly(g, jx, jy, 1.0f, 1 + .12f * (float)Math.Sin(t * 6));
                    }
                    else
                    {
                        // 비눗방울 막대: 큰 방울이 부풀어 떠오른다
                        for (int i = 0; i < 4; i++)
                        {
                            float q = ((t * .45f + i * .25f) % 1f);
                            float r = 6 + q * 30, x = sgn * (76 + q * 120) + (float)Math.Sin(q * 9 + i) * 10, y = -178 - q * 110;
                            int a = (int)(220 * (1 - q * q));
                            g.FillEllipse(RU.B(200, 230, 255, a / 3), x - r, y - r, r * 2, r * 2);
                            g.DrawEllipse(RU.Pn(Color.FromArgb(a, 120, 170, 240), 2.6f), x - r, y - r, r * 2, r * 2);
                            g.DrawArc(RU.Pn(Color.FromArgb(a, 255, 255, 255), 2.4f), x - r * .65f, y - r * .65f, r * 1.2f, r * 1.2f, 200, 60);
                            g.DrawArc(RU.Pn(Color.FromArgb(a / 2, 255, 150, 220), 2.2f), x - r * .8f, y - r * .8f, r * 1.6f, r * 1.6f, 20, 70);
                        }
                    }
                    break;
                case 1:
                    if (L.Aux > .02f)
                    {
                        // 바위 밑에 쏙: 위에서 떨어져 덮고, 끝나면 들려 올라간다
                        float cover = L.Aux, drop = (1 - cover) * 240;
                        var st = g.Save(); g.TranslateTransform(0, -drop);
                        float wob = cover > .95f ? (float)Math.Sin(t * 7) * 1.6f : 0;
                        RU.Rot(g, 0, 0, wob);
                        PointF[] rock = Geo.Smooth(new[] { P(-100, 2), P(-104, -56), P(-84, -138), P(-34, -192), P(30, -198), P(84, -150), P(104, -80), P(100, 2) }, true, 6);
                        using (var br = new LinearGradientBrush(new PointF(-100, -200), new PointF(100, 0), Color.FromArgb(150, 142, 134), Color.FromArgb(92, 88, 86)))
                            g.FillPolygon(br, rock);
                        g.DrawPolygon(ink, rock);
                        g.DrawLine(RU.Pn(70, 66, 66, 2.8f), -40, -150, -22, -112); g.DrawLine(RU.Pn(70, 66, 66, 2.8f), -22, -112, -34, -80);
                        g.DrawLine(RU.Pn(70, 66, 66, 2.8f), 42, -172, 56, -128);
                        foreach (var m in new[] { new[] { -60f, -110f }, new[] { 60f, -80f }, new[] { 10f, -170f }, new[] { -76f, -40f } }) g.FillEllipse(RU.B(112, 156, 96, 200), m[0] - 7, m[1] - 4, 14, 8);
                        g.Restore(st);
                        if (cover > .95f && (t % 3f) > 1.2f && (t % 3f) < 1.9f)
                        {   // 틈으로 엿보는 눈
                            RU.Text(g, "…", 0, -216, 34, RU.B(250, 250, 250));
                        }
                    }
                    break;
                case 2:
                {
                    // 클라리넷 소리: 삐걱대는 음표
                    for (int i = 0; i < 4; i++)
                    {
                        float q = (t * .5f + i * .25f) % 1f, x = sgn * (110 + q * 70) + (float)Math.Sin(q * 7 + i * 2) * 12, y = -150 - q * 140;
                        int a = (int)(255 * Math.Min(1, (1 - q) * 3) * Math.Min(1, q * 8));
                        Note(g, x, y, 1.15f, RU.B(i % 2 == 0 ? Color.FromArgb(120, 150, 90) : Color.FromArgb(70, 96, 140)), a);
                        if (i % 2 == 1) RU.Text(g, "♭", x - 18, y - 4, 22, RU.B(120, 150, 90, a));
                    }
                    break;
                }
                case 3:
                {
                    // 가라테: 휙휙 베는 바람 + 부딪힘 번쩍
                    float ph = t % 1.2f;
                    int swing = (int)(t / 1.2f) % 2;
                    float s2 = swing == 0 ? sgn : -sgn;
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Math.Max(0, 1 - Math.Abs(ph - .35f) * 3.5f) * (1 - i * .25f);
                        if (a <= 0) continue;
                        g.DrawArc(RU.Pn(255, 255, 255, 5f - i, (int)(210 * a)), s2 * 100 - 62 - i * 10, -250 + i * 16, 124 + i * 20, 130 - i * 8, s2 > 0 ? 100 : -80, s2 > 0 ? 100 : 100);
                    }
                    if (ph > .28f && ph < .5f) Star(g, s2 * 120, -190, 16 * (float)Math.Sin((ph - .28f) / .22f * Math.PI), RU.B(255, 236, 130), thin);
                    break;
                }
                case 4:
                {
                    // 돈 세기: 금화가 튀어 오르고, 바닥에 금화 더미
                    int pile = Math.Min(8, 2 + (int)(t * 1.2f));
                    for (int i = 0; i < pile; i++) RU.Ell(g, RU.B(246, 204, 70), -sgn * (86 - (i % 3) * 14) + 0, -4 - (i / 3) * 8, 17, 6, thin);
                    for (int i = 0; i < 3; i++)
                    {
                        float q = (t * .9f + i * .33f) % 1f, x = sgn * (60 + i * 26) + (float)Math.Sin(i * 3) * 6, y = -190 - (float)Math.Sin(q * Math.PI) * 110;
                        float flip = (float)Math.Abs(Math.Cos(t * 9 + i * 2));
                        var st = g.Save(); g.TranslateTransform(x, y); g.ScaleTransform(Math.Max(.12f, flip) * .8f, .8f); Props.DrawFoodItem(g, 4, ink); g.Restore(st);
                    }
                    RU.Text(g, "$", sgn * -60, -250 + 6 * (float)Math.Sin(t * 5), 34, RU.B(250, 210, 60, 230));
                    break;
                }
                default:
                {
                    // 음모: 번쩍이는 번개 + 보랏빛 기운
                    int flash = ((int)(t * 7)) % 3;
                    g.FillEllipse(RU.B(150, 70, 200, 40), -90, -270, 180, 280);
                    if (flash != 1)
                        foreach (int sd in new[] { -1, 1 })
                        {
                            float bx = sd * (96 + flash * 10);
                            PointF[] bolt = { P(bx, -300), P(bx - 18, -210), P(bx + 2, -214), P(bx - 22, -120), P(bx + 14, -196), P(bx - 4, -198), P(bx + 16, -300) };
                            g.FillPolygon(RU.B(255, 238, 90), bolt); g.DrawPolygon(RU.Pn(180, 120, 20, 2.4f), bolt);
                        }
                    break;
                }
            }
        }

        // ───────── 작은 도형 ─────────
        internal static void Star(Graphics g, float x, float y, float r, Brush fill, Pen edge)
        {
            if (r < .5f) return;
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? r : r * .45f; pts[i] = P(x + (float)Math.Cos(a) * rr, y + (float)Math.Sin(a) * rr); }
            g.FillPolygon(fill, pts); g.DrawPolygon(edge, pts);
        }

        internal static void Note(Graphics g, float x, float y, float s, Brush b, int alpha)
        {
            var c = ((SolidBrush)b).Color; var col = Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
            using (var br = new SolidBrush(col)) using (var pen = new Pen(col, 3f * s))
            {
                g.FillEllipse(br, x - 7 * s, y, 14 * s, 10 * s);
                g.DrawLine(pen, x + 6 * s, y + 4 * s, x + 6 * s, y - 26 * s); g.DrawLine(pen, x + 6 * s, y - 26 * s, x + 18 * s, y - 20 * s);
            }
        }
    }
}
