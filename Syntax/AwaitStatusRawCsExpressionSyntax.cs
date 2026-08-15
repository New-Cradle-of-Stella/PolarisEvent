using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>单句柄 <c>await a</c>（15.3 节）。集合形式 <c>await all/any(...)(...)</c> 不在本阶段范围内。</summary>
    public sealed class AwaitExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken AwaitKeyword { get; }
        public SyntaxToken Handle { get; }

        public AwaitExpressionSyntax(SyntaxToken awaitKeyword, SyntaxToken handle)
        {
            AwaitKeyword = awaitKeyword;
            Handle = handle;
        }

        public override TextSpan Span => TextSpan.FromBounds(AwaitKeyword.Span.Start, Handle.Span.End);

        public override string ToString() => $"Await({Handle.Text})";
    }

    /// <summary><c>status a</c>（15.5 节）：结果类型固定为 int 的表达式，只能是表达式，不是语句。</summary>
    public sealed class StatusExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken StatusKeyword { get; }
        public SyntaxToken Handle { get; }

        public StatusExpressionSyntax(SyntaxToken statusKeyword, SyntaxToken handle)
        {
            StatusKeyword = statusKeyword;
            Handle = handle;
        }

        public override TextSpan Span => TextSpan.FromBounds(StatusKeyword.Span.Start, Handle.Span.End);

        public override string ToString() => $"Status({Handle.Text})";
    }

    /// <summary><c>$raw cs [(变量,...)] '''...'''</c> 的变量传入列表（12.2 节：只能是裸标识符，不是表达式）。</summary>
    public sealed class IdentifierListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParen { get; }
        public IReadOnlyList<SyntaxToken> Identifiers { get; }
        public IReadOnlyList<SyntaxToken> Commas { get; }
        public SyntaxToken CloseParen { get; }

        public IdentifierListSyntax(SyntaxToken openParen, IReadOnlyList<SyntaxToken> identifiers, IReadOnlyList<SyntaxToken> commas, SyntaxToken closeParen)
        {
            OpenParen = openParen;
            Identifiers = identifiers;
            Commas = commas;
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);

        public override string ToString() => "(" + string.Join(", ", Identifiers.Select(t => t.Text)) + ")";
    }

    /// <summary>
    /// <c>$raw cs</c> 用作表达式时的占位节点（12.2/13.1 节，raw-cs-expression 是 primary-expression 的
    /// 一员）。本阶段只搭出结构骨架——参数列表、原始文本块三个 token；返回类型一致性等深入语义校验
    /// 留给后续处理 <c>$raw</c> 语句形式的阶段一起做。
    /// </summary>
    public sealed class RawCsExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken DollarRaw { get; }
        public SyntaxToken CsKeyword { get; }
        public IdentifierListSyntax Arguments { get; }
        public SyntaxToken OpenDelimiter { get; }
        public SyntaxToken Content { get; }
        public SyntaxToken CloseDelimiter { get; }

        public RawCsExpressionSyntax(SyntaxToken dollarRaw, SyntaxToken csKeyword, IdentifierListSyntax arguments,
            SyntaxToken openDelimiter, SyntaxToken content, SyntaxToken closeDelimiter)
        {
            DollarRaw = dollarRaw;
            CsKeyword = csKeyword;
            Arguments = arguments;
            OpenDelimiter = openDelimiter;
            Content = content;
            CloseDelimiter = closeDelimiter;
        }

        public override TextSpan Span => TextSpan.FromBounds(DollarRaw.Span.Start, CloseDelimiter.Span.End);

        public override string ToString() => $"RawCs({Arguments}, \"{Content.Value}\")";
    }
}
