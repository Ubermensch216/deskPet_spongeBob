using System;
using System.Drawing;

namespace BikiniFriends
{
    /// <summary>
    /// 상태마다 캐릭터의 자세(Pose)를 만든다. 발은 움직인 거리에 맞춰 걷고(미끄러지지 않음),
    /// 눈 깜빡임·시선·귀/안테나/눈자루·꼬리·숨쉬기는 모든 자세에 겹쳐 흐른다.
    /// 캐릭터마다 걸음걸이·손동작·표정·소품이 달라서, 같은 상태라도 "그 친구다운" 몸짓이 나온다.
    /// </summary>
    internal sealed partial class Pet
    {
        // ───────── 공통 도구 ─────────
        private const int Sp = 0, Pa = 1, Sq = 2, Sa = 3, Kr = 4, Pl = 5;   // 스폰지밥 · 뚱이 · 징징이 · 다람이 · 집게사장 · 플랑크톤
        private CharRig Rg { get { return Art.Rig; } }
        private int Ci { get { return Art.Id; } }

        private static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        private static float F(double v) { return (float)v; }
        private static double Tau { get { return Math.PI * 2; } }

        private double Hash01(double seg)
        { double v = Math.Sin(seg * 12.9898 + blinkOffset * 78.233) * 43758.5453; return v - Math.Floor(v); }

        /// <summary>팔 각도: abs 는 "아래를 0 으로 바깥쪽이 +" 인 절대 각도, q 는 기본 자세에서 얼마나 가는지(0..1).</summary>
        private float ArmA(double abs, double q) { return F((abs - Rg.RestA) * q); }
        private float ArmB(double abs, double q) { return F((abs - Rg.RestB) * q); }
        private void SetArms(Pose P, double aL, double bL, double aR, double bR)
        { P.ArmLA = ArmA(aL, 1); P.ArmLB = ArmB(bL, 1); P.ArmRA = ArmA(aR, 1); P.ArmRB = ArmB(bR, 1); }
        private void ArmsBlend(Pose P, double aL, double bL, double aR, double bR, double q)
        { P.ArmLA = ArmA(aL, q); P.ArmLB = ArmB(bL, q); P.ArmRA = ArmA(aR, q); P.ArmRB = ArmB(bR, q); }

        // 걸음 위상(걸음 한 주기 = 발 두 번). 움직인 거리로 올려서 발이 땅에서 미끄러지지 않는다.
        private double gait, lastGaitX, lastGaitY;
        private bool gaitStarted;

        private void UpdateGait(double dt)
        {
            if (!gaitStarted) { lastGaitX = X; lastGaitY = Y; gaitStarted = true; }
            double moved = Math.Sqrt((X - lastGaitX) * (X - lastGaitX) + (Y - lastGaitY) * (Y - lastGaitY));
            lastGaitX = X; lastGaitY = Y;
            switch (State)
            {
                case PetState.Walk: case PetState.Gather: case PetState.Climb:
                case PetState.Wall: case PetState.WindowClimb: case PetState.Ceiling:
                    gait += Math.Min(moved, 130 * S * Math.Max(dt, .001)) / (Art.H * Art.Info.CycleFrac);
                    break;
                case PetState.Sliding:
                    gait += dt * (Ci == Kr ? 9 : 6.5);
                    break;
            }
        }

        /// <summary>눈 깜빡임 정도(1 = 활짝, 0 = 감음). 약 4초에 한 번, 가끔 두 번 연달아.</summary>
        private float BlinkOpen(double t)
        {
            double c = (t + blinkOffset) % 4.3;
            Func<double, double> shape = x => x < 0 ? 1 : x < .05 ? 1 - x / .05 : x < .09 ? 0 : x < .16 ? (x - .09) / .07 : 1;
            double o = shape(c);
            if (((int)((t + blinkOffset) / 4.3) % 3) == 0) o = Math.Min(o, shape(c - 1.1));
            return F(o);
        }

        private float Twitch(double t, double period, double seed)
        {
            double c = (t + seed) % period;
            if (c > .4) return 0;
            return F(Math.Sin(c * 36) * (1 - c / .4) * 10);
        }

        /// <summary>두리번거리는 시선: 2.4초마다 새 곳을 보고, 눈동자는 빠르게 옮겨 간다.</summary>
        private void Wander(Pose P, double t, double range)
        {
            double u = t / 2.4, seg = Math.Floor(u), e = Ease((u - seg) / .1);
            Func<int, double, double, double> pick = (salt, lo, hi) =>
            {
                double a = lo + Hash01(seg - 1 + salt) * (hi - lo), b = lo + Hash01(seg + salt) * (hi - lo);
                return a + (b - a) * e;
            };
            P.Gx = F(pick(0, -1, 1) * range);
            P.Gy = F(pick(100, -.45, .6) * Math.Min(1, range + .3));
        }

        // ───────── 기본 서 있는 자세 ─────────
        private void IdleBase(Pose P, double t, bool lookAround)
        {
            P.Breath = F(Math.Sin(animT * 2.0));
            P.OpenL = P.OpenR = BlinkOpen(animT);
            if (lookAround) Wander(P, t, 1); else { P.Gy = .1f; P.Gx = -.25f; }
            P.Turn = Ci == Kr ? 0f : .4f;
            P.HeadTilt = F(P.Gx * 2.2 + Math.Sin(t * .8) * 1.2);
            P.EarL = Twitch(animT, 5.3, blinkOffset * 1.7); P.EarR = Twitch(animT, 7.1, blinkOffset * 2.9 + 1.3);
            if (Ci == Kr) { P.EarL += F(Math.Sin(t * 1.9) * 5); P.EarR += F(Math.Sin(t * 1.9 + 1.2) * 5); }
            if (Ci == Pl) { P.EarL += F(Math.Sin(t * 2.7) * 4); P.EarR += F(Math.Sin(t * 2.2 + 1) * 4); P.Brow = .3f; }
            if (Ci == Sq) P.Brow = .12f;
            P.Tail = F(3 + Math.Sin(t * .9) * 4); P.TailWaveAmp = 9; P.TailWavePhase = F(t * 2.1);
            P.ArmLA = F(Math.Sin(t * 2.0 + .4) * 1.5); P.ArmRA = F(Math.Sin(t * 2.0 + .1) * 1.5);
            P.ArmLB = F(Math.Sin(t * 2.0) * 2); P.ArmRB = F(Math.Sin(t * 2.0 + .3) * 2);
            P.Mouth = Ci == Sq ? MouthStyle.Neutral : MouthStyle.Smile;
            if (Ci == Kr) P.Claw = F(.18 + .1 * Math.Sin(t * 1.6));
            if (Ci == Pa) { P.Mouth = MouthStyle.Open; P.MouthOpen = .35f; P.Tongue = .3f; }
            if (Ci == Pl) { P.Mouth = MouthStyle.Open; P.MouthOpen = F(.3 + .08 * Math.Sin(t * 5)); }
        }

