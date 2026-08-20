using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary><c>elif 表达式 事件语句...</c>，只作为 <see cref="IfStatementSyntax"/> 的一部分出现。</summary>
    public sealed class ElifClauseSyntax : SyntaxNode
    {
        public SyntaxToken ElifKeyword { get; }
        public ExpressionSyntax Condition { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }

        public ElifClauseSyntax(SyntaxToken elifKeyword, ExpressionSyntax condition, IReadOnlyList<StatementSyntax> body)
        {
            ElifKeyword = elifKeyword;
            Condition = condition;
            Body = SyntaxCollections.Freeze(body);
        }

        public override TextSpan Span => TextSpan.FromBounds(ElifKeyword.Span.Start, Condition.Span.End);

        public override string ToString() => $"Elif({Condition}, [{string.Join(", ", Body)}])";
    }

    /// <summary><c>else 事件语句...</c>，只作为 <see cref="IfStatementSyntax"/> 的一部分出现。</summary>
    public sealed class ElseClauseSyntax : SyntaxNode
    {
        public SyntaxToken ElseKeyword { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }

        public ElseClauseSyntax(SyntaxToken elseKeyword, IReadOnlyList<StatementSyntax> body)
        {
            ElseKeyword = elseKeyword;
            Body = SyntaxCollections.Freeze(body);
        }

        public override TextSpan Span => ElseKeyword.Span;

        public override string ToString() => $"Else([{string.Join(", ", Body)}])";
    }

    /// <summary><c>if 表达式 ... [elif ...]* [else ...]? endif</c>（4 节）。</summary>
    public sealed class IfStatementSyntax : StatementSyntax
    {
        public SyntaxToken IfKeyword { get; }
        public ExpressionSyntax Condition { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }
        public IReadOnlyList<ElifClauseSyntax> ElifClauses { get; }
        public ElseClauseSyntax ElseClause { get; }
        public SyntaxToken EndIf { get; }

        public IfStatementSyntax(SyntaxToken ifKeyword, ExpressionSyntax condition, IReadOnlyList<StatementSyntax> body,
            IReadOnlyList<ElifClauseSyntax> elifClauses, ElseClauseSyntax elseClause, SyntaxToken endIf)
        {
            IfKeyword = ifKeyword;
            Condition = condition;
            Body = SyntaxCollections.Freeze(body);
            ElifClauses = SyntaxCollections.Freeze(elifClauses);
            ElseClause = elseClause;
            EndIf = endIf;
        }

        public override TextSpan Span => TextSpan.FromBounds(IfKeyword.Span.Start, EndIf.Span.End);

        public override string ToString()
        {
            var builder = new StringBuilder("If(").Append(Condition).Append(", [").Append(string.Join(", ", Body)).Append(']');
            foreach (ElifClauseSyntax elif in ElifClauses)
                builder.Append(", ").Append(elif);
            if (ElseClause != null)
                builder.Append(", ").Append(ElseClause);
            return builder.Append(')').ToString();
        }
    }

    /// <summary>
    /// <c>ifdef 全局标记 ... [elsedef ...]? endifdef</c>。条件只检查全局标记是否已登记，
    /// 不读取标记当前的 true/false 值。
    /// </summary>
    public sealed class IfDefStatementSyntax : StatementSyntax
    {
        public SyntaxToken IfDefKeyword { get; }
        public SyntaxToken FlagName { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }
        public SyntaxToken ElseDefKeyword { get; }
        public IReadOnlyList<StatementSyntax> ElseBody { get; }
        public SyntaxToken EndIfDefKeyword { get; }

        public IfDefStatementSyntax(SyntaxToken ifDefKeyword, SyntaxToken flagName,
            IReadOnlyList<StatementSyntax> body, SyntaxToken elseDefKeyword,
            IReadOnlyList<StatementSyntax> elseBody, SyntaxToken endIfDefKeyword)
        {
            IfDefKeyword = ifDefKeyword;
            FlagName = flagName;
            Body = SyntaxCollections.Freeze(body);
            ElseDefKeyword = elseDefKeyword;
            ElseBody = SyntaxCollections.Freeze(elseBody);
            EndIfDefKeyword = endIfDefKeyword;
        }

        public bool HasElse => ElseDefKeyword != null;

        public string FlagKey => FlagName.Value.Kind == TokenValueKind.String
            ? FlagName.Value.AsString
            : FlagName.Text;

        public override TextSpan Span => TextSpan.FromBounds(IfDefKeyword.Span.Start, EndIfDefKeyword.Span.End);

        public override string ToString() => HasElse
            ? $"IfDef({FlagKey}, [{string.Join(", ", Body)}], ElseDef([{string.Join(", ", ElseBody)}]))"
            : $"IfDef({FlagKey}, [{string.Join(", ", Body)}])";
    }

    /// <summary><c>while 表达式 ... endwhile</c>（5 节）。</summary>
    public sealed class WhileStatementSyntax : StatementSyntax
    {
        public SyntaxToken WhileKeyword { get; }
        public ExpressionSyntax Condition { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }
        public SyntaxToken EndWhile { get; }

        public WhileStatementSyntax(SyntaxToken whileKeyword, ExpressionSyntax condition, IReadOnlyList<StatementSyntax> body, SyntaxToken endWhile)
        {
            WhileKeyword = whileKeyword;
            Condition = condition;
            Body = SyntaxCollections.Freeze(body);
            EndWhile = endWhile;
        }

        public override TextSpan Span => TextSpan.FromBounds(WhileKeyword.Span.Start, EndWhile.Span.End);

        public override string ToString() => $"While({Condition}, [{string.Join(", ", Body)}])";
    }

    /// <summary><c>switch</c> 选择分支的公共基类：<see cref="CaseArmSyntax"/> 或 <see cref="DefaultArmSyntax"/>。</summary>
    public abstract class SwitchArmSyntax : SyntaxNode
    {
        public IReadOnlyList<StatementSyntax> Body { get; }

        protected SwitchArmSyntax(IReadOnlyList<StatementSyntax> body) => Body = SyntaxCollections.Freeze(body);
    }

    /// <summary><c>case 表达式 事件语句...</c>（6.2 节）。</summary>
    public sealed class CaseArmSyntax : SwitchArmSyntax
    {
        public SyntaxToken CaseKeyword { get; }
        public ExpressionSyntax Value { get; }

        public CaseArmSyntax(SyntaxToken caseKeyword, ExpressionSyntax value, IReadOnlyList<StatementSyntax> body) : base(body)
        {
            CaseKeyword = caseKeyword;
            Value = value;
        }

        public override TextSpan Span => TextSpan.FromBounds(CaseKeyword.Span.Start, Value.Span.End);

        public override string ToString() => $"Case({Value}, [{string.Join(", ", Body)}])";
    }

    /// <summary><c>default 事件语句...</c>（6.3 节）。</summary>
    public sealed class DefaultArmSyntax : SwitchArmSyntax
    {
        public SyntaxToken DefaultKeyword { get; }

        public DefaultArmSyntax(SyntaxToken defaultKeyword, IReadOnlyList<StatementSyntax> body) : base(body) => DefaultKeyword = defaultKeyword;

        public override TextSpan Span => DefaultKeyword.Span;

        public override string ToString() => $"Default([{string.Join(", ", Body)}])";
    }

    /// <summary><c>switch 表达式 (case|default)+ endswitch</c>（6.1 节）。</summary>
    public sealed class SwitchStatementSyntax : StatementSyntax
    {
        public SyntaxToken SwitchKeyword { get; }
        public ExpressionSyntax Value { get; }
        public IReadOnlyList<SwitchArmSyntax> Arms { get; }
        public SyntaxToken EndSwitch { get; }

        public SwitchStatementSyntax(SyntaxToken switchKeyword, ExpressionSyntax value, IReadOnlyList<SwitchArmSyntax> arms, SyntaxToken endSwitch)
        {
            SwitchKeyword = switchKeyword;
            Value = value;
            Arms = SyntaxCollections.Freeze(arms);
            EndSwitch = endSwitch;
        }

        public override TextSpan Span => TextSpan.FromBounds(SwitchKeyword.Span.Start, EndSwitch.Span.End);

        public override string ToString() => $"Switch({Value}, [{string.Join(", ", Arms)}])";
    }
}
