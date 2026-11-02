using System;
using System.Drawing;

namespace BikiniFriends
{
    internal enum ChatPhase { None, Listening, Thinking, Talking, Afterglow }

    /// <summary>
    /// AI 와 이야기하는 친구. 채팅창 대신 캐릭터의 몸이 대화를 한다:
    /// 말을 걸면 돌아보고(Listening), 생각에 잠기며(Thinking), 글자가 한 자씩 말풍선에 찍히고
    /// 감정에 맞는 표정·몸짓(Talking · Afterglow)이 나온다.
    /// </summary>
    internal sealed partial class Pet
    {
        internal ChatPhase Phase = ChatPhase.None;
        internal ChatMood Mood = ChatMood.Neutral;
        /// <summary>입력창이 있는 쪽(+1 오른쪽, -1 왼쪽). 친구가 그쪽을 바라본다.</summary>
        internal int ChatSide = 1;
        /// <summary>글자가 한 자 찍힐 때마다 부른다(말소리 효과음용).</summary>
        internal Action<char> OnChatChar;

        private double chatPresence, chatPhaseT, chatMoodT, chatReveal, chatTypeEnergy;
        private string chatText = "";
        private bool chatStreamDone = true, chatTypeDeleted;
        private int chatTypedLen;

        internal bool ChatActive { get { return Phase != ChatPhase.None; } }
        internal string ChatText { get { return chatText; } }
        internal bool ChatRevealing { get { return chatText.Length > 0 && chatReveal < chatText.Length; } }
        /// <summary>대화 상태가 된 뒤(또는 마지막 말이 끝난 뒤) 흐른 시간.</summary>
        internal double ChatQuietSeconds { get { return Phase == ChatPhase.Listening ? chatPhaseT : 0; } }
        internal float ChatPresence { get { return (float)chatPresence; } }

        /// <summary>지금 말을 걸어도 되는 자세인가(낙하·던져짐·벽/천장 타기 중이 아닐 때).</summary>
        internal bool CanChat
        {
            get
            {
                if (Hidden) return false;
                switch (State)
                {
                    case PetState.Falling: case PetState.Drag: case PetState.Thrown: case PetState.PilotRise: case PetState.Climb:
                    case PetState.Wall: case PetState.Ceiling: case PetState.WindowClimb:
                        return false;
                }
                return true;
            }
        }

        internal bool BeginChat(IWorld w, int side)
        {
            if (!CanChat) return false;
            ChatSide = side >= 0 ? 1 : -1;
            if (State != PetState.Chat) { ReleaseFeatures(w); SetState(PetState.Chat, w); }
            Phase = ChatPhase.Listening; Mood = ChatMood.Neutral; chatPhaseT = 0; chatMoodT = 0;
            chatText = ""; chatReveal = 0; chatStreamDone = true; chatTypeEnergy = 0; chatTypedLen = 0;
            return true;
        }

        internal void EndChat(IWorld w)
        {
            if (Phase == ChatPhase.None) return;
            if (!string.IsNullOrEmpty(chatText) && Bubble == chatText) Bubble = null;
            Phase = ChatPhase.None; chatText = ""; chatReveal = 0; Mood = ChatMood.Neutral;
            if (State == PetState.Chat) { SetState(PlayOptions.Current.PositionFixed ? PetState.Pause : PetState.Walk, w); DelayIdle(w); }
        }

        /// <summary>사용자가 말을 보냈다: 생각에 잠긴다.</summary>
        internal void ChatThink()
        {
            if (Phase == ChatPhase.None) return;
            if (!string.IsNullOrEmpty(chatText) && Bubble == chatText) Bubble = null;
            chatText = ""; chatReveal = 0; chatStreamDone = false;
            Phase = ChatPhase.Thinking; chatPhaseT = 0; SetMood(ChatMood.Think);
        }

        /// <summary>모델이 보내 주는 말(스트리밍 중에는 점점 길어진다). done 이면 끝까지 받은 것.</summary>
        internal void ChatSay(ChatMood mood, string text, bool done)
        {
            if (Phase == ChatPhase.None) return;
            if (text == null) text = "";
            if (text.Length > 0 && Phase != ChatPhase.Talking && Phase != ChatPhase.Afterglow) { Phase = ChatPhase.Talking; chatPhaseT = 0; }
            if (text.Length > 0 && Phase == ChatPhase.Afterglow && !text.StartsWith(chatText)) { chatReveal = 0; Phase = ChatPhase.Talking; chatPhaseT = 0; }
            if (text.Length > 0) SetMood(mood);
            if (text.Length < chatText.Length && !text.Equals(chatText)) chatReveal = Math.Min(chatReveal, text.Length);
            chatText = text; chatStreamDone = done;
            if (done && text.Length == 0) { Phase = ChatPhase.Listening; chatPhaseT = 0; SetMood(ChatMood.Neutral); }
        }