        private void SitDown(Pose P)
        {
            P.Sit = true; P.BodyY = Rg.SitDrop; P.Knee = 6;
            P.FootLX = P.FootRX = 0; P.FootLY = P.FootRY = 0; P.FootLRot = P.FootRRot = 8;
        }

        // ───────── 걷기 ─────────
        private struct WStyle
        {
            public float S, H, Swing, Lean, Bob, Sway, Turn;
            public WStyle(float s, float h, float sw, float l, float b, float y, float t) { S = s; H = h; Swing = sw; Lean = l; Bob = b; Sway = y; Turn = t; }
        }
        // 걸음폭 · 발 드는 높이 · 팔 흔들기 · 앞 기울임(−) · 위아래 출렁임 · 좌우 뒤뚱 · 얼굴 돌림
        private static readonly WStyle[] WS =
        {
            new WStyle(20, 18, 40, -2, 5.5f, 0, 1f),      // 스폰지밥: 통통 튀는 신나는 걸음, 팔을 크게 흔든다
            new WStyle(15, 9, 14, 0, 3.4f, 7.5f, .6f),    // 뚱이: 뒤뚱뒤뚱 오리걸음, 팔은 느슨하게
            new WStyle(26, 11, 10, 3, 1.4f, 0, .85f),     // 징징이: 뻣뻣하고 긴 보폭, 팔은 거의 안 흔든다
            new WStyle(22, 15, 28, -2, 3.8f, 0, .85f),    // 다람이: 자신만만하고 씩씩하게
            new WStyle(10, 7, 12, 0, 2.2f, 5.5f, 0f),     // 집게사장: 몸은 정면 그대로 옆걸음으로 후다닥
            new WStyle(11, 8, 22, -8, 3.2f, 0, .85f),     // 플랑크톤: 몸을 숙이고 종종걸음(음모 꾸미는 자세)
        };

        /// <summary>발 한 쪽의 걸음: 땅에서 뒤로 밀고(60%) → 들어 앞으로 내딛는다(40%).</summary>
        private static void Foot(double phase, float S, float H, out float dx, out float dy, out float rot)
        {
            double th = phase - Math.Floor(phase), fwd, lift = 0, r;
            const double stance = .6;
            if (th < stance) { double u = th / stance; fwd = S * (1 - 2 * u); r = 8 - 20 * u; }
            else
            {
                double u = (th - stance) / (1 - stance), e = (1 - Math.Cos(Math.PI * u)) / 2;
                fwd = -S + 2 * S * e; lift = Math.Sin(Math.PI * u); r = -12 + 26 * Math.Sin(Math.PI * Math.Min(1, u * 1.15));
            }
            dx = F(-fwd);                // 그림은 왼쪽(−x)이 앞
            dy = F(-H * lift); rot = F(r);
        }

        private void ApplyWalk(Pose P, double g, double ampD) { ApplyWalk(P, g, ampD, WS[Ci]); }

        private void ApplyWalk(Pose P, double g, double ampD, WStyle st)
        {
            float amp = F(ampD); if (amp <= .005f) return;
            float dx, dy, rot;
            Foot(g, st.S, st.H, out dx, out dy, out rot); P.FootLX += dx * amp; P.FootLY += dy * amp; P.FootLRot += rot * amp;
            Foot(g + .5, st.S, st.H, out dx, out dy, out rot); P.FootRX += dx * amp; P.FootRY += dy * amp; P.FootRRot += rot * amp;
            double ph = g * Tau;
            float bob = F(-Math.Abs(Math.Sin(ph)) * st.Bob);
            P.BodyY += bob * amp; P.HeadY += -bob * .35f * amp;
            P.Lean += (st.Lean + F(Math.Sin(ph) * 1.6)) * amp;
            P.BodyX += F(Math.Sin(ph) * (1.2 + st.Sway)) * amp;
            // 팔: 걸음과 반대로 번갈아 흔든다(위팔이 앞서고 아래팔이 한 박자 늦게 따라온다)
            P.ArmLA += F(Math.Sin(ph) * st.Swing) * amp; P.ArmRA -= F(Math.Sin(ph) * st.Swing) * amp;
            P.ArmLB += F(Math.Sin(ph - .8) * st.Swing * 1.15) * amp; P.ArmRB -= F(Math.Sin(ph - .8) * st.Swing * 1.15) * amp;
            P.Tail += F(Math.Sin(ph - 1.0) * 7) * amp; P.TailCurl += F(10 * amp); P.TailWaveAmp += 8 * amp; P.TailWavePhase = F(ph);
            P.EarL += F(Math.Sin(2 * ph - .8) * 4) * amp; P.EarR += F(Math.Sin(2 * ph - 1.1) * 4) * amp;
            P.HeadTilt += F(Math.Sin(ph + .5) * 1.5) * amp;
            switch (Ci)
            {
                case Pa: P.HeadTilt += F(Math.Sin(ph) * 5) * amp; P.Lean += F(Math.Sin(ph) * 4) * amp; break;       // 뒤뚱뒤뚱
                case Sq: P.HeadTilt += F(-4 * amp); P.HeadY += F(2 * amp); P.Mouth = MouthStyle.Frown; break;       // 고개 숙이고 투덜투덜
                case Kr: P.Claw = F(.25 + .55 * Math.Abs(Math.Sin(ph * 2)) * amp); P.EarL += F(Math.Sin(ph * 2) * 8) * amp; P.EarR -= F(Math.Sin(ph * 2) * 8) * amp; break;   // 집게를 딱딱
                case Pl: P.ArmLA = ArmA(40, 1) + F(Math.Sin(ph) * 6) * amp; P.ArmRA = ArmA(40, 1) - F(Math.Sin(ph) * 6) * amp; P.ArmLB = ArmB(-10, 1); P.ArmRB = ArmB(-10, 1); P.HeadY += F(3 * amp); break;   // 뒷짐
                case Sa: P.Tail += F(Math.Sin(ph * 2) * 5) * amp; break;
            }
            if (amp > .35f) { P.Turn = st.Turn; P.Gx = Ci == Kr ? 0 : -.35f; P.Gy = 0; }
        }

