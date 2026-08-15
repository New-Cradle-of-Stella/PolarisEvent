using System;
using System.Collections.Generic;

namespace Polaris.Pevt.Text
{
    /// <summary>
    /// 把字符偏移映射到 1-based 行号与列号的不可变行表。识别 <c>\r\n</c>、单独 <c>\r</c> 与单独 <c>\n</c>
    /// 三种换行，且不会把 <c>\r\n</c> 计成两行。列号按 UTF-16 char 计数，不做码点或字形聚簇折算。
    /// </summary>
    public sealed class LineMap
    {
        private readonly int[] _lineStarts;
        private readonly int _textLength;

        private LineMap(int[] lineStarts, int textLength)
        {
            _lineStarts = lineStarts;
            _textLength = textLength;
        }

        public int LineCount => _lineStarts.Length;

        public static LineMap Build(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var starts = new List<int> { 0 };
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\r')
                {
                    int next = i + 1;
                    if (next < text.Length && text[next] == '\n')
                        next++;
                    starts.Add(next);
                    i = next;
                }
                else if (c == '\n')
                {
                    i++;
                    starts.Add(i);
                }
                else
                {
                    i++;
                }
            }

            return new LineMap(starts.ToArray(), text.Length);
        }

        /// <summary>返回偏移所在的 0-based 行索引。<paramref name="position"/> 允许等于文本长度（文件末尾位置）。</summary>
        public int GetLineIndex(int position)
        {
            if (position < 0 || position > _textLength)
                throw new ArgumentOutOfRangeException(nameof(position), position, "偏移超出源文本范围。");

            int lo = 0;
            int hi = _lineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (_lineStarts[mid] <= position)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return lo;
        }

        public int GetLineStart(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lineStarts.Length)
                throw new ArgumentOutOfRangeException(nameof(lineIndex), lineIndex, "行索引超出范围。");
            return _lineStarts[lineIndex];
        }

        /// <summary>返回偏移对应的 1-based (行, 列)。</summary>
        public (int Line, int Column) GetLineColumn(int position)
        {
            int lineIndex = GetLineIndex(position);
            int column = position - _lineStarts[lineIndex] + 1;
            return (lineIndex + 1, column);
        }
    }
}
