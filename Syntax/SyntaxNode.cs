using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 不可变语法节点的公共基类。本阶段只有表达式节点；后续阶段的语句/声明节点也会派生自此类型。
    /// <see cref="ToString"/> 在每个具体节点上被重写为一份紧凑的 s-表达式风格调试文本，
    /// 供测试直接做 AST 快照比较，而不必逐字段手写断言。
    /// </summary>
    public abstract class SyntaxNode
    {
        public abstract TextSpan Span { get; }
    }

    /// <summary>全部表达式节点的公共基类。</summary>
    public abstract class ExpressionSyntax : SyntaxNode
    {
    }
}
