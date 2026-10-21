using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BikiniFriends
{
    internal sealed partial class Pet
    {
        /// <summary>한 순간의 모습: 어떤 자세로 그리고, 어떻게 돌리고, 어떤 장식·말풍선을 얹을지.</summary>
        internal struct Look
        {
            public CharacterArt Art; public Pose Pose;
            public bool FaceRight;               // 그림은 왼쪽을 보는 모습이 기준 — 오른쪽이면 좌우 반전
            public bool Upside;                  // 천장에 거꾸로 매달림
            public float Sx, Sy, Angle, Px, Py, HopY;
            public string Bubble;
            public int ZPhase; public float ZTime;
            public Decor Decor; public float DecorPhase;
            public int Variant; public float Aux;   // 대표 동작의 갈래(0/1)와 보조 값(뚱이의 바위 덮임 정도 등)
            public string AffinityLabel; public bool AffinityOnLeft;

            public string Key()
            {
                return string.Format("{0}|{1}|{2}|{3:0.00}|{4:0.00}|{5:0.0}|{6:0}|{7:0}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}",
                    Pose.Hash(), FaceRight ? 1 : 0, Upside ? 1 : 0, Sx, Sy, Angle, Px, Py, Bubble ?? "", ZPhase, (int)Decor,
                    Decor == Decor.None ? 0 : (int)(DecorPhase * 24), AffinityLabel, AffinityOnLeft, Variant, (int)(Aux * 40));
            }
        }

        private double ClimbPhase { get { return gait * Math.PI * 2; } }
        internal int hobbyVariant;

        public Look GetLook(double now)
        {
            var L = new Look { Art = Art, Pose = new Pose(), Sx = 1, Sy = 1, ZPhase = -1, FaceRight = Dir > 0 };
            Pose P = L.Pose;
            lookJitterX = 0;
            double t = animT;
            float h = Art.H;
            switch (State)
            {
                case PetState.Walk: case PetState.Gather:
                    IdleBase(P, t, false); ApplyWalk(P, gait, walkAmp);
                    break;
                case PetState.Pause: case PetState.WaitHome:
                    if (State == PetState.WaitHome) WaitPose(P); else IdleBase(P, t, true);
                    ApplyWalk(P, gait, walkAmp);
                    break;
                case PetState.Climb:
                    L.FaceRight = climbDir > 0; IdleBase(P, t, false); ApplyWalk(P, gait, walkAmp);
                    break;
                case PetState.Falling: case PetState.PilotRise:
                    // 낙하 도구 아래에 매달려 천천히 흔들린다: 회전축은 도구 쪽.
                    L.FaceRight = false; L.Decor |= Decor.Rotor; L.DecorPhase = (float)StateT;
                    FallingPose(P);
                    L.Angle = (float)(3 * Math.Sin(StateT * 2.4)); L.Px = 0; L.Py = -h * .95f;
                    break;
                case PetState.Squish:
                    SquishPose(P, ref L);
                    break;
                case PetState.Drag:
                    DragPose(P);
                    L.Angle = (float)angle; L.Px = (float)DragPx; L.Py = (float)DragPy; L.FaceRight = false;
                    break;
                case PetState.Thrown:
                    ThrownPose(P);
                    L.FaceRight = throwVx > 0; L.Px = 0; L.Py = -h * .5f;
                    L.Angle = (float)(Math.Max(-32, Math.Min(32, throwVx / (900 * S) * 30)) + Math.Sin(StateT * 13) * 6);
                    break;
                case PetState.Sleep:
                    SleepPose(P, animT); L.ZPhase = (int)(animT * 20) % 100000; L.ZTime = (float)animT; L.Decor |= Decor.Sleepy; L.DecorPhase = (float)animT;
                    break;
                case PetState.Waking:
                    if (StateT < .8) { double q = Math.Sin(Math.PI * StateT / .8); StretchPose(P, q); }
                    else IdleBase(P, t, false);
                    break;
                case PetState.Cheer:
                    CheerPose(P, now);
                    if (now < cheerHopUntil) L.HopY = (float)(-Math.Sin(Math.PI * (StateT % .42 / .42)) * 11 * S);
                    L.FaceRight = false;
                    break;
                case PetState.Resting:
                    RestLook(ref L, now);
                    break;
                case PetState.Eating:
                    EatPose(P); L.FaceRight = false;
                    break;
                case PetState.IdleCry:
                {
                    bool cry = FriendData.Current.Hungry || previewIdleCry;
                    L.FaceRight = false;
                    if (cry) HungryCryPose(P, StateT); else SitIdlePose(P, t);
                    break;
                }
                case PetState.TouchCry:
                    // 달달 떨며 운다: 처음에 움찔 후 흐느낌.
                    TouchCryPose(P); L.FaceRight = false;
                    lookJitterX = Math.Sin(StateT * 52) * .6 * S; L.HopY = (float)(Math.Sin(StateT * 40) * .4 * S);
                    break;
                case PetState.Hop:
                {
                    double q = StateT / extraDuration;
                    HopPose(P);
                    if (q >= .16 && q < .84)
                    {
                        double u = (q - .16) / .68;
                        L.HopY = (float)(-Math.Sin(u * Math.PI) * 22 * S);
                        if (Ci == Sa) { L.Px = 0; L.Py = -h * .5f; L.Angle = (float)((L.FaceRight ? 1 : -1) * 360 * Ease(u)); }   // 공중 앞돌기
                        if (Ci == Pa) { L.Px = 0; L.Py = -h * .45f; L.Angle = (float)(Math.Sin(u * Math.PI * 2) * 14); }
                    }
                    break;
                }
                case PetState.Sliding:
                    DashPose(P); L.HopY = (float)(Math.Sin(animT * 20) * .5 * S);
                    L.Decor |= Decor.Speed; L.DecorPhase = (float)animT;
                    if (Ci == Kr) L.FaceRight = Dir > 0;
                    break;
                case PetState.Wall: case PetState.WindowClimb:
                {
                    // 발을 벽에 대고 몸을 옆으로 눕혀 걷는다(머리는 벽 반대쪽). 오를 땐 위, 내릴 땐 아래를 보며 손발을 번갈아 뻗는다.
                    int wallDir = State == PetState.Wall ? wallSide : -approachSide;
                    bool down = State == PetState.Wall && wallDescending;
                    ClimbBody(P, down);
                    L.FaceRight = down ? wallDir < 0 : wallDir > 0;
                    L.Angle = (float)(-wallDir * (down ? WallDescendDeg : WallAscendDeg) + 3 * Math.Sin(ClimbPhase));
                    break;
                }
                case PetState.Ceiling:
                    // 천장에 발을 붙이고 거꾸로 걷는다: 뒤뚱의 축은 천장에 닿은 발끝.
                    ClimbBody(P, false);
                    L.Upside = true; L.FaceRight = Dir < 0;
                    L.Px = 0; L.Py = -Art.Ceiling.H; L.Angle = (float)(3 * Math.Sin(ClimbPhase));
                    break;
                case PetState.Dizzy:
                    DizzyPose(P); L.FaceRight = false;
                    L.Decor |= Decor.Stars; L.DecorPhase = (float)StateT;
                    break;
                case PetState.SecretDance:
                {
                    double phase = now - danceStarted;
                    L.FaceRight = (int)(phase / .6) % 2 == 0;
                    L.HopY = (float)(-Math.Abs(Math.Sin(phase * Math.PI * 2.5)) * 12 * S);
                    DancePose(P, phase);
                    L.Decor |= Decor.Notes; L.DecorPhase = (float)phase;
                    break;
                }
            }
            L.Bubble = PersistentBubble(hourlyBubble, Bubble);
            CompanionLook(ref L, now);
            return L;
        }

        /// <summary>앉아 쉬는 다섯 가지: 앉기 · 잠자기 · 멍때리기 · 기지개 · 대표 동작.</summary>
        private void RestLook(ref Look L, double now)
        {
            Pose P = L.Pose; double t = StateT;
            switch (restPose)
            {
                case RestPose.Sleep:
                    SleepPose(P, t); L.ZPhase = (int)(t * 20) % 100000; L.ZTime = (float)t; L.Decor |= Decor.Sleepy; L.DecorPhase = (float)t;
                    break;
                case RestPose.Daydream:
                    // 멍때리기: 위를 쳐다보며 몸이 아주 느리게 흔들리고 생각 점 세 개 뒤에 구름이 떠오른다.
                    DaydreamPose(P, t); L.Decor |= Decor.Dots; L.DecorPhase = (float)t;
                    break;
                case RestPose.Stretch:
                {
                    // 기지개: 팔을 쭉 올려 하품하며 몸을 젖혔다가 한 번 털고 내려온다.
                    double u = extraDuration <= 0 ? 1 : t / extraDuration, q;
                    if (u < .05) q = 0;
                    else if (u < .35) q = Ease((u - .05) / .3);
                    else if (u < .65) q = 1;
                    else if (u < .9) q = 1 - Ease((u - .65) / .25);
                    else q = 0;
                    StretchPose(P, q);
                    if (u >= .9) { double sh = Math.Sin((u - .9) * 150) * (1 - (u - .9) / .1); P.Lean += F(sh * 3); P.HeadTilt += F(sh * 5); P.EarL += F(sh * 10); P.EarR -= F(sh * 10); }
                    break;
                }
                case RestPose.Hobby:
                {
                    float aux;
                    HobbyPose(P, t, out aux);
                    L.Decor |= Decor.Hobby; L.DecorPhase = (float)t; L.Variant = hobbyVariant; L.Aux = aux;
                    if (Ci == Pa || Ci == Kr) L.FaceRight = false;
                    break;
                }
                default:
                    SitIdlePose(P, animT);
                    break;
            }
        }

        private void SadOverlay(Pose P, double t)
        {
            double sob = Math.Sin(t * 9);
            P.Brow = -.85f; P.Mouth = MouthStyle.Frown; P.Tears = F(t);
            P.EarL = F(-16 + sob * 2); P.EarR = F(16 - sob * 2); P.Tail = Math.Max(P.Tail, 22); P.TailWaveAmp *= .4f;
            P.PupilR = .9f; P.EyeScale = 1.04f; P.HeadY += F(3 + Math.Abs(sob));
            P.Gx = 0; P.Gy = Math.Max(P.Gy, .4f);
        }

        private void CompanionLook(ref Look look, double now)
        {
            if (PlayOptions.Current.ShowAffinity)
            {
                look.AffinityLabel = FriendData.Current.Affinity.ToString("N0");
                look.AffinityOnLeft = X + Art.W * .5 + (22 + look.AffinityLabel.Length * 8) * S > Area.Right;
            }
            bool hungry = FriendData.Current.Hungry;
            if (hungry && IsCompanionFloorPose)
            {
                if (State == PetState.Squish) look.Pose.Tears = F(animT);
                else if (State == PetState.Walk || (State == PetState.Pause && walkAmp > .05)) SadOverlay(look.Pose, animT);
                else if (State == PetState.Pause) { SadOverlay(look.Pose, animT); }
                else
                {
                    look.Pose = new Pose(); HungryCryPose(look.Pose, animT); look.FaceRight = false;
                    look.Angle = 0; look.Sx = look.Sy = 1; look.HopY = 0; look.Decor = Decor.None;
                }
                lookJitterX = Math.Sin(animT * 21) * .45 * S;
            }
            if (hungry && (IsCompanionFloorPose || State == PetState.TouchCry) && string.IsNullOrEmpty(look.Bubble) && now >= flowerUntil)
                look.Bubble = "배고파! " + Art.Info.FoodName + " 줘 ㅠㅠ";
            if (now < flowerUntil && IsCompanionFloorPose && !previewIdleCry)
            {
                look.Pose = new Pose(); FlowerPose(look.Pose, now);
                look.FaceRight = false; look.Decor = Decor.Gift; look.DecorPhase = (float)(now - flowerStarted);
                look.Angle = 0; look.Sx = look.Sy = 1; look.HopY = 0; lookJitterX = 0;
            }
        }

        internal static string PersistentBubble(string hourly, string ordinary)
        {
            if (string.IsNullOrEmpty(hourly)) return ordinary;
            if (string.IsNullOrEmpty(ordinary) || hourly == ordinary || hourly.IndexOf(ordinary, StringComparison.Ordinal) >= 0) return hourly;
            return hourly + "\n" + ordinary;
        }

        // ───────────────────────── 그리기 ─────────────────────────
        private static Font bubbleFont, affinityFont;
        private static float fontScale;
        private static void EnsureFonts()
        {
            float s = CharacterArt.Scale;
            if (bubbleFont != null && fontScale == s) return;
            fontScale = s;
            try { bubbleFont = new Font("Malgun Gothic", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel); }
            catch { bubbleFont = new Font(FontFamily.GenericSansSerif, 11f * s, FontStyle.Bold, GraphicsUnit.Pixel); }
            try { affinityFont = new Font("Segoe UI", 10f * s, FontStyle.Bold, GraphicsUnit.Pixel); }
            catch { affinityFont = new Font(FontFamily.GenericSansSerif, 10f * s, FontStyle.Bold, GraphicsUnit.Pixel); }
        }
        private static FontFamily zFamily;
        private static FontFamily ZFamily
        {
            get
            {
                if (zFamily == null) { try { zFamily = new FontFamily("Segoe UI"); } catch { zFamily = FontFamily.GenericSansSerif; } }
                return zFamily;
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static Bitmap Render(Look L, out PointF origin)
        {
            EnsureFonts();
            float scale = CharacterArt.Scale;
            CharacterArt art = L.Art;
            float w = art.W, h = art.H, k = art.K;
            var matrix = new Matrix();
            if (L.Angle != 0) { matrix.Translate(L.Px, L.Py); matrix.Rotate(L.Angle); matrix.Translate(-L.Px, -L.Py); }
            matrix.Scale(L.Sx, L.Sy);
            // 팔을 벌리거나 꼬리가 흔들려도 잘리지 않도록 몸 상자보다 넉넉하게 잡는다.
            float padX = w * .5f + 30 * k, padTop = Math.Max(3 * scale, 20 * k), padBottom = 4 * scale + 8 * k;
            var corners = new[] { new PointF(-w / 2 - padX, -h - padTop), new PointF(w / 2 + padX, -h - padTop), new PointF(w / 2 + padX, padBottom), new PointF(-w / 2 - padX, padBottom) };
            matrix.TransformPoints(corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF p in corners) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
            // 말풍선·Z 는 몸의 맨 위(머리 꼭대기)를 기준으로 얹는다
            float topY = -h - 8 * k;
            var tops = new[] { new PointF(0, topY), new PointF(-w / 2, topY), new PointF(w / 2, topY) };
            matrix.TransformPoints(tops);
            float spriteTop = Math.Min(tops[0].Y, Math.Min(tops[1].Y, tops[2].Y));

            RectangleF ext = DecorArt.Extent(L, art);
            if (ext.Width > 0 || ext.Top < 0)
            {
                var ep = new[] { new PointF(ext.Left, ext.Top), new PointF(ext.Right, ext.Top) };
                matrix.TransformPoints(ep);
                foreach (PointF p in ep) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); }
                spriteTop = Math.Min(spriteTop, minY);
            }

            SizeF textSize = SizeF.Empty; RectangleF bubble = RectangleF.Empty;
            float tail = 7 * scale;
            if (!string.IsNullOrEmpty(L.Bubble))
            {
                using (var tmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(tmp))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    textSize = g.MeasureString(L.Bubble, bubbleFont, (int)(170 * scale), StringFormat.GenericTypographic);
                }
                float pad = 7 * scale;
                float bw = (float)Math.Ceiling(textSize.Width + pad * 2 + 2 * scale), bh = (float)Math.Ceiling(textSize.Height + pad * 1.4f + 2 * scale);
                bubble = new RectangleF(-bw / 2, spriteTop - 3 * scale - tail - bh, bw, bh);
                minX = Math.Min(minX, bubble.Left); maxX = Math.Max(maxX, bubble.Right); minY = Math.Min(minY, bubble.Top);
            }
            if (L.ZPhase >= 0) { minY = Math.Min(minY, spriteTop - 44 * scale); maxX = Math.Max(maxX, w / 2 + 22 * scale); minX = Math.Min(minX, -w / 2 - 22 * scale); }
            RectangleF badge = AffinityBadgeBounds(L, w, h, scale);
            if (!badge.IsEmpty) { minX = Math.Min(minX, badge.Left); maxX = Math.Max(maxX, badge.Right); minY = Math.Min(minY, badge.Top); maxY = Math.Max(maxY, badge.Bottom); }

            const int margin = 3;
            int bwid = (int)Math.Ceiling(maxX - minX) + margin * 2, bhei = (int)Math.Ceiling(maxY - minY) + margin * 2;
            origin = new PointF(-minX + margin, -minY + margin);
            var result = new Bitmap(Math.Max(1, bwid), Math.Max(1, bhei), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                CharacterArt.Quality(g);
                var full = new Matrix(); full.Translate(origin.X, origin.Y); full.Multiply(matrix);
                g.Transform = full;
                if (L.Upside) { g.TranslateTransform(0, -h / 2); g.RotateTransform(180); g.TranslateTransform(0, h / 2); }
                Rig.Draw(g, art.Id, L.Pose, k, L.FaceRight);

                // 몸에 붙어 따라 움직이는 장식
                g.Transform = full;
                DecorArt.Draw(g, L, art);
                g.ResetTransform(); g.TranslateTransform(origin.X, origin.Y);
                DrawAffinityBadge(g, L, badge, scale);
                if (L.ZPhase >= 0) DrawZ(g, L, w, h, scale);
                if (!bubble.IsEmpty) DrawBubble(g, L, bubble, textSize, tail, scale);
            }
            return result;
        }

        private static void DrawBubble(Graphics g, Look L, RectangleF r, SizeF textSize, float tail, float scale)
        {
            using (GraphicsPath path = RoundRect(r, 8 * scale))
            {
                var tri = new[] { new PointF(-5 * scale, r.Bottom - 1), new PointF(1 * scale, r.Bottom + tail), new PointF(6 * scale, r.Bottom - 1) };
                using (var pen = new Pen(Color.FromArgb(255, 30, 30, 30), 2 * scale) { LineJoin = LineJoin.Round })
                using (var fill = new SolidBrush(Color.White))
                {
                    g.FillPath(fill, path); g.DrawPath(pen, path);
                    g.FillPolygon(fill, tri); g.DrawLine(pen, tri[0], tri[1]); g.DrawLine(pen, tri[1], tri[2]);
                    using (var cover = new Pen(Color.White, 2.2f * scale))
                        g.DrawLine(cover, tri[0].X + 2.2f * scale, r.Bottom - .5f * scale, tri[2].X - 2.2f * scale, r.Bottom - .5f * scale);
                }
            }
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var text = new SolidBrush(Color.FromArgb(255, 40, 40, 40)))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center })
            {
                var box = new RectangleF(r.Left + (r.Width - textSize.Width) / 2, r.Top + (r.Height - textSize.Height) / 2, textSize.Width + 2, textSize.Height + 2);
                g.DrawString(L.Bubble, bubbleFont, text, box, fmt);
            }
        }

        // 잠: Z 세 개가 시차를 두고(0 / 0.9 / 1.8초) 떠올라 흩어진다. 집게사장은 $ 가 떠오른다.
        private static void DrawZ(Graphics g, Look L, float w, float h, float s)
        {
            float dir = L.FaceRight ? 1 : -1, baseX = dir * w * .30f, baseY = -h * .86f;
            float[] size = { 8f, 11f, 14f }; float[] phase = { 0f, .9f, 1.8f };
            bool dollar = L.Art.Info.SleepZ == "$";
            for (int i = 0; i < 3; i++)
            {
                float t = L.ZTime;
                if (t < phase[i]) continue;
                float cycle = (((t - phase[i]) % 2.7f) + 2.7f) % 2.7f, k = cycle / 2.7f;
                int alpha = (int)(255 * Math.Max(0f, Math.Min(1f, Math.Min(cycle * 3f, 1 - k) * (k < .85f ? 1f : (1 - k) / .15f))));
                if (alpha <= 0) continue;
                float sz = size[i] * s, x = baseX + dir * (3 + cycle * 4.5f) * s, y = baseY - cycle * 10.5f * s;
                using (var path = new GraphicsPath())
                {
                    path.AddString(dollar ? "$" : (i == 0 ? "z" : "Z"), ZFamily, 1, sz, new PointF(x - sz * .3f, y - sz), StringFormat.GenericTypographic);
                    using (var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 2.4f * s) { LineJoin = LineJoin.Round })
                    using (var brush = new SolidBrush(dollar ? Color.FromArgb(alpha, 200, 150, 20) : Color.FromArgb(alpha, 70, 100, 170))) { g.DrawPath(pen, path); g.FillPath(brush, path); }
                }
            }
        }

        private static RectangleF AffinityBadgeBounds(Look L, float w, float h, float s)
        {
            if (string.IsNullOrEmpty(L.AffinityLabel)) return RectangleF.Empty;
            float bw;
            using (var img = new Bitmap(1, 1)) using (var g = Graphics.FromImage(img))
                bw = Math.Max(38 * s, g.MeasureString(L.AffinityLabel, affinityFont, PointF.Empty, StringFormat.GenericTypographic).Width + 22 * s);
            return new RectangleF(L.AffinityOnLeft ? -w * .5f - 3 * s - bw : w * .5f + 3 * s, -h * .57f - 9 * s, (float)Math.Ceiling(bw), 20 * s);
        }

        private static void DrawAffinityBadge(Graphics g, Look L, RectangleF b, float s)
        {
            if (b.IsEmpty) return;
            using (var path = RoundRect(b, 8 * s))
            using (var fill = new SolidBrush(Color.FromArgb(245, 255, 248, 246)))
            using (var border = new Pen(Color.FromArgb(225, 237, 196, 207), s)) { g.FillPath(fill, path); g.DrawPath(border, path); }
            using (var outline = new Pen(Color.FromArgb(218, 105, 139), s)) DrawHeart(g, b.Left + 8 * s, b.Top + 10 * s, 4 * s, outline);
            using (var text = new SolidBrush(Color.FromArgb(113, 73, 91)))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(L.AffinityLabel, affinityFont, text, new RectangleF(b.Left + 16 * s, b.Top, b.Width - 17 * s, b.Height), fmt);
            }
        }

        private static void DrawHeart(Graphics g, float x, float y, float size, Pen outline)
        {
            using (var path = new GraphicsPath()) using (var fill = new SolidBrush(Color.FromArgb(235, 126, 154)))
            {
                path.AddBezier(x, y + size * .7f, x - size * 1.25f, y - size * .1f, x - size * .5f, y - size, x, y - size * .32f);
                path.AddBezier(x, y - size * .32f, x + size * .5f, y - size, x + size * 1.25f, y - size * .1f, x, y + size * .7f);
                g.FillPath(fill, path); g.DrawPath(outline, path);
            }
        }
    }
}
