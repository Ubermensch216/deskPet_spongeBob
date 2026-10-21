using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BikiniFriends
{
    internal enum PetState
    {
        Falling, Walk, Pause, Squish, Drag, Climb, Sleep, Waking, Gather, WaitHome, Cheer,
        Resting, IdleCry, TouchCry, Hop, Sliding, Wall, Ceiling, WindowClimb, Eating, Thrown, Dizzy, SecretDance, PilotRise
    }

    internal interface IWorld
    {
        List<Rectangle> Areas { get; }
        Random Rng { get; }
        double Now { get; }
    }

    /// <summary>친구 한 마리의 위치·상태·행동. 그림은 PetRender 가 Look 으로 받아 그린다.</summary>
    internal sealed partial class Pet
    {
        public readonly CharacterArt Art;
        public double X, Y;               // 발바닥 가운데 (화면 좌표)
        public int Dir = 1;
        public PetState State;
        public double StateT, Vy;
        public Rectangle Area;
        public bool Hidden;
        public double DragPx, DragPy;
        public string Bubble;
        public double BubbleUntil;
        internal string hourlyBubble;
        internal IntPtr SupportWindow;
        internal Rectangle SupportBounds;
        internal bool InputHeld;
        internal PetState AfterReaction = PetState.Walk;
        internal int ClickCycle;

        private double animT, stateDur, squishDur = .4, squishAmp = .22;
        private PetState afterSquish = PetState.Walk;
        private bool flipAfterSquish;
        private double collideCooldown;
        private Rectangle climbTo;
        private int climbDir;
        private double angle, angVel, lastMouseX, mouseVx;
        private double gatherX, gatherDeadline, cheerHopUntil;
        private PetState? queuedAfterLand;
        private double walkAmp;
        private readonly double blinkOffset = new Random(Guid.NewGuid().GetHashCode()).NextDouble() * 4.3;

        private float S { get { return CharacterArt.Scale; } }
        private double WalkSpeed { get { return 34 * S; } }
        private double HalfW { get { return Art.W * .36; } }
        /// <summary>프로펠러까지 포함한 높이·반폭 (화면 맨 위/양끝 보정에 쓴다).</summary>
        internal double HeliH { get { return Art.H + Art.RotorH; } }
        internal double HeliHalf { get { return Art.W * .66; } }
        public bool CanReact { get { return State != PetState.Drag; } }

        public Pet(IWorld w, int character, double x, double y)
        {
            Art = CharacterArt.Get(character);
            X = x; Y = y;
            Dir = w.Rng.Next(2) != 0 ? 1 : -1;
            Area = FindArea(w, x, y);
            SetState(PetState.Falling, w);
            InitializeFeatures(w);
        }

        public static Rectangle FindArea(IWorld w, double x, double y)
        {
            Rectangle result = w.Areas[0]; double best = double.MaxValue;
            foreach (Rectangle a in w.Areas)
            {
                double dx = x < a.Left ? a.Left - x : (x >= a.Right ? x - a.Right + 1 : 0);
                double dy = y < a.Top ? a.Top - y : (y > a.Bottom ? y - a.Bottom : 0);
                double d = dx * 1000 + dy;
                if (d < best) { best = d; result = a; }
            }
            return result;
        }

        private static Rectangle? AreaAtX(IWorld w, double x, double nearY)
        {
            Rectangle? result = null; double best = double.MaxValue;
            foreach (Rectangle a in w.Areas)
            {
                if (x < a.Left || x >= a.Right) continue;
                double d = Math.Abs(a.Bottom - nearY);
                if (d < best) { best = d; result = a; }
            }
            return result;
        }

        public void SetState(PetState s, IWorld w)
        {
            State = s; StateT = 0;
            if (s != PetState.IdleCry) previewIdleCry = false;
            switch (s)
            {
                case PetState.Walk: stateDur = 3 + w.Rng.NextDouble() * 7; break;
                case PetState.Pause: stateDur = 1.2 + w.Rng.NextDouble() * 3; break;
                case PetState.Waking: stateDur = 1.6; break;
            }
        }

        public void StartSquish(IWorld w, double dur, double amp, PetState after, bool flip)
        { squishDur = dur; squishAmp = amp; afterSquish = after; flipAfterSquish = flip; SetState(PetState.Squish, w); }

        public void Poke(IWorld w) { ClickFeature(w); }

        public void BeginDrag(IWorld w, double mx, double my)
        {
            ReleaseFeatures(w);
            DragPx = mx - X; DragPy = my - Y;
            if (DragPy > -4 * S) DragPy = -4 * S;
            angle = angVel = 0; lastMouseX = mx; mouseVx = 0; queuedAfterLand = null;
            BeginThrowTracking(w.Now, mx, my);
            SetState(PetState.Drag, w);
        }

        public void DragTo(IWorld w, double mx, double my)
        { TrackDrag(w.Now, mx, my); X = mx - DragPx; Y = my - DragPy; }

        public void EndDrag(IWorld w)
        {
            if (State != PetState.Drag) return;
            TrackDrag(w.Now, X + DragPx, Y + DragPy);
            ReleaseFeatures(w);
            Area = FindArea(w, X, Y);
            if (X < Area.Left + HalfW) X = Area.Left + HalfW;
            if (X > Area.Right - HalfW) X = Area.Right - HalfW;
            Vy = 0;
            if (PlayOptions.Current.PositionFixed)
            {
                ResetSecretThrows(); ClampPinned(w); SetState(PetState.Pause, w);
                App app = w as App; if (app != null) app.RememberPinnedPosition();
                return;
            }
            if (TryThrow(w)) return;
            ResetSecretThrows();
            if (Y >= Area.Bottom) { Y = Area.Bottom; StartSquish(w, .25, .14, PetState.Walk, false); }
            else SetState(PetState.Falling, w);
        }

        public void FallAsleep(IWorld w)
        { if (State == PetState.Walk || State == PetState.Pause || State == PetState.Squish) SetState(PetState.Sleep, w); }

        public void WakeUp(IWorld w)
        { if (State == PetState.Sleep) SetState(PetState.Waking, w); }

        public void Cheer(IWorld w, string msg, double bubbleSec)
        {
            Bubble = msg; BubbleUntil = w.Now + bubbleSec;
            if (State == PetState.Drag) return;
            cheerHopUntil = w.Now + 3.2;
            if (State == PetState.Falling || State == PetState.Climb || State == PetState.Thrown) queuedAfterLand = PetState.Cheer;
            else SetState(PetState.Cheer, w);
        }

        public void Gather(IWorld w, double targetX, double deadline)
        {
            if (State != PetState.Drag && State != PetState.Falling && State != PetState.Climb && State != PetState.Thrown)
            {
                gatherX = Math.Max(Area.Left + HalfW, Math.Min(Area.Right - HalfW, targetX));
                gatherDeadline = deadline; SetState(PetState.Gather, w);
            }
        }

        private void UpdateWalkBlend(double dt)
        {
            bool moving = State == PetState.Walk || State == PetState.Gather || State == PetState.Climb;
            double target = moving ? 1 : 0;
            walkAmp += (target - walkAmp) * Math.Min(1, dt * 9);
            if (walkAmp < .004 && target == 0) walkAmp = 0;
        }

        public void Update(IWorld w, double dt, List<Pet> all, double mouseX)
        {
            UpdateWalkBlend(dt); UpdateGait(dt);
            if (AdvanceFeatures(w, dt)) return;
            StateT += dt; animT += dt;
            if (collideCooldown > 0) collideCooldown -= dt;
            if (Bubble != null && w.Now > BubbleUntil) Bubble = null;
            switch (State)
            {
                case PetState.Falling: HelicopterDrop(w, dt); break;
                case PetState.Walk: UpdateWalk(w, dt); break;
                case PetState.Pause:
                    SnapToFloor(w);
                    if (StateT > stateDur)
                    {
                        if (w.Rng.NextDouble() < .35) Dir = -Dir;
                        SetState(PetState.Walk, w);
                    }
                    break;
                case PetState.Squish:
                    if (StateT >= squishDur)
                    {
                        if (flipAfterSquish) Dir = -Dir;
                        SetState(afterSquish, w);
                    }
                    break;
                case PetState.Drag:
                {
                    double v = (mouseX - lastMouseX) / Math.Max(dt, .001);
                    lastMouseX = mouseX; mouseVx = mouseVx * .7 + v * .3;
                    double target = Math.Max(-45, Math.Min(45, -mouseVx * .05 / S));
                    target += Math.Sin(animT * 3.2) * 5;
                    angVel += (target - angle) * 60 * dt;
                    angVel -= angVel * 7 * dt;
                    angle += angVel * dt;
                    break;
                }
                case PetState.Climb: UpdateClimb(w, dt); break;
                case PetState.Sleep: SnapToFloor(w); break;
                case PetState.Waking:
                    SnapToFloor(w);
                    if (StateT > stateDur) SetState(PetState.Walk, w);
                    break;
                case PetState.Gather: UpdateGather(w, dt); break;
                case PetState.WaitHome: SnapToFloor(w); break;
                case PetState.Cheer:
                    SnapToFloor(w);
                    if (Bubble == null && w.Now > cheerHopUntil) SetState(PetState.Walk, w);
                    break;
            }
        }

        private void SnapToFloor(IWorld w)
        {
            if (SurfaceFloor(w)) return;
            if (Y < Area.Bottom - 1) { Vy = 0; SetState(PetState.Falling, w); }
            else if (Y > Area.Bottom) Y = Area.Bottom;
        }

        private void UpdateWalk(IWorld w, double dt)
        {
            SnapToFloor(w);
            if (State != PetState.Walk) return;
            if (StateT > stateDur) { SetState(PetState.Pause, w); return; }
            MoveHoriz(w, dt, WalkSpeed);
        }

        private void MoveHoriz(IWorld w, double dt, double speed)
        {
            speed *= PlayOptions.Current.Speed;
            if (WalkOnWindow(w, dt, speed)) return;
            if (BoundaryFeature(w, X + Dir * speed * dt)) return;
            double nx = X + Dir * speed * dt, half = HalfW;
            bool outside = Dir <= 0 ? nx - half < Area.Left : nx + half > Area.Right;
            if (!outside) { X = nx; return; }
            double probe = Dir <= 0 ? Area.Left - 2 : Area.Right + 1;
            Rectangle? other = AreaAtX(w, probe, Y);
            if (!other.HasValue || other.Value == Area)
            {
                Dir = -Dir;
                X = Dir <= 0 ? Area.Right - half : Area.Left + half;
                if (State == PetState.Walk && w.Rng.NextDouble() < .4) SetState(PetState.Pause, w);
                return;
            }
            Rectangle next = other.Value;
            if (next.Bottom < Y - 2)
            {
                // 이웃 모니터가 더 높다 → 기어오르기
                X = Dir <= 0 ? Area.Left + half * .2 : Area.Right - half * .2;
                climbTo = next; climbDir = Dir; SetState(PetState.Climb, w);
                return;
            }
            X = nx;
            double edge = Dir <= 0 ? Area.Left : Area.Right;
            if (Dir <= 0 ? X < edge : X >= edge)
            {
                Area = next;
                if (Y < next.Bottom - 1) { Vy = 0; SetState(PetState.Falling, w); }
                else Y = next.Bottom;
            }
        }

        private void UpdateClimb(IWorld w, double dt)
        {
            Y -= 30 * S * dt;
            if (Y <= climbTo.Bottom)
            {
                Y = climbTo.Bottom; Area = climbTo;
                X = climbDir <= 0 ? climbTo.Right - HalfW * .5 : climbTo.Left + HalfW * .5;
                Dir = climbDir;
                if (queuedAfterLand == PetState.Cheer) { queuedAfterLand = null; SetState(PetState.Cheer, w); }
                else StartSquish(w, .25, .1, PetState.Walk, false);
            }
        }

        private void UpdateGather(IWorld w, double dt)
        {
            SnapToFloor(w);
            if (State != PetState.Gather) return;
            double d = gatherX - X;
            if (Math.Abs(d) < 2) { X = gatherX; SetState(PetState.WaitHome, w); return; }
            Dir = Math.Sign(d);
            double secs = Math.Max(.4, gatherDeadline - w.Now - .6);
            double speed = Math.Max(WalkSpeed, Math.Abs(d) / secs);
            X += Dir * Math.Min(Math.Abs(d), speed * dt);
        }

        public static Rectangle[] FindAreas(IWorld w) { return w.Areas.ToArray(); }
    }
}
