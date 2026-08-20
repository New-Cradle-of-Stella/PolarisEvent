using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// <c>@名(...)</c>、<c>_名(...)</c> 通用的括号参数列表（11.1/14.4 节）：逗号分隔的表达式，
    /// 数量与 <see cref="Commas"/> 的对应关系是 <c>Commas.Count == Arguments.Count - 1</c>（非空时）。
    /// </summary>
    public sealed class ArgumentListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParen { get; }
        public IReadOnlyList<ExpressionSyntax> Arguments { get; }
        public IReadOnlyList<SyntaxToken> Commas { get; }
        public SyntaxToken CloseParen { get; }

        /// <summary>是否来自 <c>enable cmdarg</c> 开启的同行空格实参形式。</summary>
        public bool IsWhitespaceSeparated { get; }

        private readonly TextSpan _span;

        public ArgumentListSyntax(SyntaxToken openParen, IReadOnlyList<ExpressionSyntax> arguments, IReadOnlyList<SyntaxToken> commas, SyntaxToken closeParen)
        {
            OpenParen = openParen;
            Arguments = SyntaxCollections.Freeze(arguments);
            Commas = SyntaxCollections.Freeze(commas);
            CloseParen = closeParen;
            _span = TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);
        }

        /// <summary>构造不含括号和逗号的同行空格实参列表；<paramref name="anchorEnd"/> 是调用名或事件 ID 的结束位置。</summary>
        public ArgumentListSyntax(IReadOnlyList<ExpressionSyntax> arguments, int anchorEnd)
        {
            Arguments = SyntaxCollections.Freeze(arguments);
            Commas = SyntaxCollections.Freeze(new List<SyntaxToken>());
            OpenParen = SyntaxToken.CreateMissing(SyntaxKind.OpenParenToken, anchorEnd);
            CloseParen = SyntaxToken.CreateMissing(SyntaxKind.CloseParenToken,
                Arguments.Count == 0 ? anchorEnd : Arguments[Arguments.Count - 1].Span.End);
            IsWhitespaceSeparated = true;
            _span = Arguments.Count == 0
                ? new TextSpan(anchorEnd, 0)
                : TextSpan.FromBounds(Arguments[0].Span.Start, Arguments[Arguments.Count - 1].Span.End);
        }

        public override TextSpan Span => _span;

        public override string ToString() => IsWhitespaceSeparated
            ? (Arguments.Count == 0 ? string.Empty : " " + string.Join(" ", Arguments))
            : "(" + string.Join(", ", Arguments) + ")";
    }

    /// <summary>内置事件语句调用 <c>@名(...)</c>（11.1 节）。</summary>
    public sealed class BuiltinCallExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken AtToken { get; }
        public SyntaxToken Name { get; }
        public ArgumentListSyntax Arguments { get; }

        public BuiltinCallExpressionSyntax(SyntaxToken atToken, SyntaxToken name, ArgumentListSyntax arguments)
        {
            AtToken = atToken;
            Name = name;
            Arguments = arguments;
        }

        public override TextSpan Span => TextSpan.FromBounds(AtToken.Span.Start, Arguments.Span.End);

        public override string ToString() => $"BuiltinCall(@{Name.Text}{Arguments})";
    }

    /// <summary>自定义事件块调用 <c>_名(...)</c>（14.4 节）。</summary>
    public sealed class CustomBlockCallExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Name { get; }
        public ArgumentListSyntax Arguments { get; }

        public CustomBlockCallExpressionSyntax(SyntaxToken name, ArgumentListSyntax arguments)
        {
            Name = name;
            Arguments = arguments;
        }

        public override TextSpan Span => TextSpan.FromBounds(Name.Span.Start, Arguments.Span.End);

        public override string ToString() => $"Call({Name.Text}{Arguments})";
    }
}
