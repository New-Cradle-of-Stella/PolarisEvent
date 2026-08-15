using System;

namespace Polaris.Pevt.Text
{
    /// <summary>诊断展示用的不可变位置：源文件身份、字符跨度与 1-based 起止行列。</summary>
    public sealed class TextLocation : IEquatable<TextLocation>
    {
        public SourceText Source { get; }

        public TextSpan Span { get; }

        public int StartLine { get; }

        public int StartColumn { get; }

        public int EndLine { get; }

        public int EndColumn { get; }

        public string FilePath => Source?.FilePath ?? string.Empty;

        internal TextLocation(SourceText source, TextSpan span, int startLine, int startColumn, int endLine, int endColumn)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Span = span;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
        }

        public bool Equals(TextLocation other)
        {
            if (other is null)
                return false;
            return ReferenceEquals(Source, other.Source) && Span == other.Span;
        }

        public override bool Equals(object obj) => Equals(obj as TextLocation);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Source?.GetHashCode() ?? 0;
                return (hash * 397) ^ Span.GetHashCode();
            }
        }

        public override string ToString() =>
            StartLine == EndLine && StartColumn == EndColumn
                ? $"{FilePath}:{StartLine}:{StartColumn}"
                : $"{FilePath}:{StartLine}:{StartColumn}-{EndLine}:{EndColumn}";
    }
}
