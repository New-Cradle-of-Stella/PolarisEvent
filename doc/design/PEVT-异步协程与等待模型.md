# PEVT 异步协程与等待模型

## 1. 定位

PEVT 中每个可跨帧的 `@` 组合实现为一个由 PolarisEvent 调度的 C# 协程。协程只产出 PolarisEvent 自己的 `PevtWait` 对象，不直接产出 Unity `YieldInstruction`、`CustomYieldInstruction` 或任意 `object`。

```text
同一个 @ 组合协程
    ├─ 普通 @name(...)       → 挂到当前 PEVT 流程，调用者等待完成
    └─ 异步 @name_start(...) → 交给子协程调度器，立即返回 handler
```

- 同步与异步变体必须复用同一个协程工厂和同一组 C# 原子方法。
- `_start` 只改变协程由谁驱动、调用者是否等待以及是否产生句柄，不改变业务步骤。
- PEVT 不允许脚本作者自定义 `PevtWait` 类型；等待类型由 PolarisEvent 或受信任领域扩展用 C# 登记。
- 全部协程在 Unity 主线程推进。后台 `Task` 只能通过受控桥接等待返回主线程。

## 2. 协程签名

组合处理器使用统一签名：

```csharp
public interface IPevtCommandRoutine
{
    IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments arguments);
}
```

`PevtRoutineContext` 至少提供：

| 成员 | 作用 |
| --- | --- |
| `Execution` | 当前 PEVT 执行实例及事件 ID。 |
| `SourceSpan` | 当前 `@` 调用的源码位置。 |
| `Services` | 对话、立绘、音频、地图等受控原子服务。 |
| `Result` | 设置整条 `@` 的普通 PEVT 返回值。 |
| `Cleanup` | 登记失败、取消或事件终止时要反向执行的临时清理。 |
| `Cancellation` | 当前协程的取消状态，只读。 |

C# 迭代器不能通过 `return value` 返回普通值，因此有值 `@` 必须在正常结束前调用 `context.Result.Set(...)`。

## 3. `PevtWait` 基类

```csharp
public abstract class PevtWait
{
    public PevtWaitState State { get; }
    public PevtRuntimeDiagnostic Error { get; }

    internal abstract void Tick(PevtWaitContext context);
    internal virtual void Cancel(PevtWaitContext context);
}

public abstract class PevtWait<T> : PevtWait
{
    public T Result { get; }
}
```

| 状态 | 含义 |
| --- | --- |
| `Created` | 等待对象已建立，尚未被调度器接管。 |
| `Pending` | 等待条件尚未满足。 |
| `Cancelling` | 已请求取消，正在等待底层对象、票据或回调确认停止。 |
| `Succeeded` | 正常完成；有值等待可以读取 `Result`。 |
| `Faulted` | 等待源报错，`Error` 保存对应运行诊断。 |
| `Cancelled` | 因 `kill`、事件终止或父级取消而停止。 |

规则：

- `Tick` 只能由 PolarisEvent 协程调度器调用。
- 每个 `PevtWait` 对象只能被一个活动协程持有，不能在两个协程之间复用。
- `Succeeded`、`Faulted` 和 `Cancelled` 是终态，不能再变回 `Pending` 或 `Cancelling`。
- `Cancel` 必须幂等；已处于终态时再次取消不产生副作用。
- 读取未 `Succeeded` 的 `PevtWait<T>.Result` 是 PolarisEvent 内部错误。
- 等待对象必须能说明自己的推进源，供 `PEVTR1002` 停滞检测使用。

## 4. 内置等待类型

| 等待类型 | 推进源 | 用途 |
| --- | --- | --- |
| `PevtNextFrameWait` | PolarisEvent 更新帧 | 至少让出一次调度，替代原版 `WAIT_1`。 |
| `PevtFrameWait` | PolarisEvent 更新帧 | 等待指定逻辑帧数。 |
| `PevtTimeWait` | 受控游戏时间或实时时钟 | 为非帧制资源和工具扩展提供时间等待。必须显式指定时钟类型。 |
| `PevtInputWait` | PolarisEvent 输入服务 | 等待确认、取消或指定受控按键，可返回输入结果。 |
| `PevtMotionWait` | 角色、图层或场景实体动作票据 | 等待一个或一组受管动作完成。 |
| `PevtResourceWait` | Polaris 资源加载票据 | 等待图像、音频、地图层或资源组。 |
| `PevtSignalWait<T>` | 已登记的一次性 C# 信号 | 等待 UI 关闭、选择结果、动画回调等事件。 |
| `PevtPredicateWait` | 受信任适配器的状态检查 | 轮询没有票据或信号接口的原版对象。不向 PEVT 脚本暴露任意谓词。 |
| `PevtUiWait<T>` | 受管 UI 会话 | 对话、选择、警告、教程和标题等待。 |
| `PevtMapWait` | 地图切换或刷新会话 | 等待地图加载、锚点放置和转场结束。 |
| `PevtAudioWait` | 音频加载或渐变票据 | 等待 BGM、环境声和音量渐变。 |
| `PevtTaskWait<T>` | 受控 `Task<T>` 与取消令牌 | 桥接真正的后台 I/O。完成后只能在 PolarisEvent 主线程消费结果。 |
| `PevtLegacyEvWait` | 独占的原版 EV 桥会话 | 专供 `$raw cmd` 等待临时原版事件结束。 |
| `PevtHandlerWait<T>` | 单个异步句柄 | 实现单句柄 `await`。 |
| `PevtAllHandlersWait` | 句柄列表 | 实现 `await all`。 |
| `PevtAnyHandlerWait` | 句柄列表 | 实现 `await any` 及其他句柄的取消。 |
| `PevtCancellationWait` | 一个或多个正在取消的协程 | 实现 `kill` 和级联取消的确认与超时。 |

