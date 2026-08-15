using System;

namespace Polaris.Pevt.Text
{
    /// <summary>不可变的半开源码范围 [Start, End)，以 <see cref="SourceText"/> 的 UTF-16 char 偏移计。</summary>
    public readonly struct TextSpan : IEquatable<TextSpan>
    {
        public int Start { get; }

        public int Length { get; }

        public int End => Start + Length;

        public bool IsEmpty => Length == 0;

        public TextSpan(int start, int length)
        {
            if (start < 0)
                throw new ArgumentOutOfRangeException(nameof(start), start, "起始偏移不能为负数。");
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "长度不能为负数。");

            Start = start;
            Length = length;
        }

        public static TextSpan FromBounds(int start, int end)
        {
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end), end, "结束偏移不能小于起始偏移。");
            return new TextSpan(start, end - start);
        }

        public bool Contains(int position) => position >= Start && position < End;

        public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

        public bool OverlapsWith(TextSpan span) => Start < span.End && span.Start < End;

        public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;

        public override bool Equals(object obj) => obj is TextSpan other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start * 397) ^ Length;
            }
        }

        public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);

        public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);

        public override string ToString() => $"[{Start}..{End})";
    }
}
