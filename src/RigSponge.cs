using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>스폰지밥: 네모난 노란 몸, 큰 눈과 앞니, 가는 팔다리, 갈색 바지·빨간 넥타이·줄무늬 양말·검은 구두.</summary>
    internal sealed class SpongeRig : CharRig
    {
        private static readonly Color cY = Color.FromArgb(247, 232, 62), cYs = Color.FromArgb(226, 205, 38), cHole = Color.FromArgb(184, 204, 56);
        private static readonly Color cPants = Color.FromArgb(196, 132, 48), cPantsS = Color.FromArgb(158, 98, 30), cIris = Color.FromArgb(70, 150, 218);
        private static readonly float[][] holes =
        {
            new[]{-50f,-268f,11f}, new[]{52f,-274f,8f}, new[]{62f,-236f,6f}, new[]{-62f,-208f,6f}, new[]{-58f,-146f,9f}, new[]{-30f,-130f,5f},
            new[]{66f,-160f,11f}, new[]{72f,-130f,6f}, new[]{26f,-286f,5f}, new[]{34f,-128f,6f}, new[]{-12f,-282f,4f}, new[]{-66f,-250f,5f}
        };

        public SpongeRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -298; ChestY = -150; SitDrop = 54; IconCenter = new PointF(0, -204); IconHalf = 104; Face = new PointF(-8, -212);
        }

        private PointF wristL, dirL;
        private const float HipY = -74f, ArmRestA = 24f, ArmRestB = 12f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            if (!p.Sit) Legs(g, p, ink);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .22f, 1 - p.Squash * .18f + p.Breath * .012f); g.TranslateTransform(0, -HipY);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Body(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Props.AfterArms(g, 0, p, wristL, dirL, ink, new PointF(-8, ChestY - 6));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 30 + p.BodyX * .6f, HipY + p.BodyY - 2);
                PointF ankle = p.Sit ? new PointF(sg * (30 + 56) + fx, -13 + fy) : new PointF(sg * 33 + fx + sg * Math.Max(0, p.Knee) * .5f, -13 + fy);
                if (!p.Sit && p.BodyY > 0) ankle.X += sg * p.BodyY * .12f;
                // 노란 다리
                PointF mid = new PointF((hip.X + ankle.X) / 2 + sg * (p.Sit ? 0 : p.Knee * .4f), (hip.Y + ankle.Y) / 2);
                RU.LimbFill(g, new[] { hip, mid, ankle }, new[] { 11f, 10f, 10f }, false, RU.B(cY), ink);
                // 흰 양말(파랑·빨강 줄무늬)
                PointF top = new PointF(ankle.X + (hip.X - ankle.X) * .66f, ankle.Y + (hip.Y - ankle.Y) * .66f);
                RU.LimbFill(g, new[] { ankle, top }, new[] { 13f, 13f }, false, RU.White, ink);
                for (int i = 0; i < 2; i++)
                {
                    float t = .5f + i * .09f; PointF c = new PointF(ankle.X + (top.X - ankle.X) * t / .66f * .86f, ankle.Y + (top.Y - ankle.Y) * t / .66f * .86f);
                    g.DrawLine(RU.Pn(i == 0 ? Color.FromArgb(70, 120, 210) : Color.FromArgb(214, 52, 44), 3.2f), c.X - 6.5f, c.Y, c.X + 6.5f, c.Y);
                }
                // 검은 구두
                var st = RU.Rot(g, ankle.X, ankle.Y, right ? -rot : rot);
                float sx = right ? 9 : -9;
                RU.Ell(g, RU.Black, ankle.X + sx * (right ? 1 : 1), ankle.Y + 4, 25, 12.5f, ink);
                g.DrawArc(RU.Pn(255, 255, 255, 3f, 190), ankle.X + sx - 12, ankle.Y - 3, 20, 9, 200, 70);
                g.Restore(st);
            }
        }

        private void Body(Graphics g, Pose p, Pen ink, Pen thin)
        {
            float s = p.Turn * 14, sw = 11 + 9 * p.Turn;
            // 바지·셔츠 띠·넥타이
            PointF[] pants = Geo.Smooth(new[] { P(-80, HipY + 4), P(0, HipY + 8), P(84, HipY + 4), P(86, -100), P(-82, -100) }, true, 5);
            RU.FillOutline(g, pants, RU.B(cPants), thin);
            g.DrawLine(RU.Pn(cPantsS, 3f), 0, HipY + 6, 0, -86);
            g.DrawLine(RU.Pn(cPantsS, 3f), -80, -96, 85, -96);
            RU.FillOutline(g, new[] { P(-82, -100), P(86, -100), P(86, -112), P(-82, -112) }, RU.White, thin);
            RU.FillOutline(g, new[] { P(-24 - s, -112), P(-12 - s, -112), P(-9 - s, -100), P(-22 - s, -100) }, RU.B(214, 52, 44), thin);
            RU.FillOutline(g, new[] { P(-23 - s, -100), P(-11 - s, -100), P(-17 - s, -86) }, RU.B(214, 52, 44), thin);

            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -112, p.HeadTilt);
            // 스펀지 몸: 옆면(어두운 면) + 앞면, 가장자리는 물결 모양
            PointF[] side = Geo.Wavy(new[] { P(-76, -296), P(-76 - sw, -283), P(-82 - sw, -120), P(-82, -112) }, 2.4f, 13);
            RU.FillOutline(g, side, RU.B(cYs), thin);
            PointF[] front = Geo.Wavy(new[] { P(-76, -296), P(86, -299), P(86, -112), P(-82, -112) }, 3f, 15, 1.3f);
            RU.FillOutline(g, front, RU.B(cY), thin);
            foreach (var h in holes) g.FillEllipse(RU.B(cHole.R, cHole.G, cHole.B, 200), h[0] - h[2], h[1] - h[2] * .85f, h[2] * 2, h[2] * 1.7f);
            g.FillEllipse(RU.B(cHole.R, cHole.G, cHole.B, 170), -sw * .6f - 78, -190, 7, 12);

            // 볼·주근깨
            if (p.Flush > .01f) { g.FillEllipse(RU.B(255, 120, 110, (int)(150 * p.Flush)), -78 - s, -190, 36, 26); g.FillEllipse(RU.B(255, 120, 110, (int)(150 * p.Flush)), 34 - s, -190, 36, 26); }
            var fr = RU.Pn(236, 130, 90, 2.6f, 200);
            for (int sd = 0; sd < 2; sd++)
            {
                float cx = sd == 0 ? -58 - s : 50 - s, cy = -178;
                g.DrawEllipse(fr, cx - 9, cy - 8, 18, 15);
                g.DrawLine(fr, cx - 3, cy - 2, cx - 6, cy + 3); g.DrawLine(fr, cx + 3, cy - 2, cx, cy + 3);
            }

            // 눈
            float ex = (p.Turn > 0 ? 1 : 0);
            PointF eL = P(-30 - s, -228), eR = P(24 - s * .75f, -228);
            RU.Eye(g, p, eL, 26, 29, p.OpenL, 1, cIris, .5f, .22f, ink, true);
            RU.Eye(g, p, eR, 24 - p.Turn * 3, 28, p.OpenR, -1, cIris, .5f, .22f, ink, true);
            // 코(긴 둥근 코)
            PointF[] nose = Geo.Ellipse(-16 - s * 1.15f, -193, 21, 8.5f, 20);
            RU.FillOutline(g, nose, RU.B(cY), thin);
            // 입
            RU.Mouth(g, p, P(-6 - s, -170), 104, ink, true, false);
            if (p.Goggles) RU.GogglesFace(g, -4 - s, -226, 27, 25, 88);
            if (p.Tears >= 0) RU.Tears(g, P(eL.X - 10, eL.Y + 22), P(eR.X + 10, eR.Y + 22), p.Tears, 6.5f, Color.FromArgb(120, 190, 255), true);
            if (p.Sweat >= 0) RU.Sweat(g, P(90, -280), p.Sweat, 9);
            if (p.Anger > .01f) { RU.Anger(g, P(-84, -292), 20, p.Anger); RU.Anger(g, P(94, -296), 16, p.Anger + .3f); }
            g.Restore(st);
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 82, -128);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 16, ChestY + 6) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 50, 46, sgn, to, out E, out W, out d);
                RU.LimbFill(g, new[] { S, E, W }, new[] { 10.5f, 9.5f, 8.5f }, false, RU.B(cY), thin);
                RU.Ell(g, RU.White, S.X + sgn * 1, S.Y - 1, 13, 11, thin);          // 하얀 소매
                wr[side] = W; dr[side] = d;
            }
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; HandStyle hs = right ? p.HandR : p.HandL;
                RU.Hand(g, wr[side], dr[side], hs == HandStyle.Fist, p.Claw, RU.B(cY), thin, 26, 4, true);
            }
            wristL = wr[0]; dirL = dr[0];
        }

    }
}
