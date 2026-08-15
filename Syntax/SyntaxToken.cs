using System;
using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 词法分析产生的一个不可变 token：种类、源码跨度、原始文本、已解码字面量值及前后 trivia。
    /// </summary>
    public sealed class SyntaxToken
    {
        private static readonly IReadOnlyList<SyntaxTrivia> NoTrivia = Array.Empty<SyntaxTrivia>();

        public SyntaxKind Kind { get; }

        public TextSpan Span { get; }

        public string Text { get; }

        public TokenValue Value { get; }

        /// <summary>
        /// 解析器期望某个 token 但源码中缺失时为 true。此时 <see cref="Span"/> 是解析位置处的零长度跨度，
        /// <see cref="Text"/> 为空字符串，<see cref="Kind"/> 仍等于解析器原本期望的种类。
        /// </summary>
        public bool IsMissing { get; }

        public IReadOnlyList<SyntaxTrivia> LeadingTrivia { get; }

        public IReadOnlyList<SyntaxTrivia> TrailingTrivia { get; }

        public SyntaxToken(
            SyntaxKind kind,
            TextSpan span,
            string text,
            TokenValue value,
            IReadOnlyList<SyntaxTrivia> leadingTrivia = null,
            IReadOnlyList<SyntaxTrivia> trailingTrivia = null,
            bool isMissing = false)
        {
            Kind = kind;
            Span = span;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Value = value;
            IsMissing = isMissing;
            LeadingTrivia = leadingTrivia ?? NoTrivia;
            TrailingTrivia = trailingTrivia ?? NoTrivia;
        }

        /// <summary>为解析器期望但源码中不存在的 <paramref name="expectedKind"/> 创建一个缺失 token 表示。</summary>
        public static SyntaxToken CreateMissing(SyntaxKind expectedKind, int position) =>
            new SyntaxToken(expectedKind, new TextSpan(position, 0), string.Empty, TokenValue.None, isMissing: true);

        public override string ToString() =>
            IsMissing ? $"<missing {Kind}>" : $"{Kind} '{Text}' @ {Span}";
    }
}
