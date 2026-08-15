using System;
using System.Text;
using System.Threading;
using Polaris.Pevt.Diagnostics;

namespace Polaris.Pevt.Text
{
    /// <summary>
    /// 一段不可变的 UTF-8 源文本。PEVT 永远以原始源文本为真相（见实现总纲全局不变量），因此本类型
    /// 只保存解码后的字符串与文件身份，不做任何格式化或字节码转换。
    /// </summary>
    public sealed class SourceText
    {
        /// <summary>源文件是否以合法的 UTF-8 BOM (EF BB BF) 开头。BOM 已从 <see cref="Content"/> 中去除。</summary>
        public bool HasByteOrderMark { get; }

        public string FilePath { get; }

        public string Content { get; }

        public int Length => Content.Length;

        private readonly LineMap _lineMap;

        internal SourceText(string content, string filePath, bool hasByteOrderMark)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            FilePath = filePath ?? string.Empty;
            HasByteOrderMark = hasByteOrderMark;
            _lineMap = LineMap.Build(Content);
        }

        public char this[int position]
        {
            get
            {
                if (position < 0 || position >= Content.Length)
                    throw new ArgumentOutOfRangeException(nameof(position), position, "偏移超出源文本范围。");
                return Content[position];
            }
        }

        public int LineCount => _lineMap.LineCount;

        public string GetText(TextSpan span)
        {
            CheckSpan(span);
            return Content.Substring(span.Start, span.Length);
        }

        public TextLocation GetLocation(TextSpan span)
        {
            CheckSpan(span);
            var (startLine, startColumn) = _lineMap.GetLineColumn(span.Start);
            var (endLine, endColumn) = _lineMap.GetLineColumn(span.End);
            return new TextLocation(this, span, startLine, startColumn, endLine, endColumn);
        }

        private void CheckSpan(TextSpan span)
        {
            if (span.Start > Content.Length || span.End > Content.Length)
                throw new ArgumentOutOfRangeException(nameof(span), span, "跨度超出源文本范围。");
        }

        /// <summary>PEVT1009 InvalidSourceEncoding：源文件不是合法 UTF-8，或 BOM 出现在文件起始位置以外。</summary>
        private const string InvalidSourceEncodingId = "PEVT1009";

        /// <summary>
        /// 严格解码 UTF-8 字节，构造一份 <see cref="SourceText"/>。拒绝越界、代理区、超长编码和非法续
        /// 字节；除文件起始处合法的 BOM 前缀外，解码结果中出现的任何 U+FEFF 都视为非法编码。
        /// </summary>
        public static SourceTextLoadResult FromUtf8(byte[] bytes, string filePath, CancellationToken cancellationToken = default)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            var diagnostics = new DiagnosticBag();
            var builder = new StringBuilder(bytes.Length);

            bool decoded = TryDecodeUtf8(bytes, builder, cancellationToken, out int invalidBytePosition);
            if (!decoded)
            {
                var partial = new SourceText(builder.ToString(), filePath, hasByteOrderMark: false);
                var location = partial.GetLocation(new TextSpan(partial.Length, 0));
                diagnostics.AddError(InvalidSourceEncodingId, $"源文件不是合法的 UTF-8 编码（首个非法字节位于文件第 {invalidBytePosition} 字节）。", location);
                return new SourceTextLoadResult(null, diagnostics.ToReadOnly());
            }

            const char byteOrderMarkChar = (char)0xFEFF;
            string raw = builder.ToString();
            bool hasBom = raw.Length > 0 && raw[0] == byteOrderMarkChar;
            string content = hasBom ? raw.Substring(1) : raw;

            int strayIndex = content.IndexOf(byteOrderMarkChar);
            if (strayIndex >= 0)
            {
                var partial = new SourceText(content, filePath, hasBom);
                var location = partial.GetLocation(new TextSpan(strayIndex, 1));
                diagnostics.AddError(InvalidSourceEncodingId, "文件起始位置以外出现字节顺序标记 (BOM)。", location);
                return new SourceTextLoadResult(null, diagnostics.ToReadOnly());
            }

            var text = new SourceText(content, filePath, hasBom);
            return new SourceTextLoadResult(text, diagnostics.ToReadOnly());
        }

        /// <summary>
        /// 手写的严格 UTF-8 解码器：不像 <see cref="Encoding.UTF8"/> 默认那样用替换字符掩盖错误，
        /// 而是在第一个非法字节处停止并报告其位置，拒绝超长编码、未配对的连续字节、代理区码点
        /// 和超出 U+10FFFF 的码点。
        /// </summary>
        private static bool TryDecodeUtf8(byte[] bytes, StringBuilder builder, CancellationToken cancellationToken, out int invalidBytePosition)
        {
            invalidBytePosition = -1;
            var gate = new CancellationGate(cancellationToken);
            int i = 0;
            int n = bytes.Length;

            while (i < n)
            {
                gate.Tick();

                byte b0 = bytes[i];
                int codepoint;
                int extra;

                if (b0 <= 0x7F)
                {
                    codepoint = b0;
                    extra = 0;
                }
                else if ((b0 & 0xE0) == 0xC0)
                {
                    if (b0 < 0xC2) { invalidBytePosition = i; return false; } // 拒绝双字节超长编码
                    codepoint = b0 & 0x1F;
                    extra = 1;
                }
                else if ((b0 & 0xF0) == 0xE0)
                {
                    codepoint = b0 & 0x0F;
                    extra = 2;
                }
                else if ((b0 & 0xF8) == 0xF0)
                {
                    if (b0 > 0xF4) { invalidBytePosition = i; return false; } // 超出 U+10FFFF 的前导字节
                    codepoint = b0 & 0x07;
                    extra = 3;
                }
                else
                {
                    invalidBytePosition = i; // 孤立续字节或已废弃的 5/6 字节前导
                    return false;
                }

                if (i + extra >= n)
                {
                    invalidBytePosition = i;
                    return false;
                }

                for (int k = 1; k <= extra; k++)
                {
                    byte bk = bytes[i + k];
                    if ((bk & 0xC0) != 0x80)
                    {
                        invalidBytePosition = i;
                        return false;
                    }
                    codepoint = (codepoint << 6) | (bk & 0x3F);
                }

                if (extra == 2 && codepoint < 0x800) { invalidBytePosition = i; return false; }
                if (extra == 3 && codepoint < 0x10000) { invalidBytePosition = i; return false; }
                if (codepoint >= 0xD800 && codepoint <= 0xDFFF) { invalidBytePosition = i; return false; }
                if (codepoint > 0x10FFFF) { invalidBytePosition = i; return false; }

                if (codepoint <= 0xFFFF)
                {
                    builder.Append((char)codepoint);
                }
                else
                {
                    int shifted = codepoint - 0x10000;
                    builder.Append((char)(0xD800 + (shifted >> 10)));
                    builder.Append((char)(0xDC00 + (shifted & 0x3FF)));
                }

                i += extra + 1;
            }

            return true;
        }
    }
}
