using System;
using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary><c>callevt</c> 解析目标事件的结果。</summary>
    public enum PevtSubEventStatus
    {
        Found,

        /// <summary>注册表里没有这个事件 ID → PEVTR4301。</summary>
        NotFound,

        /// <summary>多个来源注册了同一 ID 且无法确定唯一目标 → PEVTR4302。</summary>
        Ambiguous,

        /// <summary>目标存在但拿不到可执行程序（编译失败等）→ PEVTR4304。</summary>
        StartFailed,
    }

    /// <summary>
    /// <c>callevt</c> 的运行时目标解析。由宿主实现，因为"当前有哪些事件"是注册表的事实，
    /// 而解释器不认识注册表——目标不存在是运行错误，不是构建错误（全局不变量第 8 条）。
    /// </summary>
    public interface IPevtSubEventProvider
    {
        PevtSubEventStatus TryResolve(string eventId, out PevtCompiledProgram program, out bool declaresAsync);
    }

    public sealed partial class PevtExecution
    {
        /// <summary>嵌套 <c>exec</c> 的层数上限。超出报 PEVTR1203。</summary>
        public const int MaxDynamicDepth = 4;

        /// <summary>当前同步流程或指令帧正在等的等待，供子事件驱动查询推进源。</summary>
        internal PevtWait CurrentPendingWait => _pendingWait ?? _command?.CurrentWait;

        // ---- 同步流程的挂起等待 ----

        /// <summary>
        /// 让当前同步流程挂在一个等待上。与 <c>@</c> 指令帧不同，这类等待不属于任何组合协程
        /// （<c>await</c>、<c>kill</c> 和同步 <c>callevt</c> 都是语言构造），所以单独存一份。
        /// </summary>
        private PevtExecutionResult Suspend(PevtWait wait, Func<PevtExecutionResult> continuation)
        {
            wait.Attach();
            _pendingWait = wait;
            _pendingContinuation = continuation;
            return null;
        }

        private PevtExecutionResult AdvancePendingWait()
        {
            PevtWait wait = _pendingWait;
            var context = new PevtWaitContext(_services.Clock.Frame);

            try
            {
                wait.Tick(context);
            }
            catch (Exception ex)
            {
                _pendingWait = null;
                _pendingContinuation = null;
                return Fault(new PevtRuntimeDiagnostic("PEVTR5004",
                    $"推进等待 `{wait.ProgressSource}` 时发生内部错误：{ex.GetType().Name}: {ex.Message}",
                    CurrentLocation(), BuildCallStack(), innerException: ex));
            }

            switch (wait.State)
            {
                case PevtWaitState.Succeeded:
                {
                    Func<PevtExecutionResult> continuation = _pendingContinuation;
                    _pendingWait = null;
                    _pendingContinuation = null;
                    Budget.RecordWaitProgress();
                    return continuation();
                }

                case PevtWaitState.Faulted:
                {
                    PevtRuntimeDiagnostic error = wait.Error;
                    _pendingWait = null;
                    _pendingContinuation = null;
                    return Fault(error);
                }

                case PevtWaitState.Cancelled:
                {
                    _pendingWait = null;
                    _pendingContinuation = null;
                    return Fault(new PevtRuntimeDiagnostic("PEVTR5004",
                        $"等待 `{wait.ProgressSource}` 在同步流程仍需要它时被取消。", CurrentLocation(), BuildCallStack()));
                }

                default:
                    if (!wait.HasProgressSource)
                    {
                        _pendingWait = null;
                        _pendingContinuation = null;
                        return Fault("PEVTR1002", $"等待 `{wait.ProgressSource}` 没有推进源。", CurrentSpan());
                    }

                    if (!Budget.RecordNoProgress())
                        return Fault("PEVTR1002", $"事件 `{EventId}` 连续 {Budget.Limits.StallFrames} 帧没有任何进展。", CurrentSpan());

                    return new PevtExecutionResult(PevtExecutionStatus.Suspended, PevtSuspendReason.Wait);
            }
        }

        private void CancelPendingWait()
        {
            if (_pendingWait == null)
                return;

            try
            {
                _pendingWait.Cancel();
            }
            catch (Exception)
            {
                // 取消失败不阻止后续清理；具体诊断按 PEVTR5003 由子协程侧处理。
            }

            _pendingWait = null;
            _pendingContinuation = null;
        }

        /// <summary>把未被观察的异步失败记成 PEVTR5005 警告。不改变执行状态（第 11 节）。</summary>
        private void CollectUnobservedFailures()
        {
            foreach (PevtAsyncRoutine routine in _async.UnobservedFailures())
            {
                routine.Observed = true;
                _warnings.Add(new PevtRuntimeDiagnostic("PEVTR5005",
                    $"{routine.Description} 异常结束，但事件结束前始终没有被 await 观察。",
                    routine.Error?.Location, innerDiagnostic: routine.Error));
            }
        }

        // ---- 句柄 ----

        private bool TryGetRoutine(PevtFrame frame, string handlerName, out PevtAsyncRoutine routine)
        {
            routine = null;
            return frame.Environment.TryGetHandler(handlerName, out PevtHandlerValue handler)
                && _async.TryGet(handler.RoutineId, out routine);
        }

        private PevtExecutionResult ExecuteStatus(PevtFrame frame, PevtInstruction instruction)
        {
            if (!TryGetRoutine(frame, instruction.Name, out PevtAsyncRoutine routine))
                return Fault("PEVTR9001", $"句柄 `{instruction.Name}` 在当前环境中不存在。", instruction.Span);

            frame.EvalStack.Add(PevtValue.FromInt(routine.StatusCode));
            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteAwaitHandle(PevtFrame frame, PevtInstruction instruction)
        {
            if (!TryGetRoutine(frame, instruction.Name, out PevtAsyncRoutine routine))
                return Fault("PEVTR9001", $"句柄 `{instruction.Name}` 在当前环境中不存在。", instruction.Span);

            bool produceValue = instruction.Flag;
            var wait = new PevtHandlerWait(routine);

            return Suspend(wait, () =>
            {
                if (produceValue)
                {
                    if (!routine.HasResult)
                    {
                        return Fault(new PevtRuntimeDiagnostic("PEVTR5002",
                            $"{routine.Description} 正常结束，但没有提供签名要求的返回值。",
                            LocationOf(instruction.Span), BuildCallStack()));
                    }

                    frame.EvalStack.Add(wait.Result);
                }

                frame.Ip++;
                return null;
            });
        }

        private PevtExecutionResult ExecuteKill(PevtFrame frame, PevtInstruction instruction)
        {
            if (!TryGetRoutine(frame, instruction.Name, out PevtAsyncRoutine routine))
                return Fault("PEVTR9001", $"句柄 `{instruction.Name}` 在当前环境中不存在。", instruction.Span);

            return Suspend(new PevtCancellationWait(routine), () =>
            {
                frame.Ip++;
                return null;
            });
        }

        /// <summary>
        /// 集合等待。结果绑定只在整组等待完成后统一提交，而且只提交成功句柄的值——
        /// 失败句柄对应的变量保持未初始化，真读到它才报 PEVTR3002（第 9 节）。
        /// </summary>
        private PevtExecutionResult ExecuteAggregateAwait(PevtFrame frame, PevtInstruction instruction)
        {
            var routines = new List<PevtAsyncRoutine>();
            foreach (string handleName in instruction.Names)
            {
                if (!TryGetRoutine(frame, handleName, out PevtAsyncRoutine routine))
                    return Fault("PEVTR9001", $"句柄 `{handleName}` 在当前环境中不存在。", instruction.Span);
                routines.Add(routine);
            }

            bool isAny = instruction.OpCode == PevtOpCode.AwaitAny;
            PevtWait<int> wait = isAny
                ? (PevtWait<int>)new PevtAnyHandlersWait(routines)
                : new PevtAllHandlersWait(routines);

            IReadOnlyList<string> bindings = instruction.Bindings;

            return Suspend(wait, () =>
            {
                for (int i = 0; i < bindings.Count && i < routines.Count; i++)
                {
                    PevtAsyncRoutine routine = routines[i];

                    // 声明总是执行（变量存在），但只有成功且带值的句柄才写入初始值。
                    PevtType declaredType = routine.ExpectedResultType ?? PevtType.Int;
                    PevtSlot slot = frame.Environment.Declare(bindings[i], declaredType, PevtSlotKind.Variable);

                    if (routine.State == PevtAsyncState.Succeeded && routine.HasResult
                        && routine.Result.Type == declaredType)
                    {
                        slot.Set(routine.Result);
                    }
                }

                frame.EvalStack.Add(PevtValue.FromInt(wait.Result));
                frame.Ip++;
                return null;
            });
        }

        // ---- 异步启动 ----

        private PevtExecutionResult ExecuteCallAsync(PevtFrame frame, PevtInstruction instruction)
        {
            var arguments = new PevtValue[instruction.Index];
            for (int i = instruction.Index - 1; i >= 0; i--)
                arguments[i] = Pop(frame);

            PevtAsyncRoutine routine;
            if (instruction.Descriptor != null)
            {
                PevtRuntimeDiagnostic error = StartAsyncCommand(instruction, arguments, out routine);
                if (error != null)
                    return Fault(error);
            }
            else
            {
                PevtRuntimeDiagnostic error = StartAsyncBlock(instruction, arguments, out routine);
                if (error != null)
                    return Fault(error);
            }

            // 句柄名为空表示作者丢弃了句柄；协程仍归事件所有，失败也仍会记成 PEVTR5005。
            if (instruction.HandlerName != null)
            {
                frame.Environment.SetHandler(instruction.HandlerName,
                    new PevtHandlerValue(routine.Id, Id, routine.ExpectedResultType));
            }

            frame.Ip++;
            return null;
        }

        /// <summary>
        /// 异步 <c>@</c>：拿的是同步调用用的同一个描述条目与同一个处理器，只是交给子调度器驱动。
        /// 描述条目本身是 <c>_start</c> 变体，处理器登记在它派生自的同步条目上（第 7 节）。
        /// </summary>
        private PevtRuntimeDiagnostic StartAsyncCommand(PevtInstruction instruction, PevtValue[] arguments, out PevtAsyncRoutine routine)
        {
            routine = null;
            CommandDescriptor descriptor = instruction.Descriptor;
            CommandDescriptor routineSource = descriptor.ParallelSource ?? descriptor;

            if (_commands == null || !_commands.TryGetRoutine(routineSource, out IPevtCommandRoutine implementation))
            {
                return new PevtRuntimeDiagnostic("PEVTR4001", $"`@{descriptor.Name}` 没有登记处理器。",
                    LocationOf(instruction.Span), BuildCallStack());
            }

            var context = new PevtRoutineContext(Id, EventId, LocationOf(instruction.Span), _services);
            var frame = new PevtCommandFrame(descriptor, new PevtArguments(arguments), implementation, context, instruction.Span);

            PevtRuntimeDiagnostic startError = frame.Start();
            if (startError != null)
                return startError;

            routine = _async.Register("@" + descriptor.Name, descriptor.ReturnType, new CommandDriver(frame));
            return null;
        }

        /// <summary>
        /// <c>async block</c>：把块正文作为一个子执行实例跑起来，共享同一份编译产物、同一套服务
        /// 和同一份预算。块定义只有一份，同步调用与异步启动走的是同一段指令。
        /// </summary>
        private PevtRuntimeDiagnostic StartAsyncBlock(PevtInstruction instruction, PevtValue[] arguments, out PevtAsyncRoutine routine)
        {
            routine = null;

            if (!_program.TryGetBlock(instruction.Name, out PevtBlockInfo block))
            {
                return new PevtRuntimeDiagnostic("PEVTR9001", $"未定义的事件块 `{instruction.Name}`。",
                    LocationOf(instruction.Span), BuildCallStack());
            }

            var child = new PevtExecution(_program, _services, _commands, Budget, SubEvents, DynamicDepth, TotalDepth);

            PevtRuntimeDiagnostic error = child.EnterBlockAsRoot(block, arguments, instruction.Span);
            if (error != null)
                return error;

            routine = _async.Register("_" + block.Name, block.ReturnType, new ExecutionDriver(child, "_" + block.Name));
            return null;
        }

        /// <summary>
        /// 把最外层帧换成一个块帧。用于 <c>async block</c> 的子执行实例：它的"事件正文"就是块正文，
        /// 因此块的 <c>return</c> 直接成为整个子实例的结果。
        /// </summary>
        private PevtRuntimeDiagnostic EnterBlockAsRoot(PevtBlockInfo block, PevtValue[] arguments, TextSpan callSpan)
        {
            var environment = new PevtEnvironment(block.Name);
            for (int i = 0; i < block.Parameters.Count && i < arguments.Length; i++)
            {
                KeyValuePair<string, PevtType> parameter = block.Parameters[i];
                PevtSlot slot = environment.Declare(parameter.Key, parameter.Value, PevtSlotKind.Variable);
                slot.Set(arguments[i]);
            }

            _frames.Clear();
            _frames.Add(new PevtFrame(
                PevtFrameKind.Block, block.Name, environment,
                entryPoint: block.EntryPoint, returnIp: -1,
                producesValue: block.ReturnType.HasValue, returnType: block.ReturnType,
                callSpan: callSpan, switchSlotCount: _program.SwitchSlotCount));

            return null;
        }

        // ---- callevt ----

        /// <summary>
        /// <c>callevt</c>：运行时按 ID 查全局事件表，同步调用把当前流程挂在子事件上，
        /// 异步调用要求目标声明了 <c>enable async</c>（否则 PEVTR4303）并返回句柄。两种形式都进入同一棵所有权树与同一份预算。
        /// </summary>
        private PevtExecutionResult ExecuteCallEvent(PevtFrame frame, PevtInstruction instruction)
        {
            string eventId = instruction.Name ?? string.Empty;

            if (SubEvents == null)
            {
                return Fault("PEVTR4301",
                    $"`callevt \"{eventId}\"`：当前运行时没有事件注册表可查。", instruction.Span);
            }

            PevtSubEventStatus status = SubEvents.TryResolve(eventId, out PevtCompiledProgram program, out bool declaresAsync);
            switch (status)
            {
                case PevtSubEventStatus.NotFound:
                    return Fault("PEVTR4301", $"`/event/{eventId}.pevt` 不在当前运行时注册表中。", instruction.Span);
                case PevtSubEventStatus.Ambiguous:
                    return Fault("PEVTR4302", $"事件 ID `{eventId}` 有多个已加载来源，无法确定唯一目标。", instruction.Span);
                case PevtSubEventStatus.StartFailed:
                    return Fault("PEVTR4304", $"事件 `{eventId}` 已解析，但无法创建可执行实例。", instruction.Span);
            }

            bool wantsHandler = instruction.HandlerName != null;
            if (wantsHandler && !declaresAsync)
            {
                // 句柄声明不完成初始化：这里直接以 PEVTR4303 终止，不写入任何句柄。
                return Fault("PEVTR4303",
                    $"`handler {instruction.HandlerName} = callevt \"{eventId}\"` 要求异步目标，但该事件没有声明 `enable async`。",
                    instruction.Span);
            }

            if (!Budget.IsWithinCallDepth(TotalDepth + 1))
                return Fault("PEVTR1003", $"子事件调用深度超过上限 {Budget.Limits.MaxCallDepth}。", instruction.Span);

            PevtExecution child;
            try
            {
                child = new PevtExecution(program, _services, _commands, Budget, SubEvents, DynamicDepth, TotalDepth);
            }
            catch (Exception ex)
            {
                return Fault(new PevtRuntimeDiagnostic("PEVTR4304",
                    $"事件 `{eventId}` 的执行实例创建失败：{ex.GetType().Name}: {ex.Message}",
                    LocationOf(instruction.Span), BuildCallStack(), innerException: ex));
            }

            var driver = new ExecutionDriver(child, $"callevt \"{eventId}\"");
            PevtAsyncRoutine routine = _async.Register($"callevt \"{eventId}\"", null, driver);

            if (wantsHandler)
            {
                frame.Environment.SetHandler(instruction.HandlerName,
                    new PevtHandlerValue(routine.Id, Id, null));
                frame.Ip++;
                return null;
            }

            // 同步形式：挂在子事件上，等它结束再继续下一条语句。子事件的失败就是本流程的失败。
            return Suspend(new PevtHandlerWait(routine, wrapAsAwaitFailure: false), () =>
            {
                frame.Ip++;
                return null;
            });
        }

        // ---- exec ----

        /// <summary>
        /// <c>exec</c>：运行时解析、绑定并执行一段 PEVT 片段。
        /// </summary>
        private PevtExecutionResult ExecuteExec(PevtFrame frame, PevtInstruction instruction)
        {
            var arguments = new PevtValue[instruction.Index];
            for (int i = instruction.Index - 1; i >= 0; i--)
                arguments[i] = Pop(frame);

            if (arguments.Length != 1 || arguments[0].Type != PevtType.String)
            {
                return Fault("PEVTR1201",
                    "`exec` 需要恰好一个 `string` 实参作为片段源码。", instruction.Span);
            }

            if (DynamicDepth + 1 > MaxDynamicDepth)
                return Fault("PEVTR1203", $"嵌套 `exec` 深度超过上限 {MaxDynamicDepth}。", instruction.Span);

            string fragment = arguments[0].AsString;

            PevtRuntimeDiagnostic compileError = CompileFragment(fragment, frame.Environment, instruction.Span, out PevtCompiledProgram program);
            if (compileError != null)
                return Fault(compileError);

            if (!Budget.IsWithinCallDepth(TotalDepth + 1))
                return Fault("PEVTR1003", $"`exec` 片段的调用深度超过上限 {Budget.Limits.MaxCallDepth}。", instruction.Span);

            var child = new PevtExecution(program, _services, _commands, Budget, SubEvents, DynamicDepth + 1, TotalDepth);
            child.AdoptFragmentEnvironment(frame.Environment);

            var routine = _async.Register("exec", null, new ExecutionDriver(child, "exec"));

            // exec 是同步构造：当前流程挂在片段上，片段结束后临时环境随子实例一起消失。
            return Suspend(new PevtHandlerWait(routine, wrapAsAwaitFailure: false), () =>
            {
                frame.Ip++;
                return null;
            });
        }

        /// <summary>把最外层帧的环境换成一个以宿主环境为父的临时环境。</summary>
        private void AdoptFragmentEnvironment(PevtEnvironment host)
        {
            PevtFrame root = _frames[0];
            _frames[0] = new PevtFrame(
                root.Kind, root.Name, new PevtEnvironment(root.Name + " (exec)", host),
                entryPoint: root.Ip, returnIp: root.ReturnIp, producesValue: root.ProducesValue,
                returnType: root.ReturnType, callSpan: root.CallSpan, switchSlotCount: _program.SwitchSlotCount);
        }

        /// <summary>
        /// 片段编译。用宿主的能力标记拼一个最小合法文档，然后走与嵌入源完全相同的静态管线——
        /// 绝不为动态片段另写一套宽松的检查。
        /// </summary>
        private PevtRuntimeDiagnostic CompileFragment(
            string fragment,
            PevtEnvironment hostEnvironment,
            TextSpan span,
            out PevtCompiledProgram program)
        {
            program = null;

            var builder = new StringBuilder();

            // 复用宿主的事件 ID：合成一个带后缀的 ID 会撞上 PEVT1111（事件 ID 只许字母数字与汉字），
            // 而这个 ID 只出现在诊断里，片段并不注册成事件。区分靠的是下面那个文件路径。
            builder.Append("id \"").Append(EventId).Append("\"\n");
            if (_program.HasCsCapability)
                builder.Append("enable cs\n");
            if (_program.HasAsyncCapability)
                builder.Append("enable async\n");

            int bodyStart = builder.Length;
            builder.Append(fragment);
            if (!fragment.EndsWith("\n", StringComparison.Ordinal))
                builder.Append('\n');

            int bodyEnd = builder.Length;
            builder.Append("end\n");

            PevtCompilation compilation = PevtSourceCompiler.Compile(
                new UTF8Encoding(false).GetBytes(builder.ToString()),
                EventId + "#exec",
                _commands?.Catalog.ToBuiltinApiTable(),
                seedSymbols: AuthorizedOuterSymbols(hostEnvironment),
                // 片段里的 `$raw cs` 也要过 PEVT8007–8010：不接分析器的话，一段不合法的 C# 会
                // 通过片段校验、然后在执行点以 PEVTR4102 冒出来，而它本该是 PEVTR1201。
                rawCsAnalyzer: _services.RawCs);

            // 先查禁止语句：它有自己的编号，不应该被笼统的 PEVTR1201 盖掉。
            if (compilation.Document != null)
            {
                string forbidden = FindForbiddenDynamicStatement(compilation.Document, bodyStart, bodyEnd);
                if (forbidden != null)
                {
                    return new PevtRuntimeDiagnostic("PEVTR1202",
                        $"`exec` 片段不允许使用 {forbidden}。", LocationOf(span), BuildCallStack());
                }
            }

            if (!compilation.Success)
            {
                PevtRuntimeDiagnostic inner = FirstErrorAsRuntimeDiagnostic(compilation.Diagnostics);
                return new PevtRuntimeDiagnostic("PEVTR1201",
                    "`exec` 的片段没有通过 PEVT 静态校验。", LocationOf(span), BuildCallStack(), innerDiagnostic: inner);
            }

            PevtCompileResult result = PevtCompiledProgram.Compile(compilation.Definition, _commands?.Catalog);
            if (!result.Success)
            {
                return new PevtRuntimeDiagnostic("PEVTR1201",
                    $"`exec` 的片段使用了当前运行时尚未支持的构造：{string.Join("、", result.UnsupportedFeatures)}",
                    LocationOf(span), BuildCallStack());
            }

            program = result.Program;
            return null;
        }

        /// <summary>
        /// 片段里禁止出现的语句（运行诊断表 PEVTR1202）。
        /// </summary>
        private static string FindForbiddenDynamicStatement(DocumentSyntax document, int bodyStart, int bodyEnd)
        {
            foreach (EnableDeclarationSyntax enableDeclaration in document.EnableDeclarations)
            {
                if (enableDeclaration.Span.Start >= bodyStart && enableDeclaration.Span.Start < bodyEnd)
                    return "`enable`";
            }

            foreach (StatementSyntax statement in document.Statements)
            {
                if (statement.Span.Start < bodyStart || statement.Span.Start >= bodyEnd)
                    continue;

                switch (statement)
                {
                    case BlockDefinitionStatementSyntax _:
                        return "`block` / `endblock`";
                    case LabelStatementSyntax _:
                        return "标签";
                    case GotoLabelStatementSyntax _:
                    case GotoCaseStatementSyntax _:
                        return "`goto`";
                    case ReturnStatementSyntax _:
                        return "`return`";
                    case EndStatementSyntax _:
                        return "`end`";
                }
            }

            return null;
        }

        /// <summary>
        /// 片段可以读写的外层名字。
        /// 只交出"授权的外层变量"——常量不交（片段写不了它，交出去只会把 PEVT6002 变成运行期困惑），
        /// 句柄也不交（<c>await</c>/<c>kill</c>/<c>status</c> 的目标必须留在宿主流程里）。
        /// </summary>
        private static IEnumerable<Symbol> AuthorizedOuterSymbols(PevtEnvironment host)
        {
            var symbols = new List<Symbol>();
            foreach (string name in host.SlotNames)
            {
                if (host.TryGetSlot(name, out PevtSlot slot) && slot.Kind == PevtSlotKind.Variable)
                    symbols.Add(new VariableSymbol(name, slot.DeclaredType));
            }

            return symbols;
        }

        /// <summary>把片段的第一条 Error 级静态诊断包成运行诊断，作为 PEVTR1201 的内部原因。</summary>
        private static PevtRuntimeDiagnostic FirstErrorAsRuntimeDiagnostic(IReadOnlyList<Diagnostic> diagnostics)
        {
            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;

                // 静态编号不能直接当运行诊断用，因此只把它的正文带过来，编号仍是 PEVTR1201。
                return new PevtRuntimeDiagnostic("PEVTR1201",
                    $"{diagnostic.Id}: {diagnostic.Message}", diagnostic.Location);
            }

            return null;
        }

        // ---- 驱动实现 ----

        /// <summary>把一次 <c>@</c> 组合的指令帧包成异步驱动。业务代码与同步调用完全共用。</summary>
        private sealed class CommandDriver : IPevtAsyncDriver
        {
            private readonly PevtCommandFrame _frame;

            public CommandDriver(PevtCommandFrame frame) => _frame = frame;

            public PevtWait CurrentWait => _frame.CurrentWait;

            public PevtAsyncStep Advance(PevtWaitContext context, out PevtRuntimeDiagnostic error)
            {
                switch (_frame.Advance(context, out error))
                {
                    case PevtCommandStep.Waiting:
                        return PevtAsyncStep.Waiting;
                    case PevtCommandStep.Progressed:
                        return PevtAsyncStep.Progressed;
                    case PevtCommandStep.Faulted:
                        return PevtAsyncStep.Faulted;
                    default:
                        return PevtAsyncStep.Completed;
                }
            }

            /// <summary>
            /// 返回值契约。指令帧按同步语义报 <c>PEVTR4002</c>，而异步操作的同一违反有自己的编号
            /// <c>PEVTR5002</c>，所以在这里换号并保留原因。
            /// </summary>
            public PevtRuntimeDiagnostic TakeResult(out PevtValue result, out bool hasResult)
            {
                PevtRuntimeDiagnostic error = _frame.ValidateResult(out result, out hasResult);
                if (error == null)
                    return null;

                return new PevtRuntimeDiagnostic("PEVTR5002", error.Message, error.Location, innerDiagnostic: error);
            }

            public void RequestCancel()
            {
                _frame.Context.IsCancellationRequested = true;
                _frame.CurrentWait?.Cancel();
            }

            public bool ConfirmCancel(PevtWaitContext context)
            {
                PevtWait wait = _frame.CurrentWait;
                if (wait == null || wait.IsCompleted)
                    return true;

                wait.Tick(context);
                return wait.IsCompleted;
            }

            public IReadOnlyList<Exception> Dispose() => _frame.CancelAndDispose();
        }

        /// <summary>
        /// 把一个子执行实例（异步块、子事件、<c>exec</c> 片段）包成异步驱动。
        /// 解释器只有一份，因此这三种构造的流程、预算和诊断行为必然一致。
        /// </summary>
        private sealed class ExecutionDriver : IPevtAsyncDriver
        {
            private readonly PevtExecution _child;
            private readonly string _description;

            public ExecutionDriver(PevtExecution child, string description)
            {
                _child = child;
                _description = description;
            }

            public PevtWait CurrentWait => _child.CurrentPendingWait;

            public PevtAsyncStep Advance(PevtWaitContext context, out PevtRuntimeDiagnostic error)
            {
                error = null;
                PevtExecutionResult result = _child.Resume();

                switch (result.Status)
                {
                    case PevtExecutionStatus.Completed:
                        return PevtAsyncStep.Completed;

                    case PevtExecutionStatus.Faulted:
                        error = result.Diagnostic;
                        return PevtAsyncStep.Faulted;

                    case PevtExecutionStatus.Cancelled:
                        error = new PevtRuntimeDiagnostic("PEVTR5004", $"{_description} 在推进过程中被取消。");
                        return PevtAsyncStep.Faulted;

                    default:
                        // 子实例这一帧已经把自己的预算用掉了，交回调度器等下一帧。
                        return PevtAsyncStep.Waiting;
                }
            }

            public PevtRuntimeDiagnostic TakeResult(out PevtValue result, out bool hasResult)
            {
                result = _child.ResultValue;
                hasResult = _child.HasResultValue;
                return null;
            }

            public void RequestCancel() => _child.CancelPendingWait();

            /// <summary>子实例的取消是同步完成的：<see cref="Cancel"/> 会跑完它自己的清理。</summary>
            public bool ConfirmCancel(PevtWaitContext context) => true;

            public IReadOnlyList<Exception> Dispose() => _child.Cancel();
        }
    }
}
