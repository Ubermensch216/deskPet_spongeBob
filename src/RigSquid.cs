using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>징징이: 길쭉한 몸에 불룩한 머리, 처진 눈꺼풀과 길게 늘어진 코, 주황 셔츠, 문어발 다리와 파란 발.</summary>
    internal sealed class SquidRig : CharRig
    {
        private static readonly Color cSk = Color.FromArgb(170, 212, 204), cSkS = Color.FromArgb(138, 184, 176), cShirt = Color.FromArgb(240, 164, 30), cShirtS = Color.FromArgb(206, 132, 16);
        private static readonly Color cFoot = Color.FromArgb(158, 158, 220), cSpot = Color.FromArgb(112, 112, 192), cEye = Color.FromArgb(200, 66, 60);

        public SquidRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -304; ChestY = -176; SitDrop = 82; IconCenter = new PointF(-6, -262); IconHalf = 62; Face = new PointF(-12, -262);
        }

        private PointF wristL, dirL;
        private const float HipY = -122f, ArmRestA = 54f, ArmRestB = -22f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            if (!p.Sit) Legs(g, p, ink);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .2f, 1 - p.Squash * .15f + p.Breath * .01f); g.TranslateTransform(0, -HipY);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Torso(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Head(g, p, ink, thin);
            Props.AfterArms(g, 2, p, wristL, dirL, ink, new PointF(-8, ChestY - 4));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 14 + p.BodyX * .6f, HipY + p.BodyY - 4);
                PointF ankle = p.Sit ? new PointF(sg * (16 + 62) + fx, -15 + fy) : new PointF(sg * 22 + fx + sg * Math.Max(0, p.Knee) * .5f, -20 + fy);
                if (!p.Sit && p.BodyY > 0) ankle.X += sg * p.BodyY * .1f;
                // 문어발 다리: 살짝 굽은 가는 다리
                PointF mid = new PointF((hip.X + ankle.X) / 2 + sg * (p.Sit ? 0 : 6 + p.Knee * .4f), (hip.Y + ankle.Y) / 2);
                RU.LimbFill(g, new[] { hip, mid, ankle }, new[] { 22f, 17f, 18f }, false, RU.B(cSk), ink);
                // 파란 발(점박이)
                var st = RU.Rot(g, ankle.X, ankle.Y, right ? -rot : rot);
                PointF[] foot = Geo.Smooth(new[] { P(ankle.X + 14, ankle.Y - 2), P(ankle.X - 10, ankle.Y - 8), P(ankle.X - 34, ankle.Y), P(ankle.X - 36, ankle.Y + 14), P(ankle.X - 8, ankle.Y + 20), P(ankle.X + 16, ankle.Y + 16) }, true, 5);
                RU.FillOutline(g, foot, RU.B(cFoot), ink);
                g.FillEllipse(RU.B(cSpot), ankle.X - 26, ankle.Y + 6, 7, 6); g.FillEllipse(RU.B(cSpot), ankle.X - 12, ankle.Y + 10, 6, 5); g.FillEllipse(RU.B(cSpot), ankle.X - 22, ankle.Y - 2, 5, 4);
                g.Restore(st);
            }
        }

        private void Torso(Graphics g, Pose p, Pen ink, Pen thin)
        {
            // 목 + 골반
            RU.Ell(g, RU.B(cSk), 0, HipY + 2, 30, 14, thin);
            RU.LimbFill(g, new[] { P(0, -204), P(2, -238) }, new[] { 26f, 24f }, false, RU.B(cSk), thin);
            // 주황 셔츠
            PointF[] shirt = Geo.Smooth(new[] { P(-34, -208), P(0, -212), P(34, -208), P(36, -172), P(32, -132), P(0, -126), P(-32, -132), P(-36, -172) }, true, 5);
            RU.FillOutline(g, shirt, RU.B(cShirt), ink);
            RU.FillOutline(g, new[] { P(-26, -210), P(-4, -208), P(-8, -192) }, RU.B(cShirtS), thin);     // 옷깃
            RU.FillOutline(g, new[] { P(26, -210), P(4, -208), P(8, -192) }, RU.B(cShirtS), thin);
            g.DrawLine(RU.Pn(cShirtS, 2.6f), 0, -194, 0, -134);
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 32, -198);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 12, ChestY + 4) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 46, 42, sgn, to, out E, out W, out d);
                // 짧은 소매
                RU.LimbFill(g, new[] { S, E, W }, new[] { 12f, 10.5f, 9f }, false, RU.B(cSk), thin);
                RU.Ell(g, RU.B(cShirt), S.X + sgn * 3, S.Y + 2, 16, 14, thin);
                wr[side] = W; dr[side] = d;
            }
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; HandStyle hs = right ? p.HandR : p.HandL;
                RU.Hand(g, wr[side], dr[side], hs == HandStyle.Fist, p.Claw, RU.B(cSk), thin, 20, 3, false);
            }
            wristL = wr[0]; dirL = dr[0];
        }

        private void Head(Graphics g, Pose p, Pen ink, Pen thin)
        {
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -234, p.HeadTilt);
            float s = p.Turn * 12;
            // 불룩한 머리(뒤쪽이 더 불룩)
            PointF[] head = Geo.Smooth(new[] { P(-48, -262), P(-42, -288), P(-18, -303), P(14, -306), P(42, -296), P(54, -272), P(46, -250), P(22, -240), P(-18, -236), P(-42, -244) }, true, 6);
            RU.FillOutline(g, head, RU.B(cSk), ink);
            var ridge = RU.Pn(cSkS, 3f);
            g.DrawArc(ridge, -26, -304, 70, 20, 195, 150); g.DrawArc(ridge, -30, -298, 78, 26, 195, 150); g.DrawArc(ridge, -32, -290, 82, 30, 200, 140);
            // 눈 (처진 눈꺼풀)
            PointF eL = P(-26 - s, -262), eR = P(8 - s * .8f, -262);
            for (int i = 0; i < 2; i++)
            {
                PointF c = i == 0 ? eL : eR; float rx = i == 0 ? 17f : 16f;
                g.FillEllipse(RU.B(cSkS), c.X - rx - 2, c.Y + 1, rx * 2 + 4, 14);                        // 눈 밑 그늘
                RU.Eye(g, p, c, rx, 13f, Math.Max(0, p.OpenL * (i == 0 ? 1 : 1) * (p.Brow < -.3f ? 1 : .78f)), i == 0 ? 1 : -1, cEye, .62f, .3f, ink, false);
                if (p.Eyes == EyeStyle.Open && (i == 0 ? p.OpenL : p.OpenR) > .14f)
                {
                    float lid = (1 - Math.Min(1, (i == 0 ? p.OpenL : p.OpenR))) * 8;
                    PointF[] cap = { P(c.X - rx - 3, c.Y - 2 + lid), P(c.X - rx * .6f, c.Y - 15), P(c.X + rx * .6f, c.Y - 15), P(c.X + rx + 3, c.Y - 2 + lid) };
                    RU.FillOutline(g, Geo.Smooth(new[] { cap[0], cap[1], cap[2], cap[3], P(c.X, c.Y + lid * .5f - 1 + (p.Brow > .1f ? 5 : 0)) }, true, 4), RU.B(cSkS), thin);
                }
            }
            // 코: 길게 늘어진 큰 코
            PointF n0 = P(-8 - s, -262), n1 = P(-16 - s * 1.2f, -246), n2 = P(-22 - s * 1.3f, -228);
            RU.LimbFill(g, new[] { n0, n1, n2 }, new[] { 22f, 24f, 20f }, true, RU.B(cSk), ink);
            g.DrawArc(thin, n2.X - 6, n2.Y - 2, 10, 8, 20, 130);
            // 입
            RU.Mouth(g, p, P(-38 - s * .8f, -241), 40, ink, false, false);
            if (p.Goggles) RU.GogglesFace(g, -8 - s, -262, 18, 16, 56);
            if (p.Tears >= 0) RU.Tears(g, P(eL.X - 4, eL.Y + 14), P(eR.X + 4, eR.Y + 14), p.Tears, 4.6f, Color.FromArgb(120, 190, 255), false);
            if (p.Sweat >= 0) RU.Sweat(g, P(52, -290), p.Sweat, 8);
            if (p.Anger > .01f) { RU.Anger(g, P(-50, -300), 16, p.Anger); RU.Anger(g, P(50, -306), 13, p.Anger + .3f); }
            if (p.Flush > .01f) g.FillEllipse(RU.B(255, 120, 120, (int)(120 * p.Flush)), -44 - s, -250, 22, 12);
            g.Restore(st);
        }
    }
}
