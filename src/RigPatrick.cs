using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>뚱이: 불가사리 — 머리·두 팔·두 다리가 별의 다섯 꼭짓점. 초록 반바지에 보라 꽃무늬, 크게 벌린 입.</summary>
    internal sealed class PatrickRig : CharRig
    {
        private static readonly Color cS = Color.FromArgb(247, 138, 112), cSs = Color.FromArgb(222, 104, 82), cShorts = Color.FromArgb(188, 224, 60), cShortsS = Color.FromArgb(148, 188, 38), cPurple = Color.FromArgb(124, 86, 184);

        public PatrickRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -296; ChestY = -138; SitDrop = 52; IconCenter = new PointF(0, -204); IconHalf = 104; Face = new PointF(-8, -222);
        }

        private PointF wristL, dirL;
        private const float HipY = -86f, ArmRestA = 108f, ArmRestB = 124f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            if (!p.Sit) Legs(g, p, ink);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .22f, 1 - p.Squash * .18f + p.Breath * .014f); g.TranslateTransform(0, -HipY);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Torso(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Head(g, p, ink, thin);
            Props.AfterArms(g, 1, p, wristL, dirL, ink, new PointF(-6, ChestY - 8));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 26 + p.BodyX * .6f, HipY + p.BodyY - 4);
                PointF tip = p.Sit ? new PointF(sg * (26 + 66) + fx, -20 + fy) : new PointF(sg * 36 + fx + sg * Math.Max(0, p.Knee) * .5f, -9 + fy);
                if (!p.Sit && p.BodyY > 0) tip.X += sg * p.BodyY * .14f;
                PointF mid = new PointF((hip.X + tip.X) / 2 + sg * (p.Sit ? 0 : p.Knee * .35f), (hip.Y + tip.Y) / 2);
                RU.LimbFill(g, new[] { hip, mid, tip }, new[] { 48f, 38f, 22f }, true, RU.B(cS), ink);
            }
        }

        private void Torso(Graphics g, Pose p, Pen ink, Pen thin)
        {
            // 몸통(배)
            PointF[] torso = Geo.Smooth(new[] { P(-58, -200), P(0, -206), P(58, -200), P(80, -166), P(78, -122), P(60, -92), P(0, -84), P(-60, -92), P(-78, -122), P(-80, -166) }, true, 6);
            RU.FillOutline(g, torso, RU.B(cS), ink);
            var dots = RU.B(cSs.R, cSs.G, cSs.B, 190);
            float[][] d = { new[] { -46f, -170f }, new[] { 52f, -176f }, new[] { 30f, -150f }, new[] { -62f, -130f }, new[] { 10f, -120f }, new[] { 64f, -130f }, new[] { -30f, -190f } };
            foreach (var q in d) g.FillEllipse(dots, q[0] - 2.2f, q[1] - 2.2f, 4.4f, 4.4f);
            g.DrawArc(thin, -14, -142, 18, 12, 20, 230);                   // 배꼽
            // 반바지 + 보라 꽃무늬
            PointF[] shorts = Geo.Smooth(new[] { P(-76, -112), P(0, -106), P(76, -112), P(80, -78), P(48, -54), P(14, -62), P(0, -76), P(-14, -62), P(-48, -54), P(-80, -78) }, true, 5);
            RU.FillOutline(g, shorts, RU.B(cShorts), ink);
            g.DrawLine(RU.Pn(cShortsS, 4f), -76, -108, 76, -108);
            foreach (var f in new[] { new[] { -46f, -84f }, new[] { 50f, -78f } })
            {
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2 + .5; float cx = f[0] + (float)Math.Cos(a) * 9, cy = f[1] + (float)Math.Sin(a) * 8;
                    g.FillEllipse(RU.B(cPurple), cx - 8, cy - 7, 16, 14);
                }
                g.FillEllipse(RU.B(cPurple), f[0] - 8, f[1] - 7, 16, 14);
            }
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 56, -190);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 10, ChestY + 4) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 50, 48, sgn, to, out E, out W, out d);
                RU.LimbFill(g, new[] { S, E, W }, new[] { 52f, 38f, 12f }, true, RU.B(cS), ink);
                wr[side] = new PointF(W.X - d.X * 14, W.Y - d.Y * 14); dr[side] = d;
            }
            wristL = wr[0]; dirL = dr[0];
        }

        private void Head(Graphics g, Pose p, Pen ink, Pen thin)
        {
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -190, p.HeadTilt);
            float s = p.Turn * 12;
            // 머리 꼭짓점
            PointF[] spine = { P(0, -190), P(-3 - s * .3f, -244), P(-4 - s * .5f, -296) };
            RU.LimbFill(g, spine, new[] { 98f, 66f, 14f }, true, RU.B(cS), ink);
            g.FillEllipse(RU.B(cSs.R, cSs.G, cSs.B, 190), 12, -262, 4, 4); g.FillEllipse(RU.B(cSs.R, cSs.G, cSs.B, 190), -20, -240, 4, 4);
            // 눈썹 · 눈
            g.DrawLine(RU.Pn(RU.InkColor, 3.4f), -34 - s, -246 + p.Brow * 7, -12 - s, -249 - p.Brow * 3);
            g.DrawLine(RU.Pn(RU.InkColor, 3.4f), 10 - s * .8f, -249 - p.Brow * 3, 30 - s * .8f, -246 + p.Brow * 7);
            PointF eL = P(-24 - s, -224), eR = P(12 - s * .8f, -224);
            RU.Eye(g, p, eL, 16, 18.5f, p.OpenL, 1, Color.Black, 0, .27f, ink, false);
            RU.Eye(g, p, eR, 15, 18.5f, p.OpenR, -1, Color.Black, 0, .27f, ink, false);
            RU.Mouth(g, p, P(-8 - s, -196), 84, ink, false, false, Color.FromArgb(150, 36, 52));
            if (p.Flush > .01f) { g.FillEllipse(RU.B(255, 90, 90, (int)(130 * p.Flush)), -66 - s, -204, 28, 18); g.FillEllipse(RU.B(255, 90, 90, (int)(130 * p.Flush)), 40 - s, -204, 28, 18); }
            if (p.Goggles) RU.GogglesFace(g, -6 - s, -224, 22, 20, 74);
            if (p.Tears >= 0) RU.Tears(g, P(eL.X - 6, eL.Y + 18), P(eR.X + 6, eR.Y + 18), p.Tears, 5f, Color.FromArgb(120, 190, 255), false);
            if (p.Sweat >= 0) RU.Sweat(g, P(46, -270), p.Sweat, 9);
            if (p.Anger > .01f) { RU.Anger(g, P(-52, -278), 18, p.Anger); RU.Anger(g, P(44, -282), 14, p.Anger + .3f); }
            g.Restore(st);
        }
    }
}
