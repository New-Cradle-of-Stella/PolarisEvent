using System;
using System.Text;
using System.Threading;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Text
{
    public class SourceTextTests
    {
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        [Fact]
        public void FromUtf8_PlainAscii_Decodes()
        {
            var result = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"a\"\nend\n"), "a.pevt");

            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            Assert.False(result.Text.HasByteOrderMark);
            Assert.Equal("id \"a\"\nend\n", result.Text.Content);
        }

        [Fact]
        public void FromUtf8_ChineseContent_Decodes()
        {
            var result = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"你好世界\""), "a.pevt");

            Assert.True(result.Success);
            Assert.Equal("id \"你好世界\"", result.Text.Content);
        }

        [Fact]
        public void FromUtf8_LeadingBom_IsStrippedAndFlagged()
        {
            byte[] bytes = Concat(Bom, Encoding.UTF8.GetBytes("ab"));

            var result = SourceText.FromUtf8(bytes, "a.pevt");

            Assert.True(result.Success);
            Assert.True(result.Text.HasByteOrderMark);
            Assert.Equal("ab", result.Text.Content);
        }

        [Fact]
        public void FromUtf8_BomNotAtStart_ReportsPEVT1009()
        {
            byte[] bytes = Concat(Bom, Encoding.UTF8.GetBytes("ab"), Bom, Encoding.UTF8.GetBytes("c"));

            var result = SourceText.FromUtf8(bytes, "a.pevt");

            Assert.False(result.Success);
            Assert.Single(result.Diagnostics);
            Assert.Equal("PEVT1009", result.Diagnostics[0].Id);
        }

        [Fact]
        public void FromUtf8_LoneContinuationByte_ReportsPEVT1009()
        {
            var result = SourceText.FromUtf8(new byte[] { 0x41, 0x80, 0x42 }, "a.pevt");

            Assert.False(result.Success);
            Assert.Equal("PEVT1009", result.Diagnostics[0].Id);
        }

        [Fact]
        public void FromUtf8_OverlongTwoByteEncoding_ReportsPEVT1009()
        {
            // 0xC0 0x80 是 NUL 的超长两字节编码，必须拒绝，不能静默接受为 U+0000。
            var result = SourceText.FromUtf8(new byte[] { 0xC0, 0x80 }, "a.pevt");

            Assert.False(result.Success);
            Assert.Equal("PEVT1009", result.Diagnostics[0].Id);
        }

        [Fact]
        public void FromUtf8_TruncatedMultibyteSequence_ReportsPEVT1009()
        {
            // 0xE4 0xB8 是"中"字三字节编码的前两个字节，缺第三个字节。
            var result = SourceText.FromUtf8(new byte[] { 0xE4, 0xB8 }, "a.pevt");

            Assert.False(result.Success);
            Assert.Equal("PEVT1009", result.Diagnostics[0].Id);
        }

        [Fact]
        public void FromUtf8_SurrogateCodepoint_ReportsPEVT1009()
        {
            // ED A0 80 编码码点 U+D800，落在代理区内，必须拒绝。
            var result = SourceText.FromUtf8(new byte[] { 0xED, 0xA0, 0x80 }, "a.pevt");

            Assert.False(result.Success);
            Assert.Equal("PEVT1009", result.Diagnostics[0].Id);
        }

        [Fact]
        public void FromUtf8_Cancellation_ThrowsOperationCanceled()
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', 5000));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => SourceText.FromUtf8(bytes, "a.pevt", cts.Token));
        }

        [Fact]
        public void GetText_SpanBeyondLength_Throws()
        {
            var text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("abc"), "a.pevt").Text;
            Assert.Throws<ArgumentOutOfRangeException>(() => text.GetText(new TextSpan(1, 10)));
        }

        [Fact]
        public void GetLocation_SpanBeyondLength_Throws()
        {
            var text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("abc"), "a.pevt").Text;
            Assert.Throws<ArgumentOutOfRangeException>(() => text.GetLocation(new TextSpan(0, 10)));
        }

        [Fact]
        public void Indexer_PositionAtLength_Throws()
        {
            var text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("abc"), "a.pevt").Text;
            Assert.Throws<ArgumentOutOfRangeException>(() => text[3]);
        }

        [Fact]
        public void Content_IsStableAcrossRepeatedReads()
        {
            var text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("abc"), "a.pevt").Text;

            string first = text.Content;
            string second = text.GetText(new TextSpan(0, text.Length));

            Assert.Equal(first, second);
            Assert.Equal("abc", text.Content); // 前面的读取没有以任何方式改变源文本
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var part in parts) total += part.Length;
            var result = new byte[total];
            int offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }
    }
}
