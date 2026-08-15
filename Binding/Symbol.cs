namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 词法环境中一个已声明名称的公共基类（变量/常量/参数/自定义事件块/句柄，阶段 8 第一条要求）。
    /// 只承载绑定阶段现在就需要的信息；块的完整签名重载、句柄的专属规则都是阶段 9 的工作。
    /// </summary>
    public abstract class Symbol
    {
        public string Name { get; }
        public PevtType Type { get; }

        protected Symbol(string name, PevtType type)
        {
            Name = name;
            Type = type;
        }
    }

    /// <summary>9.1 节 <c>var</c>：可写，初始化状态由 <see cref="BoundEnvironment"/> 的流敏感状态单独跟踪，不放在符号本身上——同一个符号在不同分支路径上初始化与否并不相同。</summary>
    public sealed class VariableSymbol : Symbol
    {
        public VariableSymbol(string name, PevtType type) : base(name, type)
        {
        }
    }

    /// <summary>9.2 节 <c>const</c>：声明即初始化，此后只读；<see cref="BoundEnvironment"/> 一律把它当作已初始化处理。</summary>
    public sealed class ConstantSymbol : Symbol
    {
        public ConstantSymbol(string name, PevtType type) : base(name, type)
        {
        }
    }

    /// <summary>14.1 节自定义事件块形参：进入块体时已经定义且已经初始化（9.4 节）。</summary>
    public sealed class ParameterSymbol : Symbol
    {
        public ParameterSymbol(string name, PevtType type) : base(name, type)
        {
        }
    }

    /// <summary>
    /// 14 节自定义事件块名称本身的符号占位。真正的签名重载、返回路径约束和"定义先于调用"绑定是
    /// 阶段 9 的工作；阶段 8 只需要这个符号存在，好让"给块名赋值"之类的误用能落到某个符号种类上
    /// （PEVT6002，而不是被误报成 6001 未定义变量）。
    /// </summary>
    public sealed class BlockSymbol : Symbol
    {
        public BlockSymbol(string name) : base(name, PevtType.Void)
        {
        }
    }

    /// <summary>
    /// 15.2 节 <c>handler</c> 句柄名称的符号。<see cref="AsyncReturnType"/> 记录初始化器对应的异步
    /// 定义声明的普通返回值类型（null 表示无返回值，或初始化器本身已经因为别的原因绑定失败）——
    /// 这是 <c>await</c> 能确定自己表达式类型（进而支持 PEVT6008 类型核对）以及识别
    /// "无返回值却被当表达式使用"（PEVT7211）唯一需要的信息。
    /// </summary>
    public sealed class HandlerSymbol : Symbol
    {
        public PevtType? AsyncReturnType { get; }

        public HandlerSymbol(string name, PevtType? asyncReturnType) : base(name, PevtType.Handler)
        {
            AsyncReturnType = asyncReturnType;
        }
    }
}
