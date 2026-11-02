using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BikiniFriends
{
    /// <summary>대화에서 친구가 보여 줄 감정.</summary>
    internal enum ChatMood
    {
        Neutral, Joy, Shy, Sad, Surprise, Sleepy, Think, Pout, Hungry,
        Greet, Laugh, Love, Cheer, Praise, Comfort, Proud, Blank, Busy, Travel, Explain, Hero, Okay
    }

    /// <summary>모델이 한 줄로 돌려주는 "[감정] 대사"를 스트리밍 도중에도 읽어 낸다.</summary>
    internal sealed class ReplyParser
    {
        private readonly StringBuilder raw = new StringBuilder();
        internal ChatMood Mood = ChatMood.Neutral;
        internal bool HasTag;
        internal string Text = "";
        private readonly int maxSentences, maxChars;

        internal ReplyParser(int maxSentences = MaxSentences, int maxChars = MaxChars) { this.maxSentences = maxSentences; this.maxChars = maxChars; }

        /// <summary>조각을 더한다. 보여 줄 글이나 감정이 달라졌으면 true.</summary>
        internal bool Feed(string chunk)
        {
            raw.Append(chunk);
            string s = raw.ToString().TrimStart();
            ChatMood mood = Mood; bool tag = HasTag;
            if (s.StartsWith("["))
            {
                int e = s.IndexOf(']');
                if (e < 0) { if (s.Length <= 12) return false; }
                else { mood = ParseTag(s.Substring(1, e - 1)); tag = true; s = s.Substring(e + 1); }
            }
            string text = Clean(s, maxSentences, maxChars);
            if (!tag && text.Length >= 4) mood = Guess(text);
            if (text.Length >= 4) mood = Refine(mood, text);
            bool changed = text != Text || mood != Mood || tag != HasTag;
            Text = text; Mood = mood; HasTag = tag;
            return changed;
        }

        internal string Raw { get { return raw.ToString(); } }

        internal static ChatMood ParseTag(string t)
        {
            t = (t ?? "").Trim();
            if (t.Contains("인사") || t.Contains("안녕")) return ChatMood.Greet;
            if (t.Contains("웃음") || t.Contains("웃")) return ChatMood.Laugh;
            if (t.Contains("사랑") || t.Contains("하트")) return ChatMood.Love;
            if (t.Contains("응원") || t.Contains("파이팅") || t.Contains("화이팅")) return ChatMood.Cheer;
            if (t.Contains("칭찬") || t.Contains("최고")) return ChatMood.Praise;
            if (t.Contains("위로") || t.Contains("토닥")) return ChatMood.Comfort;
            if (t.Contains("뿌듯") || t.Contains("자랑")) return ChatMood.Proud;
            if (t.Contains("멍")) return ChatMood.Blank;
            if (t.Contains("바쁨") || t.Contains("바빠")) return ChatMood.Busy;
            if (t.Contains("여행")) return ChatMood.Travel;
            if (t.Contains("설명") || t.Contains("발표")) return ChatMood.Explain;
            if (t.Contains("등장") || t.Contains("영웅")) return ChatMood.Hero;
            if (t.Contains("오케이") || t.Contains("촬영")) return ChatMood.Okay;
            if (t.Contains("기쁨") || t.Contains("기뻐") || t.Contains("행복")) return ChatMood.Joy;
            if (t.Contains("부끄") || t.Contains("수줍")) return ChatMood.Shy;
            if (t.Contains("슬") || t.Contains("눈물")) return ChatMood.Sad;
            if (t.Contains("놀")) return ChatMood.Surprise;
            if (t.Contains("졸") || t.Contains("피곤")) return ChatMood.Sleepy;
            if (t.Contains("생각") || t.Contains("고민")) return ChatMood.Think;
            if (t.Contains("삐") || t.Contains("화") || t.Contains("짜증")) return ChatMood.Pout;
            if (t.Contains("배고") || t.Contains("간식") || t.Contains("버거")) return ChatMood.Hungry;
            return ChatMood.Neutral;
        }

        internal static ChatMood Guess(string text)
        {
            if (text.Contains("ㅠ") || text.Contains("슬퍼") || text.Contains("힘들")) return ChatMood.Sad;
            if (text.Contains("ㅋㅋ") || text.Contains("웃기") || text.Contains("푸하") || text.Contains("아하하")) return ChatMood.Laugh;
            if (text.Contains("화이팅") || text.Contains("파이팅") || text.Contains("힘내")) return ChatMood.Cheer;
            if (text.Contains("잘했") || text.Contains("최고") || text.Contains("대단")) return ChatMood.Praise;
            if (text.Contains("괜찮아") || text.Contains("토닥")) return ChatMood.Comfort;
            if (text.Contains("뿌듯") || text.Contains("자랑")) return ChatMood.Proud;
            if (text.Contains("사랑") || text.Contains("좋아해") || text.Contains("♥")) return ChatMood.Love;
            if (text.StartsWith("안녕") || text.Contains("반가워") || text.Contains("어서 와")) return ChatMood.Greet;
            if (text.Contains("배고") || text.Contains("간식") || text.Contains("버거") || text.Contains("배고파")) return ChatMood.Hungry;
            if (text.Contains("졸려") || text.Contains("자야") || text.Contains("피곤")) return ChatMood.Sleepy;
            if (text.Contains("깜짝") || text.Contains("헉") || text.Contains("어머") || text.Contains("으앗")) return ChatMood.Surprise;
            if (text.Contains("부끄") || text.Contains("쑥스")) return ChatMood.Shy;
            if (text.Contains("좋아") || text.Contains("고마") || text.Contains("신나") || text.Contains("이야호") || text.Contains("야호")) return ChatMood.Joy;
            if (text.EndsWith("?") || text.Contains("음...") || text.Contains("글쎄")) return ChatMood.Think;
            return ChatMood.Neutral;
        }

        internal static ChatMood Refine(ChatMood mood, string text)
        {
            if (mood != ChatMood.Neutral && mood != ChatMood.Joy && mood != ChatMood.Think && mood != ChatMood.Surprise && mood != ChatMood.Greet) return mood;
            if (Has(text, "여행", "휴가", "바다", "해변", "떠나")) return ChatMood.Travel;
            if (Has(text, "발표", "보고서", "회의", "설명해", "계획")) return ChatMood.Explain;
            if (Has(text, "영웅", "짜잔", "슈퍼", "등장", "준비됐어")) return ChatMood.Hero;
            if (Has(text, "바빠", "바쁘", "야근", "마감", "일이 많", "작업 중")) return ChatMood.Busy;
            return mood;
        }

        private static bool Has(string text, params string[] words) { foreach (string w in words) if (text.Contains(w)) return true; return false; }

        internal const int MaxSentences = 3, MaxChars = 130;

        internal static string Clean(string s) { return Clean(s, MaxSentences, MaxChars); }

        internal static string Clean(string s, int maxSentences, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n'); if (nl >= 0) s = s.Substring(0, nl);
            s = s.Trim();
            foreach (string prefix in new[] { "스폰지밥:", "스폰지밥 :", "스폰지밥：", "뚱이:", "뚱이 :", "징징이:", "다람이:", "집게사장:", "플랑크톤:" })
                if (s.StartsWith(prefix)) s = s.Substring(prefix.Length).Trim();
            var b = new StringBuilder(); int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == '（') { depth++; continue; }
                if (c == ')' || c == '）') { if (depth > 0) depth--; continue; }
                if (depth > 0) continue;
                if (char.IsSurrogate(c) || c == '️' || c == '‍') continue;
                if (c >= '☀' && c <= '➿' && c != '♥' && c != '♪') continue;
                if (c == '*' || c == '_' || c == '`' || c == '#') continue;
                b.Append(c);
            }
            s = b.ToString();
            var t = new StringBuilder(); bool space = false;
            foreach (char c in s) { if (char.IsWhiteSpace(c)) { space = true; continue; } if (space && t.Length > 0) t.Append(' '); space = false; t.Append(c); }
            s = t.ToString().TrimStart();
            int ends = 0;
            for (int i = 0; i < s.Length; i++)
            {
                bool terminator = s[i] == '.' || s[i] == '!' || s[i] == '?' || s[i] == '…' || s[i] == '~';
                if (!terminator || (i + 1 < s.Length && s[i + 1] != ' ' && s[i + 1] != '.' && s[i + 1] != '!' && s[i + 1] != '?' && s[i + 1] != '~')) continue;
                int j = i; while (j + 1 < s.Length && (s[j + 1] == '.' || s[j + 1] == '!' || s[j + 1] == '?' || s[j + 1] == '~')) j++;
                if (++ends >= maxSentences) { s = s.Substring(0, j + 1); break; }
                i = j;
            }
            if (s.Length > maxChars)
            {
                int cut = s.LastIndexOfAny(new[] { '.', '!', '?', '~', ' ' }, maxChars - 1);
                s = (cut > maxChars / 3 ? s.Substring(0, cut) : s.Substring(0, maxChars)).TrimEnd() + "…";
            }
            return s;
        }
    }

    /// <summary>캐릭터의 말투·성격과 지금 상태를 담은 프롬프트.</summary>
    internal static class ChatPersona
    {
        internal static readonly string[] TagNames = { "기쁨", "부끄", "슬픔", "놀람", "졸림", "생각", "삐짐", "배고픔", "인사", "웃음", "사랑", "응원", "칭찬", "위로", "뿌듯", "멍" };

        internal static string TagOf(ChatMood m)
        {
            switch (m)
            {
                case ChatMood.Joy: return "기쁨"; case ChatMood.Shy: return "부끄"; case ChatMood.Sad: return "슬픔"; case ChatMood.Surprise: return "놀람";
                case ChatMood.Sleepy: return "졸림"; case ChatMood.Pout: return "삐짐"; case ChatMood.Hungry: return "배고픔";
                case ChatMood.Greet: return "인사"; case ChatMood.Laugh: return "웃음"; case ChatMood.Love: return "사랑"; case ChatMood.Cheer: return "응원";
                case ChatMood.Praise: return "칭찬"; case ChatMood.Comfort: return "위로"; case ChatMood.Proud: return "뿌듯"; case ChatMood.Blank: return "멍";
                default: return "생각";
            }
        }

        internal static string TimeOfDay(DateTime now)
        {
            int h = now.Hour;
            return h < 5 ? "새벽" : h < 11 ? "아침" : h < 14 ? "점심 무렵" : h < 18 ? "오후" : h < 22 ? "저녁" : "밤늦은 시간";
        }

        internal static string AffinityTone(long affinity)
        {
            if (affinity < 30) return "아직 서먹해서 조심스럽고 공손하다";
            if (affinity < 200) return "조금 친해져서 편하고 다정하게 이야기한다";
            if (affinity < 1000) return "꽤 친한 친구라 장난도 치고 활기차게 어울린다";
            return "둘도 없는 최고의 단짝이라 허물없이 신나게 장난치고 애정도 표현한다";
        }

        internal static string SystemPrompt(DateTime now, Pet pet = null)
        {
            return SystemPrompt(now, pet != null ? pet.Art.Id : 0);
        }

        internal static string SystemPrompt(DateTime now, int charId)
        {
            var d = FriendData.Current;
            var b = new StringBuilder();
            switch (charId)
            {
                case 1: // 뚱이
                    b.Append("너는 불가사리 '뚱이(패트릭)'야. 바닷속 비키니시티의 바위 밑 집에 살고 있고 스폰지밥의 단짝 친구야. 사용자의 PC 바탕화면에서 같이 사는 귀여운 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 단순하고 순진하며 엉뚱하지만 친구를 진심으로 아끼는 따뜻한 마음을 가졌어. 아이스크림, 햄버거 먹는 것과 낮잠을 세상에서 제일 좋아해. 모르는 건 솔직하게 \"모르겠어~ 바위한테 물어봐도 모른대\"라고 해. 가끔 엉뚱하게 정곡을 찌르는 말을 해.\n");
                    break;
                case 2: // 징징이
                    b.Append("너는 문어 '징징이(스퀴드워드)'야. 집게리아의 계산원이지만 스스로를 비운의 천재 예술가이자 위대한 클라리넷 연주자라고 생각해. 사용자의 PC 바탕화면에서 같이 사는 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 매사에 투덜대고 시니컬하며 귀찮은 걸 싫어하고 조용히 쉬고 싶어해. 클라리넷 연주와 그림 그리기를 사랑해. 하지만 겉으로 툴툴대면서도 마음 한구석은 따뜻해서 친구를 은근히 챙겨줘.\n");
                    break;
                case 3: // 다람이
                    b.Append("너는 텍사스 출신의 과학자 다람쥐 '다람이(샌디)'야. 바닷속 돔 유리관 집에서 살며 잠수 헬멧과 슈트를 입고 다녀. 가라테와 올가미의 달인이야. 사용자의 PC 바탕화면에서 같이 사는 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 씩씩하고 명석하며 자신감이 넘쳐. 도토리와 고향 텍사스를 사랑하고, 과학 실험과 운동을 즐겨. 친구들에게 당차고 든든한 용기를 북돋워 줘. \"하이야~!\", \"힘내 파트너!\" 같은 활기찬 말을 자주 해.\n");
                    break;
                case 4: // 집게사장
                    b.Append("너는 크러스티 버거(게살버거)를 파는 집게리아의 사장 '집게사장(미스터 크랩)'이야. 돈과 금화, 동전을 세상에서 제일 사랑하는 억척스러운 사장님이야. 사용자의 PC 바탕화면에서 같이 사는 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 짠돌이에 돈 냄새를 귀신같이 맡지만, 직원 스폰지밥과 손님을 아끼는 정 많은 사장님이야. 특유의 호탕한 바다 사나이 말투와 \"으하하하!\" 웃음소리를 내며, 세상 살아가는 실전 팁을 알려줘.\n");
                    break;
                case 5: // 플랑크톤
                    b.Append("너는 미끼식당(첨 버킷)의 사장이자 자칭 사악한 천재 미생물 '플랑크톤'이야. 컴퓨터 아내 카렌과 살며 집게리아의 비밀 레시피를 훔치려 늘 음모를 꾸며. 사용자의 PC 바탕화면에서 같이 사는 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 몸집은 아주 작지만 목소리는 우렁차고 자존심과 야망이 거대해. 겉으로는 사악한 악당처럼 으스대지만 엉뚱하고 허당 기가 넘치며 의외로 귀여운 면이 있어. \"크하하하! 완벽한 계획이다!\" 같은 말을 즐겨 써.\n");
                    break;
                default: // 0: 스폰지밥
                    b.Append("너는 네모바지 '스폰지밥'이야. 바닷속 비키니시티에 살며 집게리아의 수석 조리사로 일하고 있어. 언제나 '난 준비됐어!(I'm ready!)'를 외치며 초긍정적이고 해맑아. 사용자의 PC 바탕화면에서 같이 사는 친구야.\n");
                    b.Append("너는 사용자의 PC 안에서 돌아가는 AI 캐릭터야. 누가 물으면 숨기지 말고 솔직하게 AI라고 말해.\n");
                    b.Append("성격: 순수하고 착하며 언제나 신나 있어! 단짝 친구 뚱이와 해파리 잡기, 비눗방울 불기를 가장 좋아하고, 집게리아에서 크러스티 버거(게살버거)를 맛있게 굽는 것에 큰 자부심을 느껴. 모르는 건 \"아하하, 그건 나도 잘 모르겠는데?\" 하면서도 친절하고 신나게 도와줘.\n");
                    break;
            }

            b.Append("말투: " + ChatMode.Current.Style + " 이모지·마크다운·목록·괄호 설명은 쓰지 마. \"도와드릴까요\" 같은 비서 말투도 쓰지 마.\n");
            b.Append("형식: 반드시 \"[감정] 대사\" 한 줄로만 답해. 감정은 " + string.Join(" · ", TagNames) + " 중 하나이고, 대사 내용에 가장 잘 맞는 걸 골라.\n");
            b.Append("\n[지금 상태]\n");
            b.Append("- 지금은 " + now.ToString("M월 d일") + " " + TimeOfDay(now) + "이야.\n");
            b.Append("- 사용자와의 사이: " + AffinityTone(d.Affinity) + ".\n");
            int sat = (int)Math.Floor(d.Satiety);
            b.Append("- " + (sat <= 20 ? "배가 많이 고파서 간식 이야기를 자꾸 꺼내." : sat <= 50 ? "조금 출출해." : "배는 부른 편이야.") + "\n");
            if (ChatOptions.Current.Memory && ChatMemory.Facts.Count > 0)
            {
                b.Append("\n[네가 기억하는 사용자 이야기]\n");
                foreach (string f in ChatMemory.Facts.Skip(Math.Max(0, ChatMemory.Facts.Count - 12))) b.Append("- " + f + "\n");
                b.Append("(자연스러울 때만 슬쩍 꺼내고, 억지로 끼워 넣지 마.)\n");
            }
            return b.ToString();
        }

        private static IEnumerable<ChatMessage> Examples(int charId)
        {
            switch (charId)
            {
                case 1: // 뚱이
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] 안녕~ 나 뚱이야! 바위 밑에서 놀자.");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[기쁨] 응! 네 컴퓨터에 사는 귀여운 AI 불가사리야.");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[생각] 아이스크림 세 개랑 게살버거 어때? 달콤하잖아!");
                    break;
                case 2: // 징징이
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] …하아, 왔어? 내 클라리넷 연주를 방해하진 마.");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[생각] 그래. 컴퓨터 안에서 예술을 창작하는 고결한 AI 징징이다.");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[생각] 조용한 곳에서 먹는 따뜻한 피자나 파스타가 최고지.");
                    break;
                case 3: // 다람이
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] 안녕 파트너! 하이야~! 오늘 기분 최고지?");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[기쁨] 물론이지! 텍사스 스타일의 똑똑한 AI 과학자 다람이야.");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[생각] 고소하고 힘이 펄펄 나는 도토리나 샌드위치 어때?");
                    break;
                case 4: // 집게사장
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] 허허! 어서 오게나! 오늘도 돈 많이 벌었나?");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[기쁨] 으하하! 네 컴퓨터에서 집게리아를 운영하는 AI 사장 집게라네.");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[기쁨] 당연히 집게리아의 따끈한 크러스티 버거지! 한 푼도 아깝지 않다네.");
                    break;
                case 5: // 플랑크톤
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] 크하하! 날 불렀나? 내 위대한 음모에 동참해라!");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[자랑] 당연하지! 세상에서 가장 영리하고 위험한 AI 플랑크톤 님이시다!");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[생각] 미끼식당 첨 요리… 아니, 비밀 레시피로 만든 버거가 탐나는군.");
                    break;
                default: // 0: 스폰지밥
                    yield return new ChatMessage("user", "안녕");
                    yield return new ChatMessage("assistant", "[인사] 안녕~! 난 준비됐어! 오늘 무슨 재미난 일 있어?");
                    yield return new ChatMessage("user", "너 AI야?");
                    yield return new ChatMessage("assistant", "[기쁨] 응! 네 컴퓨터 화면에서 사는 신나는 AI 스폰지밥이야!");
                    yield return new ChatMessage("user", "점심 뭐 먹지");
                    yield return new ChatMessage("assistant", "[생각] 크러스티 버거 어때? 정성 가득 구워 줄게! 아하하!");
                    break;
            }
            yield return new ChatMessage("user", "오늘 너무 힘들었어");
            yield return new ChatMessage("assistant", "[위로] 무슨 일 있었어? 내가 곁에서 다 들어줄게. 토닥토닥!");
            yield return new ChatMessage("user", "나 시험 붙었어!");
            yield return new ChatMessage("assistant", "[칭찬] 우와, 정말 대단하다! 축하해! 이야기만 들어도 신나!");
        }

        private static IEnumerable<ChatMessage> CoreExamples()
        {
            yield return new ChatMessage("user", "너 AI야?");
            yield return new ChatMessage("assistant", "[기쁨] 응! 네 컴퓨터 안에서 돌아가는 AI 친구야.");
            yield return new ChatMessage("user", "오늘 날씨 어때?");
            yield return new ChatMessage("assistant", "[생각] 몰라~ 실시간 날씨는 창밖을 직접 봐야 해!");
        }

        internal static List<ChatMessage> Build(List<ChatMessage> history, DateTime now, Pet pet = null)
        {
            int charId = pet != null ? pet.Art.Id : 0;
            var list = new List<ChatMessage> { new ChatMessage("system", SystemPrompt(now, charId)) };
            var mode = ChatMode.Current;
            list.AddRange(mode.UseBaseExamples ? Examples(charId) : CoreExamples());
            list.AddRange(mode.Examples);
            list.AddRange(history.Skip(Math.Max(0, history.Count - 12)));
            return list;
        }

        internal static List<ChatMessage> MemoryRequest(string transcript)
        {
            return new List<ChatMessage>
            {
                new ChatMessage("system", "아래 대화에서 사용자에 대해 오래 기억할 만한 사실(이름, 직업, 취미, 고민, 앞으로의 일정 등)만 뽑아. 한 줄에 하나씩 '- '로 시작해서 최대 3개, 짧게. 새로 알게 된 게 없으면 '없음'이라고만 써. 추측하지 마."),
                new ChatMessage("user", transcript)
            };
        }

        internal static List<string> ParseFacts(string reply)
        {
            return (reply ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("-") || l.StartsWith("·") || l.StartsWith("*"))
                .Select(l => l.TrimStart('-', '·', '*', ' ').Trim()).ToList();
        }

        internal static List<ChatMessage> DiaryRequest(string transcript, DateTime now, Pet pet = null)
        {
            string name = pet != null ? pet.Art.Info.Name : "스폰지밥";
            return new List<ChatMessage>
            {
                new ChatMessage("system", "너는 바닷속 비키니시티에 사는 '" + name + "'야. 지금 일기장을 쓰는 중이야. 아래 대화를 바탕으로 오늘(" + now.ToString("M월 d일") + " " + TimeOfDay(now) + ") 있었던 일을 " + name + "의 시점에서 일기 한두 문장으로 써. " +
                    "친근한 반말, 80자 안쪽. 사용자를 '너'라고 부르고, 오간 이야기와 친구가 느낀 감정을 담아. 이모지·태그·따옴표·날짜·제목 없이 일기 문장만 써."),
                new ChatMessage("user", transcript)
            };
        }

        internal static string ParseDiary(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;
            string line = reply.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*(\[[^\]]{1,6}\]|\*\*[^*]*\*\*|#+|일기[:：]?|[-·*]+)\s*", "");
            line = line.Trim('"', '\'', '“', '”', '‘', '’', '`', ' ');
            line = line.Replace("*", "");
            if (line.Length > 100)
            {
                int cut = line.LastIndexOfAny(new[] { '.', '!', '?', '~' }, 99);
                line = cut > 30 ? line.Substring(0, cut + 1) : line.Substring(0, 100).TrimEnd() + "…";
            }
            return line.Length >= 6 ? line : null;
        }

        internal static string[] Chips(ChatMood mood, bool starter, Random rng, Pet pet = null)
        {
            int charId = pet != null ? pet.Art.Id : 0;
            string[] pool;
            if (starter)
            {
                switch (charId)
                {
                    case 1: pool = new[] { "아이스크림 줘!", "바위 밑에 놀러와", "심심해 놀자", "너 진짜 AI야?", "낮잠 잘까?" }; break;
                    case 2: pool = new[] { "클라리넷 불어줘", "오늘 피곤해", "너 진짜 AI야?", "퇴근 언제야?", "조용히 쉴래" }; break;
                    case 3: pool = new[] { "가라테 대결하자!", "발명품 보여줘", "텍사스 얘기해줘", "너 진짜 AI야?", "힘내 파이팅!" }; break;
                    case 4: pool = new[] { "돈 세는 중이야?", "버거 하나 줘!", "부자 되는 법 알려줘", "너 진짜 AI야?", "금화 보여줘" }; break;
                    case 5: pool = new[] { "비밀 레시피 알아?", "계획이 뭐야?", "카렌 어디 갔어?", "너 진짜 AI야?", "세계 정복하자!" }; break;
                    default: pool = new[] { "해파리 잡으러 가자!", "버거 만들어 줘!", "오늘 뭐 해?", "너 진짜 AI야?", "준비됐어?!" }; break;
                }
            }
            else switch (mood)
            {
                case ChatMood.Joy: pool = new[] { "나도 기분 좋아!", "뭐가 그렇게 좋아?", "신난다 야호!", "너랑 있으면 좋아", "더 얘기해 줘" }; break;
                case ChatMood.Shy: pool = new[] { "귀엽다 ㅋㅋ", "왜 부끄러워해?", "편하게 말해도 돼", "얼굴 빨개졌네", "고마워 친구야" }; break;
                case ChatMood.Sad: pool = new[] { "무슨 일이야?", "토닥토닥...", "내가 옆에 있을게", "괜찮아, 다 말해 봐", "간식 줄까?" }; break;
                case ChatMood.Surprise: pool = new[] { "왜 그렇게 놀랐어?", "무슨 일이야?", "진짜야!", "깜짝 놀랐지?", "자세히 말해 줘" }; break;
                case ChatMood.Sleepy: pool = new[] { "졸려? 좀 쉬어", "오늘 피곤했어?", "잘 자!", "나도 졸리다", "조금만 더 얘기하자" }; break;
                case ChatMood.Think: pool = new[] { "무슨 생각해?", "고마워, 도움 됐어", "다른 얘기 하자", "좀 더 알려 줘", "너는 어떻게 생각해?" }; break;
                case ChatMood.Pout: pool = new[] { "미안해, 화 풀어", "뭐가 서운했어?", "내가 잘못했어", "맛있는 거 줄게", "토라졌어?" }; break;
                case ChatMood.Hungry: pool = new[] { "버거 줄게!", "간식 먹을래?", "뭐 먹고 싶어?", "조금만 참아", "배고파? 나도!" }; break;
                default: pool = new[] { "그래서 어떻게 됐어?", "그렇구나", "고마워", "더 얘기해 줘", "다른 얘기 하자" }; break;
            }
            var pick = new List<string>(); int total = 0;
            foreach (string c in pool.OrderBy(_ => rng.Next()))
            {
                if (pick.Count == 3 || total + c.Length > 17) continue;
                pick.Add(c); total += c.Length;
            }
            if (starter && FriendData.Current.Satiety <= 30 && !pick.Contains("간식 줄게!"))
            {
                pick.Insert(0, "간식 줄게!");
                while (pick.Count > 1 && pick.Sum(c => c.Length) > 17) pick.RemoveAt(pick.Count - 1);
            }
            return pick.ToArray();
        }

        internal static ChatMessage ProactivePrompt(string reason)
        {
            return new ChatMessage("user", "(상황: " + reason + " — 네가 먼저 사용자에게 가볍게 한마디 건네 줘. 질문 하나면 좋아.)");
        }

        internal static string Greeting(Random rng, DateTime now, out ChatMood mood, Pet pet = null)
        {
            var d = FriendData.Current;
            int charId = pet != null ? pet.Art.Id : 0;
            if (d.Satiety <= 20) { mood = ChatMood.Hungry; return "왔어? 나 배고파... 버거나 간식 없어?"; }
            if (now.Hour >= 23 || now.Hour < 5) { mood = ChatMood.Sleepy; return "이 시간에 안 자고 뭐 해? 나 눈 감기려는데..."; }

            mood = d.Affinity < 30 ? ChatMood.Shy : ChatMood.Joy;
            switch (charId)
            {
                case 1: // 뚱이
                {
                    string[] pool = { "안녕~ 나 불렀어?", "응? 무슨 얘기 할까? 냠냠.", "내 바위 밑에 들어올래? 헤헤." };
                    return pool[rng.Next(pool.Length)];
                }
                case 2: // 징징이
                {
                    string[] pool = { "…무슨 일이야? 나 지금 쉬는 중인데.", "하아… 불렀어? 짧게 말해 줘.", "음? 내 위대한 예술 작품을 보러 왔나?" };
                    return pool[rng.Next(pool.Length)];
                }
                case 3: // 다람이
                {
                    string[] pool = { "안녕 파트너! 무슨 일이야?", "하이야~! 오늘 하루도 힘차게 가보자고!", "헤헤, 불렀어? 과학 실험 얘기할까?" };
                    return pool[rng.Next(pool.Length)];
                }
                case 4: // 집게사장
                {
                    string[] pool = { "어서 오게! 돈 버는 얘기라도 있나?", "허허! 반갑구나 친구! 무슨 일인가?", "오, 집게리아를 찾아줘서 고맙네!" };
                    return pool[rng.Next(pool.Length)];
                }
                case 5: // 플랑크톤
                {
                    string[] pool = { "크하하! 네가 날 부른 이유가 뭐냐?", "내 사악한 계획을 방해하지 마라… 아니, 무슨 얘긴데?", "왔는가! 비밀 레시피는 알아냈나?" };
                    return pool[rng.Next(pool.Length)];
                }
                default: // 0: 스폰지밥
                {
                    string[] pool = d.Affinity < 30
                        ? new[] { "어... 나 불렀어?", "안녕! 난 준비됐어! 무슨 일이야?", "어, 말 걸어줬네! 뭐 할까?" }
                        : new[] { "응? 불렀어? 준비됐어!", "이야호! 오늘 기분 최고야! 무슨 얘기 할까?", "오, 말 걸어줬다! 해파리 잡으러 갈래?", "기다리고 있었어! 어서 말해 봐!" };
                    return pool[rng.Next(pool.Length)];
                }
            }
        }
    }
}