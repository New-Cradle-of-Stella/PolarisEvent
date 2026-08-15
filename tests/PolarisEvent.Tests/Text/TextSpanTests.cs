using System;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Text
{
    public class TextSpanTests
    {
        [Fact]
        public void Constructor_RejectsNegativeStart()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 0));
        }

        [Fact]
        public void Constructor_RejectsNegativeLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1));
        }

        [Fact]
        public void End_IsStartPlusLength()
        {
            var span = new TextSpan(3, 5);
            Assert.Equal(8, span.End);
        }

        [Fact]
        public void FromBounds_RejectsEndBeforeStart()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(5, 2));
        }

        [Fact]
        public void Value_IsImmutableAcrossCopies()
        {
            var original = new TextSpan(1, 2);
            var copy = original;

            // TextSpan 是只读结构体：对副本的任何读取都不可能反过来影响原值本身没有可写入口。
            Assert.Equal(original, copy);
            Assert.Equal(1, original.Start);
            Assert.Equal(2, original.Length);
        }

        [Fact]
        public void Equality_ComparesByValue()
        {
            var a = new TextSpan(2, 4);
            var b = new TextSpan(2, 4);
            var c = new TextSpan(2, 5);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Contains_Position_IsHalfOpen()
        {
            var span = new TextSpan(2, 3); // [2, 5)
            Assert.True(span.Contains(2));
            Assert.True(span.Contains(4));
            Assert.False(span.Contains(5));
        }

        [Fact]
        public void OverlapsWith_DetectsAdjacentNonOverlap()
        {
            var a = new TextSpan(0, 3); // [0, 3)
            var b = new TextSpan(3, 3); // [3, 6)
            Assert.False(a.OverlapsWith(b));
        }
    }
}
