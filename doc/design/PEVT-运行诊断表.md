# PEVT 运行诊断表（草案）

## 1. 编号规则

- PEVT 运行时诊断统一使用大写前缀 `PEVTR`。
- 编号格式为 `PEVTRxxxx`，其中 `xxxx` 是四位十进制数字。
- `PEVTR` 与加载时静态诊断使用的 `PEVTxxxx` 相互独立。
- 已经分配的编号不得改变含义或重新用于其他诊断。

| 范围 | 分类 |
| --- | --- |
| `PEVTR1xxx` | 执行、动态解释、控制流与生命周期 |
| `PEVTR2xxx` | 数值与运算 |
| `PEVTR3xxx` | 变量与运行环境 |
| `PEVTR4xxx` | 内置指令、原始调用、回调与事件间调用 |
| `PEVTR5xxx` | 异步操作与句柄 |
| `PEVTR9xxx` | PolarisEvent 内部错误 |

## 2. 执行、控制流与生命周期

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR1001` | `ExecutionBudgetExceeded` | Runtime Error | `while`、向后 `goto` 或其他控制流执行超过 PolarisEvent 配置的最大执行步数。 |
| `PEVTR1002` | `ExecutionStalled` | Runtime Error | 事件处于等待状态，但没有异步操作、计时器、外部回调或其他可继续推动当前流程的运行源。 |
| `PEVTR1003` | `EventCallDepthExceeded` | Runtime Error | 同步 `callevt` 形成的嵌套事件调用超过 PolarisEvent 配置的最大调用深度。 |
| `PEVTR1101` | `EventCleanupFailed` | Runtime Error | 事件结束、被替换或异常退出时，PolarisEvent 无法完整停止或清理其拥有的异步操作、特效、UI 或回调。 |
| `PEVTR1201` | `DynamicSourceInvalid` | Runtime Error | `exec` 的字符串在运行时不能通过 PEVT 片段的词法、语法、类型、名称、能力或受限控制流校验。内部原因应保留对应的 `PEVTxxxx` 静态诊断。 |
| `PEVTR1202` | `ForbiddenDynamicStatement` | Runtime Error | `exec` 片段包含 `id`、`enable`、`block`、`endblock`、`return`、`end`、标签或 `goto` 等禁止动态执行的语句。 |
| `PEVTR1203` | `DynamicExecutionDepthExceeded` | Runtime Error | 嵌套 `exec` 调用超过 PolarisEvent 配置的最大动态执行深度。 |

## 3. 数值与运算

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR2001` | `IntegerOverflow` | Runtime Error | `int` 加法、减法、乘法、除法、取余或一元取负的结果超出标准 32 位有符号整数范围。整数运算使用检查溢出语义，不进行静默回绕。 |
| `PEVTR2002` | `DivideByZero` | Runtime Error | `int` 或 `float` 使用 `/` 或 `%` 时，右操作数为零。 |
| `PEVTR2003` | `NonFiniteFloatResult` | Runtime Error | `float` 运算结果为 `NaN`、正无穷或负无穷。浮点下溢为零不属于该异常。 |

