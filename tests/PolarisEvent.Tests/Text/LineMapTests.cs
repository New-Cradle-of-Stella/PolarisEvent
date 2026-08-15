using System;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Text
{
    public class LineMapTests
    {
        [Fact]
        public void Build_TreatsCrLf_AsOneLineBreak()
        {
            var map = LineMap.Build("ab\r\ncd");
            Assert.Equal(2, map.LineCount);
            Assert.Equal((1, 3), map.GetLineColumn(2)); // '\r' 之前
            Assert.Equal((2, 1), map.GetLineColumn(4)); // 'c'
        }

        [Fact]
        public void Build_TreatsLoneCr_AsLineBreak()
        {
            var map = LineMap.Build("ab\rcd");
            Assert.Equal(2, map.LineCount);
            Assert.Equal((2, 1), map.GetLineColumn(3)); // 'c'
        }

        [Fact]
        public void Build_TreatsLoneLf_AsLineBreak()
        {
            var map = LineMap.Build("ab\ncd");
            Assert.Equal(2, map.LineCount);
            Assert.Equal((2, 1), map.GetLineColumn(3)); // 'c'
        }

        [Fact]
        public void Build_MixedLineBreaks_ProducesCorrectLineCount()
        {
            var map = LineMap.Build("a\r\nb\nc\rd");
            Assert.Equal(4, map.LineCount);
        }

        [Fact]
        public void GetLineColumn_CountsChineseCharactersAsOneColumnEach()
        {
            // "你好，世界" 之后紧跟 "X"；每个汉字在 UTF-16 里都是单个 char，第六列应落在 'X' 上。
            var text = "你好，世界X";
            var map = LineMap.Build(text);
            Assert.Equal((1, 6), map.GetLineColumn(5));
        }

        [Fact]
        public void GetLineIndex_AllowsEndOfTextPosition()
        {
            var map = LineMap.Build("abc");
            Assert.Equal(0, map.GetLineIndex(3));
        }

        [Fact]
        public void GetLineIndex_RejectsPositionBeyondText()
        {
            var map = LineMap.Build("abc");
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetLineIndex(4));
        }

        [Fact]
        public void GetLineIndex_RejectsNegativePosition()
        {
            var map = LineMap.Build("abc");
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetLineIndex(-1));
        }
    }
}