        // ───────── 상태별 자세 ─────────
        private void SquishPose(Pose P, ref Look L)
        {
            double k = Math.Sin(Math.PI * Math.Min(1, StateT / squishDur));
            bool poke = squishAmp > .2;        // 클릭해서 눌린 것(착지·부딪힘보다 크게 눌림)
            L.Sx = F(1 + .32 * squishAmp * k); L.Sy = F(1 - .32 * squishAmp * k);
            if (poke)
            {
                // 콕 찔려 발끈: 몸을 웅크리고 주먹을 쥐며 💢
                IdleBase(P, animT, false);
                P.BodyY = F(16 * k); P.Knee = F(12 * k); P.Lean = F(-4 * k);
                P.HandL = P.HandR = HandStyle.Fist;
                P.Brow = F(.95 * Math.Min(1, k * 2.2)); P.Mouth = MouthStyle.Frown; P.PupilR = F(.85 - .15 * k);
                P.Gx = -.2f; P.Gy = 0; P.Anger = F(Math.Min(1, StateT * 3 + .15)) * (k > .05 ? 1f : 0f);
                P.EarL = F(-10 * k); P.EarR = F(10 * k); P.Tail = F(-14 * k); P.TailWaveAmp = 16; P.TailWavePhase = F(animT * 26);
                lookJitterX = Math.Sin(StateT * 70) * .5 * S * k;
                P.HeadY = F(3 * k);
                switch (Ci)
                {
                    case Sp: ArmsBlend(P, 36, 20, 36, 20, k); P.FootLY = F(-9 * Math.Abs(Math.Sin(StateT * 26)) * k); P.FootRY = F(-9 * Math.Abs(Math.Cos(StateT * 26)) * k); break;   // 발을 쿵쿵 구른다
                    case Pa: ArmsBlend(P, 150, 165, 150, 165, k); P.Mouth = MouthStyle.Open; P.MouthOpen = F(.7 * k); break;                                                // 두 팔을 번쩍 들고 으르렁
                    case Sq: ArmsBlend(P, 42, -70, 42, -70, k); P.Mouth = MouthStyle.Frown; P.HeadTilt += F(-6 * k); break;                                                // 팔짱 끼고 노려본다
                    case Sa: ArmsBlend(P, 70, 170, 95, 170, k); P.Mouth = MouthStyle.Open; P.MouthOpen = F(.5 * k); break;                                                 // 가라테 자세
                    case Kr: ArmsBlend(P, 100, 140, 100, 140, k); P.Claw = F(.2 + .8 * Math.Abs(Math.Sin(StateT * 30)) * k); break;                                        // 집게를 딱딱
                    case Pl: ArmsBlend(P, 150, 168, 150, 168, k); P.HeadY += F(-3 * k); P.Mouth = MouthStyle.Open; P.MouthOpen = F(.6 * k); break;                         // 주먹을 부들부들
                }
                return;
            }
            // 착지·부딪힘: 무릎을 굽히며 팔로 균형을 잡는다
            IdleBase(P, animT, false);
            P.BodyY = F(20 * k * Math.Min(1.6, squishAmp / .15)); P.Knee = F(14 * k);
            P.FootLRot = P.FootRRot = F(4 * k);
            P.ArmLA += F(26 * k); P.ArmRA += F(26 * k); P.ArmLB += F(26 * k); P.ArmRB += F(26 * k);
            P.OpenL = P.OpenR = F(1 - .7 * k * Math.Min(1, StateT * 8));
            P.EarL = F(10 * k); P.EarR = F(-10 * k);
            P.Tail = F(-10 * k);
            P.Mouth = k > .4 ? MouthStyle.Pout : P.Mouth;
        }

        private void ThrownPose(Pose P)
        {
            // 팔다리를 쫙 벌리고 날아간다
            double sh = Math.Sin(StateT * 22);
            P.Breath = 0; P.OpenL = P.OpenR = 1; P.EyeScale = 1.08f; P.PupilR = .7f; P.Gy = -.3f; P.Gx = 0; P.Turn = 0;
            SetArms(P, 128 + sh * 8, 150 + sh * 10, 128 - sh * 8, 150 - sh * 10);
            P.FootLX = -16; P.FootRX = 16; P.FootLY = -12; P.FootRY = -10; P.FootLRot = -8; P.FootRRot = -8; P.Knee = 6;
            P.Mouth = MouthStyle.Open; P.MouthOpen = F(.55 + sh * .1);
            P.EarL = F(-26 + sh * 6); P.EarR = F(26 - sh * 6);
            P.Tail = F(-20 + sh * 6); P.TailCurl = -16; P.TailWaveAmp = 18; P.TailWavePhase = F(StateT * 24);
            P.Brow = -.3f; P.Sweat = F(StateT); P.Claw = 1f;
        }

        private void FallingPose(Pose P)
        {
            // 낙하 도구에 매달려 천천히 흔들리며 내려온다: 도구마다 붙잡는 모습이 다르다
            double t = StateT, sway = Math.Sin(t * 2.4);
            P.Breath = F(Math.Sin(t * 3));
            P.OpenL = P.OpenR = BlinkOpen(t); P.Gy = -.95f; P.Gx = F(Math.Sin(t * 1.3) * .2); P.Turn = 0;
            P.PupilR = .85f;
            SetArms(P, 96 + sway * 6, 112 + sway * 10, 96 - sway * 6, 112 - sway * 10);
            P.FootLX = F(-5 - sway * 4); P.FootRX = F(5 - sway * 4); P.FootLY = F(-9 + Math.Sin(t * 3.1) * 3); P.FootRY = F(-9 + Math.Sin(t * 3.1 + 1.3) * 3);
            P.FootLRot = F(-14 + sway * 5); P.FootRRot = F(-14 - sway * 5); P.Knee = 5;
            double flutter = Math.Sin(t * 42);
            P.EarL = F(-12 + flutter * 7); P.EarR = F(12 - flutter * 7);
            P.Tail = F(-6 + sway * 8); P.TailWaveAmp = 20; P.TailWavePhase = F(t * 9); P.TailCurl = 12;
            P.Mouth = MouthStyle.Open; P.MouthOpen = .35f;
            switch (Ci)
            {
                case Sp: SetArms(P, 150 + sway * 6, 168, 150 - sway * 6, 168); P.Mouth = MouthStyle.Open; P.MouthOpen = F(.5 + .1 * Math.Sin(t * 5)); break;                  // 해파리를 올려다보며 와~
                case Pa: SetArms(P, 128 + sway * 8, 140, 128 - sway * 8, 140); P.Mouth = MouthStyle.Open; P.MouthOpen = .6f; break;                                           // 모자를 쓰고 팔을 활짝
                case Sq: SetArms(P, 166, 172, 166, 172); P.Mouth = MouthStyle.Frown; P.Brow = .3f; P.EarL = P.EarR = 0; break;                                                  // 우산 손잡이를 꽉 붙잡고 시무룩
                case Sa: SetArms(P, 100 + sway * 6, 112, 178, 178); P.Mouth = MouthStyle.Open; P.MouthOpen = .5f; break;                                                        // 한 손으로 올가미를 돌린다
                case Kr: SetArms(P, 120, 150, 120, 150); P.Claw = F(.5 + .5 * Math.Sin(t * 6)); P.Mouth = MouthStyle.Frown; P.Brow = .35f; break;                               // 낙하산 줄을 잡고 걱정
                case Pl: SetArms(P, 160, 172, 160, 172); P.Mouth = MouthStyle.Open; P.MouthOpen = .45f; break;
            }
            if (IsPilotFlight) { P.Goggles = true; P.Mouth = MouthStyle.Smile; P.Gy = -.2f; P.Gx = 0; }
        }

