using System;
using System.Collections.Generic;
using System.Linq;

namespace Polaris.Pevt.Diagnostics
{
    /// <summary>
    /// PEVTRxxxx 运行诊断编号的唯一集中目录，逐条对应 PEVT-运行诊断表.md。
    /// </summary>
    public static class RuntimeDiagnosticCatalog
    {
        private static readonly DiagnosticDescriptor[] Entries =
        {
            new DiagnosticDescriptor("PEVTR1001", "ExecutionBudgetExceeded", DiagnosticSeverity.Error, "`while`、向后 `goto` 或其他控制流执行超过 PolarisEvent 配置的最大执行步数。"),
            new DiagnosticDescriptor("PEVTR1002", "ExecutionStalled", DiagnosticSeverity.Error, "事件处于等待状态，但没有异步操作、计时器、外部回调或其他可继续推动当前流程的运行源。"),
            new DiagnosticDescriptor("PEVTR1003", "EventCallDepthExceeded", DiagnosticSeverity.Error, "同步 `callevt` 形成的嵌套事件调用超过 PolarisEvent 配置的最大调用深度。"),
            new DiagnosticDescriptor("PEVTR1101", "EventCleanupFailed", DiagnosticSeverity.Error, "事件结束、被替换或异常退出时，PolarisEvent 无法完整停止或清理其拥有的异步操作、特效、UI 或回调。"),
            new DiagnosticDescriptor("PEVTR1201", "DynamicSourceInvalid", DiagnosticSeverity.Error, "`exec` 的字符串在运行时不能通过 PEVT 片段的词法、语法、类型、名称、能力或受限控制流校验。内部原因应保留对应的 `PEVTxxxx` 静态诊断。"),
            new DiagnosticDescriptor("PEVTR1202", "ForbiddenDynamicStatement", DiagnosticSeverity.Error, "`exec` 片段包含 `id`、`enable`、`block`、`endblock`、`return`、`end`、标签或 `goto` 等禁止动态执行的语句。"),
            new DiagnosticDescriptor("PEVTR1203", "DynamicExecutionDepthExceeded", DiagnosticSeverity.Error, "嵌套 `exec` 调用超过 PolarisEvent 配置的最大动态执行深度。"),

            new DiagnosticDescriptor("PEVTR2001", "IntegerOverflow", DiagnosticSeverity.Error, "`int` 加法、减法、乘法、除法、取余或一元取负的结果超出标准 32 位有符号整数范围。整数运算使用检查溢出语义，不进行静默回绕。"),
            new DiagnosticDescriptor("PEVTR2002", "DivideByZero", DiagnosticSeverity.Error, "`int` 或 `float` 使用 `/` 或 `%` 时，右操作数为零。"),
            new DiagnosticDescriptor("PEVTR2003", "NonFiniteFloatResult", DiagnosticSeverity.Error, "`float` 运算结果为 `NaN`、正无穷或负无穷。浮点下溢为零不属于该异常。"),

            new DiagnosticDescriptor("PEVTR3001", "RepeatedDeclarationExecution", DiagnosticSeverity.Error, "同一次文件外层事件或自定义事件块调用的运行环境中，控制流再次执行一个已经执行过的变量或常量声明。"),
            new DiagnosticDescriptor("PEVTR3002", "UninitializedRuntimeValue", DiagnosticSeverity.Error, "运行时读取尚未初始化的值，包括异步异常、取消、集合等待失败或动态 PEVT 片段读取未初始化外层变量的情况。"),
            new DiagnosticDescriptor("PEVTR3003", "NullRuntimeValue", DiagnosticSeverity.Error, "内置指令、C# 回调或 `$raw cs` 向不支持 `null` 的 PEVT 普通类型返回空值。"),

            new DiagnosticDescriptor("PEVTR4001", "BuiltinExecutionFailed", DiagnosticSeverity.Error, "`@` 内置事件语句执行时发生无法作为正常结果返回的游戏状态、资源、演出对象或底层引擎错误。"),
            new DiagnosticDescriptor("PEVTR4002", "BuiltinContractViolation", DiagnosticSeverity.Error, "`@` 内置事件语句的实际返回行为不符合 API 表登记的返回值类型或纯调用约定。"),
            new DiagnosticDescriptor("PEVTR4101", "RawCommandFailed", DiagnosticSeverity.Error, "`$raw cmd` 提交给原始游戏 DSL 后解析失败、执行失败或被原始事件运行时拒绝。"),
            new DiagnosticDescriptor("PEVTR4102", "RawCSharpException", DiagnosticSeverity.Error, "`$raw cs` 中的 C# 代码在执行时抛出未在该原始代码块内处理的异常。"),
            new DiagnosticDescriptor("PEVTR4201", "CallbackException", DiagnosticSeverity.Error, "PEVT 调用的自定义 C# 回调抛出未处理异常。"),
            new DiagnosticDescriptor("PEVTR4301", "EventCallTargetNotFound", DiagnosticSeverity.Error, "执行 `callevt` 时，当前 PolarisEvent 运行时注册表中没有对应的事件 ID。"),
            new DiagnosticDescriptor("PEVTR4302", "AmbiguousEventCallTarget", DiagnosticSeverity.Error, "多个已加载事件注册了相同 ID，且运行时无法依据模组覆盖或优先级规则得到唯一目标。"),
            new DiagnosticDescriptor("PEVTR4303", "EventCallTargetNotAsync", DiagnosticSeverity.Error, "`handler 名称 = callevt \"ID\"` 要求异步调用，但运行时解析出的目标事件没有声明 `enable async`。"),
            new DiagnosticDescriptor("PEVTR4304", "EventCallStartFailed", DiagnosticSeverity.Error, "目标事件已经成功解析，但 PolarisEvent 无法创建或启动对应的同步或异步事件执行实例。"),
            new DiagnosticDescriptor("PEVTR4401", "ActorNotFound", DiagnosticSeverity.Error, "执行人物相关 `@` 指令时，全局人物目录中不存在指定的可读人物 ID。"),
            new DiagnosticDescriptor("PEVTR4402", "ActorVisualNotFound", DiagnosticSeverity.Error, "人物存在，但请求的 portrait、appearance、world sprite 或人物专用 anchor 没有登记。"),
            new DiagnosticDescriptor("PEVTR4403", "ActorResourceFailed", DiagnosticSeverity.Error, "人物视觉引用的原版 PXLS 或 PolarisRes 资源不存在、加载失败或在等待上限内未就绪。"),
            new DiagnosticDescriptor("PEVTR4404", "ActorCatalogConflict", DiagnosticSeverity.Error, "多个已加载程序集注册相同最终人物 ID，人物目录无法得到唯一目标。"),
            new DiagnosticDescriptor("PEVTR4405", "ActorVisualContractViolation", DiagnosticSeverity.Error, "已登记视觉的实际资源类型、PXLS pose/frame 或生成注册契约与人物目录声明不一致。"),
            new DiagnosticDescriptor("PEVTR4501", "GameQueryKeyNotFound", DiagnosticSeverity.Error, "执行 `@game_read_*` 时，宿主的只读查询表中没有登记指定的键，或当前运行时根本没有接查询表。"),
            new DiagnosticDescriptor("PEVTR4502", "GameQueryArgumentsInvalid", DiagnosticSeverity.Error, "只读查询键存在，但给定的查询参数数量、取值或格式不满足该键的要求。"),
            new DiagnosticDescriptor("PEVTR4503", "GameQueryResultNotConvertible", DiagnosticSeverity.Error, "只读查询成功回报了结果，但该结果无法转换成调用点要求的 PEVT 普通类型。"),
            new DiagnosticDescriptor("PEVTR4601", "CameraTargetNotFound", DiagnosticSeverity.Error, "`@camera_move` 的 `entity:`、`anchor:` 或裸标签点目标在当前地图上不存在。"),
            new DiagnosticDescriptor("PEVTR4602", "CameraTargetLost", DiagnosticSeverity.Error, "镜头跟随的地图实体在动作进行中消失，镜头动作无法按目标完成。"),

            new DiagnosticDescriptor("PEVTR5001", "AwaitFailedOperation", DiagnosticSeverity.Error, "对异常结束或已经被 `kill` 的句柄执行单句柄 `await`。原异步异常应作为该异常的内部原因保留。"),
            new DiagnosticDescriptor("PEVTR5002", "AsyncResultContractViolation", DiagnosticSeverity.Error, "异步操作报告正常完成，但没有提供其定义签名规定的返回值，或实际返回值类型不正确。"),
            new DiagnosticDescriptor("PEVTR5003", "AsyncCancellationFailed", DiagnosticSeverity.Error, "PolarisEvent 要求停止异步操作，但该操作无法取消、拒绝取消或未能在规定时间内完成取消。"),
            new DiagnosticDescriptor("PEVTR5004", "AsyncSchedulerFailure", DiagnosticSeverity.Error, "PolarisEvent 无法创建、登记、恢复或调度一个异步操作。"),
            new DiagnosticDescriptor("PEVTR5005", "UnobservedAsyncFailure", DiagnosticSeverity.Warning, "没有保存句柄，或句柄在事件结束前始终没有被 `await`，而对应异步操作已经异常结束。该诊断不反向中断已经继续执行的事件。"),

            new DiagnosticDescriptor("PEVTR9001", "RuntimeInternalError", DiagnosticSeverity.Error, "解释器状态损坏、非法内部指令、调度器不变量被破坏，或进入理论上不可到达的运行状态。"),
        };

        public static IReadOnlyList<DiagnosticDescriptor> All { get; } = Array.AsReadOnly(Entries);

        private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ById =
            Entries.ToDictionary(entry => entry.Id);

        public static DiagnosticDescriptor Find(string id) => ById.TryGetValue(id ?? string.Empty, out var d) ? d : null;

        public static bool TryFind(string id, out DiagnosticDescriptor descriptor) =>
            ById.TryGetValue(id ?? string.Empty, out descriptor);

        /// <summary>目录里没有这个编号时抛出——运行时不允许临时另造编号。</summary>
        public static DiagnosticDescriptor Require(string id) =>
            Find(id) ?? throw new ArgumentException($"未知运行诊断编号: {id}", nameof(id));
    }
}
