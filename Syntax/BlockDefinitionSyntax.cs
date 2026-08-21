using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>形参声明 <c>名 : 类型 [= 常量表达式]</c>。事件头与自定义事件块共用。</summary>
    public sealed class ParameterSyntax : SyntaxNode
    {
        public SyntaxToken Name { get; }
        public SyntaxToken Colon { get; }
        public SyntaxToken Type { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax DefaultValue { get; }

        public ParameterSyntax(SyntaxToken name, SyntaxToken colon, SyntaxToken type,
            SyntaxToken equalsToken = null, ExpressionSyntax defaultValue = null)
        {
            Name = name;
            Colon = colon;
            Type = type;
            EqualsToken = equalsToken;
            DefaultValue = defaultValue;
        }

        public bool HasDefaultValue => DefaultValue != null;

        public override TextSpan Span => TextSpan.FromBounds(Name.Span.Start, (DefaultValue?.Span ?? Type.Span).End);

        public override string ToString() => DefaultValue == null
            ? $"{Name.Text}: {Type.Text}"
            : $"{Name.Text}: {Type.Text} = {DefaultValue}";
    }

    /// <summary>形参列表 <c>(名 : 类型, ...)</c>；空参数列表写作 <c>()</c>（14.1 节）。</summary>
    public sealed class ParameterListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParen { get; }
        public IReadOnlyList<ParameterSyntax> Parameters { get; }
        public IReadOnlyList<SyntaxToken> Commas { get; }
        public SyntaxToken CloseParen { get; }

        public ParameterListSyntax(SyntaxToken openParen, IReadOnlyList<ParameterSyntax> parameters, IReadOnlyList<SyntaxToken> commas, SyntaxToken closeParen)
        {
            OpenParen = openParen;
            Parameters = SyntaxCollections.Freeze(parameters);
            Commas = SyntaxCollections.Freeze(commas);
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);

        public override string ToString() => "(" + string.Join(", ", Parameters) + ")";
    }

    /// <summary>
    /// 自定义事件块定义 <c>[async] block _名(参数...) [: 返回类型] 正文 endblock</c>（14.1/14.2 节）。
    /// 定义本身不被外层事件顺序执行，真正的执行发生在调用它的 <see cref="CustomBlockCallExpressionSyntax"/> 处。
    /// </summary>
    public sealed class BlockDefinitionStatementSyntax : StatementSyntax
    {
        public SyntaxToken AsyncKeyword { get; }
        public SyntaxToken BlockKeyword { get; }
        public SyntaxToken Name { get; }
        public ParameterListSyntax Parameters { get; }
        public SyntaxToken Colon { get; }
        public SyntaxToken ReturnType { get; }
        public IReadOnlyList<StatementSyntax> Body { get; }
        public SyntaxToken EndBlockKeyword { get; }

        public BlockDefinitionStatementSyntax(SyntaxToken asyncKeyword, SyntaxToken blockKeyword, SyntaxToken name,
            ParameterListSyntax parameters, SyntaxToken colon, SyntaxToken returnType,
            IReadOnlyList<StatementSyntax> body, SyntaxToken endBlockKeyword)
        {
            AsyncKeyword = asyncKeyword;
            BlockKeyword = blockKeyword;
            Name = name;
            Parameters = parameters;
            Colon = colon;
            ReturnType = returnType;
            Body = SyntaxCollections.Freeze(body);
            EndBlockKeyword = endBlockKeyword;
        }

        public override TextSpan Span => TextSpan.FromBounds((AsyncKeyword ?? BlockKeyword).Span.Start, EndBlockKeyword.Span.End);

        public override string ToString()
        {
            string prefix = AsyncKeyword == null ? "Block" : "AsyncBlock";
            string returns = ReturnType == null ? "" : $" : {ReturnType.Text}";
            return $"{prefix}({Name.Text}{Parameters}{returns}, [{string.Join(", ", Body)}])";
        }
    }

    /// <summary>
    /// <c>return [目标]</c>（14.3 节）：<see cref="Target"/> 为 null 表示无返回值的 <c>return</c>，
    /// 也用于缺失或非法目标时的恢复路径。
    /// </summary>
    public sealed class ReturnStatementSyntax : StatementSyntax
    {
        public SyntaxToken ReturnKeyword { get; }
        public SyntaxToken Target { get; }

        public ReturnStatementSyntax(SyntaxToken returnKeyword, SyntaxToken target)
        {
            ReturnKeyword = returnKeyword;
            Target = target;
        }

        public override TextSpan Span => TextSpan.FromBounds(ReturnKeyword.Span.Start, (Target ?? ReturnKeyword).Span.End);

        public override string ToString() => Target == null ? "Return" : $"Return({Target.Text})";
    }
}