        private void DragPose(Pose P)
        {
            // 들려 올라가서 허우적: 흔들리는 방향의 반대로 팔다리가 늘어지고 발을 버둥거린다
            double sw = Math.Max(-1, Math.Min(1, angle / 30)), t = animT;
            double kick = Math.Sin(t * 7.5);
            P.Breath = F(Math.Sin(t * 5)); P.Turn = 0;
            P.OpenL = P.OpenR = BlinkOpen(t); P.EyeScale = 1.06f; P.PupilR = .75f; P.Gy = -.35f; P.Gx = F(-sw * .6);
            SetArms(P, 105 + kick * 14 + sw * 14, 130 + kick * 18, 105 - kick * 14 - sw * 14, 130 - kick * 18);
            P.FootLX = F(-5 + kick * 7 - sw * 3); P.FootRX = F(5 - kick * 7 - sw * 3);
            P.FootLY = F(-14 - kick * 3); P.FootRY = F(-14 + kick * 3); P.FootLRot = F(-16 + kick * 6); P.FootRRot = F(-16 - kick * 6); P.Knee = 4;
            P.Mouth = MouthStyle.Scared; P.MouthOpen = F(.6 + .3 * Math.Sin(t * 9));
            P.EarL = F(-14 - sw * 10 + Math.Sin(t * 11) * 5); P.EarR = F(14 - sw * 10 - Math.Sin(t * 11) * 5);
            P.Tail = F(sw * 28); P.TailCurl = F(-sw * 26); P.TailWaveAmp = 14; P.TailWavePhase = F(t * 8);
            P.Sweat = F(t); P.Claw = F(.5 + .5 * kick);
            if (Ci == Sq) { P.Mouth = MouthStyle.Frown; P.Brow = -.3f; }
        }

        private void SleepPose(Pose P, double t)
        {
            SitDown(P);
            P.Eyes = EyeStyle.Sleep; P.OpenL = P.OpenR = 0; P.Turn = 0;
            double nod = Math.Sin(t * 1.15);
            P.Breath = F(Math.Sin(t * 1.7) * 1.4);
            P.HeadTilt = F(7 + nod * 3.5); P.HeadY = F(9 + nod * 2); P.HeadX = F(-3);
            P.Mouth = MouthStyle.Neutral; P.Gy = 0;
            P.EarL = F(-6 + Math.Sin(t * 1.15) * 1.5); P.EarR = F(6 + Math.Sin(t * 1.15) * 1.5);
            P.ArmsFront = true; P.HandLTo = new PointF(-14, 14); P.HandRTo = new PointF(14, 14);
            P.Tail = F(24 + Math.Sin(t * 1.7) * 2); P.TailCurl = 36; P.TailWaveAmp = 3; P.TailWavePhase = F(t * 1.7);
            P.Flush = .35f;
            switch (Ci)
            {
                case Pa: P.Mouth = MouthStyle.Open; P.MouthOpen = F(.25 + .1 * Math.Sin(t * 1.7)); P.HandLTo = P.HandRTo = null; P.ArmsFront = false; P.ArmLA = ArmA(70, 1); P.ArmRA = ArmA(70, 1); break;   // 입 벌리고 쿨쿨
                case Kr: P.EarL = F(-30 + nod * 3); P.EarR = F(30 + nod * 3); P.Claw = .1f; P.HandLTo = new PointF(-26, 4); P.HandRTo = new PointF(-34, 14); break;                                          // 돈주머니를 끌어안고
                case Pl: P.EarL = F(-34); P.EarR = F(34); break;
                case Sq: P.Mouth = MouthStyle.Frown; break;
            }
        }

        private void StretchPose(Pose P, double q)
        {
            // q: 0..1..0 로 폈다가 접는 정도. 팔을 쭉 올리고 하품하며 몸을 뒤로 젖힌다.
            IdleBase(P, animT, false);
            P.Eyes = q > .25 ? EyeStyle.Sleep : EyeStyle.Open; P.OpenL = P.OpenR = q > .25 ? 0 : 1;
            ArmsBlend(P, 172, 178, 172, 178, q);
            P.Lean = F(7 * q); P.HeadTilt = F(9 * q); P.BodyY = F(-4 * q); P.HeadY = F(-2 * q);
            P.FootLY = P.FootRY = F(-5 * q); P.FootLRot = P.FootRRot = F(-18 * q);
            P.Mouth = MouthStyle.Open; P.MouthOpen = F(Math.Max(0, q - .15) * 1.1); if (q < .2) P.Mouth = Ci == Sq ? MouthStyle.Neutral : MouthStyle.Smile;
            P.Tail = F(-24 * q); P.TailCurl = F(-20 * q); P.EarL = F(14 * q); P.EarR = F(-14 * q);
            if (Ci == Kr) P.Claw = F(.2 + .8 * q);
            if (q > .6) { P.ArmLA += F(Math.Sin(animT * 30) * 1.5); P.ArmRA -= F(Math.Sin(animT * 30) * 1.5); }
        }

        private void DaydreamPose(Pose P, double t)
        {
            SitDown(P);
            P.Breath = F(Math.Sin(t * 1.1) * .6);
            P.OpenL = P.OpenR = BlinkOpen(t * .6);
            P.Gx = F(Math.Sin(t * .45) * .55); P.Gy = F(-.55 + Math.Sin(t * .31) * .2); P.Turn = .2f;
            P.HeadTilt = F(-4 + Math.Sin(t * .7) * 3); P.HeadY = F(-2);
            P.Mouth = MouthStyle.Pout; P.MouthOpen = .3f;
            P.EarL = Twitch(t, 6.2, 1.3) * .6f; P.EarR = Twitch(t, 8.4, 4.1) * .6f + F(Math.Sin(t * .9) * 2);
            P.Tail = F(14 + Math.Sin(t * .8) * 8); P.TailCurl = 22; P.TailWaveAmp = 7; P.TailWavePhase = F(t * 1.6);
            P.ArmLA = F(Math.Sin(t * .6) * 3); P.ArmRA = F(-Math.Sin(t * .6) * 3);
            if (Ci == Pa) { P.Mouth = MouthStyle.Open; P.MouthOpen = .3f; }
            if (Ci == Kr) P.Claw = F(.2 + .1 * Math.Sin(t));
        }

        private void SitIdlePose(Pose P, double t)
        {
            SitDown(P);
            IdleBase(P, t, true);
            P.Sit = true; P.BodyY = Rg.SitDrop;
            P.ArmLA = F(Math.Sin(t * 2.0) * 1.5); P.ArmRA = F(Math.Sin(t * 2.0 + .3) * 1.5);
            P.Tail = F(16 + Math.Sin(t * 1.1) * 6); P.TailCurl = 26; P.TailWaveAmp = 6; P.TailWavePhase = F(t * 1.9);
            // 가끔 한 번 크게 하품
            double yc = (animT + blinkOffset * 2) % 11;
            if (yc < 1.7)
            {
                double q = Math.Sin(Math.PI * yc / 1.7);
                P.Mouth = MouthStyle.Open; P.MouthOpen = F(q * .9); P.Eyes = q > .5 ? EyeStyle.Sleep : EyeStyle.Open; P.OpenL = P.OpenR = q > .5 ? 0 : 1;
                P.HeadTilt = F(P.HeadTilt - 5 * q); P.ArmLA += F(30 * q); P.ArmRA += F(30 * q);
            }
        }

