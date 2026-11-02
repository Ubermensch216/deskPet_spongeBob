using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 친구 ↔ 로컬 LLM 대화를 이끄는 곳. 말 걸기(입력칸) → 친구가 생각 → 스트리밍으로 받은 말을 감정 태그와 함께
    /// 친구의 표정·말풍선으로 옮긴다. 대화 상태는 한 번에 한 마리와만 한다.
    /// </summary>
    internal sealed class ChatController : IDisposable
    {
        private enum Kind { Token, Done, Error, Probe, Facts, Diary, Silent }
        private sealed class Evt { internal int Gen; internal Kind Kind; internal string Text; internal ProbeResult Probe; internal List<string> Facts; internal DateTime When; }

        private readonly App app;
        private readonly ConcurrentQueue<Evt> inbox = new ConcurrentQueue<Evt>();
        private readonly List<ChatMessage> history = new List<ChatMessage>();
        private Pet pet; private ChatInput input; private ReplyParser parser; private ChatRequest request;
        private int gen, extractedUpTo;
        private bool streaming, chipsWanted;
        private double lastActivity, nextProactive = -1, lastWarm = -999, lastTop, clickGraceUntil;
        private bool mouseWasDown;
        internal bool TestIgnoreClicks;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        private static bool MouseDown() { try { return GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0; } catch { return false; } }
        private HotkeyWindow hotkey;
        private readonly Random rng = new Random();

        /// <summary>단축키가 실제로 등록돼 있는가(다른 프로그램이 이미 쓰는 조합이면 false).</summary>
        internal bool HotkeyRegistered { get { return hotkey != null; } }
        internal Pet Pet { get { return pet; } }
        internal bool Active { get { return pet != null; } }
        internal List<ChatMessage> History { get { return history; } }

        internal ChatController(App app)
        {
            this.app = app;
            ChatOptions.Load();
            ApplyOptions();
        }

        /// <summary>설정이 바뀌면 단축키를 다시 건다.</summary>
        internal void ApplyOptions()
        {
            var o = ChatOptions.Current;
            if (hotkey != null) { hotkey.Dispose(); hotkey = null; }
            if (o.Enabled && o.Hotkey)
            {
                try { hotkey = new HotkeyWindow(); hotkey.Pressed += delegate { app.StartChatFromHotkey(); }; if (!hotkey.Register(o.HotkeyMods, o.HotkeyKey)) { hotkey.Dispose(); hotkey = null; } }
                catch { hotkey = null; }
            }
            if (!o.Enabled && pet != null) End();
            nextProactive = -1;
        }

        // ───────── 시작·끝 ─────────
        internal void Begin(Pet p)
        {
            var o = ChatOptions.Current; double now = app.Now;
            if (p == null) return;
            if (!o.Enabled) { Say(p, "AI 대화가 꺼져 있어. 설정 → AI 대화에서 켤 수 있어!"); return; }
            if (pet == p && input != null) { input.Focus(); return; }
            if (pet != null && pet != p) End();
            if (pet == null)
            {
                if (!p.CanChat) { Say(p, "잠깐, 땅에 내려가서 얘기하자!"); return; }
                if (!p.BeginChat(app, p.ChatSide)) return;
                pet = p; streaming = false; chipsWanted = true;
                ChatMood mood; string hello = ChatPersona.Greeting(rng, DateTime.Now, out mood, p);
                p.ChatSayInstant(mood, hello);
                StartProbe();
            }
            lastActivity = now; clickGraceUntil = now + .6; mouseWasDown = MouseDown();
            pet.OnChatChar = ch => ChatSound.Char(ch, pet != null ? pet.Mood : ChatMood.Neutral);
            OpenInput();
        }

        private void Say(Pet p, string text) { p.Bubble = text; p.BubbleUntil = app.Now + 4; }

        private void OpenInput()
        {
            if (input != null || pet == null) return;
            input = new ChatInput(); input.SetCue(ChatMode.Current.Cue);
            input.Submitted += Send;
            input.Cancelled += End;
            input.Typing += n => { lastActivity = app.Now; if (pet != null) pet.ChatTyping(n); };
            input.ActivityChanged += delegate { lastActivity = app.Now; };
            Point pos = InputPosition();
            input.Show(pos.X, pos.Y);
        }

        /// <summary>입력칸은 친구 발치 옆(공간이 있는 쪽)에 둔다. 친구도 그쪽을 바라본다.</summary>
        private Point InputPosition()
        {
            float s = CharacterArt.Scale; Rectangle a = pet.Area; int pw = input.PillWidth;
            double half = pet.Art.W * .5, gap = 6 * s;
            bool rightOk = pet.X + half + gap + pw <= a.Right - 2, leftOk = pet.X - half - gap - pw >= a.Left + 2;
            int side = pet.ChatSide;
            if (side > 0 && !rightOk && leftOk) side = -1; else if (side < 0 && !leftOk && rightOk) side = 1;
            pet.ChatSide = side;
            int left = side > 0 ? (int)(pet.X + half + gap) : (int)(pet.X - half - gap - pw);
            left = Math.Max(a.Left + 2, Math.Min(a.Right - pw - 2, left));
            int bottom = (int)(pet.Y - 6 * s);
            return input.Anchor(left, bottom);
        }

        internal void End()
        {
            if (request != null) request.Abort();
            streaming = false; chipsWanted = false; gen++;
            Pet p = pet; pet = null;
            if (p != null) { p.OnChatChar = null; p.EndChat(app); }
            if (input != null) { var i = input; input = null; i.Dispose(); }
            Digest(p);
        }

        // ───────── 말 보내기·받기 ─────────
        private void Send(string text)
        {
            if (pet == null) return;
            lastActivity = app.Now;
            if (request != null) request.Abort();
            input.ClearChips(); chipsWanted = false;
            input.Echo(text);
            history.Add(new ChatMessage("user", text));
            pet.ChatThink();
            StartStream(ChatPersona.Build(history, DateTime.Now, pet), false);
        }

        private void StartStream(List<ChatMessage> msgs, bool proactive)
        {
            var opts = ChatOptions.Current; int g = ++gen;
            var mode = ChatMode.Current; parser = new ReplyParser(mode.MaxSentences, mode.MaxChars); var req = request = new ChatRequest(); streaming = true;
            var worker = new Thread(delegate ()
            {
                if (proactive && OllamaClient.Probe(opts) != ProbeResult.Ok) { inbox.Enqueue(new Evt { Gen = g, Kind = Kind.Silent }); return; }
                string err = OllamaClient.Stream(opts, msgs, tok => inbox.Enqueue(new Evt { Gen = g, Kind = Kind.Token, Text = tok }), req, mode.NumPredict, mode.Temperature);
                if (err == null) inbox.Enqueue(new Evt { Gen = g, Kind = Kind.Done });
                else if (err != "cancelled") inbox.Enqueue(new Evt { Gen = g, Kind = proactive ? Kind.Silent : Kind.Error, Text = err });
            }) { IsBackground = true };
            worker.Start();
        }

        private void StartProbe()
        {
            var opts = ChatOptions.Current; int g = gen; double now = app.Now;
            bool warm = now - lastWarm > 25 * 60; if (warm) lastWarm = now;
            new Thread(delegate ()
            {
                ProbeResult r = OllamaClient.Probe(opts);
                inbox.Enqueue(new Evt { Gen = g, Kind = Kind.Probe, Probe = r });
                if (r == ProbeResult.Ok && warm) OllamaClient.Warmup(opts);
            }) { IsBackground = true }.Start();
        }

        internal void ModeChanged() { if (input != null) input.SetCue(ChatMode.Current.Cue); }

        // ───────── 틱 ─────────
        internal void Tick(double now)
        {
            Evt e;
            while (inbox.TryDequeue(out e)) Handle(e, now);
            var o = ChatOptions.Current;
            if (pet != null)
            {
                if (pet.Hidden || pet.State != PetState.Chat || app.AllHidden) { End(); return; }
                if (input != null)
                {
                    bool down = MouseDown(), pressed = down && !mouseWasDown; mouseWasDown = down;
                    if (pressed && !TestIgnoreClicks && now > clickGraceUntil && !input.Contains(Cursor.Position)) { End(); return; }
                    bool focused = input.Focused;
                    if (!streaming && !pet.ChatRevealing && now - lastActivity > (focused ? 60 : 12)) { End(); return; }
                    Point pos = InputPosition(); input.Place(pos.X, pos.Y);
                    if (now - lastTop > 2) { lastTop = now; input.KeepOnTop(); }
                    if (chipsWanted && !streaming && !pet.ChatRevealing && pet.Phase != ChatPhase.Thinking)
                    {
                        chipsWanted = false;
                        if (o.Chips) input.ShowChips(ChatPersona.Chips(pet.Mood, history.Count == 0, rng, pet));
                    }
                }
                else if (pet.Phase == ChatPhase.Listening && !streaming) End();
                return;
            }
            TickProactive(now, o);
        }

        private void Handle(Evt e, double now)
        {
            if (e.Kind == Kind.Facts) { if (e.Facts != null) ChatMemory.Add(e.Facts); return; }
            if (e.Kind == Kind.Diary) { ChatDiary.Add(e.When, e.Text); return; }
            if (pet == null || e.Gen != gen) return;
            switch (e.Kind)
            {
                case Kind.Probe:
                    if (e.Probe == ProbeResult.Offline) { pet.ChatSayInstant(ChatMood.Sleepy, "...zzz 아직 못 일어나겠어. Ollama가 꺼져 있나 봐!"); }
                    else if (e.Probe == ProbeResult.ModelMissing) pet.ChatSayInstant(ChatMood.Think, "내가 쓸 모델(" + ChatOptions.Current.Model + ")이 안 보여. 설정에서 이름을 확인해 줘!");
                    break;
                case Kind.Token:
                    if (parser != null && parser.Feed(e.Text)) pet.ChatSay(parser.Mood, parser.Text, false);
                    break;
                case Kind.Done:
                {
                    streaming = false; lastActivity = now; chipsWanted = true;
                    ChatMood mood = parser.Mood; string text = parser.Text;
                    if (string.IsNullOrEmpty(text)) { mood = ChatMood.Think; text = "음... 뭐라고 해야 할지 모르겠어."; }
                    pet.ChatSay(mood, text, true);
                    history.Add(new ChatMessage("assistant", "[" + ChatPersona.TagOf(mood) + "] " + text));
                    while (history.Count > 40) { history.RemoveAt(0); extractedUpTo = Math.Max(0, extractedUpTo - 1); }
                    break;
                }
                case Kind.Error:
                    streaming = false; lastActivity = now;
                    if (history.Count > 0 && history[history.Count - 1].Role == "user") history.RemoveAt(history.Count - 1);
                    bool timeout = e.Text != null && (e.Text.Contains("초과") || e.Text.ToLowerInvariant().Contains("timeout"));
                    pet.ChatSayInstant(ChatMood.Sleepy, timeout
                        ? "으음... 답변이 너무 늦어졌어. 모델을 불러오는 중일 수 있으니 잠시 뒤에 다시 말해 줄래?"
                        : "으음... 잘 안 들렸어. Ollama가 켜져 있는지 확인해 줄래?");
                    break;
                case Kind.Silent:
                    streaming = false; End(); nextProactive = now + 600;
                    break;
            }
        }

        // ───────── 먼저 말 걸기(선톡) ─────────
        private void TickProactive(double now, ChatOptions o)
        {
            if (!o.Enabled || !o.Proactive) { nextProactive = -1; return; }
            if (nextProactive < 0) { nextProactive = now + o.ProactiveMinutes * 60.0 * (.6 + .8 * rng.NextDouble()); return; }
            if (now < nextProactive) return;
            nextProactive = now + 60;
            if (app.Paused || app.AllHidden || PlayOptions.IsFocus(DateTime.Now) || Native.IdleSeconds() > 90) return;
            if (StartProactive(now)) nextProactive = now + o.ProactiveMinutes * 60.0 * (.6 + .8 * rng.NextDouble());
        }

        internal bool StartProactive(double now)
        {
            if (pet != null) return false;
            Pet p = app.LiveWindows.Select(w => w.Pet).FirstOrDefault(x => !x.Hidden && (x.State == PetState.Walk || x.State == PetState.Pause) && x.CanChat);
            if (p == null || !p.BeginChat(app, rng.Next(2) == 0 ? 1 : -1)) return false;
            pet = p; p.ChatThink(); lastActivity = now;
            p.OnChatChar = ch => ChatSound.Char(ch, pet != null ? pet.Mood : ChatMood.Neutral);
            history.Add(ChatPersona.ProactivePrompt(ProactiveReason()));
            StartStream(ChatPersona.Build(history, DateTime.Now, pet), true);
            return true;
        }

        private string ProactiveReason()
        {
            DateTime n = DateTime.Now; bool weekday = n.DayOfWeek != DayOfWeek.Saturday && n.DayOfWeek != DayOfWeek.Sunday;
            if (FriendData.Current.Satiety <= 30) return "네가 출출해서 간식이 먹고 싶어";
            if (n.Hour >= 23 || n.Hour < 5) return "사용자가 밤늦게까지 안 자고 있어";
            if (n.Hour == 11 || n.Hour == 12) return "점심시간 무렵이야";
            if (weekday && n.Hour >= 17 && n.Hour < 19) return "퇴근할 시간이 다가와";
            string[] pool = { "그냥 심심하고 사용자가 뭘 하는지 궁금해", "사용자에게 잠깐 쉬어 가자고 말하고 싶어", "사용자의 오늘 하루가 어땠는지 궁금해" };
            return pool[rng.Next(pool.Length)];
        }

        // ───────── 기억·일기 ─────────
        private void Digest(Pet digestingPet)
        {
            var o = ChatOptions.Current;
            var fresh = history.Skip(extractedUpTo).ToList(); extractedUpTo = history.Count;
            if ((!o.Memory && !o.Diary) || fresh.Count(m => m.Role == "user" && !m.Content.StartsWith("(상황")) < 2) return;
            var sb = new StringBuilder();
            foreach (var m in fresh)
            {
                if (m.Role == "user" && m.Content.StartsWith("(상황")) continue;
                sb.Append(m.Role == "user" ? "사용자: " : (digestingPet != null ? digestingPet.Art.Info.Name : "스폰지밥") + ": ").Append(m.Content).Append('\n');
            }
            string transcript = sb.ToString(); DateTime when = DateTime.Now;
            new Thread(delegate ()
            {
                if (o.Memory)
                {
                    string reply = OllamaClient.Complete(o, ChatPersona.MemoryRequest(transcript), 120, .2);
                    if (!string.IsNullOrWhiteSpace(reply)) inbox.Enqueue(new Evt { Kind = Kind.Facts, Facts = ChatPersona.ParseFacts(reply) });
                }
                if (o.Diary)
                {
                    string line = ChatPersona.ParseDiary(OllamaClient.Complete(o, ChatPersona.DiaryRequest(transcript, when, digestingPet), 160, .7));
                    if (line != null) inbox.Enqueue(new Evt { Kind = Kind.Diary, Text = line, When = when });
                }
            }) { IsBackground = true }.Start();
        }

        // ───────── 점검용 ─────────
        internal string DebugState { get { return pet == null ? "idle" : pet.Phase + "/" + pet.Mood + (streaming ? "/streaming" : "") + "/" + (pet.ChatText ?? ""); } }
        internal void TestType(string text) { if (input != null) input.Text = text; }
        internal void TestSubmit() { if (input != null) { string t = input.Text.Trim(); if (t.Length > 0) Send(t); } }
        internal string[] TestChips { get { return input != null ? input.Chips : null; } }
        internal bool TestPickChip(int number) { return input != null && input.ChooseChip(number); }
        internal Rectangle? InputBounds { get { return input != null ? (Rectangle?)input.Bounds : null; } }

        public void Dispose()
        {
            if (request != null) request.Abort();
            if (hotkey != null) { hotkey.Dispose(); hotkey = null; }
            if (input != null) { input.Dispose(); input = null; }
        }

        // ───────── 전역 단축키 ─────────
        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
            [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
            internal event Action Pressed;
            private bool registered;

            internal HotkeyWindow() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }
            internal bool Register(uint mods, uint key) { return registered = RegisterHotKey(Handle, 0x4247, mods | 0x4000, key); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) { if (Pressed != null) Pressed(); return; }
                base.WndProc(ref m);
            }
            public void Dispose()
            {
                try { if (registered && Handle != IntPtr.Zero) UnregisterHotKey(Handle, 0x4247); } catch { }
                registered = false;
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }
    }
}