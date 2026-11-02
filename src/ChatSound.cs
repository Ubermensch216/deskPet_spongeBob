using System;
using System.IO;
using System.Media;

namespace BikiniFriends
{
    /// <summary>말할 때 글자마다 나는 짧은 "뽁" 소리(동물의 숲 같은 말소리). 파일 없이 코드로 만들어 낸다.</summary>
    internal static class ChatSound
    {
        private static readonly SoundPlayer[][] bank = new SoundPlayer[3][];
        private static int tick;
        private static readonly double[] scale = { 523.25, 587.33, 659.25, 783.99, 880.0 };

        private static SoundPlayer Make(double hz)
        {
            const int rate = 22050; int n = rate * 55 / 1000;
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)rate, env = Math.Min(1, i / 90.0) * Math.Exp(-t * 55);
                    double v = Math.Sin(2 * Math.PI * hz * t) + .35 * Math.Sin(4 * Math.PI * hz * t);
                    w.Write((short)(v * env * 5200));
                }
            }
            ms.Position = 0;
            var p = new SoundPlayer(ms); p.Load(); return p;
        }

        /// <summary>글자 하나가 찍힐 때 부른다. 두 글자에 한 번만 울리고, 글자마다 음높이가 다르다.</summary>
        internal static void Char(char c, ChatMood mood)
        {
            if (!ChatOptions.Current.Sound) return;
            if ((++tick & 1) != 0) return;
            try
            {
                int set = mood == ChatMood.Joy || mood == ChatMood.Surprise ? 1 : mood == ChatMood.Sleepy || mood == ChatMood.Sad ? 2 : 0;
                if (bank[set] == null)
                {
                    bank[set] = new SoundPlayer[scale.Length];
                    double mul = set == 1 ? 1.26 : set == 2 ? .75 : 1;
                    for (int i = 0; i < scale.Length; i++) bank[set][i] = Make(scale[i] * mul);
                }
                bank[set][c % scale.Length].Play();
            }
            catch { }
        }
    }
}