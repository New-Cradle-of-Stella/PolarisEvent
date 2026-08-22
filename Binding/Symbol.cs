namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 词法环境中一个已声明名称的公共基类（变量/常量/参数/自定义事件块/句柄）。
    /// 只承载绑定阶段需要的信息。
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
    /// 14 节自定义事件块名称本身的符号占位。有了它，"给块名赋值"之类的误用才能落到 PEVT6002，
    /// 而不是被误报成 PEVT6001 未定义变量。
    /// </summary>
    public sealed class BlockSymbol : Symbol
    {
        public BlockSymbol(string name) : base(name, PevtType.Void)
        {
        }
    }

    /// <summary>
    /// 15.2 节 <c>handler</c> 句柄名称的符号。<see cref="AsyncReturnType"/> 记录初始化器对应异步定义的普通返回值类型
    /// （null 表示无返回值或初始化器本身绑定失败），这是 <c>await</c> 确定表达式类型和识别 PEVT7211 唯一需要的信息。
    /// </summary>
    public sealed class HandlerSymbol : Symbol
    {
        public PevtType? AsyncReturnType { get; }

        public HandlerSymbol(string name, PevtType? asyncReturnType) : base(name, PevtType.Handler)
        {
            AsyncReturnType = asyncReturnType;
        }
    }

    /// <summary>
    /// PEVT-E05 <c>schedule</c> 的 timelineId 符号占位。和 <see cref="BlockSymbol"/> 一样只用于占住名字——
    /// timelineId 不像 <c>handler</c> 那样能被后续语句引用（<c>flush schedules</c>/<c>clear schedules</c>
    /// 一律作用于当前环境全部尚未触发的项），因此不需要携带额外信息。
    /// </summary>
    public sealed class ScheduleSymbol : Symbol
    {
        public ScheduleSymbol(string name) : base(name, PevtType.Void)
        {
        }
    }
}