        private void EatPose(Pose P)
        {
            SitDown(P);
            double chew = Math.Sin(animT * 2.4 * Tau), prog = Math.Min(1, StateT / Math.Max(1, extraDuration));
            P.Breath = F(chew * .3); P.Turn = 0;
            P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Mouth = MouthStyle.Neutral;
            P.HeadY = F(1.5 + chew * 1.6); P.HeadTilt = F(chew * 1.4);
            P.ArmsFront = true; P.HandLTo = new PointF(-22, -2); P.HandRTo = new PointF(8, -2);
            P.Food = F(prog); P.Flush = .7f;
            P.EarL = F(-3 + chew * 3); P.EarR = F(3 - chew * 3);
            P.Tail = F(14 + Math.Sin(animT * 3.4) * 9); P.TailCurl = 24; P.TailWaveAmp = 9; P.TailWavePhase = F(animT * 6);
            switch (Ci)
            {
                case Sq: P.Eyes = EyeStyle.Sleep; P.Flush = .2f; P.Mouth = MouthStyle.Frown; break;          // 투덜대며 먹는다
                case Kr: P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.EyesDollar = true; P.Claw = F(.5 + .5 * Math.Abs(chew)); P.Mouth = MouthStyle.Smile; break;   // 금화를 깨물어 본다
                case Pl: P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.Gx = .6f; P.Gy = 0; P.Brow = .4f; P.Flush = .2f; break;   // 눈치를 보며 몰래 먹는다
            }
        }

        private void HungryCryPose(Pose P, double t)
        {
            SitDown(P);
            double sob = Math.Sin(t * 9);
            P.Breath = F(Math.Sin(t * 5) * .8); P.Turn = 0;
            P.OpenL = P.OpenR = 1; P.EyeScale = 1.04f; P.PupilR = .9f; P.Gy = .5f; P.Gx = 0;
            P.Brow = -.85f; P.Mouth = MouthStyle.Frown; P.MouthOpen = 0;
            P.HeadTilt = F(-4 + sob * 1.2); P.HeadY = F(5 + Math.Abs(sob) * 1.5);
            P.ArmsFront = true; P.HandLTo = new PointF(-14, 8); P.HandRTo = new PointF(14, 8);
            P.Tears = F(t); P.EarL = F(-16 + sob * 2); P.EarR = F(16 - sob * 2);
            P.Tail = F(30); P.TailCurl = 12; P.TailWaveAmp = 3; P.TailWavePhase = F(t * 4);
            lookJitterX = Math.Sin(t * 21) * .45 * S;
            if (Ci == Pa) { P.Mouth = MouthStyle.Open; P.MouthOpen = .7f; P.HandLTo = P.HandRTo = null; P.ArmsFront = false; P.ArmLA = ArmA(40, 1); P.ArmRA = ArmA(40, 1); }
            if (Ci == Sp) { P.Mouth = MouthStyle.Open; P.MouthOpen = F(.8 + .1 * sob); }
        }

        private void TouchCryPose(Pose P)
        {
            // 놀라 눈이 커지고 눈동자가 위로 올라가며 달달 떨며 운다.
            double t = StateT, shiver = Math.Sin(t * 52);
            P.Breath = F(Math.Sin(t * 14) * .8); P.Turn = 0;
            P.OpenL = P.OpenR = F(t < .12 ? 1 : BlinkOpen(t + 1.7)); P.EyeScale = 1.1f; P.PupilR = .62f; P.Gy = -.78f; P.Gx = F(Math.Sin(t * 3.1) * .25);
            P.Brow = -.7f; P.Mouth = MouthStyle.Scared; P.MouthOpen = F(.5 + .5 * Math.Sin(t * 18));
            P.Clasp = true; P.ArmsFront = true;
            P.EarL = F(-14 + shiver * 2); P.EarR = F(14 - shiver * 2);
            P.HeadTilt = F(-3 + Math.Sin(t * 9) * 1.6); P.HeadY = F(4); P.BodyY = F(5 + Math.Abs(shiver) * .6);
            P.Knee = 5; P.FootLRot = P.FootRRot = 5;
            P.Tears = F(t); P.Sweat = F(t);
            P.Tail = F(26); P.TailCurl = 16; P.TailWaveAmp = 6; P.TailWavePhase = F(t * 30);
            if (t < .18) { double q = Math.Sin(t / .18 * Math.PI); P.BodyY += F(7 * q); P.EyeScale = F(1.1 + .08 * q); }
            switch (Ci)
            {
                case Sp:   // 엉엉 우는 스폰지밥: 입을 크게 벌리고 눈물이 줄줄, 두 손으로 눈가를 비빈다
                    P.Clasp = false; P.ArmsFront = false; SetArms(P, 150 + shiver * 3, 168, 150 - shiver * 3, 168);
                    P.Mouth = MouthStyle.Open; P.MouthOpen = F(.8 + .15 * Math.Sin(t * 14)); P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; break;
                case Pa:   // 뚱이: 입을 크게 벌리고 손을 부들부들
                    P.Mouth = MouthStyle.Open; P.MouthOpen = F(.7 + .2 * Math.Sin(t * 16)); break;
                case Sq:   // 징징이: 이를 악물고 징징
                    P.Mouth = MouthStyle.Frown; P.Brow = -.5f; break;
                case Kr:   // 집게사장: 돈을 잃고 눈물이 금빛으로
                    P.Claw = F(.4 + .4 * Math.Sin(t * 20)); P.EyesDollar = t < .5; break;
            }
        }

