using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BikiniFriends
{
    /// <summary>플랑크톤: 작은 초록 콩 모양 몸에 커다란 외눈, 끝이 가는 마디 안테나 두 개, 큰 입과 가는 팔다리.</summary>
    internal sealed class PlanktonRig : CharRig
    {
        private static readonly Color cSk = Color.FromArgb(112, 172, 162), cSkS = Color.FromArgb(84, 142, 134), cAnt = Color.FromArgb(34, 92, 70), cSclera = Color.FromArgb(252, 244, 150), cIris = Color.FromArgb(230, 54, 44);

        public PlanktonRig()
        {
            RestA = ArmRestA; RestB = ArmRestB; HeadTop = -300; ChestY = -124; SitDrop = 20; IconCenter = new PointF(0, -190); IconHalf = 70; Face = new PointF(-4, -156);
        }

        private PointF wristL, dirL;
        private const float HipY = -52f, ArmRestA = 36f, ArmRestB = 18f;

        public override void Draw(Graphics g, Pose p, Pen ink)
        {
            var thin = RU.Pn(RU.InkColor, ink.Width * .8f);
            if (!p.Sit) Legs(g, p, ink);
            var upper = g.Save();
            g.TranslateTransform(p.BodyX, p.BodyY);
            RU.Rot(g, 0, HipY, p.Lean);
            g.TranslateTransform(0, HipY); g.ScaleTransform(1 + p.Squash * .2f, 1 - p.Squash * .16f + p.Breath * .014f); g.TranslateTransform(0, -HipY);
            Antennae(g, p, ink);
            if (!p.ArmsFront) Arms(g, p, ink, thin);
            Body(g, p, ink, thin);
            if (p.ArmsFront) Arms(g, p, ink, thin);
            Props.AfterArms(g, 5, p, wristL, dirL, ink, new PointF(-4, ChestY - 4));
            g.Restore(upper);
            if (p.Sit) Legs(g, p, ink);
        }

        private void Legs(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                PointF hip = new PointF(sg * 14 + p.BodyX * .6f, HipY + p.BodyY - 2);
                PointF ankle = p.Sit ? new PointF(sg * (18 + 34) + fx, -9 + fy) : new PointF(sg * 17 + fx + sg * Math.Max(0, p.Knee) * .4f, -9 + fy);
                RU.LimbFill(g, new[] { hip, new PointF((hip.X + ankle.X) / 2, (hip.Y + ankle.Y) / 2), ankle }, new[] { 13f, 11f, 10f }, false, RU.B(cSk), ink);
                var st = RU.Rot(g, ankle.X, ankle.Y, right ? -rot : rot);
                RU.Ell(g, RU.B(cSkS), ankle.X - 7, ankle.Y + 3, 14, 7, ink);
                g.Restore(st);
            }
        }

        private void Body(Graphics g, Pose p, Pen ink, Pen thin)
        {
            float s = p.Turn * 8;
            // 콩 모양 몸
            PointF[] body = Geo.Smooth(new[] { P(-32, -186), P(0, -200), P(32, -186), P(42, -146), P(38, -100), P(26, -60), P(0, -44), P(-26, -60), P(-38, -100), P(-42, -146) }, true, 6);
            RU.FillOutline(g, body, RU.B(cSk), ink);
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY); RU.Rot(g, 0, -120, p.HeadTilt * .6f);
            // 외눈 + 굵은 눈썹
            PointF ec = P(-3 - s, -152);
            RU.Eye(g, p, ec, 29, 29, Math.Max(p.OpenL, p.OpenR), 1, cIris, .56f, .26f, ink, false, RU.B(cSclera));
            float br = p.Brow * 7 + 5;
            var brow = RU.Pn(24, 22, 26, 8f);
            g.DrawLine(brow, ec.X - 32, ec.Y - 28 + br, ec.X + 2, ec.Y - 34 - br * .2f);
            g.DrawLine(brow, ec.X + 2, ec.Y - 34 - br * .2f, ec.X + 28, ec.Y - 26 + br * .4f);
            // 큰 입
            RU.Mouth(g, p, P(-3 - s, -104), 60, ink, false, true);
            if (p.Flush > .01f) g.FillEllipse(RU.B(255, 120, 120, (int)(110 * p.Flush)), -36 - s, -118, 16, 9);
            if (p.Goggles) RU.GogglesFace(g, -3 - s, -152, 0, 26, 50);
            if (p.Tears >= 0) RU.Tears(g, P(ec.X - 16, ec.Y + 24), P(ec.X + 16, ec.Y + 24), p.Tears, 3.6f, Color.FromArgb(150, 210, 255), false);
            if (p.Sweat >= 0) RU.Sweat(g, P(44, -190), p.Sweat, 7);
            if (p.Anger > .01f) { RU.Anger(g, P(-44, -206), 14, p.Anger); RU.Anger(g, P(46, -208), 11, p.Anger + .3f); }
            g.Restore(st);
        }

        // 안테나: 두 가닥, 검은 마디. EarL/EarR 로 휘어진다.
        private void Antennae(Graphics g, Pose p, Pen ink)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; float sg = right ? 1 : -1;
                PointF b = P(sg * 13 + p.HeadX * .5f, -194 + p.HeadY);
                float rest = sg * (right ? 10 : 24);
                float a1 = (rest + (right ? p.EarR : p.EarL) + p.HeadTilt) * (float)Math.PI / 180, a2 = a1 * 1.35f + sg * .06f * (float)Math.Sin(p.TailWavePhase + side) ;
                PointF m = new PointF(b.X + (float)Math.Sin(a1) * 50, b.Y - (float)Math.Cos(a1) * 50);
                PointF t = new PointF(m.X + (float)Math.Sin(a2) * 56, m.Y - (float)Math.Cos(a2) * 56);
                var spine = new[] { b, m, t };
                var poly = Geo.Limb(spine, new[] { 8f, 7f, 5f }, true, 6);
                g.FillPolygon(RU.B(cAnt), poly); g.DrawPolygon(RU.Pn(RU.InkColor, 2.4f), poly);
                // 검은 마디
                PointF[] sm = Geo.Smooth(spine, false, 10);
                foreach (float f in new[] { .22f, .4f, .58f, .76f, .92f })
                {
                    int i = Math.Min(sm.Length - 2, (int)(f * (sm.Length - 1)));
                    PointF q = sm[i], q2 = sm[i + 1]; float dx = q2.X - q.X, dy = q2.Y - q.Y, l = (float)Math.Sqrt(dx * dx + dy * dy); if (l < 1e-3) continue;
                    float nx = -dy / l * 8, ny = dx / l * 8;
                    g.DrawLine(RU.Pn(20, 20, 22, 3.2f), q.X - nx, q.Y - ny, q.X + nx, q.Y + ny);
                }
            }
        }

        private void Arms(Graphics g, Pose p, Pen ink, Pen thin)
        {
            PointF[] wr = new PointF[2], dr = new PointF[2];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int sgn = right ? 1 : -1;
                PointF S = P(sgn * 34, -126);
                float A = ArmRestA + (right ? p.ArmRA : p.ArmLA), B = ArmRestB + (right ? p.ArmRB : p.ArmLB);
                PointF? to = p.Clasp ? P(sgn * 8, ChestY + 4) : Abs(right ? p.HandRTo : p.HandLTo);
                PointF E, W, d; RU.Arm(S, A, B, 32, 30, sgn, to, out E, out W, out d);
                RU.LimbFill(g, new[] { S, E, W }, new[] { 10f, 9f, 8f }, false, RU.B(cSk), thin);
                wr[side] = W; dr[side] = d;
            }
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; HandStyle hs = right ? p.HandR : p.HandL;
                RU.Hand(g, wr[side], dr[side], hs == HandStyle.Fist, p.Claw, RU.B(cSk), thin, 17, 3, true);
            }
            wristL = wr[0]; dirL = dr[0];
        }
    }
}
