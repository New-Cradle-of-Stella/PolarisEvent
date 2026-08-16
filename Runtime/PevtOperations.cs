using System;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 二元运算的运行期语义（8.4/8.6/8.7 节）。
    ///
    /// 类型匹配由绑定器在加载期保证，这里只负责真正的算术和三条运行期失败：
    /// <c>int</c> 用 checked 溢出 → PEVTR2001，除零 → PEVTR2002，
    /// <c>float</c> 结果非有限 → PEVTR2003。
    /// </summary>
    internal static class PevtOperations
    {
        public static PevtRuntimeDiagnostic Evaluate(
            SyntaxKind op,
            PevtValue left,
            PevtValue right,
            TextSpan span,
            SourceText source,
            out PevtValue result)
        {
            result = default;

            switch (op)
            {
                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                case SyntaxKind.StarToken:
                case SyntaxKind.SlashToken:
                case SyntaxKind.PercentToken:
                    return Arithmetic(op, left, right, span, source, out result);

                case SyntaxKind.EqualsEqualsToken:
                    result = PevtValue.FromBool(left.Equals(right));
                    return null;

                case SyntaxKind.ExclamationEqualsToken:
                    result = PevtValue.FromBool(!left.Equals(right));
                    return null;

                case SyntaxKind.LessThanToken:
                case SyntaxKind.LessThanEqualsToken:
                case SyntaxKind.GreaterThanToken:
                case SyntaxKind.GreaterThanEqualsToken:
                    return Compare(op, left, right, span, source, out result);

                case SyntaxKind.AmpersandToken:
                    result = PevtValue.FromBool(left.AsBool & right.AsBool);
                    return null;

                case SyntaxKind.PipeToken:
                    result = PevtValue.FromBool(left.AsBool | right.AsBool);
                    return null;

                case SyntaxKind.CaretToken:
                    result = PevtValue.FromBool(left.AsBool ^ right.AsBool);
                    return null;

                default:
                    return Internal($"未知二元运算符 {op}。", span, source);
            }
        }

        private static PevtRuntimeDiagnostic Arithmetic(
            SyntaxKind op, PevtValue left, PevtValue right, TextSpan span, SourceText source, out PevtValue result)
        {
            result = default;

            if (left.Type == PevtType.Int && right.Type == PevtType.Int)
            {
                int a = left.AsInt;
                int b = right.AsInt;

                if ((op == SyntaxKind.SlashToken || op == SyntaxKind.PercentToken) && b == 0)
                    return Diagnostic("PEVTR2002", "`int` 除法或取余的右操作数为零。", span, source);

                try
                {
                    // 整数运算使用检查溢出语义，不进行静默回绕。
                    checked
                    {
                        switch (op)
                        {
                            case SyntaxKind.PlusToken: result = PevtValue.FromInt(a + b); return null;
                            case SyntaxKind.MinusToken: result = PevtValue.FromInt(a - b); return null;
                            case SyntaxKind.StarToken: result = PevtValue.FromInt(a * b); return null;
                            case SyntaxKind.SlashToken: result = PevtValue.FromInt(a / b); return null;
                            case SyntaxKind.PercentToken: result = PevtValue.FromInt(a % b); return null;
                        }
                    }
                }
                catch (OverflowException)
                {
                    return Diagnostic("PEVTR2001", "`int` 运算结果超出 32 位有符号整数范围。", span, source);
                }

                return Internal($"未知整数运算 {op}。", span, source);
            }

            if (left.Type == PevtType.Float && right.Type == PevtType.Float)
            {
                float a = left.AsFloat;
                float b = right.AsFloat;

                if ((op == SyntaxKind.SlashToken || op == SyntaxKind.PercentToken) && b == 0f)
                    return Diagnostic("PEVTR2002", "`float` 除法或取余的右操作数为零。", span, source);

                float value;
                switch (op)
                {
                    case SyntaxKind.PlusToken: value = a + b; break;
                    case SyntaxKind.MinusToken: value = a - b; break;
                    case SyntaxKind.StarToken: value = a * b; break;
                    case SyntaxKind.SlashToken: value = a / b; break;
                    case SyntaxKind.PercentToken: value = a % b; break;
                    default: return Internal($"未知浮点运算 {op}。", span, source);
                }

                // 下溢为零不算异常；只有 NaN 与无穷才是 PEVTR2003。
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return Diagnostic("PEVTR2003", "`float` 运算结果为 NaN 或无穷。", span, source);

                result = PevtValue.FromFloat(value);
                return null;
            }

            return Internal($"数学运算不支持 {left.Type.DisplayName()} 与 {right.Type.DisplayName()}。", span, source);
        }

        private static PevtRuntimeDiagnostic Compare(
            SyntaxKind op, PevtValue left, PevtValue right, TextSpan span, SourceText source, out PevtValue result)
        {
            result = default;
            int comparison;

            if (left.Type == PevtType.Int && right.Type == PevtType.Int)
                comparison = left.AsInt.CompareTo(right.AsInt);
            else if (left.Type == PevtType.Float && right.Type == PevtType.Float)
                comparison = left.AsFloat.CompareTo(right.AsFloat);
            else
                return Internal($"顺序比较不支持 {left.Type.DisplayName()} 与 {right.Type.DisplayName()}。", span, source);

            switch (op)
            {
                case SyntaxKind.LessThanToken: result = PevtValue.FromBool(comparison < 0); return null;
                case SyntaxKind.LessThanEqualsToken: result = PevtValue.FromBool(comparison <= 0); return null;
                case SyntaxKind.GreaterThanToken: result = PevtValue.FromBool(comparison > 0); return null;
                case SyntaxKind.GreaterThanEqualsToken: result = PevtValue.FromBool(comparison >= 0); return null;
                default: return Internal($"未知比较运算符 {op}。", span, source);
            }
        }

        private static PevtRuntimeDiagnostic Diagnostic(string id, string message, TextSpan span, SourceText source) =>
            new PevtRuntimeDiagnostic(id, message, Locate(span, source));

        private static PevtRuntimeDiagnostic Internal(string message, TextSpan span, SourceText source) =>
            new PevtRuntimeDiagnostic("PEVTR9001", message, Locate(span, source));

        private static TextLocation Locate(TextSpan span, SourceText source)
        {
            if (source == null)
                return null;
            int end = Math.Min(span.End, source.Length);
            int start = Math.Min(span.Start, end);
            return source.GetLocation(TextSpan.FromBounds(start, end));
        }
    }
}
