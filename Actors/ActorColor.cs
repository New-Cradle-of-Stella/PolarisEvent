using System;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// `.pactor` 的对话名颜色，只接受 <c>#RRGGBB</c> 或 <c>#RRGGBBAA</c>；不接受颜色名、<c>rgb()</c> 函数或三位简写，避免把表达式能力带进纯数据文件。
    /// </summary>
    public readonly struct ActorColor : IEquatable<ActorColor>
    {
        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public byte A { get; }

        public ActorColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static bool TryParse(string value, out ActorColor color)
        {
            color = default;

            if (string.IsNullOrEmpty(value) || value[0] != '#')
                return false;
            if (value.Length != 7 && value.Length != 9)
                return false;

            byte[] channels = new byte[4];
            channels[3] = 0xFF;

            int channelCount = (value.Length - 1) / 2;
            for (int i = 0; i < channelCount; i++)
            {
                if (!TryParseHexDigit(value[1 + (i * 2)], out int high) ||
                    !TryParseHexDigit(value[2 + (i * 2)], out int low))
                {
                    return false;
                }

                channels[i] = (byte)((high << 4) | low);
            }

            color = new ActorColor(channels[0], channels[1], channels[2], channels[3]);
            return true;
        }

        private static bool TryParseHexDigit(char c, out int value)
        {
            if (c >= '0' && c <= '9') { value = c - '0'; return true; }
            if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
            if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
            value = 0;
            return false;
        }

        public bool Equals(ActorColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object obj) => obj is ActorColor other && Equals(other);

        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

        public static bool operator ==(ActorColor left, ActorColor right) => left.Equals(right);

        public static bool operator !=(ActorColor left, ActorColor right) => !left.Equals(right);

        public override string ToString() =>
            A == 0xFF ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }
}
