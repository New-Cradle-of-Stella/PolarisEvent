using System;
using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 不可变语法节点的公共基类。<see cref="ToString"/> 在每个具体节点上被重写为紧凑的 s-表达式风格调试文本。
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
    /// G5 深不可变门的核心防线：持有集合的语法节点必须在这里把调用者传入的列表复制进独立数组，
    /// 否则调用者事后修改原始集合仍会改写已经构造好的节点。<see cref="SyntaxToken"/> 的 trivia 列表同样通过这里冻结。
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
