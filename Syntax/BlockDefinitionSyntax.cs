using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>自定义事件块签名中的单个形参 <c>名 : 类型</c>（14.1 节）。</summary>
    public sealed class ParameterSyntax : SyntaxNode
    {
        public SyntaxToken Name { get; }
        public SyntaxToken Colon { get; }
        public SyntaxToken Type { get; }

        public ParameterSyntax(SyntaxToken name, SyntaxToken colon, SyntaxToken type)
        {
            Name = name;
            Colon = colon;
            Type = type;
        }

        public override TextSpan Span => TextSpan.FromBounds(Name.Span.Start, Type.Span.End);

        public override string ToString() => $"{Name.Text}: {Type.Text}";
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
            Parameters = parameters;
            Commas = commas;
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(OpenParen.Span.Start, CloseParen.Span.End);

        public override string ToString() => "(" + string.Join(", ", Parameters) + ")";
    }

    /// <summary>
    /// 自定义事件块定义 <c>[async] block _名(参数...) [: 返回类型] 正文 endblock</c>（14.1/14.2 节）。
    /// 定义本身不由外层事件顺序执行——它只是被顺序解析出来的一个语法节点，真正的"顺序执行"发生在
    /// 调用它的 <see cref="CustomBlockCallExpressionSyntax"/> 处，绑定阶段才需要关心这个区别。
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
            Body = body;
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
    /// 也用于恢复路径（缺失/非法目标时不强行编造一个占位标识符）。目标类型是否与块声明的返回值类型
    /// 匹配（PEVT7109）需要类型绑定，留给后续阶段。
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
