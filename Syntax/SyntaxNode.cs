using System;
using System.Collections.Generic;
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

    /// <summary>
    /// G5 深不可变门的核心防线：每个持有集合的语法节点构造函数都必须在这里把调用者传入的
    /// <see cref="IReadOnlyList{T}"/> 复制进一份独立数组，而不是直接持有调用者的引用——否则
    /// 类型只做到了"get-only 属性"这一层浅不可变，调用者事后修改原始 List/array 仍会改写
    /// 已经构造好的节点。<see cref="SyntaxToken"/> 的 trivia 列表同样通过这里冻结。
    /// </summary>
    internal static class SyntaxCollections
    {
        public static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> items)
        {
            if (items == null)
                return Array.Empty<T>();

            var copy = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
                copy[i] = items[i];
            return Array.AsReadOnly(copy);
        }
    }
}
