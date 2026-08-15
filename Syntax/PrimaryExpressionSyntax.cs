using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>字面常量：整数/浮点/字符/字符串/布尔（8.9 节）。</summary>
    public sealed class LiteralExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Token { get; }

        public LiteralExpressionSyntax(SyntaxToken token) => Token = token;

        public override TextSpan Span => Token.Span;

        public override string ToString() => $"Literal({Token.Text})";
    }

    /// <summary>已定义变量或常量的名称引用（8.1 节）。</summary>
    public sealed class NameExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Identifier { get; }

        public NameExpressionSyntax(SyntaxToken identifier) => Identifier = identifier;

        public override TextSpan Span => Identifier.Span;

        public override string ToString() => $"Name({Identifier.Text})";
    }

    /// <summary>
    /// 当前位置无法解析出合法表达式时的占位节点（PEVT5001 等）。让调用方（参数列表、后续阶段的语句
    /// 解析）始终能拿到一个非 null 的 <see cref="ExpressionSyntax"/> 继续走完剩余结构。
    /// </summary>
    public sealed class MissingExpressionSyntax : ExpressionSyntax
    {
        private readonly int _position;

        public MissingExpressionSyntax(int position) => _position = position;

        public override TextSpan Span => new TextSpan(_position, 0);

        public override string ToString() => "Missing";
    }

    /// <summary>括号表达式；括号内是独立的子表达式，递归解析（8.8 节）。</summary>
    public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenParen { get; }
        public ExpressionSyntax Inner { get; }
        public SyntaxToken CloseParen { get; }

        public ParenthesizedExpressionSyntax(SyntaxToken openParen, ExpressionSyntax inner, SyntaxToken closeParen)
        {
            OpenParen = openParen;
            Inner = inner;
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);

        public override string ToString() => $"Paren({Inner})";
    }

    /// <summary>显式类型转换 <c>(float)x</c> / <c>(string)x</c>（8.3 节）；目标只能是一个裸变量名。</summary>
    public sealed class ConversionExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenParen { get; }
        public SyntaxToken TargetType { get; }
        public SyntaxToken CloseParen { get; }
        public SyntaxToken Variable { get; }

        public ConversionExpressionSyntax(SyntaxToken openParen, SyntaxToken targetType, SyntaxToken closeParen, SyntaxToken variable)
        {
            OpenParen = openParen;
            TargetType = targetType;
            CloseParen = closeParen;
            Variable = variable;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, Variable.Span.End);

        public override string ToString() => $"Conversion({TargetType.Text}, {Variable.Text})";
    }
}
