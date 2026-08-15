using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>全部语句节点的公共基类。</summary>
    public abstract class StatementSyntax : SyntaxNode
    {
    }

    /// <summary><c>end</c>（第 3 节）：终止当前执行路径。</summary>
    public sealed class EndStatementSyntax : StatementSyntax
    {
        public SyntaxToken EndKeyword { get; }

        public EndStatementSyntax(SyntaxToken endKeyword) => EndKeyword = endKeyword;

        public override TextSpan Span => EndKeyword.Span;

        public override string ToString() => "End";
    }

    /// <summary>事件内标签声明 <c>#Name</c>（7.1 节）。</summary>
    public sealed class LabelStatementSyntax : StatementSyntax
    {
        public SyntaxToken Hash { get; }
        public SyntaxToken Name { get; }

        public LabelStatementSyntax(SyntaxToken hash, SyntaxToken name)
        {
            Hash = hash;
            Name = name;
        }

        public override TextSpan Span => TextSpan.FromBounds(Hash.Span.Start, Name.Span.End);

        public override string ToString() => $"Label(#{Name.Text})";
    }

    /// <summary><c>goto #Name</c>（7.2 节）：跳转到当前标签环境中的标签。</summary>
    public sealed class GotoLabelStatementSyntax : StatementSyntax
    {
        public SyntaxToken GotoKeyword { get; }
        public SyntaxToken Hash { get; }
        public SyntaxToken Name { get; }

        public GotoLabelStatementSyntax(SyntaxToken gotoKeyword, SyntaxToken hash, SyntaxToken name)
        {
            GotoKeyword = gotoKeyword;
            Hash = hash;
            Name = name;
        }

        public override TextSpan Span => TextSpan.FromBounds(GotoKeyword.Span.Start, Name.Span.End);

        public override string ToString() => $"GotoLabel(#{Name.Text})";
    }

    /// <summary><c>goto 表达式</c>（6.5 节）：只在 switch 内有效，匹配某个 case 表达式；有效性留给后续阶段核对。</summary>
    public sealed class GotoCaseStatementSyntax : StatementSyntax
    {
        public SyntaxToken GotoKeyword { get; }
        public ExpressionSyntax Target { get; }

        public GotoCaseStatementSyntax(SyntaxToken gotoKeyword, ExpressionSyntax target)
        {
            GotoKeyword = gotoKeyword;
            Target = target;
        }

        public override TextSpan Span => TextSpan.FromBounds(GotoKeyword.Span.Start, Target.Span.End);

        public override string ToString() => $"GotoCase({Target})";
    }

    /// <summary>赋值语句 <c>标识符 = 表达式</c>（8.5 节）。</summary>
    public sealed class AssignmentStatementSyntax : StatementSyntax
    {
        public SyntaxToken Target { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Value { get; }

        public AssignmentStatementSyntax(SyntaxToken target, SyntaxToken equalsToken, ExpressionSyntax value)
        {
            Target = target;
            EqualsToken = equalsToken;
            Value = value;
        }

        public override TextSpan Span => TextSpan.FromBounds(Target.Span.Start, Value.Span.End);

        public override string ToString() => $"Assign({Target.Text}, {Value})";
    }

    /// <summary>
    /// 恢复用占位语句：孤立的闭合符（如落单的 <c>elif</c>/<c>endif</c>）、无法识别的语句起始，
    /// 或本阶段尚未支持的语句形态（<c>@</c>/<c>_</c> 调用等留给阶段 7）都用它包一层，
    /// 具体原因由对应位置报告的诊断编号说明,节点本身只负责占住这个位置。
    /// </summary>
    public sealed class UnknownStatementSyntax : StatementSyntax
    {
        public SyntaxToken LeadingToken { get; }

        public UnknownStatementSyntax(SyntaxToken leadingToken) => LeadingToken = leadingToken;

        public override TextSpan Span => LeadingToken.Span;

        public override string ToString() => $"Unknown({LeadingToken.Text})";
    }
}