领域扩展可以添加 `PevtWait` 子类，但必须完整实现超时、取消、错误转换、调试描述和推进源报告。

## 5. 协程实例

调度器中的每个活动协程由 `PevtCoroutine` 保存：

| 字段 | 作用 |
| --- | --- |
| `Id` | 运行期唯一且单调递增的协程 ID。 |
| `Owner` | 创建它的 PEVT 执行实例或父协程。 |
| `Enumerator` | `IPevtCommandRoutine.Run` 产生的 `IEnumerator<PevtWait>`。 |
| `CurrentWait` | 当前协程产出且尚未处理完成的等待对象。 |
| `State` | `Running`、`Waiting`、`Cancelling`、`Succeeded`、`Faulted` 或 `Cancelled`。 |
| `Result` | 协程成功后的可选普通 PEVT 值。 |
| `Error` | 协程失败或取消的运行诊断。 |
| `Cleanup` | 按后进先出顺序执行的临时清理动作。 |
| `Observed` | 失败是否已被 `await` 或其他运行时通道观察。 |

同步 `@` 调用也可以使用 `PevtCoroutine`，但它作为当前 PEVT 指令帧的子帧存在，不登记公开 `handler`。

## 6. 调度算法

每个 PolarisEvent 更新帧按协程 `Id` 升序推进，保证重现性。

1. 若 `CurrentWait` 处于 `Pending`，调用其 `Tick`。
2. 仍为 `Pending` 时停止推进该协程，继续下一个协程。
3. `CurrentWait` 变为 `Cancelling` 时不再进入 `MoveNext()`，只继续推进取消；变为 `Faulted` 或 `Cancelled` 时，将协程转入相同终态并清理。
4. `CurrentWait` 变为 `Succeeded` 时，清除它并调用 `Enumerator.MoveNext()`。
5. 协程产出新 `PevtWait` 时，验证所有权并将其设为 `CurrentWait`。
6. `MoveNext()` 返回 `false` 时，验证普通返值契约，然后将协程设为 `Succeeded`。
7. 无等待的连续原子步骤可在同一帧继续执行，但必须受每帧步数预算限制。预算用完时内部产生 `PevtNextFrameWait`，不报错。

协程产出 `null`、产出已归属其他协程的等待对象，或在返回类型不符时结束，均使用 `PEVTR9001`。

## 7. 同步与异步注册

可并行 API 不实现两份处理器，而是登记一个共享协程工厂和两种调用模式：

```csharp
registry.RegisterPair(
    syncName: "actor_move",
    asyncName: "actor_move_start",
    signature: PevtSignature.Void(String, String, Int),
    routine: ActorMoveRoutine.Instance);
```

| 模式 | 行为 |
| --- | --- |
| `Attached` | 普通 `@actor_move(...)`。协程附着当前 PEVT 流程，完成前不推进下一条语句。 |
| `DetachedOwned` | 异步 `@actor_move_start(...)`。协程交给当前事件的子调度器，调用位置立即获得句柄并继续。 |

异步协程即使在创建当帧就完成，也会产生一个有效句柄；该句柄的 `status` 立即为 `1`。

异步调用作为独立事件语句且丢弃句柄时，调度器仍保留该协程的内部记录、所有权和失败状态，直到它结束并完成事件级清理。

## 8. 句柄模型

PEVT 中的 `handler` 对应只读 `PevtHandler` 引用：

| 字段 | 含义 |
| --- | --- |
| `CoroutineId` | 被跟踪的协程 ID。 |
| `OwnerExecutionId` | 拥有该协程的 PEVT 执行实例。 |
| `ExpectedResultType` | 可选的普通 PEVT 返回类型。 |
| `Observed` | 是否已通过等待读取结果或异常。 |

`handler` 本身不保存可变运行状态的副本；`status`、`await` 和 `kill` 始终按 `CoroutineId` 查询调度器中的唯一状态。