        private void HopPose(Pose P)
        {
            double t = StateT / extraDuration;
            IdleBase(P, animT, false);
            P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Mouth = Ci == Sq ? MouthStyle.Neutral : MouthStyle.Smile;
            if (t < .16)
            {
                double q = Math.Sin(t / .16 * Math.PI);                     // 웅크리기
                P.BodyY = F(20 * q); P.Knee = F(14 * q); ArmsBlend(P, 40, 30, 40, 30, q);
                P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.Mouth = MouthStyle.Neutral; P.Brow = F(.4 * q);
            }
            else if (t < .84)
            {
                double q = (t - .16) / .68, up = Math.Sin(q * Math.PI);     // 공중: 팔을 번쩍
                P.FootLY = F(-12 * up); P.FootRY = F(-12 * up); P.FootLRot = P.FootRRot = F(-14 * up); P.FootLX = F(-5 * up); P.FootRX = F(5 * up);
                P.EarL = F(10 * up); P.EarR = F(-10 * up); P.Tail = F(-18 * up);
                P.Mouth = MouthStyle.Open; P.MouthOpen = F(.55 * up); P.Lean = F(-3 * Math.Sin(q * Math.PI * 2));
                switch (Ci)
                {
                    case Sp: ArmsBlend(P, 168, 176, 168, 176, up); P.MouthOpen = F(.8 * up); break;                                 // "준비됐어요!" 두 팔 번쩍 아하하
                    case Pa: ArmsBlend(P, 150, 150, 150, 150, up); P.Lean = F(10 * Math.Sin(q * Math.PI * 2)); break;               // 별 모양으로 활짝
                    case Sq: ArmsBlend(P, 30, 10, 30, 10, up); P.Mouth = MouthStyle.Frown; P.MouthOpen = 0; P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = .8f; break;   // 마지못해 폴짝
                    case Sa: ArmsBlend(P, 176, 178, 130, 150, up); P.HandL = P.HandR = HandStyle.Fist; break;                       // 가라테 날아차기
                    case Kr: ArmsBlend(P, 130, 150, 130, 150, up); P.Claw = F(up); P.EyesDollar = true; P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; break;   // 번쩍 돈 눈
                    case Pl: ArmsBlend(P, 168, 176, 168, 176, up); P.Brow = .5f; P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; break;
                }
            }
            else
            {
                double q = Math.Sin((t - .84) / .16 * Math.PI);
                P.BodyY = F(18 * q); P.Knee = F(12 * q); P.ArmLA += F(20 * q); P.ArmRA += F(20 * q);
            }
        }

        private void CheerPose(Pose P, double now)
        {
            IdleBase(P, animT, false);
            P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Mouth = MouthStyle.Open; P.MouthOpen = .5f; P.Flush = .5f;
            if (now < cheerHopUntil)
            {
                double q = StateT % .42 / .42, up = Math.Sin(Math.PI * q);
                ArmsBlend(P, 165, 175, 165, 175, up);
                P.FootLY = F(-9 * up); P.FootRY = F(-9 * up); P.FootLRot = P.FootRRot = F(-12 * up);
                P.BodyY = F(q < .12 ? 10 * (1 - q / .12) : 0);
                P.EarL = F(8 * up); P.EarR = F(-8 * up); P.Tail = F(-14 * up);
                P.Lean = F(Math.Sin(q * Tau) * 2.5);
                if (Ci == Kr) P.Claw = F(up);
            }
            else { SetArms(P, 150, 170, 150, 170); P.Eyes = EyeStyle.Happy; if (Ci == Kr) P.Claw = 1; }
        }

        private void DashPose(Pose P)
        {
            // 도망: 몸을 숙이고 다리를 빠르게 구르며 혀를 내민다
            double g = gait, ph = g * Tau, t = animT;
            P.Turn = Ci == Kr ? 0f : 1f; P.Breath = 0;
            var st = WS[Ci];
            P.Lean = F(-12 + Math.Sin(ph) * 1.5); P.BodyY = F(-Math.Abs(Math.Sin(ph)) * 3 - 2);
            float dx, dy, rot;
            Foot(g, st.S * 1.6f, st.H * 1.5f, out dx, out dy, out rot); P.FootLX = dx; P.FootLY = dy; P.FootLRot = rot;
            Foot(g + .5, st.S * 1.6f, st.H * 1.5f, out dx, out dy, out rot); P.FootRX = dx; P.FootRY = dy; P.FootRRot = rot;
            SetArms(P, 120 + Math.Sin(ph) * 22, 142 + Math.Sin(ph - .6) * 24, 120 - Math.Sin(ph) * 22, 142 - Math.Sin(ph - .6) * 24);
            P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.EyeScale = 1.04f; P.Gx = -.9f; P.Gy = 0; P.PupilR = .85f;
            P.Mouth = MouthStyle.Open; P.MouthOpen = F(.45 + Math.Sin(ph * 2) * .06); P.Tongue = .85f;
            P.EarL = F(30 + Math.Sin(ph * 2) * 5); P.EarR = F(30 + Math.Sin(ph * 2 + .5) * 5);
            P.Tail = F(-8 + Math.Sin(ph - 1) * 6); P.TailCurl = -6; P.TailWaveAmp = 10; P.TailWavePhase = F(ph * 2);
            P.HeadTilt = F(-4); P.HeadX = -4; P.Sweat = F(t);
            switch (Ci)
            {
                case Sp: P.BodyY += F(-3 * Math.Abs(Math.Sin(ph))); break;
                case Pa: P.Lean += F(Math.Sin(ph) * 8); P.BodyX = F(Math.Sin(ph) * 6); break;                                   // 뒤뚱뒤뚱 전력 질주
                case Sq: P.Mouth = MouthStyle.Scared; P.MouthOpen = .7f; P.Lean = F(-6 + Math.Sin(ph) * 3); SetArms(P, 60 + Math.Sin(ph) * 10, 30, 60 - Math.Sin(ph) * 10, 30); break;   // 팔을 몸에 붙이고 후다닥
                case Sa: P.Lean = F(-16); SetArms(P, 140, 160, 60 + Math.Sin(ph) * 18, 60); P.HandL = P.HandR = HandStyle.Fist; break;                        // 한 팔을 쭉 뻗고 질주
                case Kr: P.Lean = 0; P.BodyY = F(-Math.Abs(Math.Sin(ph * 2)) * 4); P.Claw = F(.4 + .6 * Math.Abs(Math.Sin(ph * 3))); P.Tongue = 0; P.Mouth = MouthStyle.Scared; P.MouthOpen = .5f; break;   // 옆으로 쏜살같이 집게발을 딱딱
                case Pl: P.Lean = F(-18); P.Mouth = MouthStyle.Open; break;
            }
        }

        private void ClimbBody(Pose P, bool descending)
        {
            // 벽·천장·창을 타는 자세: 얼굴을 올라가는 쪽으로 돌리고 손발을 번갈아 뻗는다(몸 전체를 눕히는 회전은 Look 이 담당)
            IdleBase(P, animT, false);
            P.Turn = 1f; P.Gx = -.3f; P.Gy = 0; P.PupilR = .9f;
            ApplyWalk(P, gait, 1f, new WStyle(24, 20, 30, 0, 2, 0, 1f));
            P.ArmLA += 38; P.ArmRA += 38;
            P.Mouth = Ci == Sq ? MouthStyle.Frown : MouthStyle.Neutral;
            P.Tail = F(P.Tail + 6); P.Brow = F(.25);
            if (descending) P.Brow = 0;
        }

