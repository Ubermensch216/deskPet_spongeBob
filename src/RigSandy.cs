using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>다람이: 물이 담긴 유리 헬멧(분홍 꽃)과 하얀 우주복, 둥근 갈색 꼬리(배낭 자리), 회색 장화, 하얀 장갑.</summary>
    internal sealed class SandyRig : CharRig
    {
        private static readonly Color cFur = Color.FromArgb(196, 122, 56), cFurS = Color.FromArgb(160, 94, 38), cCream = Color.FromArgb(250, 226, 178);
        private static readonly Color cSuit = Color.FromArgb(247, 247, 252), cSuitS = Color.FromArgb(210, 216, 230), cBoot = Color.FromArgb(164, 166, 174), cBootS = Color.FromArgb(124, 126, 136);
        private static readonly Color cRed = Color.FromArgb(224, 54, 54), cBlue = Color.FromArgb(90, 120, 220);

        public SandyRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -304; ChestY = -158; SitDrop = 52; IconCenter = new PointF(0, -250); IconHalf = 66; Face = new PointF(-4, -248);
        }

        private PointF wristL, dirL;
        private const float HipY = -104f, ArmRestA = 22f, ArmRestB = 8f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            Tail(g, p, ink);
            g.Restore(upper);
            if (!p.Sit) Legs(g, p, ink);
            upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .2f, 1 - p.Squash * .16f + p.Breath * .012f); g.TranslateTransform(0, -HipY);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Torso(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Head(g, p, ink, thin);
            Props.AfterArms(g, 3, p, wristL, dirL, ink, new PointF(-8, ChestY - 4));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        // 꼬리: 몸 뒤에서 위로 솟는 커다란 갈색 꼬리(배낭 자리)
        private void Tail(Graphics g, Pose p, Pen ink)
        {
            const int N = 4;
            PointF[] spine = new PointF[N + 1]; spine[0] = P(40, -112);
            float[] len = { 30, 34, 34, 30 };
            double baseAng = (-62 + p.Tail) * Math.PI / 180;
            for (int i = 0; i < N; i++)
            {
                double a = baseAng + (p.TailCurl * (i + 1) / N + p.TailWaveAmp * Math.Sin(p.TailWavePhase - i * .9) * (i + 1) / N) * Math.PI / 180 * -1;
                spine[i + 1] = new PointF(spine[i].X + (float)Math.Cos(a) * len[i], spine[i].Y + (float)Math.Sin(a) * len[i]);
            }
            PointF[] outline = Geo.Limb(spine, new[] { 36f, 66f, 62f, 50f, 24f }, true, 8);
            RU.FillOutline(g, outline, RU.B(cFur), ink);
            var st = g.Save();
            using (var clip = new GraphicsPath()) { clip.AddPolygon(outline); g.SetClip(clip, CombineMode.Intersect); }
            PointF[] s = Geo.Smooth(spine, false, 6);
            for (int i = 6; i < s.Length - 4; i += 7)
                g.DrawArc(RU.Pn(cFurS, 5f), s[i].X - 22, s[i].Y - 14, 44, 28, 200, 140);
            g.Restore(st);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 24 + p.BodyX * .6f, HipY + p.BodyY - 4);
                PointF ankle = p.Sit ? new PointF(sg * (30 + 62) + fx, -22 + fy) : new PointF(sg * 28 + fx + sg * Math.Max(0, p.Knee) * .5f, -34 + fy);
                if (!p.Sit && p.BodyY > 0) ankle.X += sg * p.BodyY * .12f;
                PointF mid = new PointF((hip.X + ankle.X) / 2 + sg * (p.Sit ? 0 : p.Knee * .35f), (hip.Y + ankle.Y) / 2);
                RU.LimbFill(g, new[] { hip, mid, ankle }, new[] { 28f, 24f, 24f }, false, RU.B(cSuit), ink);
                // 회색 장화 (빨간 점)
                var st = RU.Rot(g, ankle.X, ankle.Y, right ? -rot : rot);
                PointF[] boot = Geo.Smooth(new[] { P(ankle.X - 30, ankle.Y + 32), P(ankle.X - 32, ankle.Y + 12), P(ankle.X - 22, ankle.Y - 4), P(ankle.X + 22, ankle.Y - 4), P(ankle.X + 30, ankle.Y + 14), P(ankle.X + 32, ankle.Y + 32) }, true, 5);
                RU.FillOutline(g, boot, RU.B(cBoot), ink);
                g.FillRectangle(RU.B(cBootS), ankle.X - 32, ankle.Y + 27, 64, 6);
                g.FillEllipse(RU.B(cRed), ankle.X - 6, ankle.Y + 10, 12, 12); g.DrawEllipse(RU.Pn(RU.InkColor, 2.4f), ankle.X - 6, ankle.Y + 10, 12, 12);
                g.Restore(st);
            }
        }

        private void Torso(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] torso = Geo.Smooth(new[] { P(-48, -206), P(0, -210), P(48, -206), P(58, -172), P(58, -132), P(46, -106), P(0, -98), P(-46, -106), P(-58, -132), P(-58, -172) }, true, 6);
            RU.FillOutline(g, torso, RU.B(cSuit), ink);
            g.DrawArc(RU.Pn(cSuitS, 4f), -52, -140, 100, 40, 20, 140);
            g.DrawLine(RU.Pn(cSuitS, 2.4f), 0, -204, 1, -140); g.DrawLine(RU.Pn(cSuitS, 2.4f), 1, -140, -2, -108);
            // 노란 도토리 패치
            var patch = new[] { P(18, -186), P(42, -186), P(42, -158), P(18, -158) };
            RU.FillOutline(g, patch, RU.B(252, 210, 66), thin);
            g.FillEllipse(RU.B(cFurS), 24, -176, 12, 14); g.FillRectangle(RU.B(cFurS), 25, -180, 10, 5);
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 52, -182);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 14, ChestY + 4) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 42, 38, sgn, to, out E, out W, out d);
                RU.LimbFill(g, new[] { S, E, W }, new[] { 30f, 26f, 22f }, false, RU.B(cSuit), thin);
                // 빨강·파랑 줄무늬 띠
                Band(g, S, E, .45f, 25, cRed); Band(g, S, E, .72f, 24, cBlue); Band(g, E, W, .35f, 23, cRed); Band(g, E, W, .62f, 22, cBlue);
                wr[side] = W; dr[side] = d;
            }
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; HandStyle hs = right ? p.HandR : p.HandL;
                RU.Hand(g, wr[side], dr[side], hs == HandStyle.Fist, p.Claw, RU.White, thin, 28, 4, true);
            }
            wristL = wr[0]; dirL = dr[0];
        }

        private static void Band(Graphics g, PointF a, PointF b, float t, float w, Color c)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy); if (len < 1) return;
            PointF m = new PointF(a.X + dx * t, a.Y + dy * t); float nx = -dy / len * w / 2, ny = dx / len * w / 2;
            g.DrawLine(RU.Pn(c, 3.4f), m.X - nx, m.Y - ny, m.X + nx, m.Y + ny);
        }

        private void Head(Graphics g, Pose p, Pen ink, Pen thin)
        {
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -204, p.HeadTilt);
            float s = p.Turn * 10;
            PointF hc = P(0, -250); const float R = 58f;
            // 헬멧 (물이 찬 유리)
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(hc.X - R, hc.Y - R, R * 2, R * 2);
                using (var br = new LinearGradientBrush(new PointF(0, hc.Y - R), new PointF(0, hc.Y + R), Color.FromArgb(104, 212, 246), Color.FromArgb(30, 150, 206)))
                    g.FillPath(br, path);
            }
            // 얼굴
            PointF[] face = Geo.Smooth(new[] { P(-34 - s, -252), P(-26 - s, -274), P(0 - s, -282), P(26 - s * .5f, -274), P(34 - s * .5f, -252), P(24 - s * .5f, -226), P(-2 - s, -218), P(-26 - s, -226) }, true, 6);
            for (int sd = -1; sd <= 1; sd += 2)      // 귀
            {
                float ex = sd * 27 - s * .8f;
                RU.Ell(g, RU.B(cFur), ex, -276, 10, 11, ink); RU.Ell(g, RU.B(cFurS), ex, -276, 5, 6, null);
            }
            RU.FillOutline(g, face, RU.B(cFur), ink);
            RU.Ell(g, RU.B(cCream), -3 - s, -232, 25, 17, null);
            PointF eL = P(-13 - s, -256), eR = P(10 - s * .7f, -256);
            RU.Eye(g, p, eL, 10.5f, 12, p.OpenL, 1, Color.Black, 0, .5f, ink, false);
            RU.Eye(g, p, eR, 10, 12, p.OpenR, -1, Color.Black, 0, .5f, ink, false);
            RU.Ell(g, RU.B(238, 120, 150), -2 - s, -244, 5.5f, 4.5f, thin);
            RU.Mouth(g, p, P(-2 - s, -232), 44, ink, true, false);
            if (p.Flush > .01f) { g.FillEllipse(RU.B(255, 130, 130, (int)(130 * p.Flush)), -32 - s, -240, 14, 9); g.FillEllipse(RU.B(255, 130, 130, (int)(130 * p.Flush)), 22 - s, -240, 14, 9); }
            if (p.Goggles) RU.GogglesFace(g, -2 - s, -256, 14, 12, 52);
            if (p.Tears >= 0) RU.Tears(g, P(eL.X - 3, eL.Y + 10), P(eR.X + 3, eR.Y + 10), p.Tears, 3.6f, Color.FromArgb(150, 210, 255), false);
            if (p.Sweat >= 0) RU.Sweat(g, P(44, -296), p.Sweat, 7);
            // 유리 반사
            g.DrawArc(RU.Pn(255, 255, 255, 5f, 230), hc.X - R + 6, hc.Y - R + 6, R * 2 - 12, R * 2 - 12, -52, 34);
            g.DrawEllipse(RU.Pn(236, 248, 255, 3.6f), hc.X - R, hc.Y - R, R * 2, R * 2);
            // 머리 위 분홍 꽃
            DrawFlower(g, P(-38, -294), 17, ink);
            g.Restore(st);
            // 목 고리(헬멧 아래를 가린다)
            RU.Ell(g, RU.B(cSuit), 0, -204, 54, 12, ink);
            g.DrawArc(RU.Pn(cSuitS, 3f), -48, -206, 96, 18, 10, 160);
            if (p.Anger > .01f) { RU.Anger(g, P(-62, -296), 16, p.Anger); RU.Anger(g, P(62, -300), 13, p.Anger + .3f); }
        }

        internal static void DrawFlower(Graphics g, PointF c, float r, Pen ink)
        {
            var pn = RU.Pn(140, 40, 110, 2.4f);
            for (int i = 0; i < 5; i++)
            {
                double a = i * Math.PI * 2 / 5 - Math.PI / 2 + .3; float px = c.X + (float)Math.Cos(a) * r * .62f, py = c.Y + (float)Math.Sin(a) * r * .62f;
                g.FillEllipse(RU.B(240, 112, 196), px - r * .46f, py - r * .46f, r * .92f, r * .92f); g.DrawEllipse(pn, px - r * .46f, py - r * .46f, r * .92f, r * .92f);
            }
            g.FillEllipse(RU.B(255, 176, 60), c.X - r * .3f, c.Y - r * .3f, r * .6f, r * .6f); g.DrawEllipse(pn, c.X - r * .3f, c.Y - r * .3f, r * .6f, r * .6f);
        }
    }
}