## 4. 变量与运行环境

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR3001` | `RepeatedDeclarationExecution` | Runtime Error | 同一次文件外层事件或自定义事件块调用的运行环境中，控制流再次执行一个已经执行过的变量或常量声明。 |
| `PEVTR3002` | `UninitializedRuntimeValue` | Runtime Error | 运行时读取尚未初始化的值，包括异步异常、取消、集合等待失败或动态 PEVT 片段读取未初始化外层变量的情况。 |
| `PEVTR3003` | `NullRuntimeValue` | Runtime Error | 内置指令、C# 回调或 `$raw cs` 向不支持 `null` 的 PEVT 普通类型返回空值。 |

## 5. 内置指令、原始调用与回调

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR4001` | `BuiltinExecutionFailed` | Runtime Error | `@` 内置事件语句执行时发生无法作为正常结果返回的游戏状态、资源、演出对象或底层引擎错误。 |
| `PEVTR4002` | `BuiltinContractViolation` | Runtime Error | `@` 内置事件语句的实际返回行为不符合 API 表登记的返回值类型或纯调用约定。 |
| `PEVTR4101` | `RawCommandFailed` | Runtime Error | `$raw cmd` 提交给原始游戏 DSL 后解析失败、执行失败或被原始事件运行时拒绝。 |
| `PEVTR4102` | `RawCSharpException` | Runtime Error | `$raw cs` 中的 C# 代码在执行时抛出未在该原始代码块内处理的异常。 |
| `PEVTR4201` | `CallbackException` | Runtime Error | PEVT 调用的自定义 C# 回调抛出未处理异常。 |
| `PEVTR4301` | `EventCallTargetNotFound` | Runtime Error | 执行 `callevt` 时，当前 PolarisEvent 运行时注册表中没有对应的事件 ID。 |
| `PEVTR4302` | `AmbiguousEventCallTarget` | Runtime Error | 多个已加载事件注册了相同 ID，且运行时无法依据模组覆盖或优先级规则得到唯一目标。 |
| `PEVTR4303` | `EventCallTargetNotAsync` | Runtime Error | `handler 名称 = callevt "ID"` 要求异步调用，但运行时解析出的目标事件没有声明 `enable async`。 |
| `PEVTR4304` | `EventCallStartFailed` | Runtime Error | 目标事件已经成功解析，但 PolarisEvent 无法创建或启动对应的同步或异步事件执行实例。 |
| `PEVTR4401` | `ActorNotFound` | Runtime Error | 执行人物相关 `@` 指令时，全局人物目录中不存在指定的可读人物 ID。 |
| `PEVTR4402` | `ActorVisualNotFound` | Runtime Error | 人物存在，但请求的 portrait、appearance、world sprite 或人物专用 anchor 没有登记。 |
| `PEVTR4403` | `ActorResourceFailed` | Runtime Error | 人物视觉引用的原版 PXLS 或 PolarisRes 资源不存在、加载失败或在等待上限内未就绪。 |
| `PEVTR4404` | `ActorCatalogConflict` | Runtime Error | 多个已加载程序集注册相同最终人物 ID，人物目录无法得到唯一目标。 |
| `PEVTR4405` | `ActorVisualContractViolation` | Runtime Error | 已登记视觉的实际资源类型、PXLS pose/frame 或生成注册契约与人物目录声明不一致。 |

## 6. 异步操作与句柄

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR5001` | `AwaitFailedOperation` | Runtime Error | 对异常结束或已经被 `kill` 的句柄执行单句柄 `await`。原异步异常应作为该异常的内部原因保留。 |
| `PEVTR5002` | `AsyncResultContractViolation` | Runtime Error | 异步操作报告正常完成，但没有提供其定义签名规定的返回值，或实际返回值类型不正确。 |
| `PEVTR5003` | `AsyncCancellationFailed` | Runtime Error | PolarisEvent 要求停止异步操作，但该操作无法取消、拒绝取消或未能在规定时间内完成取消。 |
| `PEVTR5004` | `AsyncSchedulerFailure` | Runtime Error | PolarisEvent 无法创建、登记、恢复或调度一个异步操作。 |
| `PEVTR5005` | `UnobservedAsyncFailure` | Runtime Warning | 没有保存句柄，或句柄在事件结束前始终没有被 `await`，而对应异步操作已经异常结束。该诊断不反向中断已经继续执行的事件。 |

## 7. PolarisEvent 内部错误

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVTR9001` | `RuntimeInternalError` | Runtime Error | 解释器状态损坏、非法内部指令、调度器不变量被破坏，或进入理论上不可到达的运行状态。 |

## 8. 传播与清理规则

### 8.1 同步异常

- 文件外层事件的同步操作发生未处理运行时异常时，当前事件立即进入异常终止流程。
- 同步自定义事件块中的异常沿调用关系向外传播，直到被 PolarisEvent 作为当前事件的未处理异常接收。
- 事件异常终止后，PolarisEvent 必须对该事件拥有且尚未结束的全部异步操作执行清理和 `kill`。

### 8.2 异步异常

