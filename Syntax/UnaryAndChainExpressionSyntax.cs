using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>一元取负 <c>-x</c> 或逻辑非 <c>!x</c>（8.4/8.7 节），可以连续嵌套（<c>a - -b</c>）。</summary>
    public sealed class UnaryExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Operand { get; }

        public UnaryExpressionSyntax(SyntaxToken operatorToken, ExpressionSyntax operand)
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }

        public override TextSpan Span => TextSpan.FromBounds(OperatorToken.Span.Start, Operand.Span.End);

        public override string ToString() => $"Unary({OperatorToken.Text}, {Operand})";
    }

    /// <summary>链式二元表达式中的一节：一个运算符加它右侧的操作数。</summary>
    public sealed class BinaryChainSegment
    {
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Operand { get; }

        public BinaryChainSegment(SyntaxToken operatorToken, ExpressionSyntax operand)
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }
    }

    /// <summary>
    /// PEVT 8.8 节的线性从左到右二元链：没有运算符优先级，第一个操作数后面挂着任意多个
    /// "运算符 + 操作数"节；<c>a + b * c</c> 是这一个节点、两段的平铺链，绝不会因为 <c>*</c>
    /// 而把 <c>b</c>、<c>c</c> 单独聚成一个嵌套子节点——那只有显式括号才会发生。
    /// </summary>
    public sealed class ChainedBinaryExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax First { get; }
        public IReadOnlyList<BinaryChainSegment> Segments { get; }

        public ChainedBinaryExpressionSyntax(ExpressionSyntax first, IReadOnlyList<BinaryChainSegment> segments)
        {
            First = first;
            Segments = segments;
        }

        public override TextSpan Span => TextSpan.FromBounds(First.Span.Start, Segments[Segments.Count - 1].Operand.Span.End);

        public override string ToString()
        {
            var builder = new StringBuilder("Chain(").Append(First);
            foreach (BinaryChainSegment segment in Segments)
                builder.Append(", ").Append(segment.OperatorToken.Text).Append(' ').Append(segment.Operand);
            return builder.Append(')').ToString();
        }
    }
}
