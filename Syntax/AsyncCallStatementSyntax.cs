using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 表达式被用作独立事件语句时的包装（<c>@名(...)</c>、<c>_名(...)</c>、裸 <c>await</c> 等，11.1/14.4/15.3 节）。
    /// 语句位置本身不改变表达式的求值方式，只是丢弃其结果（如果有）。
    /// </summary>
    public sealed class ExpressionStatementSyntax : StatementSyntax
    {
        public ExpressionSyntax Expression { get; }

        public ExpressionStatementSyntax(ExpressionSyntax expression) => Expression = expression;

        public override TextSpan Span => Expression.Span;

        public override string ToString() => Expression.ToString();
    }

    /// <summary>
    /// <c>handler 名 = 异步调用</c>（15.2 节）。初始化器只能是 <c>@</c> 调用、<c>_</c> 调用或
    /// <c>callevt</c>；哪一种调用"静态已知"确实是异步的（PEVT7204 等）需要签名/环境绑定，留给后续阶段。
    /// </summary>
    public sealed class HandlerDeclarationStatementSyntax : StatementSyntax
    {
        public SyntaxToken HandlerKeyword { get; }
        public SyntaxToken Name { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Initializer { get; }

        public HandlerDeclarationStatementSyntax(SyntaxToken handlerKeyword, SyntaxToken name, SyntaxToken equalsToken, ExpressionSyntax initializer)
        {
            HandlerKeyword = handlerKeyword;
            Name = name;
            EqualsToken = equalsToken;
            Initializer = initializer;
        }

        public override TextSpan Span => TextSpan.FromBounds(HandlerKeyword.Span.Start, Initializer.Span.End);

        public override string ToString() => $"Handler({Name.Text}, {Initializer})";
    }

    /// <summary><c>kill 句柄</c>（15.4 节）：句柄是否已定义（PEVT7210）留给环境绑定阶段。</summary>
    public sealed class KillStatementSyntax : StatementSyntax
    {
        public SyntaxToken KillKeyword { get; }
        public SyntaxToken Handle { get; }

        public KillStatementSyntax(SyntaxToken killKeyword, SyntaxToken handle)
        {
            KillKeyword = killKeyword;
            Handle = handle;
        }

        public override TextSpan Span => TextSpan.FromBounds(KillKeyword.Span.Start, Handle.Span.End);

        public override string ToString() => $"Kill({Handle.Text})";
    }

    /// <summary>
    /// <c>callevt "事件ID"</c>（10 节）。目标是否存在、是否声明 <c>enable async</c> 只在运行时解析
    /// （10.4 节）——本节点只承载语法层面已经确定的目标字面量。作为独立语句、handler 初始化器以外的
    /// 表达式位置使用时报 PEVT7304（见 <see cref="Parser.ParsePrimaryOperand"/>）。
    /// </summary>
    public sealed class EventCallExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken CallEvtKeyword { get; }
        public SyntaxToken Target { get; }

        public EventCallExpressionSyntax(SyntaxToken callEvtKeyword, SyntaxToken target)
        {
            CallEvtKeyword = callEvtKeyword;
            Target = target;
        }

        public override TextSpan Span => TextSpan.FromBounds(CallEvtKeyword.Span.Start, Target.Span.End);

        public override string ToString() => $"CallEvt({Target.Text})";
    }

    /// <summary>
    /// <c>exec(source)</c>（13.1 节）：动态解析并执行一段 PEVT 源文本。片段内容本身的校验发生在运行时
    /// （13.4 节），加载期只检查参数数量（PEVT7401/7402）与位置（PEVT7404/7406）；参数静态类型是否为
    /// <c>string</c>（PEVT7403）需要类型绑定，留给后续阶段。
    /// </summary>
    public sealed class ExecCallExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken ExecKeyword { get; }
        public ArgumentListSyntax Arguments { get; }

        public ExecCallExpressionSyntax(SyntaxToken execKeyword, ArgumentListSyntax arguments)
        {
            ExecKeyword = execKeyword;
            Arguments = arguments;
        }

        public override TextSpan Span => TextSpan.FromBounds(ExecKeyword.Span.Start, Arguments.Span.End);

        public override string ToString() => $"Exec{Arguments}";
    }

    /// <summary>
    /// <c>await all(句柄...)(绑定...)</c> / <c>await any(句柄...)(绑定...)</c>（15.6 节）。句柄/绑定
    /// 列表都只是逗号分隔的裸标识符，复用 <see cref="IdentifierListSyntax"/>；绑定列表允许为空
    /// （放弃全部返回值），句柄列表不允许为空（PEVT7217）。列表内容的名称有效性、去重、数量匹配
    /// （PEVT7218/7219/7221/7223/7224）以及带绑定形式只能出现在语句/初始化器位置（PEVT7225）
    /// 都需要环境/绑定信息，留给后续阶段。
    /// </summary>
    public sealed class AggregateAwaitExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken AwaitKeyword { get; }
        public SyntaxToken ModeKeyword { get; }
        public IdentifierListSyntax Handles { get; }
        public IdentifierListSyntax Bindings { get; }

        public AggregateAwaitExpressionSyntax(SyntaxToken awaitKeyword, SyntaxToken modeKeyword, IdentifierListSyntax handles, IdentifierListSyntax bindings)
        {
            AwaitKeyword = awaitKeyword;
            ModeKeyword = modeKeyword;
            Handles = handles;
            Bindings = bindings;
        }

        public override TextSpan Span => TextSpan.FromBounds(AwaitKeyword.Span.Start, Bindings.Span.End);

        public override string ToString() => $"AwaitAggregate({ModeKeyword.Text}, {Handles}, {Bindings})";
    }

    /// <summary>
    /// <c>$raw cmd'''...'''</c> 作为独立事件语句（12.1 节）：永远不产生值，只能是语句，因此不需要像
    /// <c>$raw cs</c> 那样有对应的表达式形态——出现在表达式位置本身就是 PEVT8006（见既有的
    /// <see cref="Parser.ParseRawCsExpression"/>）。
    /// </summary>
    public sealed class RawCmdStatementSyntax : StatementSyntax
    {
        public SyntaxToken DollarRaw { get; }
        public SyntaxToken CmdKeyword { get; }
        public SyntaxToken OpenDelimiter { get; }
        public SyntaxToken Content { get; }
        public SyntaxToken CloseDelimiter { get; }

        public RawCmdStatementSyntax(SyntaxToken dollarRaw, SyntaxToken cmdKeyword, SyntaxToken openDelimiter, SyntaxToken content, SyntaxToken closeDelimiter)
        {
            DollarRaw = dollarRaw;
            CmdKeyword = cmdKeyword;
            OpenDelimiter = openDelimiter;
            Content = content;
            CloseDelimiter = closeDelimiter;
        }

        public override TextSpan Span => TextSpan.FromBounds(DollarRaw.Span.Start, CloseDelimiter.Span.End);

        public override string ToString() => $"RawCmd(\"{Content.Value}\")";
    }
}
