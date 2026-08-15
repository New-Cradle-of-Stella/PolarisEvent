using Polaris.Pevt.Syntax;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 8.2 节的五种普通类型，加上两个绑定阶段专用的辅助值：<see cref="Void"/>（无返回值调用/语句
    /// 结果，不是合法操作数）和 <see cref="Error"/>（某个子表达式已经报过错，绑定器据此避免连锁重复
    /// 报告——绝不是语言里的第六个"真"类型）。<see cref="Handler"/> 对应全局不变量里"句柄是运行时
    /// 专用包装，不进入普通类型系统"的那一类值；本阶段只声明这个占位，句柄自身的绑定规则留给阶段 9。
    /// </summary>
    public enum PevtType
    {
        Error,
        Void,
        Int,
        Float,
        Bool,
        Char,
        String,
        Handler,
    }

    public static class PevtTypeFacts
    {
        public static bool IsNumeric(this PevtType type) => type == PevtType.Int || type == PevtType.Float;

        /// <summary>五种普通类型之一，且不是本阶段绑定专用的 <see cref="PevtType.Error"/>/<see cref="PevtType.Void"/>/<see cref="PevtType.Handler"/>。</summary>
        public static bool IsOrdinaryType(this PevtType type) => type switch
        {
            PevtType.Int or PevtType.Float or PevtType.Bool or PevtType.Char or PevtType.String => true,
            _ => false,
        };

        public static string DisplayName(this PevtType type) => type switch
        {
            PevtType.Int => "int",
            PevtType.Float => "float",
            PevtType.Bool => "bool",
            PevtType.Char => "char",
            PevtType.String => "string",
            PevtType.Handler => "handler",
            PevtType.Void => "void",
            _ => "error",
        };

        /// <summary>9.1/9.2 节声明类型 token（<c>int</c>/<c>float</c>/.../<c>string</c>）到 <see cref="PevtType"/>
        /// 的映射；解析阶段已经保证这里只会看到五种类型关键字之一（见 <c>Parser.ParseTypeNameOrMissing</c>）。</summary>
        public static PevtType FromTypeKeyword(SyntaxKind kind) => kind switch
        {
            SyntaxKind.IntKeyword => PevtType.Int,
            SyntaxKind.FloatKeyword => PevtType.Float,
            SyntaxKind.BoolKeyword => PevtType.Bool,
            SyntaxKind.CharKeyword => PevtType.Char,
            SyntaxKind.StringKeyword => PevtType.String,
            _ => PevtType.Error,
        };
    }
}