        /// <summary>대화에 실패했을 때(Ollama 가 꺼져 있을 때 등): 사정을 말한다.</summary>
        internal void ChatSayInstant(ChatMood mood, string text)
        {
            if (Phase == ChatPhase.None) return;
            chatReveal = 0; Phase = ChatPhase.Talking; chatPhaseT = 0; SetMood(mood);
            chatText = text ?? ""; chatStreamDone = true;
        }

        private void SetMood(ChatMood m) { if (m != Mood) { Mood = m; chatMoodT = 0; } }

        /// <summary>입력창에서 글자를 치는 중: 눈으로 따라가며 반응한다.</summary>
        internal void ChatTyping(int length)
        {
            if (Phase != ChatPhase.Listening && Phase != ChatPhase.Afterglow) { chatTypedLen = length; return; }
            if (length != chatTypedLen) { chatTypeDeleted = length < chatTypedLen; chatTypeEnergy = 1; }
            chatTypedLen = length;
        }

        private void UpdateChat(IWorld w, double dt)
        {
            double target = Phase != ChatPhase.None ? 1 : 0;
            if (chatPresence != target) chatPresence += Math.Sign(target - chatPresence) * Math.Min(Math.Abs(target - chatPresence), dt * (target > chatPresence ? 2.6 : 3.5));
            if (Phase == ChatPhase.None) return;
            chatPhaseT += dt; chatMoodT += dt;
            chatTypeEnergy = Math.Max(0, chatTypeEnergy - dt * 1.3);
            if (chatText.Length > 0)
            {
                double rate = Mood == ChatMood.Sleepy ? 9 : (Mood == ChatMood.Joy || Mood == ChatMood.Surprise || Mood == ChatMood.Pout) ? 19 : 15;
                if (chatText.Length - chatReveal > 30) rate *= 1.8;
                int before = (int)chatReveal;
                chatReveal = Math.Min(chatText.Length, chatReveal + rate * dt);
                for (int i = before; i < (int)chatReveal && i < chatText.Length; i++)
                    if (!char.IsWhiteSpace(chatText[i]) && OnChatChar != null) { try { OnChatChar(chatText[i]); } catch { } }
                Bubble = chatText; BubbleUntil = w.Now + 1.5;
                if (chatReveal >= chatText.Length && chatStreamDone && Phase == ChatPhase.Talking) { Phase = ChatPhase.Afterglow; chatPhaseT = 0; }
            }
            if (Phase == ChatPhase.Afterglow && chatPhaseT > 6.5 + chatText.Length * .08)
            {
                if (Bubble == chatText) Bubble = null;
                chatText = ""; chatReveal = 0; Phase = ChatPhase.Listening; chatPhaseT = 0; SetMood(ChatMood.Neutral);
            }
        }

        /// <summary>말풍선에 지금까지 찍힌 글자 수(-1 이면 전부).</summary>
        private int ChatRevealCount
        {
            get
            {
                if (Phase == ChatPhase.None || chatText.Length == 0 || Bubble != chatText) return -1;
                return chatReveal >= chatText.Length ? -1 : (int)chatReveal;
            }
        }

        // ───────── 자세 ─────────
        private void ChatPose(Pose P, ref Look L, double now)
        {
            double t = animT;
            L.FaceRight = false;
            IdleBase(P, t, false);
            int side = ChatSide;
            if (Phase == ChatPhase.Listening || Phase == ChatPhase.None) ListeningPose(P, ref L, t, side);
            else if (Phase == ChatPhase.Thinking) ThinkingPose(P, ref L, t, side);
            else
            {
                double k = Ease(chatMoodT / .3);
                if (Phase == ChatPhase.Afterglow) k *= 1 - Ease((chatPhaseT - 3.6) / 1.6);
                ListeningPose(P, ref L, t, side);
                MoodPose(P, ref L, t, side, k);
            }
        }

        /// <summary>듣는 자세: 말을 거는 쪽을 보고 고개를 갸웃. 글자를 치면 시선이 아래(입력창)로, 지우면 갸웃.</summary>
        private void ListeningPose(Pose P, ref Look L, double t, int side)
        {
            double e = chatTypeEnergy;
            P.Gx = F(side * (.35 - .08 * e)); P.Gy = F(.25 + .4 * e);
            P.HeadTilt = F(P.HeadTilt + side * 2.5 + (chatTypeDeleted ? -side * 4 * e : side * 1.5 * e));
            P.Brow = F(chatTypeDeleted ? -.25 * e : 0);
            P.EarL += F(Math.Sin(t * 15) * 3.5 * e); P.EarR += F(Math.Sin(t * 15 + 1) * 3.5 * e);
            if (chatTypedLen >= 24) { P.EyeScale = 1.06f; }
        }

