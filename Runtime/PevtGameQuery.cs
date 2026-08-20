using System;
using System.Collections.Generic;
using System.Globalization;

namespace Polaris.Pevt.Runtime
{
    // 本文件定义 PEVT-E01「任意游戏值只读查询」的中立服务边界。
    // 它刻意不是"求值任意表达式"的入口：调用方只能给出一个已登记的键和一串字符串参数，
    // 服务只能回报一个数值或一段文本，因此这里没有任何写入、反射、方法调用或对象引用的通道。

    /// <summary>一次只读查询回报的值形态。</summary>
    public enum PevtQueryValueKind
    {
        /// <summary>数值结果。游戏侧的数值查询表统一以 <c>double</c> 回报。</summary>
        Number,

        /// <summary>文本结果。</summary>
        Text,
    }

    /// <summary>
    /// 一次只读查询的原始结果。它是查询表自己的形态，不是 PEVT 普通类型——
    /// 转成 <c>int</c>/<c>float</c>/<c>bool</c>/<c>string</c> 是调用点的事，失败要产生明确诊断。
    /// </summary>
    public readonly struct PevtQueryValue
    {
        private readonly double _number;
        private readonly string _text;

        private PevtQueryValue(PevtQueryValueKind kind, double number, string text)
        {
            Kind = kind;
            _number = number;
            _text = text;
        }

        public PevtQueryValueKind Kind { get; }

        /// <summary>数值结果；文本结果时为 0。</summary>
        public double Number => _number;

        /// <summary>文本结果；数值结果时为 null。</summary>
        public string Text => _text;

        public static PevtQueryValue FromNumber(double number) =>
            new PevtQueryValue(PevtQueryValueKind.Number, number, null);

        /// <summary>null 文本不是合法查询结果——查询表回报 null 等于"这个键没有值"。</summary>
        public static PevtQueryValue FromText(string text) =>
            new PevtQueryValue(PevtQueryValueKind.Text, 0d, text ?? throw new ArgumentNullException(nameof(text)));

        /// <summary>F8 展示用的原始结果描述。</summary>
        public string Describe() =>
            Kind == PevtQueryValueKind.Number
                ? _number.ToString("R", CultureInfo.InvariantCulture)
                : "\"" + _text + "\"";

        public override string ToString() => Describe();
    }

    /// <summary>一次只读查询的解析结果。</summary>
    public enum PevtQueryStatus
    {
        Found,

        /// <summary>查询表里没有登记这个键 → PEVTR4501。</summary>
        UnknownKey,

        /// <summary>键存在，但给定参数不满足它的要求 → PEVTR4502。</summary>
        InvalidArguments,
    }

    /// <summary>
    /// 任意已登记游戏值的只读查询入口。
    /// 键名不由 PolarisEvent 白名单固定：宿主把自己的只读查询表包装进来，PEVT 就能读到其中任何一个键，
    /// 但读到的永远只是一个数值或一段文本。
    /// </summary>
    public interface IPevtGameQuery
    {
        /// <summary>
        /// 读取一个已登记的键。<paramref name="arguments"/> 只作为该键的查询参数，
        /// 不允许被拼接成一段完整表达式再求值。
        /// </summary>
        PevtQueryStatus TryRead(string key, IReadOnlyList<string> arguments, out PevtQueryValue value);
    }
}