- 异步操作内部发生异常时，不立即在启动该操作的调用位置抛出。
- PolarisEvent 将原始异常保存到对应句柄，并将句柄状态设为异常结束；此后 `status` 返回 `2`。
- 对该句柄执行单句柄 `await` 时，抛出 `PEVTR5001`，并保留原始异常作为内部原因。
- `await all` 和 `await any` 将成员操作的正常结束和异常结束都视为已经结束，不因单个成员异常直接抛出异常。
- 集合等待中异常结束的句柄不初始化对应结果变量；之后读取该变量时抛出 `PEVTR3002`。
- 未被观察的异步异常产生 `PEVTR5005`，但不反向终止已经继续运行的调用者流程。

### 8.3 `kill`

- `kill` 是正常的异步控制操作，本身不视为异常。
- 对已经成功完成、异常结束或已经被停止的句柄再次执行 `kill` 是幂等操作，不产生运行时异常。
- 只有底层异步操作无法按要求取消时，才产生 `PEVTR5003`。

### 8.4 清理异常

- 如果事件因为其他异常进入清理流程，而清理期间又发生异常，最初异常保持为主异常。
- 清理期间产生的 `PEVTR1101` 或 `PEVTR5003` 作为附加异常记录，不得覆盖最初异常。
- 如果事件原本正常结束但清理失败，则 `PEVTR1101` 成为该事件的主运行时异常。

### 8.5 事件间调用

- `callevt` 的目标存在性和 `enable async` 标记只在执行到调用语句时解析，加载时静态分析不提前验证。
- 找不到目标时抛出 `PEVTR4301`；目标不唯一时抛出 `PEVTR4302`。
- 独立的 `callevt` 根据目标的 `enable async` 标记自动选择同步等待或异步启动。
- `handler 名称 = callevt "ID"` 明确要求异步目标；目标没有声明 `enable async` 时抛出 `PEVTR4303`，句柄声明不完成初始化。
- 同步目标发生未处理异常时，保留原始诊断编号并沿事件调用栈传播，不额外替换为通用事件调用异常。
- 异步目标发生未处理异常时，原始异常保存到调用产生的句柄，之后遵守普通异步异常传播规则。
- 异步事件调用产生的句柄不包含普通返回值。
- 同步事件调用深度超过运行时限制时抛出 `PEVTR1003`，避免事件递归最终表现为宿主语言栈溢出。

### 8.6 动态 PEVT 执行

- `exec` 参数表达式先按普通 PEVT 规则求值；求值失败时保留并传播原运行时异常。
- 动态片段校验失败时抛出 `PEVTR1201`，并把片段产生的一个或多个 `PEVTxxxx` 诊断作为内部原因记录。
- 片段使用禁止语句时抛出 `PEVTR1202`。
- 嵌套深度超过限制时抛出 `PEVTR1203`；动态片段的普通语句数量同时计入 `PEVTR1001` 的统一执行预算。
- 片段开始执行后，其中普通语句产生的运行时异常保留原编号并同步传播，不替换成通用 `exec` 异常。
- 动态片段异常退出时销毁其局部环境；已经启动的异步操作仍归当前事件所有，并进入当前事件的统一异常清理流程。

## 9. 诊断信息要求

每个运行时诊断至少应记录：

- 诊断编号和名称；
- 事件 ID；
- `.pevt` 源文件路径；
- 对应语句的行号和列号；
- 如果异常来自 `exec`，还应记录宿主 `exec` 位置、动态片段内行列以及经过安全截断的片段摘要；
- 当前自定义事件块调用栈；
- 相关内置指令、变量、句柄或原始调用名称；
- 原始异常和内部异常链；
- 异步异常涉及的句柄 ID。

## 10. 非异常结果

- 可以预期并允许事件脚本分支处理的结果，不应通过运行时异常表达。
- 资源是否存在、角色是否在场、用户是否完成选择等可恢复状态，应由相应 `@` 指令返回 `bool`、`int` 或其他明确状态值。
- 事件正常执行 `end`、自定义事件块正常 `return`、用户主动 `kill` 异步操作，以及 `await any` 因全部成员异常而返回 `0`，均不属于运行时异常。