        private void DizzyPose(Pose P)
        {
            double t = StateT, sw = Math.Sin(t * 5.2);
            P.Eyes = EyeStyle.Spiral; P.SpiralT = F(t * 11); P.Turn = 0;
            P.Breath = F(Math.Sin(t * 4));
            P.BodyX = F(sw * 7); P.Lean = F(sw * 9); P.HeadTilt = F(-sw * 7 + Math.Sin(t * 3) * 3); P.HeadX = F(sw * 3);
            SetArms(P, 95 + Math.Sin(t * 5.2 + 1) * 28, 110 + Math.Sin(t * 5.2) * 20, 95 + Math.Sin(t * 5.2 + 3.1) * 28, 110 - Math.Sin(t * 5.2) * 20);
            float dx, dy, rot;
            Foot(t * 1.3, 10, 7, out dx, out dy, out rot); P.FootLX = dx; P.FootLY = dy; P.FootLRot = rot * .4f;
            Foot(t * 1.3 + .5, 10, 7, out dx, out dy, out rot); P.FootRX = dx; P.FootRY = dy; P.FootRRot = rot * .4f;
            P.Mouth = MouthStyle.Pout; P.MouthOpen = F(.5 + .4 * Math.Sin(t * 6));
            P.EarL = F(Math.Sin(t * 5.2 + .6) * 9); P.EarR = F(-Math.Sin(t * 5.2 + .6) * 9);
            P.Tail = F(-sw * 20); P.TailWaveAmp = 14; P.TailWavePhase = F(t * 8); P.Claw = F(.5 + .5 * sw);
        }

        private void DancePose(Pose P, double phase)
        {
            double hop = Math.Abs(Math.Sin(phase * Math.PI * 2.5)), sw = Math.Sin(phase * Math.PI * 2.5);
            IdleBase(P, animT, false);
            P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Mouth = MouthStyle.Open; P.MouthOpen = F(.45 + .2 * hop); P.Flush = .6f;
            bool leftUp = ((int)(phase / .3)) % 2 == 0;
            SetArms(P, leftUp ? 160 : 30, leftUp ? 172 : 20, leftUp ? 30 : 160, leftUp ? 20 : 172);
            P.Lean = F(sw * 6); P.HeadTilt = F(sw * 8); P.BodyY = F(-hop * 2);
            P.FootLY = F(-hop * 10 * (leftUp ? 1 : .3)); P.FootRY = F(-hop * 10 * (leftUp ? .3 : 1)); P.FootLRot = P.FootRRot = F(-10 * hop);
            P.EarL = F(hop * 10); P.EarR = F(-hop * 10); P.Tail = F(sw * 22); P.TailWaveAmp = 14; P.TailWavePhase = F(phase * 14);
            P.Claw = F(hop);
        }

        private void FlowerPose(Pose P, double now)
        {
            double t = now - flowerStarted;
            IdleBase(P, animT, false);
            P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Mouth = Ci == Sq ? MouthStyle.Neutral : MouthStyle.Smile; P.Flush = .8f; P.Turn = 0;
            P.ArmsFront = true; P.HandLTo = new PointF(-18, 4); P.HandRTo = new PointF(18, 4);
            P.HeadTilt = F(Math.Sin(t * 2.2) * 3.5); P.BodyY = F(-Math.Abs(Math.Sin(t * 3.2)) * 2.5);
            P.EarL = F(Math.Sin(t * 3.2) * 4); P.EarR = F(-Math.Sin(t * 3.2) * 4);
            P.Tail = F(Math.Sin(t * 4) * 10); P.TailWaveAmp = 16; P.TailWavePhase = F(t * 8);
            if (Ci == Kr) { P.EyesDollar = false; P.Eyes = EyeStyle.Happy; P.Claw = .15f; }
        }

        private void WaitPose(Pose P)
        {
            // 퇴근 시간을 기다리며 두리번, 발끝으로 톡톡
            IdleBase(P, animT, true);
            double tap = Math.Max(0, Math.Sin(animT * 6.5)) * (Math.Sin(animT * .9) > 0 ? 1 : 0);
            P.FootRY = F(-5 * tap); P.FootRRot = F(10 * tap);
            P.Mouth = Ci == Sq ? MouthStyle.Neutral : MouthStyle.Smile;
        }

