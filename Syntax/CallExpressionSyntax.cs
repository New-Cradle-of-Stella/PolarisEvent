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

        public ArgumentListSyntax(SyntaxToken openParen, IReadOnlyList<ExpressionSyntax> arguments, IReadOnlyList<SyntaxToken> commas, SyntaxToken closeParen)
        {
            OpenParen = openParen;
            Arguments = arguments;
            Commas = commas;
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);

        public override string ToString() => "(" + string.Join(", ", Arguments) + ")";
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
