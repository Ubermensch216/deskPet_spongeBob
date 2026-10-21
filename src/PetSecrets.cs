using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BikiniFriends
{
    /// <summary>숨겨진 반응(어질어질·몰래 군무·비밀 조종사), 꽃다발, 친구끼리 부딪힘.</summary>
    internal sealed partial class Pet
    {
        private double flowerUntil, flowerStarted, orbitAngle, orbitTotal, orbitAt, orbitStarted, dizzyCooldown;
        private double orbitCenterX, orbitCenterY, orbitMotionAngle, orbitCurveTotal;
        private Point orbitMouse;
        private bool orbitMotionSeen, orbitSeen, pilotFlight;
        private int orbitDirection, throwCount;
        private double throwSequenceStarted, danceStarted, contactReactionUntil;

        internal bool IsCompanionFloorPose
        { get { return State == PetState.Walk || State == PetState.Pause || State == PetState.Resting || State == PetState.IdleCry || State == PetState.Squish; } }
        internal bool IsPilotFlight { get { return pilotFlight && State == PetState.PilotRise; } }

        internal bool CanDance(double now)
        { return !PlayOptions.Current.PositionFixed && !Hidden && !InputHeld && now >= flowerUntil && (IsCompanionFloorPose || State == PetState.Sliding); }

        private void ResetSecretThrows() { throwCount = 0; throwSequenceStarted = 0; pilotFlight = false; }

        /// <summary>10초 안에 같은 친구를 세 번 던지면 비밀 조종사.</summary>
        internal void RegisterSecretThrow(IWorld w)
        {
            pilotFlight = false;
            if (!PlayOptions.Current.Secrets) { ResetSecretThrows(); return; }
            if (throwCount == 0 || w.Now - throwSequenceStarted > 10) { throwCount = 0; throwSequenceStarted = w.Now; }
            throwCount++;
            if (throwCount >= 3)
            { throwCount = 0; pilotFlight = true; FriendData.Current.Discover("pilot"); Bubble = "비밀 조종사! 천장으로 출발!"; BubbleUntil = w.Now + 4; }
        }

        internal void StartFlower(IWorld w, long milestone)
        {
            flowerStarted = w.Now; flowerUntil = w.Now + 6; mouseGrace = flowerUntil + 1;
            Bubble = "친밀도 " + milestone.ToString("N0") + " 달성!\n" + Art.Info.GiftLine + " ♥"; BubbleUntil = flowerUntil;
            SetState(PetState.Pause, w);
        }

        internal void StartSecretDance(IWorld w, double togetherStart)
        { danceStarted = togetherStart; extraDuration = 6; mouseGrace = w.Now + 8; orbitSeen = false; orbitTotal = 0; SetState(PetState.SecretDance, w); }

        internal void StartDizzy(IWorld w)
        {
            dizzyCooldown = w.Now + 20; orbitTotal = 0; orbitSeen = false;
            extraDuration = 4; mouseGrace = w.Now + 5; Bubble = Art.Info.DizzyLine; BubbleUntil = w.Now + 3.5;
            SetState(PetState.Dizzy, w); FriendData.Current.Discover("dizzy");
        }

        private bool CirclingMouse(double now)
        { return orbitSeen && Math.Abs(orbitTotal) > .10 && Math.Abs(orbitCurveTotal) > .16 && now - orbitAt < .45; }

        private bool AdvanceSecrets(IWorld w, double dt)
        {
            if (!PlayOptions.Current.Secrets)
            { ResetSecretThrows(); if (State == PetState.PilotRise) SetState(PetState.Falling, w); }
            if (State == PetState.PilotRise)
            {
                orbitSeen = false; orbitTotal = 0; StateT += dt; animT += dt;
                if (Bubble != null && w.Now > BubbleUntil) Bubble = null;
                double half = HeliHalf;
                X += helicopterDrift * dt; helicopterDrift *= Math.Exp(-4 * dt);
                X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
                Y -= 185 * S * dt;
                if (Y <= Area.Top + HeliH + 1)
                {
                    ClearAttachments(); pilotFlight = false; Vy = 0; helicopterDrift = 0;
                    half = Art.Ceiling.W / 2.0;
                    X = Math.Max(Area.Left + half + 1, Math.Min(Area.Right - half - 1, X));
                    if (X > Area.Right - Art.Ceiling.W * 2) Dir = -1; else if (X < Area.Left + Art.Ceiling.W * 2) Dir = 1;
                    Y = Area.Top + Art.CeilingContactHeight; mouseGrace = w.Now + 1; SetState(PetState.Ceiling, w);
                    Bubble = "천장 도착! 살금살금…"; BubbleUntil = w.Now + 2.5;
                }
                return true;
            }
            if (State == PetState.Dizzy || State == PetState.SecretDance)
            {
                StateT += dt; animT += dt;
                if (Bubble != null && w.Now > BubbleUntil) Bubble = null;
                SnapToFloor(w);
                if (StateT >= extraDuration) { SetState(PetState.Walk, w); DelayIdle(w); }
                return true;
            }
            if (w.Now < flowerUntil && IsCompanionFloorPose) { animT += dt; SnapToFloor(w); return true; }
            if (!PlayOptions.Current.Secrets || (!IsCompanionFloorPose && State != PetState.Sliding) || InputHeld || w.Now < dizzyCooldown)
            { orbitSeen = false; orbitTotal = 0; return false; }
            Point mouse = DesktopSurfaces.Mouse;
            if (Math.Abs(mouse.X - X) > Art.W * 3.5 || mouse.Y < Y - Art.H * 4 || mouse.Y > Y + Art.H * .6)
            { orbitSeen = false; orbitTotal = 0; return false; }
            bool fresh = !orbitSeen || w.Now - orbitAt > .8 || w.Now - orbitStarted > 6;
            if (fresh) { orbitCenterX = X; orbitCenterY = Y - Art.H * .8; }
            double dx = mouse.X - orbitCenterX, dy = mouse.Y - orbitCenterY;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            if (radius < Art.W * .12 || radius > Art.W * 3.5) { orbitSeen = false; orbitTotal = 0; return false; }
            double angleNow = Math.Atan2(dy, dx);
            if (fresh)
            { orbitSeen = true; orbitStarted = orbitAt = w.Now; orbitAngle = angleNow; orbitTotal = orbitCurveTotal = 0; orbitDirection = 0; orbitMouse = mouse; orbitMotionSeen = false; return false; }
            double mx = mouse.X - orbitMouse.X, my = mouse.Y - orbitMouse.Y; orbitMouse = mouse;
            if (mx * mx + my * my >= 1)
            {
                double motion = Math.Atan2(my, mx);
                if (orbitMotionSeen)
                {
                    double turn = motion - orbitMotionAngle;
                    while (turn > Math.PI) turn -= Math.PI * 2;
                    while (turn < -Math.PI) turn += Math.PI * 2;
                    orbitCurveTotal += turn;
                }
                orbitMotionAngle = motion; orbitMotionSeen = true;
            }
            double diff = angleNow - orbitAngle;
            while (diff > Math.PI) diff -= Math.PI * 2;
            while (diff < -Math.PI) diff += Math.PI * 2;
            orbitAngle = angleNow;
            if (Math.Abs(diff) > 1.9) { orbitSeen = false; orbitTotal = 0; return false; }
            if (Math.Abs(diff) > .012)
            {
                int direction = Math.Sign(diff);
                if (orbitDirection != 0 && direction != orbitDirection) orbitTotal = 0;
                orbitDirection = direction; orbitTotal += diff; orbitAt = w.Now;
            }
            if (Math.Abs(orbitTotal) >= Math.PI * 2 - .18 && Math.Abs(orbitCurveTotal) > Math.PI) { StartDizzy(w); return true; }
            if (CirclingMouse(w.Now))
            {
                // 일부러 그리는 원은 도망보다 우선: 가운데를 지키며 제자리에 선다
                if (State == PetState.Sliding) SetState(PetState.Pause, w);
                mouseGrace = w.Now + .5; hoverSince = -1; animT += dt; SnapToFloor(w); return true;
            }
            return false;
        }

        /// <summary>친구끼리 부딪힘 판정에 쓰는 몸통 영역(프로펠러·장식 제외).</summary>
        internal RectangleF ContactBody(double now)
        {
            if (State == PetState.Wall) return WallBody(wallSide, wallDescending);
            if (State == PetState.WindowClimb) return WallBody(-approachSide, false);
            if (State == PetState.Falling || State == PetState.PilotRise || State == PetState.Thrown)
                return new RectangleF((float)X - Art.W * .43f, (float)Y - Art.H, Art.W * .86f, Art.H * .95f);
            Look look = GetLook(now);
            float bw = Art.ContactW * look.Sx, bh = Art.H * (look.Pose.Sit ? .92f : .98f) * look.Sy;
            return new RectangleF((float)X - bw / 2, (float)Y - bh, Math.Max(8, bw), Math.Max(8, bh));
        }

        internal void Bump(IWorld w, int direction)
        {
            if (PlayOptions.Current.PositionFixed) return;
            Dir = direction;
            if (State == PetState.Wall) { wallDescending = direction > 0; return; }
            if (State == PetState.Thrown) { throwVx = direction * Math.Max(110 * S, Math.Abs(throwVx) * .65); return; }
            if (State == PetState.Falling) { helicopterDrift = direction * 70 * S; return; }
            if (!IsCompanionFloorPose && State != PetState.Sliding && State != PetState.TouchCry && State != PetState.Hop && State != PetState.Eating) return;
            if (w.Now < contactReactionUntil) return;
            contactReactionUntil = w.Now + .45;
            StartSquish(w, .22, .12, PetState.Walk, false); mouseGrace = w.Now + .6;
        }

        internal void ClampContact()
        {
            if (State == PetState.Drag || State == PetState.Wall || State == PetState.WindowClimb) return;
            double half = Art.W * .46;
            X = Math.Max(Area.Left + half, Math.Min(Area.Right - half, X));
            if (SupportWindow != IntPtr.Zero) X = Math.Max(SupportBounds.Left + half, Math.Min(SupportBounds.Right - half, X));
        }

        internal void ClampWallContact()
        {
            if (State != PetState.Wall && State != PetState.WindowClimb) return;
            Y = Math.Max(Area.Top + TopExt(wallDescending) + 1, Math.Min(Area.Bottom - BottomExt(wallDescending), Y));
        }
    }
}
