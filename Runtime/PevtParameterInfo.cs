using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Syntax;

namespace Polaris.Pevt.Runtime
{
    /// <summary>事件或自定义块的一个已编译形参，包括可选的编译期默认值。</summary>
    public sealed class PevtParameterInfo
    {
        public string Name { get; }
        public PevtType Type { get; }
        public bool HasDefaultValue { get; }
        public PevtValue DefaultValue { get; }

        public PevtParameterInfo(string name, PevtType type)
            : this(name, type, false, PevtValue.None)
        {
        }

        public PevtParameterInfo(string name, PevtType type, PevtValue defaultValue)
            : this(name, type, true, defaultValue)
        {
        }

        private PevtParameterInfo(string name, PevtType type, bool hasDefaultValue, PevtValue defaultValue)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            HasDefaultValue = hasDefaultValue;
            DefaultValue = defaultValue;
        }

        public override string ToString() => HasDefaultValue
            ? $"{Name} : {Type.DisplayName()} = {DefaultValue}"
            : $"{Name} : {Type.DisplayName()}";
    }

    internal static class PevtParameterDefaults
    {
        public static bool TryEvaluate(ExpressionSyntax expression, out PevtValue value)
        {
            value = PevtValue.None;
            if (expression == null)
                return false;

            switch (expression)
            {
                case LiteralExpressionSyntax literal:
                    return TryLiteral(literal.Token, out value);

                case ParenthesizedExpressionSyntax parenthesized:
                    return TryEvaluate(parenthesized.Inner, out value);

                case UnaryExpressionSyntax unary:
                    if (!TryEvaluate(unary.Operand, out PevtValue operand))
                        return false;
                    try
                    {
                        if (unary.OperatorToken.Kind == SyntaxKind.ExclamationToken && operand.Type == PevtType.Bool)
                        {
                            value = PevtValue.FromBool(!operand.AsBool);
                            return true;
                        }
                        if (unary.OperatorToken.Kind == SyntaxKind.MinusToken && operand.Type == PevtType.Int)
                        {
                            value = PevtValue.FromInt(checked(-operand.AsInt));
                            return true;
                        }
                        if (unary.OperatorToken.Kind == SyntaxKind.MinusToken && operand.Type == PevtType.Float)
                        {
                            float result = -operand.AsFloat;
                            if (!float.IsNaN(result) && !float.IsInfinity(result))
                            {
                                value = PevtValue.FromFloat(result);
                                return true;
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                    }
                    return false;

                case ChainedBinaryExpressionSyntax chain:
                    if (!TryEvaluate(chain.First, out value))
                        return false;
                    foreach (BinaryChainSegment segment in chain.Segments)
                    {
                        if (!TryEvaluate(segment.Operand, out PevtValue right))
                            return false;
                        try
                        {
                            if (PevtOperations.Evaluate(segment.OperatorToken.Kind, value, right, expression.Span, null, out PevtValue result) != null)
                                return false;
                            value = result;
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }
                    }
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryLiteral(SyntaxToken token, out PevtValue value)
        {
            value = PevtValue.None;
            switch (token.Value.Kind)
            {
                case TokenValueKind.Integer: value = PevtValue.FromInt(token.Value.AsInteger); return true;
                case TokenValueKind.Float: value = PevtValue.FromFloat(token.Value.AsFloat); return true;
                case TokenValueKind.Boolean: value = PevtValue.FromBool(token.Value.AsBoolean); return true;
                case TokenValueKind.Char: value = PevtValue.FromChar(token.Value.AsChar); return true;
                case TokenValueKind.String: value = PevtValue.FromString(token.Value.AsString); return true;
                default: return false;
            }
        }
    }

    internal static class PevtParameterBinding
    {
        public static int RequiredCount(IReadOnlyList<PevtParameterInfo> parameters)
        {
            int required = 0;
            while (required < parameters.Count && !parameters[required].HasDefaultValue)
                required++;
            return required;
        }

        public static bool TryBind(IReadOnlyList<PevtParameterInfo> parameters, PevtValue[] supplied,
            out PevtValue[] bound, out string error)
        {
            supplied = supplied ?? Array.Empty<PevtValue>();
            int required = RequiredCount(parameters);
            if (supplied.Length < required || supplied.Length > parameters.Count)
            {
                bound = null;
                error = $"提供了 {supplied.Length} 个实参，但签名要求 {required} 到 {parameters.Count} 个。";
                return false;
            }

            bound = new PevtValue[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                PevtParameterInfo parameter = parameters[i];
                PevtValue value = i < supplied.Length ? supplied[i] : parameter.DefaultValue;
                if (value.Type != parameter.Type)
                {
                    error = $"第 {i + 1} 个实参类型是 {value.Type.DisplayName()}，但形参 `{parameter.Name}` 声明为 {parameter.Type.DisplayName()}。";
                    bound = null;
                    return false;
                }
                bound[i] = value;
            }

            error = null;
            return true;
        }
    }
}
