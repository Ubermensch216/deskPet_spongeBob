using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BikiniFriends
{
    /// <summary>던지기·헬리콥터 낙하·벽/천장/창 타기·도망·쉬기·클릭 반응·위치 고정.</summary>
    internal sealed partial class Pet
    {
        private double extraDuration = 6;
        private double nextIdle, nextClimb, mouseGrace, nextEscape, hoverSince = -1;
        private bool hadNearMouse;
        private int wallSide;
        private bool wallDescending;
        internal bool previewIdleCry;
        private IntPtr approachWindow;
        private int approachSide;
        private double lookJitterX;
        private bool focusWasOn;
        private bool pinnedActive;

        private struct DragSample { internal double Time, X, Y; internal DragSample(double t, double x, double y) { Time = t; X = x; Y = y; } }
        private readonly List<DragSample> dragSamples = new List<DragSample>();
        private double throwVx, throwVy, throwDuration, helicopterDrift;

        internal void InitializeFeatures(IWorld w)
        {
            nextIdle = w.Now + 15 + w.Rng.NextDouble() * 15;
            nextClimb = w.Now + 25 + w.Rng.NextDouble() * 20;
            mouseGrace = w.Now + 1;
            Y = Math.Max(Y, Area.Top + HeliH + 5);   // 처음 낙하는 프로펠러까지 화면 안에서 보이게
        }

        private void DelayIdle(IWorld w)
        {
            nextIdle = w.Now + 20 + w.Rng.NextDouble() * 30;
            nextClimb = Math.Max(nextClimb, w.Now + 15);
        }

        private void ClearAttachments() { SupportWindow = IntPtr.Zero; approachWindow = IntPtr.Zero; wallDescending = false; }

        internal void ReleaseFeatures(IWorld w)
        {
            ClearAttachments(); hoverSince = -1; hadNearMouse = false; mouseGrace = w.Now + .8;
            throwVx = throwVy = helicopterDrift = 0; pilotFlight = false;
        }

        private void BeginThrowTracking(double now, double x, double y) { dragSamples.Clear(); TrackDrag(now, x, y); }

        private void TrackDrag(double now, double x, double y)
        {
            var sample = new DragSample(now, x, y);
            int last = dragSamples.Count - 1;
            if (last >= 0 && now - dragSamples[last].Time < .0001) dragSamples[last] = sample; else dragSamples.Add(sample);
            while (dragSamples.Count > 1 && now - dragSamples[0].Time > .12) dragSamples.RemoveAt(0);
        }

        private bool TryThrow(IWorld w)
        {
            if (dragSamples.Count < 2) { dragSamples.Clear(); return false; }
            var first = dragSamples[0]; var last = dragSamples[dragSamples.Count - 1];
            double dt = last.Time - first.Time;
            double vx = (last.X - first.X) / Math.Max(.008, dt), vy = (last.Y - first.Y) / Math.Max(.008, dt);
            dragSamples.Clear();
            double speed = Math.Sqrt(vx * vx + vy * vy);
            if (dt < .008 || speed < 300 * S) return false;
            double limit = Math.Min(1, 1600 * S / speed);
            throwVx = vx * limit * .7;
            throwVy = Math.Max(-700 * S, Math.Min(700 * S, vy * limit * .7 - 80 * S));
            if (Y > Area.Bottom - 24 * S) throwVy = Math.Min(throwVy, -240 * S);   // 바닥에서 던지면 먼저 떠오른다
            double half = Art.W * .75;
            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
            Y = Math.Max(Area.Top + HeliH + 1, Math.Min(Area.Bottom - Art.H * .25, Y));
            throwDuration = .45 + .15 * Math.Min(1, Math.Max(0, -throwVy / (600 * S)));
            if (Math.Abs(throwVx) > 25 * S) Dir = throwVx < 0 ? -1 : 1;
            RegisterSecretThrow(w);
            SetState(PetState.Thrown, w);
            DelayIdle(w); return true;
        }

        private void OpenHelicopter(IWorld w)
        {
            double half = HeliHalf;
            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
            Y = Math.Max(Area.Top + HeliH + 1, Math.Min(Area.Bottom - 1, Y));
            Vy = Math.Max(0, Math.Min(95 * S * PlayOptions.Current.FallSpeed, throwVy * .25));
            helicopterDrift = throwVx * .45; throwVx = throwVy = 0;
            if (pilotFlight) { Vy = 0; SetState(PetState.PilotRise, w); }
            else SetState(PetState.Falling, w);
        }

        private void AdvanceThrow(IWorld w, double dt)
        {
            double oldY = Y;
            throwVy += 900 * S * dt;
            double nx = X + throwVx * dt, ny = Y + throwVy * dt;
            throwVx *= Math.Exp(-1.6 * dt);
            Area = FindArea(w, nx, ny);
            double half = Art.W * .75;
            bool edge = nx < Area.Left + half || nx > Area.Right - half;
            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, nx));
            double top = Area.Top + HeliH + 1;
            bool hitTop = ny < top;
            Y = Math.Max(top, Math.Min(Area.Bottom - 1, ny));
            bool nearFloor = throwVy > 0 && ny >= Area.Bottom - 10 * S;
            bool nearWindow = false;
            if (throwVy > 0 && PlayOptions.Current.Windows && !PlayOptions.IsFocus(DateTime.Now))
            {
                foreach (var win in DesktopSurfaces.Cached)
                {
                    Rectangle r;
                    if (!DesktopSurfaces.TryBounds(win.Handle, out r) || r.Top < oldY || r.Top > ny + 18 * S || X < r.Left + HalfW || X > r.Right - HalfW || r.Top < top || !DesktopSurfaces.TopVisible(win.Handle, X, r.Top)) continue;
                    Y = Math.Min(Y, r.Top - 1); nearWindow = true;
                }
            }
            if (edge) throwVx = -throwVx * .2;
            if (hitTop) throwVy = Math.Max(0, -throwVy * .2);
            if (StateT >= throwDuration || edge || hitTop || nearFloor || nearWindow) OpenHelicopter(w);
        }

        internal void ClickFeature(IWorld w)
        {
            if (State == PetState.Drag || State == PetState.Falling || State == PetState.Thrown || State == PetState.PilotRise) return;
            mouseGrace = w.Now + .7; hoverSince = -1;
            if (State == PetState.Wall || State == PetState.Ceiling || State == PetState.WindowClimb || State == PetState.Climb)
            {
                ReleaseFeatures(w); ResetSecretThrows(); queuedAfterLand = null; Vy = 0;
                OpenHelicopter(w); DelayIdle(w); FriendData.Current.Like(1); return;
            }
            if (State == PetState.Sleep) { SetState(PetState.Waking, w); return; }
            if (State == PetState.Gather || State == PetState.WaitHome || State == PetState.Cheer) return;
            if (PlayOptions.Current.ClickMode == ClickReaction.None) return;
            PetState resume = PetState.Walk;
            if (State == PetState.TouchCry || State == PetState.Hop || State == PetState.Squish) resume = AfterReaction;
            AfterReaction = resume;
            int choice;
            switch (PlayOptions.Current.ClickMode)
            {
                case ClickReaction.Random: choice = w.Rng.Next(3); break;
                case ClickReaction.Squash: choice = 0; break;
                case ClickReaction.Cry: choice = 1; break;
                case ClickReaction.Hop: choice = 2; break;
                default: choice = ClickCycle++ % 3; break;
            }
            if (choice == 0) { StartSquish(w, .42, .24, resume, false); Say(w, Art.Info.PokeLine, 2); }
            else if (choice == 1) { extraDuration = 1.6; SetState(PetState.TouchCry, w); Say(w, Art.Info.CryLine, 2.2); }
            else { extraDuration = .7; SetState(PetState.Hop, w); Say(w, Art.Info.HopLine, 2); }
            DelayIdle(w);
            FriendData.Current.Like(1);
        }

        /// <summary>말풍선 한마디(다른 말풍선이 떠 있으면 건드리지 않는다).</summary>
        private void Say(IWorld w, string text, double sec)
        { if (string.IsNullOrEmpty(Bubble) && !string.IsNullOrEmpty(text)) { Bubble = text; BubbleUntil = w.Now + sec; } }

        internal void Feed(IWorld w)
        {
            if (State == PetState.Drag || State == PetState.Falling || State == PetState.Thrown || State == PetState.PilotRise)
            { Bubble = "내려가면 " + Art.Info.FoodName + " 먹자!"; BubbleUntil = w.Now + 3; return; }
            if (State == PetState.Wall || State == PetState.Ceiling || State == PetState.WindowClimb)
            { ClearAttachments(); SetState(PetState.Falling, w); return; }
            if (!FriendData.Current.TrySnack())
            { Bubble = "배가 꽉 찼어! 조금 출출해지면 먹자"; BubbleUntil = w.Now + 3; return; }
            AfterReaction = PetState.Walk; extraDuration = 3;
            SetState(PetState.Eating, w); Bubble = Art.Info.EatLine; BubbleUntil = w.Now + 3;
            mouseGrace = w.Now + 5; DelayIdle(w);
        }

        /// <summary>우클릭 → 동작 바로 보기.</summary>
        internal void Demonstrate(IWorld w, string kind)
        {
            mouseGrace = w.Now + 8; hoverSince = -1;
            if (kind == "fall")
            { ReleaseFeatures(w); X = Area.Left + Area.Width * .5; Y = Area.Top + HeliH + 5; Vy = 0; SetState(PetState.Falling, w); return; }
            if (kind == "wall")
            { ClearAttachments(); wallSide = 1; wallDescending = false; X = WallX(Area.Left, Area.Right, 1, false); Y = Area.Bottom - BottomExt(false); SetState(PetState.Wall, w); return; }
            if (kind == "walldown")
            { ClearAttachments(); wallSide = 1; wallDescending = true; X = WallX(Area.Left, Area.Right, 1, true); Y = Area.Top + TopExt(true) + 1; SetState(PetState.Wall, w); return; }
            if (kind == "ceiling")
            { ClearAttachments(); X = Area.Left + Area.Width * .5; Y = Area.Top + Art.CeilingContactHeight; Dir = 1; SetState(PetState.Ceiling, w); return; }
            if (kind == "window")
            {
                DesktopSurfaces.Refresh();
                var win = DesktopSurfaces.Cached.FirstOrDefault(a => a.Bounds.Top > Area.Top + Art.H && a.Bounds.Top < Area.Bottom - 40);
                if (win != null)
                {
                    ClearAttachments(); SupportWindow = win.Handle; SupportBounds = win.Bounds;
                    approachSide = win.Bounds.Right + Art.W < Area.Right ? 1 : -1;
                    X = approachSide == 1 ? WallX(win.Bounds.Right, win.Bounds.Right, -1, false) : WallX(win.Bounds.Left, win.Bounds.Left, 1, false);
                    Y = Math.Min(Area.Bottom, win.Bounds.Bottom) - BottomExt(false); SetState(PetState.WindowClimb, w);
                }
                else { Bubble = "창 크기를 줄여서 띄워줘!"; BubbleUntil = w.Now + 4; }
                return;
            }
            if (State == PetState.Falling || State == PetState.Drag || State == PetState.Thrown) return;
            if (kind == "dizzy") { ClearAttachments(); SnapToFloor(w); StartDizzy(w); return; }
            AfterReaction = PetState.Walk;
            if (kind == "cry") { extraDuration = 3; SetState(PetState.TouchCry, w); }
            if (kind == "sit") BeginRestPose(w, RestPose.Sit, PlayOptions.Current.ShortRest ? 8 : 25);
            if (kind == "sleep") BeginRestPose(w, RestPose.Sleep, 8);
            if (kind == "daydream") BeginRestPose(w, RestPose.Daydream, 8);
            if (kind == "stretch") BeginRestPose(w, RestPose.Stretch, 0);
            if (kind == "hobby") BeginRestPose(w, RestPose.Hobby, 10, 0);
            if (kind == "hobby2") BeginRestPose(w, RestPose.Hobby, 10, 1);
            if (kind == "weep") { extraDuration = 3; previewIdleCry = true; SetState(PetState.IdleCry, w); }
            if (kind == "hop") { extraDuration = .7; SetState(PetState.Hop, w); }
            if (kind == "slide") { extraDuration = .85; SetState(PetState.Sliding, w); }
            DelayIdle(w);
        }

        /// <summary>특수 동작 처리. true 를 돌려주면 기본 Update 는 건너뛴다.</summary>
        internal bool AdvanceFeatures(IWorld w, double dt)
        {
            var opts = PlayOptions.Current;
            if (AdvancePinned(w, dt)) return true;
            if (AdvanceSecrets(w, dt)) return true;
            bool focus = PlayOptions.IsFocus(DateTime.Now);
            if (focus && !focusWasOn && (SupportWindow != IntPtr.Zero || State == PetState.Wall || State == PetState.Ceiling || State == PetState.WindowClimb))
            { ClearAttachments(); SetState(PetState.Falling, w); }
            focusWasOn = focus;
            if ((State == PetState.Wall && !opts.Walls) || (State == PetState.Ceiling && !opts.Ceiling) || (State == PetState.WindowClimb && !opts.Windows))
            { ClearAttachments(); SetState(PetState.Falling, w); }
            if (SupportWindow != IntPtr.Zero && State != PetState.Falling && State != PetState.Drag)
            {
                Rectangle r;
                if (!DesktopSurfaces.TryBounds(SupportWindow, out r)) { ClearAttachments(); SetState(PetState.Falling, w); }
                else
                {
                    X += r.Left - SupportBounds.Left;
                    if (State != PetState.WindowClimb) Y = r.Top;
                    SupportBounds = r;
                    if (State != PetState.WindowClimb && (X < r.Left + 5 || X > r.Right - 5 || !DesktopSurfaces.TopVisible(SupportWindow, X, r.Top)))
                    { ClearAttachments(); Y += 3; SetState(PetState.Falling, w); }
                }
            }
            if (State == PetState.Falling) return false;
            if (State == PetState.Walk || State == PetState.Pause || State == PetState.Resting)
            {
                Point mouse = DesktopSurfaces.Mouse;
                bool near = Math.Abs(mouse.X - X) < Art.W * .9 && mouse.Y >= Y - Art.H - 15 * S && mouse.Y <= Y + 8 * S;
                if (near && !hadNearMouse) hoverSince = w.Now;
                if (!near) hoverSince = -1;
                hadNearMouse = near;
                if (!focus && opts.Escape && near && hoverSince >= 0 && w.Now - hoverSince > opts.EscapeDelay && w.Now > mouseGrace && w.Now > nextEscape && !CirclingMouse(w.Now) && !InputHeld && !DesktopSurfaces.MousePressed)
                {
                    Dir = mouse.X < X ? 1 : -1;
                    if (X > Area.Right - Art.W * 2) Dir = -1;
                    if (X < Area.Left + Art.W * 2) Dir = 1;
                    extraDuration = .7 + w.Rng.NextDouble() * .25;
                    SetState(PetState.Sliding, w); nextEscape = w.Now + 2.5; hoverSince = -1; DelayIdle(w);
                }
                else if (!focus && opts.Follow && !opts.Escape && w.Now > mouseGrace && Math.Abs(mouse.Y - Y) < 140 * S && !InputHeld && Math.Abs(mouse.X - X) > Art.W)
                    Dir = mouse.X < X ? -1 : 1;
                if (opts.Idle && w.Now >= nextIdle && !InputHeld && !near && (State == PetState.Walk || State == PetState.Pause))
                {
                    extraDuration = opts.ShortRest ? (focus ? 4 : 6 + w.Rng.NextDouble() * 5) : 20 + w.Rng.NextDouble() * 10;
                    if (!focus && FriendData.Current.Hungry) { extraDuration = 2.4 + w.Rng.NextDouble(); SetState(PetState.IdleCry, w); }
                    else BeginRestPose(w, ChooseRestPose(w), extraDuration);
                    DelayIdle(w);
                }
                if (!focus && opts.Windows && State == PetState.Walk && SupportWindow == IntPtr.Zero && w.Now > nextClimb)
                {
                    nextClimb = w.Now + 35 + w.Rng.NextDouble() * 30;
                    var possible = DesktopSurfaces.Cached.Where(a => a.Bounds.Top >= Area.Top + 70 * S && a.Bounds.Top < Area.Bottom - 70 * S && a.Bounds.IntersectsWith(Area)).ToList();
                    if (possible.Count > 0)
                    {
                        var a = possible[w.Rng.Next(possible.Count)];
                        if (a.Bounds.Left - Art.W >= Area.Left) { approachWindow = a.Handle; approachSide = -1; }
                        else if (a.Bounds.Right + Art.W <= Area.Right) { approachWindow = a.Handle; approachSide = 1; }
                    }
                }
                if (approachWindow != IntPtr.Zero && State == PetState.Walk)
                {
                    Rectangle r;
                    if (!DesktopSurfaces.TryBounds(approachWindow, out r)) approachWindow = IntPtr.Zero;
                    else
                    {
                        double target = approachSide < 0 ? r.Left - Art.W / 2.0 : r.Right + Art.W / 2.0;
                        if (Math.Abs(target - X) <= WalkSpeed * opts.Speed * dt + 2)
                        { X = target; SupportWindow = approachWindow; SupportBounds = r; approachWindow = IntPtr.Zero; SetState(PetState.WindowClimb, w); }
                        else Dir = target < X ? -1 : 1;
                    }
                }
            }
            if (State < PetState.Resting) return false;
            StateT += dt; animT += dt;
            if (Bubble != null && w.Now > BubbleUntil) Bubble = null;
            switch (State)
            {
                case PetState.Thrown: AdvanceThrow(w, dt); break;
                case PetState.Resting:
                case PetState.IdleCry:
                    if (StateT >= extraDuration) { SetState(PetState.Walk, w); DelayIdle(w); }
                    break;
                case PetState.TouchCry:
                case PetState.Hop:
                case PetState.Eating:
                    if (StateT >= extraDuration) SetState(AfterReaction, w);
                    break;
                case PetState.Sliding:
                    if (StateT >= extraDuration) { SetState(PetState.Walk, w); break; }
                    if (SupportWindow != IntPtr.Zero) WalkOnWindow(w, dt, 210 * S * opts.SlideSpeed);
                    else { X += Dir * 210 * S * opts.SlideSpeed * dt; X = Math.Max(Area.Left + HalfW, Math.Min(Area.Right - HalfW, X)); }
                    break;
                case PetState.Wall:
                    X = WallX(Area.Left, Area.Right, wallSide, wallDescending);
                    Y += (wallDescending ? 1 : -1) * 42 * S * opts.Speed * dt;
                    if (!wallDescending && Y <= Area.Top + TopExt(false) + 2)
                    {
                        if (opts.Ceiling && !focus)
                        {
                            double half = Art.Ceiling.W / 2.0;
                            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
                            Y = Area.Top + Art.CeilingContactHeight; Dir = -wallSide; SetState(PetState.Ceiling, w);
                        }
                        else { ClearAttachments(); SetState(PetState.Falling, w); }
                    }
                    else if (wallDescending && Y + BottomExt(true) >= Area.Bottom)
                    {
                        Y = Area.Bottom; Dir = -wallSide;
                        X = wallSide < 0 ? Area.Left + HalfW : Area.Right - HalfW;
                        StartSquish(w, .28, .13, PetState.Walk, false);
                    }
                    break;
                case PetState.Ceiling:
                    Y = Area.Top + Art.CeilingContactHeight;
                    X += Dir * 36 * S * opts.Speed * dt;
                    if (StateT > 14 && w.Rng.NextDouble() < dt * .08) { ClearAttachments(); SetState(PetState.Falling, w); }
                    else if ((Dir < 0 && X <= Area.Left + Art.Ceiling.W / 2.0) || (Dir > 0 && X >= Area.Right - Art.Ceiling.W / 2.0))
                    {
                        if (!opts.Walls) { ClearAttachments(); SetState(PetState.Falling, w); }
                        else
                        {
                            wallSide = Dir; wallDescending = true;
                            X = WallX(Area.Left, Area.Right, wallSide, true);
                            Y = Area.Top + TopExt(true) + 1; SetState(PetState.Wall, w);
                        }
                    }
                    break;
                case PetState.WindowClimb:
                    Rectangle rect;
                    if (SupportWindow == IntPtr.Zero || !DesktopSurfaces.TryBounds(SupportWindow, out rect)) { ClearAttachments(); SetState(PetState.Falling, w); break; }
                    X = approachSide < 0 ? WallX(rect.Left, rect.Left, 1, false) : WallX(rect.Right, rect.Right, -1, false);
                    Y -= 46 * S * opts.Speed * dt;
                    if (Y <= rect.Top + TopExt(false) * .75)
                    { Y = rect.Top; X = approachSide < 0 ? rect.Left + HalfW + 8 : rect.Right - HalfW - 8; Dir = -approachSide; BeginRestPose(w, RestPose.Sit, 5 + w.Rng.NextDouble() * 5); }
                    break;
            }
            return true;
        }

        internal bool BoundaryFeature(IWorld w, double nextX)
        {
            if (!PlayOptions.Current.Walls || PlayOptions.IsFocus(DateTime.Now) || State != PetState.Walk || SupportWindow != IntPtr.Zero || approachWindow != IntPtr.Zero) return false;
            bool hit = Dir > 0 ? nextX + HalfW >= Area.Right : nextX - HalfW <= Area.Left;
            if (!hit) return false;
            double outside = Dir > 0 ? Area.Right + 1 : Area.Left - 1;
            if (w.Areas.Any(r => r != Area && outside >= r.Left && outside < r.Right && Math.Abs(r.Bottom - Y) < 4)) return false;
            wallSide = Dir; wallDescending = false; X = WallX(Area.Left, Area.Right, wallSide, false); Y = Area.Bottom - BottomExt(false); SetState(PetState.Wall, w); return true;
        }

        internal bool WalkOnWindow(IWorld w, double dt, double speed)
        {
            if (SupportWindow == IntPtr.Zero) return false;
            Rectangle r;
            if (!DesktopSurfaces.TryBounds(SupportWindow, out r)) { ClearAttachments(); SetState(PetState.Falling, w); return true; }
            double nx = X + Dir * speed * dt;
            if (nx <= r.Left + HalfW || nx >= r.Right - HalfW)
            {
                Dir = -Dir; nx = Math.Max(r.Left + HalfW + 2, Math.Min(r.Right - HalfW - 2, nx));
                if (State == PetState.Walk && w.Rng.NextDouble() < .35) SetState(PetState.Pause, w);
            }
            X = nx; Y = r.Top; return true;
        }

        internal bool SurfaceFloor(IWorld w)
        {
            if (SupportWindow == IntPtr.Zero) return false;
            Rectangle r;
            if (!DesktopSurfaces.TryBounds(SupportWindow, out r)) { ClearAttachments(); SetState(PetState.Falling, w); return true; }
            Y = r.Top; return true;
        }

        /// <summary>헬리콥터로 천천히 내려와 바닥이나 열린 창 윗면에 착지.</summary>
        internal void HelicopterDrop(IWorld w, double dt)
        {
            double oldY = Y;
            if (Math.Abs(helicopterDrift) > .1)
            {
                double half = HeliHalf;
                X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X + helicopterDrift * dt));
                helicopterDrift *= Math.Exp(-4 * dt);
            }
            double speed = PlayOptions.Current.FallSpeed;
            Vy = Math.Min(95 * S * speed, Math.Max(35 * S * speed, Vy + 55 * S * speed * dt));
            Y += Vy * dt;
            Area = FindArea(w, X, Math.Min(Y, Area.Bottom));
            SurfaceWindow land = null;
            if (PlayOptions.Current.Windows && !PlayOptions.IsFocus(DateTime.Now))
            {
                foreach (var win in DesktopSurfaces.Cached)
                {
                    Rectangle r;
                    if (!DesktopSurfaces.TryBounds(win.Handle, out r)) continue;
                    if (r.Top < oldY - .5 || r.Top > Y || X < r.Left + HalfW || X > r.Right - HalfW || r.Top < Area.Top + Art.H || !DesktopSurfaces.TopVisible(win.Handle, X, r.Top)) continue;
                    if (land == null || r.Top < land.Bounds.Top) land = new SurfaceWindow(win.Handle, r);
                }
            }
            if (land != null)
            {
                SupportWindow = land.Handle; SupportBounds = land.Bounds; Y = land.Bounds.Top; Vy = 0; helicopterDrift = 0;
                StartSquish(w, .3, .15, queuedAfterLand ?? PetState.Walk, false); queuedAfterLand = null; DelayIdle(w); mouseGrace = w.Now + .8;
            }
            else if (Y >= Area.Bottom)
            {
                Y = Area.Bottom; Vy = 0; helicopterDrift = 0; ClearAttachments();
                StartSquish(w, .3, .15, queuedAfterLand ?? PetState.Walk, false); queuedAfterLand = null; DelayIdle(w); mouseGrace = w.Now + .8;
            }
        }

        internal double FeatureJitterX { get { return lookJitterX; } }

        // ───── 쉬는 자세: 앉기 · 잠자기 · 멍때리기 · 기지개 ─────
        internal enum RestPose { Sit, Sleep, Daydream, Stretch, Hobby }
        internal RestPose restPose = RestPose.Sit;
        private RestPose lastRestPose = RestPose.Sit;

        /// <summary>자세는 무작위로 뽑되, 직전과 같으면 한 번 다시 뽑는다(같은 동작이 반복되면 멈춘 것처럼 보인다).</summary>
        private RestPose ChooseRestPose(IWorld w)
        {
            Func<RestPose> roll = () =>
            {
                double r = w.Rng.NextDouble();
                return r < .26 ? RestPose.Sit : r < .46 ? RestPose.Hobby : r < .66 ? RestPose.Daydream : r < .84 ? RestPose.Stretch : RestPose.Sleep;
            };
            RestPose pose = roll();
            if (pose == lastRestPose) pose = roll();
            return pose;
        }

        internal void BeginRestPose(IWorld w, RestPose pose, double baseSeconds, int variant = -1)
        {
            restPose = lastRestPose = pose;
            switch (pose)
            {
                case RestPose.Sleep: extraDuration = baseSeconds * 1.4; break;
                case RestPose.Daydream: extraDuration = baseSeconds * .75; break;
                case RestPose.Stretch: extraDuration = 4.2; break;
                case RestPose.Hobby: extraDuration = Math.Max(8, Math.Min(12, baseSeconds)); break;
                default: extraDuration = baseSeconds; break;
            }
            SetState(PetState.Resting, w);
            if (pose == RestPose.Hobby)
            {
                // 스폰지밥·다람이는 대표 동작이 두 가지다(해파리 잡이/비눗방울, 가라테/올가미)
                hobbyVariant = (Ci == Sp || Ci == Sa) ? (variant >= 0 ? variant : w.Rng.Next(2)) : 0;
                if (Ci == Pa) extraDuration = 9;
                string[] lines = Art.Info.HobbyLines;
                Bubble = lines[hobbyVariant % lines.Length]; BubbleUntil = w.Now + 3.5;
            }
        }

        // ───── 벽 타기 자세: 발을 벽에 대고 몸을 옆으로 눕혀 걷는다 (머리는 벽 반대쪽) ─────
        private const float WallAscendDeg = 80f, WallDescendDeg = 100f;

        /// <summary>벽 오른쪽(side=+1) 기준, 발끝 원점에서 본 기울어진 몸의 영역.</summary>
        private RectangleF TiltedBox(bool descending)
        {
            var m = new System.Drawing.Drawing2D.Matrix();
            m.Rotate(-(descending ? WallDescendDeg : WallAscendDeg));
            var p = new[] { new PointF(-Art.W / 2f, -Art.H), new PointF(Art.W / 2f, -Art.H), new PointF(Art.W / 2f, 0), new PointF(-Art.W / 2f, 0) };
            m.TransformPoints(p);
            float l = p.Min(q => q.X), r = p.Max(q => q.X), t = p.Min(q => q.Y), b = p.Max(q => q.Y);
            return new RectangleF(l, t, r - l, b - t);
        }
        private double TopExt(bool descending) { return -TiltedBox(descending).Top; }
        private double BottomExt(bool descending) { return TiltedBox(descending).Bottom; }
        /// <summary>발끝이 벽에 닿도록 하는 X.</summary>
        private double WallX(double left, double right, int side, bool descending)
        { float edge = TiltedBox(descending).Right; return side < 0 ? left + edge : right - edge; }
        /// <summary>벽에 붙은 몸이 차지하는 화면 영역(친구끼리 부딪힘 판정용).</summary>
        internal RectangleF WallBody(int side, bool descending)
        {
            RectangleF b = TiltedBox(descending);
            float l = side > 0 ? b.Left : -b.Right, r = side > 0 ? b.Right : -b.Left;
            return new RectangleF((float)X + l, (float)Y + b.Top, r - l, b.Height);
        }


        // ───── 위치 고정 ─────
        internal void ClampPinned(IWorld world)
        {
            Area = FindArea(world, X, Y);
            double half = Art.W / 2.0 + 2;
            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
            Y = Math.Max(Area.Top + Art.H + 2, Math.Min(Area.Bottom, Y));
        }

        internal void RestorePinned(IWorld world)
        {
            var o = PlayOptions.Current;
            if (!double.IsNaN(o.FixedX) && !double.IsNaN(o.FixedY)) { X = o.FixedX; Y = o.FixedY; }
            SyncPinned(world);
        }

        internal void SyncPinned(IWorld world)
        {
            if (PlayOptions.Current.PositionFixed)
            {
                pinnedActive = true; ReleaseFeatures(world); ResetSecretThrows(); queuedAfterLand = null;
                ClampPinned(world); SetState(PetState.Pause, world); Vy = 0;
            }
            else if (pinnedActive)
            {
                pinnedActive = false; ReleaseFeatures(world); Vy = 0;
                SetState(Y < Area.Bottom - 1 ? PetState.Falling : PetState.Walk, world); DelayIdle(world);
            }
        }

        private bool AdvancePinned(IWorld world, double dt)
        {
            if (!PlayOptions.Current.PositionFixed) { if (pinnedActive) SyncPinned(world); return false; }
            if (State == PetState.Drag) return false;
            if (!pinnedActive) SyncPinned(world);
            StateT += dt; animT += dt;
            if (Bubble != null && world.Now > BubbleUntil) Bubble = null;
            if (State == PetState.Squish && StateT >= squishDur
                || (State == PetState.TouchCry || State == PetState.Hop || State == PetState.Eating || State == PetState.IdleCry || State == PetState.Resting) && StateT >= extraDuration
                || State == PetState.Waking && StateT > stateDur
                || State == PetState.Cheer && Bubble == null && world.Now > cheerHopUntil)
                SetState(PetState.Pause, world);
            if (State == PetState.Gather || State == PetState.Walk || State == PetState.Falling || State == PetState.Thrown || State == PetState.PilotRise
                || State == PetState.Wall || State == PetState.Ceiling || State == PetState.WindowClimb || State == PetState.Climb || State == PetState.SecretDance || State == PetState.Dizzy)
                SyncPinned(world);
            return true;
        }
    }
}