        // ───────── 대표 동작(취미) ─────────
        /// <summary>
        /// 캐릭터마다 가장 "그 친구답다"는 동작. Look 쪽에서 장식(해파리·바위·음표·번개…)을 얹는다.
        /// 스폰지밥: 해파리 잡이 / 비눗방울, 뚱이: 바위 밑에 숨기, 징징이: 클라리넷 연주,
        /// 다람이: 가라테 / 올가미, 집게사장: 돈 세기, 플랑크톤: 음모 꾸미기(번개·악당 웃음).
        /// </summary>
        private void HobbyPose(Pose P, double t, out float aux)
        {
            aux = 0;
            IdleBase(P, animT, false);
            double d = Math.Max(1, extraDuration);
            switch (Ci)
            {
                case Sp:
                    if (hobbyVariant == 0)
                    {
                        // 그물: 뒤로 젖혔다가(0~1.1초) 휙 휘두르고(≈0.3초) 되돌아온다. 한 번에 2.6초.
                        double c = t % 2.6, swing;
                        if (c < 1.1) swing = -Ease(c / 1.1); else if (c < 1.4) swing = -1 + 2 * Ease((c - 1.1) / .3); else swing = 1 - Ease((c - 1.4) / 1.2);
                        P.Prop = Prop.Net; P.PropRot = 0; P.PropT = F(t);
                        // swing -1: 그물을 머리 뒤로 번쩍 / +1: 앞으로 쭉
                        P.ArmLA = ArmA(150 - 55 * (swing + 1) / 2 * 1.0, 1); P.ArmLB = ArmB(176 - 82 * (swing + 1) / 2, 1);
                        P.ArmRA = ArmA(60, 1); P.ArmRB = ArmB(40, 1); P.HandL = HandStyle.Fist;
                        P.Lean = F(-6 * (swing + 1) / 2); P.BodyY = F(Math.Abs(swing) * 2); P.Turn = 1f; P.Gx = -.8f; P.Gy = F(-.2 + .3 * swing);
                        P.Mouth = MouthStyle.Open; P.MouthOpen = F(.35 + .15 * Math.Sin(t * 6)); P.EyeScale = 1.06f;
                        P.FootLY = F(-6 * Math.Max(0, Math.Sin(t * 5))); P.FootRY = F(-6 * Math.Max(0, -Math.Sin(t * 5)));
                    }
                    else
                    {
                        // 비눗방울: 막대를 입 앞에 대고 후~ 불고 활짝 웃는다.
                        double c = t % 1.8, blow = c < .9 ? 1 : 0;
                        P.Prop = Prop.Wand; P.PropRot = -25; P.HandLTo = new PointF(-58, -16); P.ArmsFront = true;
                        P.ArmRA = ArmA(40, 1); P.ArmRB = ArmB(30, 1); P.Turn = 1f; P.Gx = -.6f; P.Gy = -.2f;
                        P.Mouth = blow > 0 ? MouthStyle.Pout : MouthStyle.Smile; P.HeadY = F(blow * 2); P.Breath = F(blow * -1.0);
                        P.HeadTilt = F(Math.Sin(t * 2) * 2);
                    }
                    break;
                case Pa:
                {
                    // 바위 밑에 쏙: 떨어져 덮고(1초) 숨어서 팔만 흔들다가 들려 올라간다
                    SitDown(P);
                    double drop = Ease(t / 1.0), lift = Ease((t - (d - 1.3)) / 1.0);
                    aux = F(t < d - 1.3 ? drop : 1 - lift);
                    P.Mouth = MouthStyle.Open; P.MouthOpen = .45f; P.Turn = 0;
                    double wave = Math.Sin(t * 5);
                    SetArms(P, 128 + wave * 20, 140 + wave * 24, 128 - wave * 20, 140 - wave * 24);
                    if (t > d - 1.4) { P.Eyes = EyeStyle.Open; P.EyeScale = 1.12f; P.Mouth = MouthStyle.Open; P.MouthOpen = .8f; }
                    break;
                }
                case Sq:
                {
                    // 클라리넷: 눈을 감고 심취해 몸을 흔들다 삑! 하는 순간 눈을 번쩍 뜬다
                    double sway = Math.Sin(t * 2.2), squeak = (t % 3.2) > 2.7 ? 1 : 0;
                    P.Prop = Prop.Clarinet; P.PropRot = 180; P.ArmsFront = true;
                    P.HandLTo = new PointF(-50, -22); P.HandRTo = new PointF(-30, -48);
                    P.Eyes = squeak > 0 ? EyeStyle.Open : EyeStyle.Happy; P.OpenL = P.OpenR = squeak > 0 ? 1 : 0; if (squeak > 0) { P.EyeScale = 1.08f; P.PupilR = .6f; P.Brow = -.4f; }
                    P.Lean = F(sway * 5); P.HeadTilt = F(-6 + sway * 3); P.Mouth = MouthStyle.Pout; P.Turn = .9f;
                    P.FootLY = F(-5 * Math.Max(0, Math.Sin(t * 4.4))); P.BodyY = F(Math.Abs(sway) * 1.5);
                    break;
                }
                case Sa:
                    if (hobbyVariant == 0)
                    {
                        // 가라테: 준비 → 손날 베기 → 높이 차기를 반복한다 (한 번에 1.2초)
                        double c = (t % 1.2) / 1.2; int n = (int)(t / 1.2) % 2;
                        P.HandL = P.HandR = HandStyle.Fist; P.Turn = .6f;
                        if (c < .3) { double q = Ease(c / .3); ArmsBlend(P, 70, 170, 70, 170, q); P.BodyY = F(10 * q); P.Knee = F(8 * q); P.Mouth = MouthStyle.Smile; }
                        else if (c < .55) { double q = Ease((c - .3) / .25); SetArms(P, 70 - 60 * q, 170 - 70 * q, 70 + 90 * q, 170 - 10 * q); P.Lean = F(-8 * q); P.Mouth = MouthStyle.Open; P.MouthOpen = .6f; P.Brow = .5f; }
                        else if (c < .8)
                        {
                            double q = Ease((c - .55) / .25); SetArms(P, 10 + 100 * q, 100 + 40 * q, 160 - 20 * q, 160);
                            P.FootLX = F(-30 * q); P.FootLY = F(-30 * q); P.FootLRot = F(-40 * q); P.Lean = F(10 * q); P.Mouth = MouthStyle.Open; P.MouthOpen = .8f; P.Brow = .5f;
                        }
                        else { double q = Ease((c - .8) / .2); ArmsBlend(P, 70, 170, 70, 170, 1 - q); P.Mouth = MouthStyle.Smile; }
                        if (n == 1) P.Turn = .9f;
                    }
                    else
                    {
                        // 올가미: 머리 위에서 빙빙 돌리며 신나게 웃는다
                        P.Prop = Prop.Lasso; P.PropT = F(t); P.Turn = .5f;
                        P.ArmLA = ArmA(172, 1); P.ArmLB = ArmB(176, 1); P.ArmRA = ArmA(40, 1); P.ArmRB = ArmB(80, 1); P.HandR = HandStyle.Fist;
                        P.Mouth = MouthStyle.Open; P.MouthOpen = .5f; P.Lean = F(Math.Sin(t * 3) * 3); P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0;
                        P.FootLY = F(-5 * Math.Max(0, Math.Sin(t * 6))); P.FootRY = F(-5 * Math.Max(0, -Math.Sin(t * 6)));
                    }
                    break;
                case Kr:
                {
                    // 돈 세기: 눈이 $ 로 변하고 지폐를 쓸어 담듯 집게를 비비며 좋아한다
                    double sn = Math.Sin(t * 8);
                    P.EyesDollar = true; P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1;
                    P.Prop = Prop.Cash; P.PropT = F(t);
                    P.ArmLA = ArmA(95 + sn * 8, 1); P.ArmLB = ArmB(120 + sn * 12, 1); P.ArmRA = ArmA(95 - sn * 8, 1); P.ArmRB = ArmB(120 - sn * 12, 1);
                    P.Claw = F(.3 + .3 * Math.Abs(sn)); P.Mouth = MouthStyle.Open; P.MouthOpen = .35f; P.Flush = .4f;
                    P.BodyY = F(Math.Abs(Math.Sin(t * 4)) * -3); P.Turn = 0;
                    P.EarL = F(Math.Sin(t * 7) * 10); P.EarR = F(-Math.Sin(t * 7) * 10);
                    break;
                }
                default:
                {
                    // 음모 꾸미기: 설계도를 펼치고 비열하게 웃다가(몸을 젖히며 으하하) 주먹을 불끈
                    double laugh = Math.Sin(t * 14), c = t % 3.0;
                    P.Prop = Prop.Blueprint; P.PropRot = -20; P.PropT = F(t);
                    P.ArmLA = ArmA(70, 1); P.ArmLB = ArmB(120, 1);
                    if (c > 1.6) { P.ArmRA = ArmA(165, 1); P.ArmRB = ArmB(172, 1); P.HandR = HandStyle.Fist; P.HeadTilt = -12; P.Lean = F(5 + laugh * 1.5); P.Mouth = MouthStyle.Open; P.MouthOpen = F(.85 + .1 * laugh); P.BodyY = F(Math.Abs(laugh) * 1.5); P.Gy = -.5f; }
                    else { P.ArmRA = ArmA(60, 1); P.ArmRB = ArmB(120, 1); P.Mouth = MouthStyle.Smile; P.Brow = .7f; P.Gx = F(Math.Sin(t * 4) * .8); P.Lean = -4; }
                    P.Brow = Math.Max(P.Brow, .55f); P.EarL = F(laugh * 6); P.EarR = F(-laugh * 6); P.Turn = .6f;
                    break;
                }
            }
        }
    }
}