        private void ThinkingPose(Pose P, ref Look L, double t, int side)
        {
            double e = Ease(chatPhaseT / .35);
            P.Gx = F(side * .25); P.Gy = F(-.55 * e);
            P.HeadTilt = F(P.HeadTilt - 4 * e * side);
            P.Eyes = e > .5 ? EyeStyle.Sleep : EyeStyle.Open;
            P.Brow = F(-.15 * e);
            P.ArmsFront = true;
            if (Ci == Kr) P.Claw = F(e);
            else if (Ci == Sq) { P.ArmsFront = true; P.Clasp = true; }
            L.Decor |= Decor.Dots; L.DecorPhase = (float)t;
        }

        private void MoodPose(Pose P, ref Look L, double t, int side, double k)
        {
            if (k <= .01) return;
            double mt = chatMoodT;
            switch (Mood)
            {
                case ChatMood.Joy:
                {
                    P.Eyes = EyeStyle.Happy; P.OpenL = P.OpenR = 0; P.Flush = F(.45 * k); P.Mouth = MouthStyle.Open; P.MouthOpen = .5f;
                    double q = (mt % .72) / .72, up = mt < 2.9 ? Math.Sin(Math.PI * q) : 0;
                    L.HopY = F(-up * 9 * S * k);
                    P.ArmLA = F(60 + 90 * up * k); P.ArmRA = F(60 + 90 * up * k); P.ArmLB = F(43 + 110 * up * k); P.ArmRB = F(43 + 110 * up * k);
                    P.FootLY = P.FootRY = F(-6 * up * k);
                    break;
                }
                case ChatMood.Shy:
                {
                    P.Flush = F(.95 * k); P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = F(.65 + .2 * Math.Sin(t * 1.3));
                    P.Gx = F(-side * .8 * k); P.Gy = F(.55 * k);
                    P.HeadTilt = F(P.HeadTilt - side * 5 * k); P.Lean = F(Math.Sin(t * 2.2) * 1.6 * k);
                    P.ArmsFront = true;
                    break;
                }
                case ChatMood.Sad:
                    SadOverlay(P, mt); P.Gx = F(P.Gx * .5);
                    break;
                case ChatMood.Surprise:
                {
                    double q = Math.Max(0, Math.Min(1, 1 - mt / .35));
                    P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.EyeScale = F(1.08 + .1 * q); P.PupilR = .65f; P.Brow = F(-.35 * k); P.Gy = F(-.3); P.Gx = 0;
                    P.Mouth = MouthStyle.Open; P.MouthOpen = .5f;
                    P.Sweat = F(mt);
                    break;
                }
                case ChatMood.Sleepy:
                {
                    double nod = Math.Sin(t * 1.1);
                    P.Eyes = EyeStyle.Sleep; P.OpenL = P.OpenR = 0; P.Gx = 0; P.Gy = F(.3);
                    P.HeadTilt = F(P.HeadTilt + 7 * k * (nod * .5 + .5)); P.HeadY = F(2 * k * (nod * .5 + .5));
                    L.Decor |= Decor.Sleepy; L.ZPhase = (int)(t * 20) % 100000; L.ZTime = (float)t;
                    break;
                }
                case ChatMood.Think:
                {
                    P.Eyes = EyeStyle.Sleep; P.Gx = F(side * .3); P.Gy = F(-.5 * k);
                    P.HeadTilt = F(P.HeadTilt - 4 * k * side);
                    P.ArmsFront = true;
                    break;
                }
                case ChatMood.Pout:
                {
                    P.Eyes = EyeStyle.Open; P.OpenL = P.OpenR = 1; P.Brow = F(.9 * k); P.PupilR = .85f;
                    P.Mouth = MouthStyle.Pout; P.MouthOpen = .2f;
                    P.Gx = F(-side * .6 * k); P.Gy = F(.1);
                    P.Anger = F(Math.Min(1, mt * 3 + .15)) * (mt < 2.4 ? 1f : 0f);
                    P.Lean = F(Math.Sin(mt * 9) * 1.2 * k);
                    break;
                }
                case ChatMood.Hungry:
                    HungryCryPose(P, mt);
                    break;
                case ChatMood.Blank:
                    DaydreamPose(P, mt);
                    break;
                default:
                    P.Eyes = EyeStyle.Happy; P.Flush = F(.2 * k);
                    if (Ci == Kr) P.EyesDollar = mt < 1.5;
                    break;
            }
        }
    }
}