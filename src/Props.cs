using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>손에 쥐는 소품과 간식. 모두 리그 단위(키 300)로, 리그가 그리는 도중에 불린다.</summary>
    internal static class Props
    {
        private static PointF P(float x, float y) { return new PointF(x, y); }

        /// <summary>팔과 몸을 다 그린 뒤: 앞손의 소품과 가슴 앞 간식을 얹는다.</summary>
        internal static void AfterArms(Graphics g, int id, Pose p, PointF wrist, PointF dir, Pen ink, PointF foodAt)
        {
            if (p.Prop != Prop.None)
            {
                var st = g.Save();
                g.TranslateTransform(wrist.X, wrist.Y);
                if (p.Prop != Prop.Lasso) g.RotateTransform((float)(Math.Atan2(dir.Y, dir.X) * 180 / Math.PI) + p.PropRot);
                Held(g, p.Prop, p.PropT, ink);
                g.Restore(st);
            }
            if (p.Food >= 0) Food(g, id, foodAt, p.Food, ink);
        }

        // 손목에서 +x 방향이 손이 뻗는 쪽. 쥔 자리는 (10,0) 근처.
        private static void Held(Graphics g, Prop prop, float t, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            switch (prop)
            {
                case Prop.Net:   // 해파리 잡이 그물
                {
                    g.DrawLine(RU.Pn(120, 78, 36, 8f), -30, 0, 126, 0);
                    g.DrawLine(RU.Pn(190, 140, 78, 4f), -30, -1, 126, -1);
                    PointF[] bag = Geo.Smooth(new[] { P(138, -36), P(190, -30), P(214, 0), P(190, 30), P(138, 36) }, false, 6);
                    var bagPoly = new System.Collections.Generic.List<PointF>(bag); bagPoly.Add(P(138, -36));
                    g.FillPolygon(RU.B(190, 232, 200, 120), bagPoly.ToArray());
                    var mesh = RU.Pn(120, 160, 120, 1.8f, 200);
                    for (int i = -2; i <= 2; i++) g.DrawLine(mesh, 140, i * 14, 206 - Math.Abs(i) * 8, i * 8);
                    for (int i = 1; i <= 3; i++) g.DrawLine(mesh, 140 + i * 16, -30 + i * 4, 140 + i * 16, 30 - i * 4);
                    g.DrawPolygon(RU.Pn(150, 110, 70, 2.4f), bagPoly.ToArray());
                    g.DrawEllipse(RU.Pn(120, 78, 36, 7f), 124, -38, 28, 76);
                    break;
                }
                case Prop.Wand:  // 비눗방울 막대
                {
                    g.DrawLine(RU.Pn(236, 110, 170, 6f), -22, 0, 52, 0);
                    g.FillEllipse(RU.B(210, 235, 255, 70), 46, -26, 52, 52);
                    g.DrawEllipse(RU.Pn(236, 110, 170, 5f), 46, -26, 52, 52);
                    break;
                }
                case Prop.Spatula:
                {
                    g.DrawLine(RU.Pn(70, 56, 52, 7f), -22, 0, 52, 0);
                    PointF[] head = { P(50, -16), P(92, -16), P(92, 16), P(50, 16) };
                    RU.FillOutline(g, head, RU.B(196, 200, 212), thin);
                    for (int i = 0; i < 3; i++) g.FillEllipse(RU.B(120, 124, 140), 62 + i * 10, -4, 5, 8);
                    // 뒤집히는 크러스티 버거
                    float up = (float)Math.Abs(Math.Sin(t * 3.1)) * 70, rot = (float)(Math.Sin(t * 3.1) > 0 ? t * 3.1 % 6.28 * 60 : 0);
                    var st = g.Save(); g.TranslateTransform(74, -26 - up); g.RotateTransform(rot);
                    RU.Ell(g, RU.B(112, 66, 38), 0, 0, 24, 8, thin);
                    g.Restore(st);
                    break;
                }
                case Prop.Clarinet:
                {
                    var tube = RU.Pn(34, 32, 38, 11f);
                    g.DrawLine(tube, -8, 0, 98, 0);
                    PointF[] bell = { P(96, -6), P(120, -16), P(120, 16), P(96, 6) };
                    RU.FillOutline(g, bell, RU.B(34, 32, 38), thin);
                    g.FillRectangle(RU.B(226, 226, 230), -22, -4, 16, 8);
                    for (int i = 0; i < 5; i++) { g.FillEllipse(RU.B(220, 220, 226), 14 + i * 16, -3, 6, 6); }
                    g.DrawLine(RU.Pn(200, 200, 210, 2.4f), 6, -9, 88, -9);
                    break;
                }
                case Prop.Lasso:  // 위로 도는 올가미(손목 위로 올라간다)
                {
                    var rope = RU.Pn(214, 170, 100, 4.4f);
                    g.DrawLine(rope, 0, -4, 6, -44);
                    float w = 20 + 70 * (float)Math.Abs(Math.Cos(t * 12));
                    g.DrawEllipse(RU.Pn(214, 170, 100, 4.4f), 6 - w, -70, w * 2, 28);
                    g.DrawEllipse(RU.Pn(214, 170, 100, 2.2f, 120), 6 - w * .8f, -66, w * 1.6f, 20);
                    break;
                }
                case Prop.Cash:
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        var st = g.Save(); g.TranslateTransform(22, 0); g.RotateTransform(i * 14 + (float)Math.Sin(t * 8 + i) * 3);
                        PointF[] bill = { P(-4, -12), P(34, -12), P(34, 12), P(-4, 12) };
                        RU.FillOutline(g, bill, RU.B(148, 206, 128), thin);
                        g.DrawEllipse(RU.Pn(80, 140, 70, 2f), 8, -7, 14, 14);
                        RU.Text(g, "$", 15, 0, 14, RU.B(60, 110, 50));
                        g.Restore(st);
                    }
                    break;
                }
                case Prop.Blueprint:
                {
                    PointF[] sheet = { P(8, -22), P(52, -26), P(54, 20), P(10, 24) };
                    RU.FillOutline(g, sheet, RU.B(138, 186, 240), thin);
                    var wl = RU.Pn(255, 255, 255, 1.8f, 230);
                    g.DrawEllipse(wl, 20, -16, 16, 16); g.DrawLine(wl, 38, -14, 48, -16); g.DrawLine(wl, 14, 6, 48, 4); g.DrawLine(wl, 14, 14, 40, 12);
                    g.DrawLine(RU.Pn(255, 255, 255, 1.6f, 200), 28, -8, 28, -2);
                    break;
                }
                case Prop.Brush:
                {
                    g.DrawLine(RU.Pn(150, 104, 56, 6f), -10, 0, 62, 0);
                    g.DrawLine(RU.Pn(180, 184, 196, 8f), 62, 0, 74, 0);
                    g.DrawLine(RU.Pn(220, 70, 60, 9f), 74, 0, 90, 0);
                    break;
                }
            }
        }

        // ───────── 간식 ─────────
        /// <summary>종류: 0 크러스티 버거 · 1 아이스크림 · 2 피자 · 3 도토리 · 4 금화 · 5 버거(몰래).</summary>
        internal static int FoodKind(int id) { return id == 5 ? 0 : id; }

        internal static void Food(Graphics g, int id, PointF at, float bite, Pen ink)
        {
            bite = Math.Max(0, Math.Min(1, bite));
            var st = g.Save();
            g.TranslateTransform(at.X, at.Y);
            float sc = 1.25f - .5f * bite; g.ScaleTransform(sc, sc);
            if (bite > .03f)
            {
                using (var path = new GraphicsPath(FillMode.Winding))
                {
                    path.AddEllipse(-34 - 4, -10 - 18 * bite, 24 * bite + 14 * bite, 24 * bite + 14 * bite);
                    path.AddEllipse(-6 - 14 * bite, -34 - 8 * bite, 20 * bite + 12 * bite, 20 * bite + 12 * bite);
                    path.AddEllipse(-30 - 6, 4 + 10 * bite, 18 * bite + 14 * bite, 18 * bite + 14 * bite);
                    path.AddEllipse(-12, -26, 30 * bite * bite, 26 * bite * bite);
                    g.SetClip(path, CombineMode.Exclude);
                }
            }
            DrawFoodItem(g, FoodKind(id), ink);
            g.Restore(st);
        }

        internal static void DrawFoodItem(Graphics g, int kind, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, Math.Max(2.4f, ink.Width * .8f));
            switch (kind)
            {
                case 0:   // 크러스티 버거
                {
                    RU.FillOutline(g, Geo.Smooth(new[] { P(-24, 8), P(0, 11), P(24, 8), P(22, 18), P(0, 21), P(-22, 18) }, true, 4), RU.B(232, 172, 92), thin);
                    RU.FillOutline(g, new[] { P(-26, -2), P(26, -2), P(26, 8), P(-26, 8) }, RU.B(112, 66, 38), thin);
                    PointF[] let = { P(-28, -6), P(-18, -1), P(-8, -6), P(2, -1), P(12, -6), P(22, -1), P(28, -6), P(28, -2), P(-28, -2) };
                    RU.FillOutline(g, let, RU.B(128, 198, 74), thin);
                    RU.FillOutline(g, new[] { P(-22, -10), P(22, -10), P(22, -6), P(-22, -6) }, RU.B(224, 62, 48), thin);
                    RU.FillOutline(g, new[] { P(-24, -8), P(-8, -4), P(-16, 2) }, RU.B(252, 212, 60), thin);
                    PointF[] top = Geo.Smooth(new[] { P(-25, -10), P(-20, -24), P(0, -30), P(20, -24), P(25, -10) }, true, 5);
                    RU.FillOutline(g, top, RU.B(238, 174, 86), thin);
                    foreach (var s in new[] { new[] { -10f, -22f }, new[] { 2f, -25f }, new[] { 12f, -20f }, new[] { -2f, -16f } }) g.FillEllipse(RU.B(252, 236, 200), s[0] - 2.4f, s[1] - 1.4f, 5, 3);
                    break;
                }
                case 1:   // 아이스크림
                {
                    RU.FillOutline(g, new[] { P(-13, 0), P(13, 0), P(0, 40) }, RU.B(232, 188, 112), thin);
                    var wl = RU.Pn(180, 130, 60, 1.6f); g.DrawLine(wl, -8, 8, 6, 14); g.DrawLine(wl, 8, 8, -6, 14); g.DrawLine(wl, -4, 20, 4, 24);
                    RU.Ell(g, RU.B(252, 168, 204), 0, -6, 15, 13, thin);
                    RU.Ell(g, RU.B(166, 200, 250), 0, -22, 12, 11, thin);
                    RU.Ell(g, RU.B(224, 50, 62), 3, -34, 4.5f, 4.5f, thin);
                    break;
                }
                case 2:   // 피자
                {
                    RU.FillOutline(g, new[] { P(-24, -16), P(24, -16), P(0, 28) }, RU.B(252, 208, 76), thin);
                    RU.FillOutline(g, Geo.Smooth(new[] { P(-26, -20), P(0, -23), P(26, -20), P(24, -12), P(0, -15), P(-24, -12) }, true, 4), RU.B(226, 150, 70), thin);
                    foreach (var s in new[] { new[] { -8f, -4f }, new[] { 8f, -6f }, new[] { 0f, 8f } }) RU.Ell(g, RU.B(214, 70, 60), s[0], s[1], 5.5f, 5.5f, null);
                    break;
                }
                case 3:   // 도토리
                {
                    RU.Ell(g, RU.B(184, 124, 56), 0, 6, 14, 18, thin);
                    PointF[] cap = Geo.Smooth(new[] { P(-16, -4), P(-12, -16), P(0, -20), P(12, -16), P(16, -4), P(0, 2) }, true, 4);
                    RU.FillOutline(g, cap, RU.B(120, 78, 32), thin);
                    g.DrawLine(RU.Pn(80, 50, 20, 1.6f), -8, -10, 8, -4); g.DrawLine(RU.Pn(80, 50, 20, 1.6f), 8, -10, -8, -4);
                    g.DrawLine(RU.Pn(100, 64, 26, 3.4f), 0, -19, 2, -27);
                    break;
                }
                default:  // 금화
                {
                    RU.Ell(g, RU.B(246, 204, 70), 0, 0, 22, 22, thin);
                    g.DrawEllipse(RU.Pn(190, 140, 30, 2.4f), -16, -16, 32, 32);
                    RU.Text(g, "$", 0, 1, 26, RU.B(150, 104, 20));
                    g.DrawArc(RU.Pn(255, 255, 255, 3f, 200), -16, -16, 26, 26, 200, 50);
                    break;
                }
            }
        }
    }
}