| PEVT `status` | 协程状态 |
| --- | --- |
| `0` | `Running`、`Waiting` 或 `Cancelling` |
| `1` | `Succeeded` |
| `2` | `Faulted` 或 `Cancelled` |

## 9. `await`、`all` 与 `any`

- 单句柄 `await` 创建 `PevtHandlerWait<T>`。
- 目标成功时，等待对象返回协程的普通值；目标失败或取消时产生 `PEVTR5001`。
- `await all` 创建 `PevtAllHandlersWait`，它只在全部目标进入终态后完成，结果为成功句柄数量。
- `await any` 创建 `PevtAnyHandlerWait`，每帧按输入列表顺序检查。第一个成功句柄确定后，固定其返回序号，并请求取消其他尚未结束的句柄。
- `PevtAnyHandlerWait` 必须等待上述取消全部进入终态后才返回，避免未停止的动作继续泄漏到后续演出。
- 同一帧多个句柄成功时，`await any` 使用输入列表中序号最小的句柄。
- 全部句柄失败或取消时，`await any` 返回 `0`。
- 结果绑定只在集合等待完成后统一提交；失败句柄对应的新变量保持未初始化。

## 10. 取消与清理

`kill` 或事件级联清理一个协程时：

1. 将协程设为 `Cancelling`，防止再次进入 `MoveNext()`。
2. 对 `CurrentWait` 调用幂等 `Cancel`。底层可立即进入 `Cancelled`，也可进入 `Cancelling` 并等待确认。
3. 底层取消尚未确认时，调度器继续 `Tick(CurrentWait)`，但不再推进协程业务代码。
4. `CurrentWait` 进入终态后，对协程迭代器调用 `Dispose()`，使 C# `finally` 块正常执行。
5. 按后进先出顺序执行 `PevtRoutineContext.Cleanup` 中的临时清理。
6. 将协程设为 `Cancelled`，句柄 `status` 为 `2`。

如果等待源不能取消，调度器在配置的取消宽限内继续轮询；超时后产生 `PEVTR5003`，强制断开该等待源，然后仍必须执行迭代器 `Dispose()` 和显式清理。

`kill` 语句内部使用 `PevtCancellationWait`，因此在目标真正停止或取消超时之前，当前 PEVT 同步流程不会推进下一条语句。
对已经 `Succeeded`、`Faulted` 或 `Cancelled` 的目标执行 `kill` 时，`PevtCancellationWait` 立即成功，不修改原有结果或异常。

父 PEVT 事件执行 `end`、被替换、异常结束或卸载时，按协程 ID 逆序取消它拥有的全部未结束子协程。

## 11. 异常与返回值

- `PevtWait` 进入 `Faulted` 时，其运行诊断直接成为当前协程的原始异常。
- `MoveNext`、`Tick`、`Cancel` 或 `Dispose` 抛出的 C# 异常必须在调度器边界捕获，并转换为对应 `PEVTR4xxx`、`PEVTR5xxx` 或 `PEVTR9001`。
- 同步附着协程失败时，异常立即终止当前 PEVT 同步流程。
- 异步拥有协程失败时，异常保存在协程和句柄状态中，不回溯至启动语句。
- 声明普通返值的协程正常结束时没有设置结果，或结果类型不符，使用 `PEVTR5002`。
- 异步失败在事件结束前未被观察时，产生 `PEVTR5005` 警告。

## 12. 协程组合示例

角色入场：

```csharp
public IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments args)
{
    string actor = args.String(0);
    string position = args.String(1);
    string appearance = args.String(2);
    int frames = args.Int(3);

    yield return context.Services.Resources.RequirePortrait(actor, appearance);
    context.Services.Portrait.SetAppearance(actor, appearance);
    yield return context.Services.Portrait.Place(actor, position, frames);
}
```

简单选择：

```csharp
public IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments args)
{
    context.Services.Choice.Reset();
    context.Services.Choice.Begin(args.String(0));

    for (int i = 1; i < args.Count; i++)
        context.Services.Choice.AddIndex(args.String(i));

    PevtUiWait<int> wait = context.Services.Choice.PresentIndex();
    yield return wait;
    context.Result.Set(PevtValue.FromInt(wait.Result));
    context.Services.Choice.Reset();
}
```

临时状态必须使用 `try/finally` 或 `context.Cleanup` 保证在成功、失败和取消时都能释放。

## 13. 禁止的实现方式

- 不允许同步版和 `_start` 版各自复制一份业务代码。
- 不允许将 Unity `Coroutine` 返回的不透明句柄直接存入 PEVT `handler`。
- 不允许产出任意 Unity yield 对象；全部等待都必须是可诊断、可取消、可测试的 `PevtWait`。
- 不允许在后台线程读写 Unity 对象或游戏状态。
- 不允许 `kill` 只改句柄状态而不停止底层等待和副作用。
- 不允许用一个包含字符串 `kind` 的万能等待类型代替明确的子类和契约。
