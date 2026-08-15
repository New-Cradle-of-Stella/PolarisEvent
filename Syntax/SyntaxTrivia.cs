using System;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 附着在 token 前后、不参与语法分析的内容种类：空白、换行、行注释与块注释（1.3 节）。
    /// </summary>
    public enum TriviaKind
    {
        Whitespace,
        LineBreak,
        LineComment,
        BlockComment,
    }

    /// <summary>一段不可变的 trivia：种类、源码跨度与原始文本。</summary>
    public sealed class SyntaxTrivia
    {
        public TriviaKind Kind { get; }

        public TextSpan Span { get; }

        public string Text { get; }

        public SyntaxTrivia(TriviaKind kind, TextSpan span, string text)
        {
            Kind = kind;
            Span = span;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }
    }
}
