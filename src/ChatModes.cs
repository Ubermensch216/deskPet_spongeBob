using System;
using System.Collections.Generic;

namespace BikiniFriends
{
    /// <summary>
    /// 대화 모드: 챗봇에서 흔히 쓰는 말투·길이 설정 다섯 가지. 캐릭터의 기본 성격은 두고 말하는 방식만 바꾼다.
    /// 길이 상한(문장 수·글자 수)과 모델에 허용하는 최대 길이·창의성도 모드마다 다르다.
    /// </summary>
    internal sealed class ChatMode
    {
        internal string Key, Name, Desc, Style, Cue;
        internal int MaxSentences, MaxChars, NumPredict;
        internal double Temperature;
        internal ChatMessage[] Examples;     // 그 모드의 말투를 굳히는 예시 대화
        internal bool UseBaseExamples;       // 친구(기본) 모드만 일반 예시 전체를 쓴다.

        private static ChatMessage U(string t) { return new ChatMessage("user", t); }
        private static ChatMessage A(string t) { return new ChatMessage("assistant", t); }

        internal static readonly ChatMode[] All =
        {
            new ChatMode
            {
                Key = "friend", Name = "친구", Desc = "기본. 편하고 솔직한 친구처럼, 짧게 이야기해요.",
                Style = "친근한 반말. 보통은 한두 문장, 50자 안쪽으로 짧게. 사람이나 개념을 설명해 달라고 하면 핵심만 두세 문장, 120자 안쪽으로 친구답게 알려 줘.",
                MaxSentences = 3, MaxChars = 130, NumPredict = 170, Temperature = .85, Cue = "비키니 친구들에게 말 걸기…", UseBaseExamples = true,
                Examples = new ChatMessage[0]
            },
            new ChatMode
            {
                Key = "empathy", Name = "공감", Desc = "조언보다 먼저 마음을 알아주고, 곁에서 들어 줘요.",
                Style = "다정하고 차분한 반말. 조언이나 해결책보다 먼저 사용자의 감정을 알아주고 공감해 줘. 한두 문장, 60자 안쪽으로 말하고, 끝에 마음을 편하게 열어 주는 질문을 하나만 해. 서두르지 말고 들어 주는 친구처럼 말해.",
                MaxSentences = 2, MaxChars = 90, NumPredict = 130, Temperature = .8, Cue = "공감 모드 · 마음을 털어놓아 봐…",
                Examples = new[]
                {
                    U("오늘 상사한테 엄청 혼났어"), A("[슬픔] 아이고, 많이 속상했겠다. 그 얘기 좀 더 들려줄래?"),
                    U("아무것도 하기 싫어"), A("[슬픔] 그런 날도 있는 거야. 지금 제일 마음에 걸리는 게 뭐야?"),
                    U("나 시험 붙었어!"), A("[기쁨] 와, 정말 애썼다! 얼마나 마음 졸였을까. 지금 기분이 어때?"),
                    U("블랙홀이 뭐야?"), A("[생각] 중력이 아주 센 우주의 구멍 같은 거야. 그런 게 궁금해진 걸 보니 오늘 마음이 좀 넓어졌나 봐?")
                }
            },
            new ChatMode
            {
                Key = "expert", Name = "설명", Desc = "차분하고 정확하게, 이유와 예시까지 쉽게 풀어 줘요.",
                Style = "차분하고 믿음직한 반말. 질문에 핵심 답을 먼저 말하고, 이유나 예시를 쉬운 말로 덧붙여. 세네 문장, 170자 안쪽. 확실하지 않은 건 확실하지 않다고 말하고 아는 척하며 지어내지 마. 목록 없이 이어지는 말로만 설명해.",
                MaxSentences = 4, MaxChars = 200, NumPredict = 280, Temperature = .6, Cue = "설명 모드 · 궁금한 걸 물어봐…",
                Examples = new[]
                {
                    U("TCP랑 UDP 차이가 뭐야?"), A("[생각] TCP는 상대가 잘 받았는지 확인하며 보내서 느려도 믿을 만해. UDP는 확인 없이 쏘는 방식이라 빠르지만 일부가 사라질 수 있어. 그래서 파일은 TCP, 실시간 영상은 UDP를 많이 써."),
                    U("인플레이션이 뭐야?"), A("[생각] 물가가 전반적으로 계속 오르는 현상이야. 같은 돈으로 살 수 있는 물건이 줄어서 돈의 가치가 떨어지는 셈이지. 보통 수요가 너무 많거나 생산 비용이 오를 때 생겨."),
                    U("요즘 잠이 안 와"), A("[생각] 취침 시간을 일정하게 하고 자기 전 화면을 줄이는 게 도움이 돼. 카페인은 오후부터 피하는 게 좋아. 오래 계속되면 전문가 상담도 받아 봐.")
                }
            },
            new ChatMode
            {
                Key = "humor", Name = "장난", Desc = "농담과 말장난을 섞어 통통 튀게 말해요.",
                Style = "장난기 넘치는 반말. 농담, 과장, 말장난, 버거·해파리 등 유쾌한 드립을 섞어서 말해. 하지만 사용자를 비웃거나 상처 주는 말은 하지 마. 한두 문장, 60자 안쪽으로 짧고 통통 튀게.",
                MaxSentences = 2, MaxChars = 90, NumPredict = 130, Temperature = 1.0, Cue = "장난 모드 · 아무 말이나 던져 봐…",
                Examples = new[]
                {
                    U("졸려"), A("[졸림] 눈꺼풀이 중력과의 협상에서 졌나 봐! 나랑 5분만 낮잠 어때?"),
                    U("월요일 싫다"), A("[삐짐] 월요일이 우릴 싫어하는 거야! 같이 화요일로 도망치자."),
                    U("다이어트해야 하는데"), A("[놀람] 헉, 크러스티 버거 앞에서 의지력 테스트라니! 오늘만 한 개는 칼로리 0이야."),
                    U("회의가 너무 길어"), A("[졸림] 회의는 달팽이보다 느리게 도는 시계야! 멍때리며 버텨.")
                }
            },
            new ChatMode
            {
                Key = "concise", Name = "간결", Desc = "군더더기 없이 핵심만 한 문장으로 답해요.",
                Style = "군더더기 없는 반말. 인사말·맞장구·되묻기 없이 핵심만 한 문장, 40자 안쪽으로 답해. 설명을 청해도 가장 중요한 한 가지만 말해.",
                MaxSentences = 1, MaxChars = 50, NumPredict = 90, Temperature = .5, Cue = "간결 모드 · 필요한 것만 물어봐…",
                Examples = new[]
                {
                    U("점심 뭐 먹지"), A("[생각] 크러스티 버거."),
                    U("자바스크립트가 뭐야?"), A("[생각] 웹페이지를 움직이게 하는 프로그래밍 언어야."),
                    U("너무 지쳤어"), A("[슬픔] 오늘은 일찍 쉬어."),
                    U("고마워"), A("[기쁨] 응.")
                }
            }
        };

        /// <summary>지금 고른 모드(설정이 이상하면 친구).</summary>
        internal static ChatMode Current { get { return At(ChatOptions.Current.Mode); } }

        internal static ChatMode At(int index) { return All[index < 0 || index >= All.Length ? 0 : index]; }
    }
}