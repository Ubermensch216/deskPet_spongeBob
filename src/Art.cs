using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace BikiniFriends
{
    internal sealed class CharInfo
    {
        public string Key, Name;
        public Color Accent;                  // 말풍선·장식 등에 어울리는 색
        public float Height = 1f;             // 화면에서 키 (BaseHeight 배)
        public float WidthRatio = .6f;        // 걸을 때 몸 상자 폭 / 키
        public float ContactRatio = .5f;      // 친구끼리 부딪힘 폭 / 키
        public float CycleFrac = .22f;        // 걸음 한 주기(발 두 번)가 키의 몇 배 거리인지
        public float RotorU = 90;             // 낙하 도구가 머리 위로 올라오는 높이(리그 단위)
        public string Hobby;                  // 대표 동작 이름 (메뉴)
        public string[] HobbyLines;           // 대표 동작 때 하는 말
        public string PokeLine, CryLine, HopLine, EatLine, GiftLine, CheerLine, DizzyLine, FoodName;
        public string SleepZ = "Z";
    }

    /// <summary>벽·천장 상태에서 쓰는 몸 크기 상자.</summary>
    internal sealed class Dim
    {
        public int W, H;
    }

    /// <summary>
    /// 캐릭터의 크기와 기준 치수. 그림은 Rig*.cs 가 부위별로 그린다(팔·다리·눈·입이 따로 움직인다).
    /// 기준: 키 300 단위 = 화면 H 픽셀. 캐릭터마다 H 가 다르다(징징이·다람이는 크고 플랑크톤은 작다).
    /// </summary>
    internal sealed class CharacterArt
    {
        internal static readonly CharInfo[] Infos =
        {
            new CharInfo { Key = "spongebob", Name = "스폰지밥", Accent = Color.FromArgb(244, 214, 40), Height = 1.00f, WidthRatio = .78f, ContactRatio = .62f, CycleFrac = .24f, RotorU = 104,
                Hobby = "해파리 잡이 · 비눗방울",
                HobbyLines = new[] { "해파리 잡으러 가자~!", "비눗방울 불자~ 후~" },
                PokeLine = "으앗! 왜 그래~", CryLine = "흑흑… 으아앙!", HopLine = "준비됐어요?! 아하하하!", EatLine = "크러스티 버거다! 냠냠", GiftLine = "바닷속 꽃다발이야!", CheerLine = "이야호~!", DizzyLine = "빙글빙글… 해파리가 돌아…", FoodName = "크러스티 버거" },
            new CharInfo { Key = "patrick", Name = "뚱이", Accent = Color.FromArgb(244, 132, 108), Height = 1.04f, WidthRatio = 1.0f, ContactRatio = .86f, CycleFrac = .26f, RotorU = 84,
                Hobby = "바위 밑에 숨기",
                HobbyLines = new[] { "여긴 내 집이야~", "쉿… 바위 밑이 제일 좋아" },
                PokeLine = "응? 나 불렀어?", CryLine = "내 뇌가… 으앙!", HopLine = "나는 뚱이야!", EatLine = "아이스크림이다! 냠냠", GiftLine = "내 아이스크림 줄게!", CheerLine = "야호~ 뚱이다!", DizzyLine = "어… 방이 돌아…", FoodName = "아이스크림" },
            new CharInfo { Key = "squidward", Name = "징징이", Accent = Color.FromArgb(240, 164, 30), Height = 1.26f, WidthRatio = .5f, ContactRatio = .42f, CycleFrac = .34f, RotorU = 96,
                Hobby = "클라리넷 연주",
                HobbyLines = new[] { "내 예술을 들어라…", "…또 음이 나갔잖아" },
                PokeLine = "…건드리지 마.", CryLine = "으으… 왜 나만…", HopLine = "…한 번만 뛴다.", EatLine = "…피자나 먹지 뭐.", GiftLine = "내 자화상이야… 받아.", CheerLine = "…드디어 퇴근이군.", DizzyLine = "으으… 머리야…", FoodName = "피자" },
            new CharInfo { Key = "sandy", Name = "다람이", Accent = Color.FromArgb(120, 200, 236), Height = 1.28f, WidthRatio = .64f, ContactRatio = .52f, CycleFrac = .28f, RotorU = 90,
                Hobby = "가라테 · 올가미",
                HobbyLines = new[] { "하이야~! 가라테 초퍼!", "텍사스 올가미 솜씨 봐라!" },
                PokeLine = "어머, 놀랐잖아!", CryLine = "흑… 텍사스가 그리워…", HopLine = "하이야~!", EatLine = "도토리 최고! 냠냠", GiftLine = "도토리 꽃다발이야!", CheerLine = "야호~ 텍사스 만세!", DizzyLine = "어지러워~ 올가미가 꼬였나…", FoodName = "도토리" },
            new CharInfo { Key = "krabs", Name = "집게사장", Accent = Color.FromArgb(228, 72, 54), Height = .94f, WidthRatio = 1.1f, ContactRatio = .98f, CycleFrac = .17f, RotorU = 92,
                Hobby = "돈 세기",
                HobbyLines = new[] { "돈이다! 돈!", "한 푼이라도 아껴야지!" },
                PokeLine = "내 돈에 손대지 마!", CryLine = "내 돈… 내 돈이…", HopLine = "돈이 굴러온다!", EatLine = "금화는 깨물어 봐야 알지!", GiftLine = "특별히 금화 한 닢이다!", CheerLine = "돈 벌러 가자!", DizzyLine = "동전이 빙글빙글…", FoodName = "금화", SleepZ = "$" },
            new CharInfo { Key = "plankton", Name = "플랑크톤", Accent = Color.FromArgb(110, 172, 160), Height = .84f, WidthRatio = .66f, ContactRatio = .5f, CycleFrac = .24f, RotorU = 76,
                Hobby = "음모 꾸미기",
                HobbyLines = new[] { "무하하하! 완벽한 계획!", "비밀 레시피는 내 거야!" },
                PokeLine = "누가 감히!", CryLine = "카렌… 도와줘…", HopLine = "무하하하하!", EatLine = "(몰래) 냠냠… 레시피 연구 중", GiftLine = "(몰래) 비밀 레시피… 아니, 선물이야!", CheerLine = "오늘은 꼭 성공한다!", DizzyLine = "계획이… 빙글빙글…", FoodName = "크러스티 버거" },
        };
        internal static int Count { get { return Infos.Length; } }
        internal static float Scale = 1f;
        /// <summary>서 있을 때 키(화면 픽셀). 배율 1 기준이고, 캐릭터마다 Height 를 곱한다.</summary>
        internal const float BaseHeight = 85f;

        private static readonly Dictionary<int, CharacterArt> cache = new Dictionary<int, CharacterArt>();
        internal static CharacterArt Get(int id)
        {
            id = ((id % Count) + Count) % Count;
            CharacterArt a;
            if (!cache.TryGetValue(id, out a)) cache[id] = a = new CharacterArt(id);
            return a;
        }
        internal static void Reset(float scale) { Scale = scale; cache.Clear(); }

        public readonly int Id;
        public readonly CharInfo Info;
        public readonly CharRig Rig;
        public readonly float K;          // 리그 단위 1 = 화면 K 픽셀
        public readonly int W, H;         // 서 있는 모습의 상자(걷기 기준) — 이동·충돌 기준
        public readonly int ContactW;     // 친구끼리 부딪힘 폭
        public readonly int RotorH;       // 낙하 도구가 머리 위로 올라오는 높이(픽셀)
        public readonly Dim Ceiling;
        public int CeilingTopInset { get { return 0; } }
        public int CeilingContactHeight { get { return Ceiling.H; } }

        private CharacterArt(int id)
        {
            Id = id; Info = Infos[id]; Rig = BikiniFriends.Rig.Of(id);
            H = (int)Math.Round(BaseHeight * Scale * Info.Height);
            K = H / BikiniFriends.Rig.RefH;
            W = (int)Math.Round(H * Info.WidthRatio);
            ContactW = (int)Math.Round(H * Info.ContactRatio);
            RotorH = (int)Math.Round(Info.RotorU * K);
            Ceiling = new Dim { W = W, H = H };
        }

        internal static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        /// <summary>투명하지 않은 부분의 경계.</summary>
        internal static Rectangle Bounds(Bitmap b, int threshold = 30)
        {
            int l = b.Width, t = b.Height, r = -1, bot = -1;
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * b.Height]; System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                        if (bytes[y * data.Stride + x * 4 + 3] > threshold) { if (x < l) l = x; if (y < t) t = y; if (x > r) r = x; if (y > bot) bot = y; }
            }
            finally { b.UnlockBits(data); }
            return r < l ? new Rectangle(0, 0, b.Width, b.Height) : Rectangle.FromLTRB(l, t, r + 1, bot + 1);
        }

        /// <summary>트레이·창 아이콘용 작은 그림.</summary>
        internal static Icon MakeIcon(int id)
        {
            try
            {
                int n = Math.Max(16, (int)Math.Round(16 * Scale));
                using (Bitmap bmp = IconBitmap(id, n))
                {
                    IntPtr hicon = bmp.GetHicon();
                    Icon icon = (Icon)Icon.FromHandle(hicon).Clone(); Native.DestroyIcon(hicon); return icon;
                }
            }
            catch { return SystemIcons.Application; }
        }

        /// <summary>얼굴이 잘 보이도록 머리를 확대한 아이콘 그림.</summary>
        internal static Bitmap IconBitmap(int id, int n)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            CharRig rig = BikiniFriends.Rig.Of(id);
            using (var g = Graphics.FromImage(bmp))
            {
                Quality(g); g.Clear(Color.Transparent);
                float k = (n - 1.5f) / (rig.IconHalf * 2);
                g.TranslateTransform(n / 2f - rig.IconCenter.X * k, n / 2f - rig.IconCenter.Y * k);
                var pose = new Pose { Mouth = MouthStyle.Smile };
                BikiniFriends.Rig.Draw(g, id, pose, k, false);
            }
            return bmp;
        }
    }
}
