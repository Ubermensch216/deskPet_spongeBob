using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>집게사장: 둥근 배에 연푸른 바지·검은 벨트·금 버클, 머리 위로 솟은 눈자루 두 개, 커다란 빨간 집게발, 짧은 게 다리.</summary>
    internal sealed class KrabsRig : CharRig
    {
        private static readonly Color cRed = Color.FromArgb(228, 72, 54), cRedS = Color.FromArgb(190, 50, 38);
        private static readonly Color cShirt = Color.FromArgb(176, 206, 242), cPants = Color.FromArgb(152, 166, 220), cPantsS = Color.FromArgb(118, 132, 192);
        private static readonly Color cStalk = Color.FromArgb(234, 240, 190), cStalkS = Color.FromArgb(200, 214, 150), cGold = Color.FromArgb(236, 194, 64);

        public KrabsRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -300; ChestY = -150; SitDrop = 24; IconCenter = new PointF(0, -230); IconHalf = 78; Face = new PointF(-6, -204);
        }

        private PointF wristL, dirL;
        private const float HipY = -44f, ArmRestA = 62f, ArmRestB = 36f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            if (!p.Sit) Legs(g, p, ink);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .16f, 1 - p.Squash * .14f + p.Breath * .014f); g.TranslateTransform(0, -HipY);
            Stalks(g, p, ink, thin);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Torso(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Head(g, p, ink, thin);
            Props.AfterArms(g, 4, p, wristL, dirL, ink, new PointF(-6, ChestY - 8));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 40 + p.BodyX * .6f, HipY + p.BodyY - 4);
                PointF ankle = p.Sit ? new PointF(sg * (46 + 40) + fx, -12 + fy) : new PointF(sg * 46 + fx + sg * Math.Max(0, p.Knee) * .4f, -8 + fy);
                RU.LimbFill(g, new[] { hip, new PointF((hip.X + ankle.X) / 2 + sg * 4, (hip.Y + ankle.Y) / 2), ankle }, new[] { 22f, 17f, 14f }, true, RU.B(cRed), ink);
                var st = RU.Rot(g, ankle.X, ankle.Y, right ? -rot : rot);
                PointF[] foot = Geo.Smooth(new[] { P(ankle.X - 22, ankle.Y + 8), P(ankle.X - 10, ankle.Y - 6), P(ankle.X + 12, ankle.Y - 4), P(ankle.X + 14, ankle.Y + 8) }, true, 5);
                RU.FillOutline(g, foot, RU.B(cRed), ink);
                g.Restore(st);
            }
        }

        private void Torso(Graphics g, Pose p, Pen ink, Pen thin)
        {
            // 배 바지
            PointF[] pants = Geo.Smooth(new[] { P(-72, -156), P(0, -166), P(72, -156), P(94, -118), P(86, -70), P(52, -42), P(0, -36), P(-52, -42), P(-86, -70), P(-94, -118) }, true, 6);
            RU.FillOutline(g, pants, RU.B(cPants), ink);
            g.DrawArc(RU.Pn(cPantsS, 5f), -80, -110, 100, 70, 100, 90);
            g.DrawLine(RU.Pn(cPantsS, 3.2f), -26, -112, -24, -70);
            // 검은 벨트 + 금 버클
            PointF[] belt = { P(-91, -140), P(-60, -150), P(60, -150), P(91, -140), P(92, -118), P(60, -128), P(-60, -128), P(-92, -118) };
            RU.FillOutline(g, Geo.Smooth(belt, true, 4), RU.B(36, 34, 40), thin);
            RU.FillOutline(g, new[] { P(-42, -154), P(-12, -154), P(-12, -124), P(-42, -124) }, RU.B(cGold), thin);
            g.DrawRectangle(RU.Pn(150, 110, 30, 3f), -36, -148, 18, 18);
            // 셔츠 윗부분 + 옷깃
            PointF[] shirt = Geo.Smooth(new[] { P(-66, -176), P(0, -182), P(66, -176), P(74, -156), P(0, -166), P(-74, -156) }, true, 5);
            RU.FillOutline(g, shirt, RU.B(cShirt), thin);
            RU.FillOutline(g, new[] { P(-24, -180), P(-2, -176), P(-12, -160) }, RU.B(236, 244, 255), thin);
            RU.FillOutline(g, new[] { P(24, -180), P(2, -176), P(12, -160) }, RU.B(236, 244, 255), thin);
        }

        // 눈자루: 머리 위로 솟은 두 줄기. 끝에 까만 눈이 있다.
        private void Stalks(Graphics g, Pose p, Pen ink, Pen thin)
        {
            float s = p.Turn * 4;
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                PointF b = P(sg * 15 + p.HeadX * .5f, -222 + p.HeadY);
                float tilt = (sg * 5 + (right ? p.EarR : p.EarL) + p.HeadTilt * .5f) * (float)Math.PI / 180;
                float len = 78;
                PointF mid = new PointF(b.X + (float)Math.Sin(tilt * .5f) * len * .5f, b.Y - len * .5f), tip = new PointF(b.X + (float)Math.Sin(tilt) * len, b.Y - (float)Math.Cos(tilt) * len);
                RU.LimbFill(g, new[] { b, mid, tip }, new[] { 22f, 19f, 17f }, true, RU.B(cStalk), ink);
                g.DrawLine(RU.Pn(cStalkS, 3f), b.X + sg * 5, b.Y - 6, tip.X + sg * 4, tip.Y + 6);
                // 눈
                float ey = tip.Y + 22, ex = tip.X - 1 - s + p.Gx * 3;
                float open = right ? p.OpenR : p.OpenL;
                if (p.EyesDollar)
                {
                    g.FillEllipse(RU.B(cGold), ex - 13, ey - 13, 26, 26); g.DrawEllipse(RU.Pn(150, 110, 30, 3f), ex - 13, ey - 13, 26, 26);
                    RU.Text(g, "$", ex, ey, 22, RU.B(110, 76, 16));
                }
                else if (p.Eyes == EyeStyle.Spiral)
                {
                    g.FillEllipse(RU.White, ex - 12, ey - 12, 24, 24); RU.Spiral(g, new PointF(ex, ey), 10, p.SpiralT, right);
                }
                else if (p.Eyes != EyeStyle.Open || open < .14f)
                {
                    if (p.Eyes == EyeStyle.Happy) g.DrawArc(RU.Pn(30, 28, 32, 4f), ex - 7, ey - 5, 14, 12, 200, 140);
                    else g.DrawArc(RU.Pn(30, 28, 32, 4f), ex - 7, ey - 7, 14, 12, 20, 140);
                }
                else
                {
                    float rx = 6.6f * p.EyeScale * p.PupilR, ry = 10.4f * p.EyeScale * open;
                    g.FillEllipse(RU.Black, ex - rx, ey - ry + p.Gy * 3, rx * 2, ry * 2);
                    g.FillEllipse(RU.White, ex - rx * .5f, ey - ry * .6f + p.Gy * 3, 3.4f, 3.4f);
                }
                var lash = RU.Pn(RU.InkColor, 2.8f);
                g.DrawLine(lash, tip.X - 4, tip.Y - 1, tip.X - 8, tip.Y - 11); g.DrawLine(lash, tip.X + 3, tip.Y - 1, tip.X + 7, tip.Y - 10);
            }
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 72, -164);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 22, ChestY + 4) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 44, 40, sgn, to, out E, out W, out d);
                RU.LimbFill(g, new[] { S, E, W }, new[] { 14f, 12f, 11f }, false, RU.B(cRed), thin);
                wr[side] = W; dr[side] = d;
            }
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; HandStyle hs = right ? p.HandR : p.HandL;
                Claw(g, wr[side], dr[side], hs == HandStyle.Fist ? 0f : p.Claw, ink);
            }
            wristL = wr[0]; dirL = dr[0];
        }

        /// <summary>집게발: 큰 빨간 원에서 쐐기 모양을 파낸 모양. open 이 클수록 크게 벌어진다.</summary>
        private static void Claw(Graphics g, PointF wrist, PointF d, float open, Pen ink)
        {
            float ang = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI), r = 32, cx = r * .78f;
            var st = g.Save(); g.TranslateTransform(wrist.X, wrist.Y); g.RotateTransform(ang);
            float th = (5 + open * 42) * (float)Math.PI / 180, far = r * 2.6f;
            PointF apex = new PointF(cx - r * .12f, 0);
            PointF[] wedge = { apex, new PointF(apex.X + (float)Math.Cos(th) * far, -(float)Math.Sin(th) * far), new PointF(apex.X + (float)Math.Cos(th) * far, (float)Math.Sin(th) * far) };
            var inside = g.Save();
            using (var gp = new GraphicsPath()) { gp.AddPolygon(wedge); g.SetClip(gp, CombineMode.Exclude); }
            g.FillEllipse(RU.B(cRed), cx - r, -r, r * 2, r * 2); g.DrawEllipse(ink, cx - r, -r, r * 2, r * 2);
            g.Restore(inside);
            // 쐐기 가장자리(톱니 모양 안쪽)
            using (var gp = new GraphicsPath()) { gp.AddEllipse(cx - r, -r, r * 2, r * 2); g.SetClip(gp, CombineMode.Intersect); }
            g.DrawLine(ink, wedge[0], wedge[1]); g.DrawLine(ink, wedge[0], wedge[2]);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float u = .42f; PointF e = new PointF(apex.X + (wedge[1].X - apex.X) * u * .7f, sd * Math.Abs(wedge[1].Y - apex.Y) * u * .7f);
                PointF tooth = new PointF(e.X - 4, e.Y - sd * 8);
                g.FillPolygon(RU.B(cRed), new[] { new PointF(e.X + 9, e.Y), new PointF(e.X - 7, e.Y), tooth });
                g.DrawLine(ink, e.X + 9, e.Y, tooth.X, tooth.Y); g.DrawLine(ink, e.X - 7, e.Y, tooth.X, tooth.Y);
            }
            g.ResetClip();
            g.FillEllipse(RU.B(255, 255, 255, 105), cx - r * .62f, -r * .66f, r * .5f, r * .74f);
            g.Restore(st);
        }

        private void Head(Graphics g, Pose p, Pen ink, Pen thin)
        {
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -176, p.HeadTilt);
            float s = p.Turn * 8;
            PointF[] head = Geo.Smooth(new[] { P(-46, -196), P(-30, -226), P(0, -234), P(30, -226), P(46, -196), P(34, -176), P(0, -170), P(-34, -176) }, true, 6);
            RU.FillOutline(g, head, RU.B(cRed), ink);
            // 눈썹 + 입
            float br = p.Brow * 8;
            g.DrawLine(RU.Pn(RU.InkColor, 4.4f), -32 - s, -208 + br, -10 - s, -204 - br * .4f);
            g.DrawLine(RU.Pn(RU.InkColor, 4.4f), 8 - s, -204 - br * .4f, 30 - s * .8f, -208 + br);
            RU.Mouth(g, p, P(-8 - s, -190), 46, ink, false, false);
            if (p.Flush > .01f) g.FillEllipse(RU.B(255, 140, 120, (int)(120 * p.Flush)), -40 - s, -192, 20, 11);
            if (p.Goggles) RU.GogglesFace(g, -8 - s, -202, 18, 14, 58);
            if (p.Tears >= 0) RU.Tears(g, P(-24 - s, -198), P(10 - s, -198), p.Tears, 4.2f, p.EyesDollar ? Color.FromArgb(240, 200, 70) : Color.FromArgb(120, 190, 255), false);
            if (p.Sweat >= 0) RU.Sweat(g, P(58, -228), p.Sweat, 8);
            if (p.Anger > .01f) { RU.Anger(g, P(-62, -250), 17, p.Anger); RU.Anger(g, P(62, -254), 14, p.Anger + .3f); }
            g.Restore(st);
        }
    }
}
