using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>一次 <see cref="PevtExecution.Resume"/> 的结果。</summary>
    public enum PevtExecutionStatus
    {
        /// <summary>尚未开始。</summary>
        Created,

        /// <summary>本帧预算用完或正在等待，下一帧继续。</summary>
        Suspended,

        /// <summary>执行到 <c>end</c> 或事件正文末尾，正常结束。</summary>
        Completed,

        /// <summary>运行诊断终止了执行。</summary>
        Faulted,

        /// <summary>被取消（事件替换、停止或插件卸载）。</summary>
        Cancelled,
    }

    /// <summary>执行暂停或终止的原因，供诊断展示。</summary>
    public enum PevtSuspendReason
    {
        None,

        /// <summary>本帧步数预算用完。</summary>
        FrameBudget,

        /// <summary>正在等待一个 <see cref="PevtWait"/>。</summary>
        Wait,
    }

    /// <summary>一次 Resume 的返回值。</summary>
    public sealed class PevtExecutionResult
    {
        public PevtExecutionStatus Status { get; }

        public PevtSuspendReason SuspendReason { get; }

        public PevtRuntimeDiagnostic Diagnostic { get; }

        internal PevtExecutionResult(PevtExecutionStatus status, PevtSuspendReason reason = PevtSuspendReason.None, PevtRuntimeDiagnostic diagnostic = null)
        {
            Status = status;
            SuspendReason = reason;
            Diagnostic = diagnostic;
        }

        public bool IsRunning => Status == PevtExecutionStatus.Suspended || Status == PevtExecutionStatus.Created;

        public override string ToString() =>
            Diagnostic != null ? $"{Status}: {Diagnostic.Id}" : $"{Status}({SuspendReason})";
    }

    /// <summary>显式执行帧的种类。</summary>
    public enum PevtFrameKind
    {
        Event,
        Block,
    }

    /// <summary>
    /// 一层显式执行帧。事件与事件块调用都压这个栈，绝不使用 C# 递归——否则深层嵌套会以宿主语言
    /// 栈溢出的形式表现出来，既不可诊断也绕开了 PEVTR1003。
    /// </summary>
    public sealed class PevtFrame
    {
        public PevtFrameKind Kind { get; }

        public string Name { get; }

        public PevtEnvironment Environment { get; }

        /// <summary>当前指令下标。</summary>
        public int Ip { get; internal set; }

        /// <summary>返回后调用方从哪条指令继续；事件帧为 -1。</summary>
        public int ReturnIp { get; }

        /// <summary>本帧是否要向调用方的求值栈压一个返回值。</summary>
        public bool ProducesValue { get; }

        public PevtType? ReturnType { get; }

        /// <summary>调用点位置，用于诊断调用栈。</summary>
        public TextSpan CallSpan { get; }

        internal readonly List<PevtValue> EvalStack = new List<PevtValue>();

        internal readonly PevtValue[] SwitchSlots;

        internal PevtFrame(
            PevtFrameKind kind,
            string name,
            PevtEnvironment environment,
            int entryPoint,
            int returnIp,
            bool producesValue,
            PevtType? returnType,
            TextSpan callSpan,
            int switchSlotCount)
        {
            Kind = kind;
            Name = name;
            Environment = environment;
            Ip = entryPoint;
            ReturnIp = returnIp;
            ProducesValue = producesValue;
            ReturnType = returnType;
            CallSpan = callSpan;
            SwitchSlots = new PevtValue[Math.Max(switchSlotCount, 1)];
        }

        public int EvalStackDepth => EvalStack.Count;

        public override string ToString() => $"{Kind} {Name} @{Ip}";
    }

    /// <summary>
    /// 一次事件执行实例：可跨帧恢复的同步 PEVT 解释器。
    /// </summary>
    public sealed partial class PevtExecution
    {
        private static long _nextId;

        private readonly PevtCompiledProgram _program;
        private readonly PevtCommandRegistry _commands;
        private readonly PevtServices _services;
        private readonly bool _ownsEventSession;
        private readonly List<PevtFrame> _frames = new List<PevtFrame>();
        private PevtCommandFrame _command;

        /// <summary>本事件拥有的子协程。异步 <c>@</c>、<c>async block</c> 与 <c>callevt</c> 都登记在这里。</summary>
        private readonly PevtAsyncScheduler _async = new PevtAsyncScheduler();

        /// <summary>当前同步流程正在等的那个等待（<c>await</c>/<c>kill</c>/同步子事件），以及它完成后要做的事。</summary>
        private PevtWait _pendingWait;
        private Func<PevtExecutionResult> _pendingContinuation;

        private readonly List<PevtRuntimeDiagnostic> _warnings = new List<PevtRuntimeDiagnostic>();

        public long Id { get; }

        public string EventId => _program.EventId;

        public PevtExecutionBudget Budget { get; }

        public PevtExecutionStatus Status { get; private set; } = PevtExecutionStatus.Created;

        public PevtRuntimeDiagnostic Diagnostic { get; private set; }

        /// <summary>事件拥有的临时清理栈；结束、异常和取消时逆序执行。</summary>
        public PevtCleanupStack Cleanup { get; } = new PevtCleanupStack();

        public PevtExecution(
            PevtCompiledProgram program,
            PevtServices services,
            PevtCommandRegistry commands = null,
            PevtBudgetLimits limits = null)
            : this(program, services, commands, new PevtExecutionBudget(limits), null, 0, 0, true)
        {
            InitializeRootParameters(Array.Empty<PevtValue>());
        }

        /// <summary>创建根执行实例并提供事件实参；缺失的尾部可选形参在构造完成前补齐。</summary>
        public PevtExecution(
            PevtCompiledProgram program,
            PevtServices services,
            IReadOnlyList<PevtValue> eventArguments,
            PevtCommandRegistry commands = null,
            PevtBudgetLimits limits = null)
            : this(program, services, commands, new PevtExecutionBudget(limits), null, 0, 0, true)
        {
            PevtValue[] arguments = eventArguments == null ? Array.Empty<PevtValue>() : eventArguments.ToArray();
            InitializeRootParameters(arguments);
        }

        private void InitializeRootParameters(PevtValue[] arguments)
        {
            PevtRuntimeDiagnostic error = SeedEventParameters(arguments);
            if (error != null)
                throw new PevtEventStartException(error);
        }

        /// <summary>
        /// 子执行实例（<c>async block</c>、<c>callevt</c> 子事件、<c>exec</c> 片段）的构造入口。
        /// 子实例共享根事件的服务与 Session，但不拥有 Session 的最终恢复权；只有公开构造出来的
        /// 根执行实例能调用 <see cref="PevtEventSession.RestoreAll"/>。
        /// </summary>
        private PevtExecution(
            PevtCompiledProgram program,
            PevtServices services,
            PevtCommandRegistry commands,
            PevtExecutionBudget budget,
            IPevtSubEventProvider subEvents,
            int dynamicDepth,
            int depthOffset,
            bool ownsEventSession = false)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _commands = commands;
            _ownsEventSession = ownsEventSession;
            Budget = budget ?? new PevtExecutionBudget(null);
            SubEvents = subEvents;
            DynamicDepth = dynamicDepth;
            DepthOffset = depthOffset;
            Id = ++_nextId;

            _frames.Add(new PevtFrame(
                PevtFrameKind.Event, program.EventId, new PevtEnvironment(program.EventId),
                entryPoint: 0, returnIp: -1, producesValue: false, returnType: null,
                callSpan: new TextSpan(0, 0), switchSlotCount: program.SwitchSlotCount));

            DeclareResourceGroups();
        }

        /// <summary>
        /// PEVT-E08：把这份编译产物声明的资源组交给资源服务，立即开始预载。<c>async block</c>/<c>exec</c>
        /// 子实例与父级共用同一份 <see cref="_program"/>，因此这里可能对同一个 groupId 被调用多次——
        /// <see cref="IPevtResources.DeclareResourceGroup"/> 的契约要求实现按 groupId 幂等，这里不需要自己去重。
        /// </summary>
        private void DeclareResourceGroups()
        {
            if (_program.ResourceGroups.Count == 0)
                return;

            IPevtResources resources = _services.Resources;
            if (resources == null)
                return;

            foreach (PevtResourceGroupInfo group in _program.ResourceGroups)
                resources.DeclareResourceGroup(group.GroupId, group.Members);
        }

        /// <summary>
        /// <c>callevt</c> 的目标解析入口。由宿主提供；为 null 时任何 <c>callevt</c> 都以
        /// PEVTR4301 结束（没有注册表可查，等价于目标不存在）。
        /// </summary>
        public IPevtSubEventProvider SubEvents { get; set; }

        /// <summary>嵌套 <c>exec</c> 的层数，用于 PEVTR1203。</summary>
        public int DynamicDepth { get; private set; }

        /// <summary>
        /// 本实例之外已经占用的帧深度。
        /// </summary>
        public int DepthOffset { get; }

        /// <summary>整棵调用树上当前的总帧深度。</summary>
        public int TotalDepth => DepthOffset + _frames.Count;

        /// <summary>本事件拥有的子协程，供只读诊断查询。</summary>
        public PevtAsyncScheduler AsyncRoutines => _async;

        /// <summary>执行期产生的警告（目前只有 PEVTR5005）。不中断执行。</summary>
        public IReadOnlyList<PevtRuntimeDiagnostic> Warnings => new ReadOnlyCollection<PevtRuntimeDiagnostic>(_warnings);

        /// <summary>本执行实例的普通返回值；<c>async block</c> 的结果由它带出。</summary>
        public PevtValue ResultValue { get; private set; }

        public bool HasResultValue { get; private set; }

        public IReadOnlyList<PevtFrame> Frames => new ReadOnlyCollection<PevtFrame>(_frames);

        /// <summary>当前正在执行的 <c>@</c> 指令帧；没有时为 null。</summary>
        public PevtCommandFrame CurrentCommand => _command;

        /// <summary>外层事件环境，供调试查询。</summary>
        public PevtEnvironment RootEnvironment => _frames.Count > 0 ? _frames[0].Environment : null;

        public bool IsFinished =>
            Status == PevtExecutionStatus.Completed || Status == PevtExecutionStatus.Faulted || Status == PevtExecutionStatus.Cancelled;

        /// <summary>
        /// 推进一个更新帧。返回 <see cref="PevtExecutionStatus.Suspended"/> 时下一帧继续调用。
        /// </summary>
        public PevtExecutionResult Resume()
        {
            if (IsFinished)
                return new PevtExecutionResult(Status, PevtSuspendReason.None, Diagnostic);

            Budget.BeginFrame();
            Status = PevtExecutionStatus.Suspended;

            // 子协程先推进：同步流程这一帧里看到的 status/await 结果，来自同一帧已经推进过的状态，
            // 而不是上一帧的旧值。
            _async.Tick(new PevtWaitContext(_services.Clock.Frame));

            // 调度项同理：到期的项在同一帧里启动，而不是拖到下一帧才被 @wait_motion/F8 看见。
            TickSchedules();

            while (true)
            {
                if (_pendingWait != null)
                {
                    PevtExecutionResult waitResult = AdvancePendingWait();
                    if (waitResult != null)
                        return waitResult;
                    continue;
                }

                if (_command != null)
                {
                    PevtExecutionResult commandResult = AdvanceCommand();
                    if (commandResult != null)
                        return commandResult;
                    continue;
                }

                if (!Budget.HasFrameBudget)
                {
                    // 预算用完只是让出一帧，不是错误。
                    if (!Budget.RecordNoProgress())
                        return Fault("PEVTR1002", $"事件 `{EventId}` 连续 {Budget.Limits.StallFrames} 帧没有任何进展。", CurrentSpan());
                    return new PevtExecutionResult(PevtExecutionStatus.Suspended, PevtSuspendReason.FrameBudget);
                }

                if (!Budget.TryConsumeStep())
                    return Fault("PEVTR1001", $"事件 `{EventId}` 执行步数超过上限 {Budget.Limits.TotalSteps}。", CurrentSpan());

                PevtExecutionResult stepResult = Step();
                if (stepResult != null)
                    return stepResult;
            }
        }

        /// <summary>取消执行：逆序清理并进入 <see cref="PevtExecutionStatus.Cancelled"/>。</summary>
        public IReadOnlyList<Exception> Cancel()
        {
            if (IsFinished)
                return Array.AsReadOnly(Array.Empty<Exception>());

            var failures = new List<Exception>();

            if (_command != null)
            {
                failures.AddRange(_command.CancelAndDispose());
                _command = null;
            }

            CancelPendingWait();

            // 尚未触发的调度项直接丢弃：它们还没启动任何东西，不需要 kill。
            _schedules.Clear();

            // 事件被取消时按 ID 逆序 kill 掉它拥有的全部未结束子协程（第 10 节）。
            failures.AddRange(_async.ForceFinishAll());
            CollectUnobservedFailures();

            failures.AddRange(Cleanup.RunAll());
            if (_ownsEventSession)
                failures.AddRange(_services.Session.RestoreAll());

            Status = PevtExecutionStatus.Cancelled;
            return new ReadOnlyCollection<Exception>(failures);
        }

        // ---- 指令执行 ----

        private PevtExecutionResult Step()
        {
            PevtFrame frame = _frames[_frames.Count - 1];
            if (frame.Ip < 0 || frame.Ip >= _program.Code.Count)
                return Fault("PEVTR9001", $"指令指针 {frame.Ip} 越界。", frame.CallSpan);

            PevtInstruction instruction = _program.Code[frame.Ip];

            switch (instruction.OpCode)
            {
                case PevtOpCode.PushLiteral:
                    frame.EvalStack.Add(instruction.Constant);
                    frame.Ip++;
                    return null;

                case PevtOpCode.PushName:
                    return ExecutePushName(frame, instruction);

                case PevtOpCode.Convert:
                    return ExecuteConvert(frame, instruction);

                case PevtOpCode.Negate:
                    return ExecuteNegate(frame, instruction);

                case PevtOpCode.Not:
                {
                    PevtValue operand = Pop(frame);
                    frame.EvalStack.Add(PevtValue.FromBool(!operand.AsBool));
                    frame.Ip++;
                    return null;
                }

                case PevtOpCode.Binary:
                    return ExecuteBinary(frame, instruction);

                case PevtOpCode.Pop:
                    Pop(frame);
                    frame.Ip++;
                    return null;

                case PevtOpCode.Declare:
                    return ExecuteDeclare(frame, instruction);

                case PevtOpCode.Store:
                    return ExecuteStore(frame, instruction);

                case PevtOpCode.Jump:
                    frame.Ip = instruction.Target;
                    return null;

                case PevtOpCode.JumpIfFalse:
                {
                    PevtValue condition = Pop(frame);
                    frame.Ip = condition.AsBool ? frame.Ip + 1 : instruction.Target;
                    return null;
                }

                case PevtOpCode.JumpIfTrue:
                {
                    PevtValue condition = Pop(frame);
                    frame.Ip = condition.AsBool ? instruction.Target : frame.Ip + 1;
                    return null;
                }

                case PevtOpCode.StoreSwitch:
                    frame.SwitchSlots[instruction.Index] = Pop(frame);
                    frame.Ip++;
                    return null;

                case PevtOpCode.LoadSwitch:
                    frame.EvalStack.Add(frame.SwitchSlots[instruction.Index]);
                    frame.Ip++;
                    return null;

                case PevtOpCode.PushGlobalFlagDefined:
                {
                    IPevtState state = _services.Domains?.State;
                    if (state == null)
                        return Fault("PEVTR4001", "`ifdef` 需要 State 服务，但当前宿主没有登记。", instruction.Span);
                    frame.EvalStack.Add(PevtValue.FromBool(state.HasFlag("global", instruction.Name)));
                    frame.Ip++;
                    return null;
                }

                case PevtOpCode.End:
                    return Complete();

                case PevtOpCode.Return:
                    return ExecuteReturn(frame, instruction);

                case PevtOpCode.CallBlock:
                    return ExecuteCallBlock(frame, instruction);

                case PevtOpCode.CallBuiltin:
                    return ExecuteCallBuiltin(frame, instruction);

                case PevtOpCode.CallAsync:
                    return ExecuteCallAsync(frame, instruction);

                case PevtOpCode.CallEvent:
                    return ExecuteCallEvent(frame, instruction);

                case PevtOpCode.Status:
                    return ExecuteStatus(frame, instruction);

                case PevtOpCode.AwaitHandle:
                    return ExecuteAwaitHandle(frame, instruction);

                case PevtOpCode.AwaitAll:
                case PevtOpCode.AwaitAny:
                    return ExecuteAggregateAwait(frame, instruction);

                case PevtOpCode.Kill:
                    return ExecuteKill(frame, instruction);

                case PevtOpCode.Exec:
                    return ExecuteExec(frame, instruction);

                case PevtOpCode.RawCmd:
                    return ExecuteRawCmd(frame, instruction);

                case PevtOpCode.RawCs:
                    return ExecuteRawCs(frame, instruction);

                case PevtOpCode.ScheduleAfter:
                    return ExecuteScheduleAfter(frame, instruction);

                case PevtOpCode.FlushSchedules:
                    return ExecuteFlushSchedules(frame, instruction);

                case PevtOpCode.ClearSchedules:
                    return ExecuteClearSchedules(frame, instruction);

                default:
                    return Fault("PEVTR9001", $"未知指令 {instruction.OpCode}。", instruction.Span);
            }
        }

        private PevtExecutionResult ExecutePushName(PevtFrame frame, PevtInstruction instruction)
        {
            if (!frame.Environment.TryGetSlot(instruction.Name, out PevtSlot slot))
                return Fault("PEVTR9001", $"环境 `{frame.Environment.ScopeName}` 中不存在名称 `{instruction.Name}`。", instruction.Span);

            if (!slot.IsInitialized)
                return Fault("PEVTR3002", $"读取了尚未初始化的 `{instruction.Name}`。", instruction.Span);

            frame.EvalStack.Add(slot.Value);
            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteConvert(PevtFrame frame, PevtInstruction instruction)
        {
            PevtValue operand = Pop(frame);

            if (instruction.Type == PevtType.Float && operand.Type == PevtType.Int)
                frame.EvalStack.Add(PevtValue.FromFloat(operand.AsInt));
            else if (instruction.Type == PevtType.String && operand.Type == PevtType.Char)
                frame.EvalStack.Add(PevtValue.FromString(operand.AsChar.ToString()));
            else
                return Fault("PEVTR9001", $"不支持的转换：{operand.Type.DisplayName()} -> {instruction.Type.DisplayName()}。", instruction.Span);

            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteNegate(PevtFrame frame, PevtInstruction instruction)
        {
            PevtValue operand = Pop(frame);

            if (operand.Type == PevtType.Int)
            {
                try
                {
                    // int 一元取负同样使用 checked：-int.MinValue 溢出，不静默回绕。
                    frame.EvalStack.Add(PevtValue.FromInt(checked(-operand.AsInt)));
                }
                catch (OverflowException)
                {
                    return Fault("PEVTR2001", "`int` 一元取负结果超出 32 位有符号整数范围。", instruction.Span);
                }
            }
            else if (operand.Type == PevtType.Float)
            {
                frame.EvalStack.Add(PevtValue.FromFloat(-operand.AsFloat));
            }
            else
            {
                return Fault("PEVTR9001", $"一元取负不支持 {operand.Type.DisplayName()}。", instruction.Span);
            }

            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteBinary(PevtFrame frame, PevtInstruction instruction)
        {
            PevtValue right = Pop(frame);
            PevtValue left = Pop(frame);

            PevtValue result;
            PevtRuntimeDiagnostic error = PevtOperations.Evaluate(instruction.Operator, left, right, instruction.Span, _program.Source, out result);
            if (error != null)
                return Fault(error);

            frame.EvalStack.Add(result);
            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteDeclare(PevtFrame frame, PevtInstruction instruction)
        {
            if (!frame.Environment.MarkDeclarationExecuted(instruction.Index))
            {
                return Fault("PEVTR3001",
                    $"同一次调用的环境中重复执行了 `{instruction.Name}` 的声明。",
                    instruction.Span);
            }

            PevtSlotKind kind = instruction.Operator == SyntaxKind.ConstKeyword ? PevtSlotKind.Constant : PevtSlotKind.Variable;
            PevtSlot slot = frame.Environment.Declare(instruction.Name, instruction.Type, kind);

            if (instruction.Flag)
            {
                PevtValue value = Pop(frame);
                if (value.Type != instruction.Type)
                    return Fault("PEVTR9001", $"`{instruction.Name}` 声明为 {instruction.Type.DisplayName()}，初始化值是 {value.Type.DisplayName()}。", instruction.Span);
                slot.Set(value);
            }

            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteStore(PevtFrame frame, PevtInstruction instruction)
        {
            PevtValue value = Pop(frame);

            if (!frame.Environment.TryGetSlot(instruction.Name, out PevtSlot slot))
                return Fault("PEVTR9001", $"环境 `{frame.Environment.ScopeName}` 中不存在名称 `{instruction.Name}`。", instruction.Span);

            if (value.Type != slot.DeclaredType)
                return Fault("PEVTR9001", $"`{instruction.Name}` 声明为 {slot.DeclaredType.DisplayName()}，不能写入 {value.Type.DisplayName()}。", instruction.Span);

            slot.Set(value);
            frame.Ip++;
            return null;
        }

        private PevtExecutionResult ExecuteReturn(PevtFrame frame, PevtInstruction instruction)
        {
            if (frame.Kind != PevtFrameKind.Block)
                return Fault("PEVTR9001", "`return` 出现在事件块之外。", instruction.Span);

            PevtValue result = default;
            bool hasResult = instruction.Flag;
            if (hasResult)
                result = Pop(frame);

            // 异步块作为子执行实例运行时，它的块帧就是最外层帧：返回值是整个子执行实例的结果。
            if (_frames.Count == 1)
            {
                if (hasResult)
                {
                    ResultValue = result;
                    HasResultValue = true;
                }

                return Complete();
            }

            _frames.RemoveAt(_frames.Count - 1);
            PevtFrame caller = _frames[_frames.Count - 1];

            if (frame.ProducesValue)
            {
                if (!hasResult)
                    return Fault("PEVTR9001", $"事件块 `{frame.Name}` 声明了返回值但没有返回。", instruction.Span);
                caller.EvalStack.Add(result);
            }

            caller.Ip = frame.ReturnIp;
            return null;
        }

        private PevtExecutionResult ExecuteCallBlock(PevtFrame frame, PevtInstruction instruction)
        {
            if (!_program.TryGetBlock(instruction.Name, out PevtBlockInfo block))
                return Fault("PEVTR9001", $"未定义的事件块 `{instruction.Name}`。", instruction.Span);

            if (!Budget.IsWithinCallDepth(TotalDepth + 1))
                return Fault("PEVTR1003", $"事件块调用深度超过上限 {Budget.Limits.MaxCallDepth}。", instruction.Span);

            var arguments = new PevtValue[instruction.Index];
            for (int i = instruction.Index - 1; i >= 0; i--)
                arguments[i] = Pop(frame);

            if (!PevtParameterBinding.TryBind(block.Parameters, arguments, out PevtValue[] boundArguments, out string argumentError))
                return Fault("PEVTR9001", $"事件块 `{block.Name}` 调用参数不匹配：{argumentError}", instruction.Span);

            // 每次调用都建立一个新的局部环境；事件块不隐式捕获外层变量。
            var environment = new PevtEnvironment(block.Name);
            for (int i = 0; i < block.Parameters.Count; i++)
            {
                PevtParameterInfo parameter = block.Parameters[i];
                PevtSlot slot = environment.Declare(parameter.Name, parameter.Type, PevtSlotKind.Variable);
                slot.Set(boundArguments[i]);
            }

            _frames.Add(new PevtFrame(
                PevtFrameKind.Block, block.Name, environment,
                entryPoint: block.EntryPoint,
                returnIp: frame.Ip + 1,
                producesValue: block.ReturnType.HasValue,
                returnType: block.ReturnType,
                callSpan: instruction.Span,
                switchSlotCount: _program.SwitchSlotCount));

            return null;
        }

        private PevtExecutionResult ExecuteCallBuiltin(PevtFrame frame, PevtInstruction instruction)
        {
            CommandDescriptor descriptor = instruction.Descriptor;

            if (_commands == null || !_commands.TryGetRoutine(descriptor, out IPevtCommandRoutine routine))
                return Fault("PEVTR4001", $"`@{descriptor.Name}` 没有登记处理器。", instruction.Span);

            var arguments = new PevtValue[instruction.Index];
            for (int i = instruction.Index - 1; i >= 0; i--)
                arguments[i] = Pop(frame);

            var context = new PevtRoutineContext(Id, EventId, LocationOf(instruction.Span), _services);
            var localized = new PevtArguments(PevtTextArguments.Localize(descriptor, arguments, _services.Localization));
            _command = new PevtCommandFrame(descriptor, localized, routine, context, instruction.Span);

            PevtRuntimeDiagnostic startError = _command.Start();
            if (startError != null)
            {
                _command = null;
                return Fault(startError);
            }

            return null;
        }

        /// <summary>推进当前指令帧。返回非 null 表示本次 Resume 结束。</summary>
        private PevtExecutionResult AdvanceCommand()
        {
            PevtCommandFrame command = _command;
            var waitContext = new PevtWaitContext(_services.Clock.Frame);

            PevtCommandStep step = command.Advance(waitContext, out PevtRuntimeDiagnostic error);

            switch (step)
            {
                case PevtCommandStep.Waiting:
                    if (command.CurrentWait != null && !command.CurrentWait.HasProgressSource)
                        return FinishCommandWithFault(command, "PEVTR1002", $"`@{command.Descriptor.Name}` 的等待没有推进源（{command.CurrentWait.ProgressSource}）。");

                    if (command.CurrentWait?.AllowsIndefiniteWait == true)
                        Budget.RecordWaitProgress();
                    else if (!Budget.RecordNoProgress())
                        return FinishCommandWithFault(command, "PEVTR1002", $"事件 `{EventId}` 连续 {Budget.Limits.StallFrames} 帧没有任何进展。");

                    return new PevtExecutionResult(PevtExecutionStatus.Suspended, PevtSuspendReason.Wait);

                case PevtCommandStep.Progressed:
                    Budget.RecordWaitProgress();
                    if (!Budget.HasFrameBudget)
                        return new PevtExecutionResult(PevtExecutionStatus.Suspended, PevtSuspendReason.FrameBudget);
                    return null;

                case PevtCommandStep.Faulted:
                {
                    // 先取调用栈：CancelAndDispose 之后指令帧就不在栈上了，诊断会丢掉最内层那一层。
                    IReadOnlyList<PevtCallFrame> stack = BuildCallStack();
                    command.CancelAndDispose();
                    _command = null;
                    return Fault(WithCallStack(error, stack));
                }

                case PevtCommandStep.Completed:
                default:
                {
                    PevtRuntimeDiagnostic contractError = command.ValidateResult(out PevtValue result, out bool hasResult);
                    command.Dispose();
                    _command = null;

                    if (contractError != null)
                        return Fault(contractError);

                    PevtFrame frame = _frames[_frames.Count - 1];
                    if (hasResult)
                        frame.EvalStack.Add(result);

                    frame.Ip++;
                    Budget.RecordWaitProgress();
                    return null;
                }
            }
        }

        private PevtExecutionResult FinishCommandWithFault(PevtCommandFrame command, string id, string message)
        {
            IReadOnlyList<PevtCallFrame> stack = BuildCallStack();
            command.CancelAndDispose();
            _command = null;
            return Fault(new PevtRuntimeDiagnostic(id, message, LocationOf(command.Span), stack));
        }

        /// <summary>诊断本身还没有调用栈时补上一份；已经有的不覆盖。</summary>
        private static PevtRuntimeDiagnostic WithCallStack(PevtRuntimeDiagnostic diagnostic, IReadOnlyList<PevtCallFrame> stack) =>
            diagnostic.CallStack.Count > 0
                ? diagnostic
                : new PevtRuntimeDiagnostic(diagnostic.Id, diagnostic.Message, diagnostic.Location, stack,
                    diagnostic.InnerDiagnostic, diagnostic.InnerException);

        // ---- 辅助 ----

        private PevtValue Pop(PevtFrame frame)
        {
            int last = frame.EvalStack.Count - 1;
            if (last < 0)
                throw new InvalidOperationException("求值栈下溢：指令序列已损坏。");

            PevtValue value = frame.EvalStack[last];
            frame.EvalStack.RemoveAt(last);
            return value;
        }

        private PevtExecutionResult Complete()
        {
            // 尚未触发的调度项随事件自然结束一起丢弃（第 15.7 节）。
            _schedules.Clear();

            // 事件正常结束同样要 kill 掉还在跑的子协程，并把没人看过的失败记成 PEVTR5005。
            var asyncFailures = new List<Exception>(_async.ForceFinishAll());
            CollectUnobservedFailures();

            IReadOnlyList<Exception> failures = Cleanup.RunAll();
            var sessionFailures = _ownsEventSession
                ? new List<Exception>(_services.Session.RestoreAll())
                : new List<Exception>();
            sessionFailures.AddRange(failures);
            sessionFailures.AddRange(asyncFailures);

            if (sessionFailures.Count > 0)
            {
                Status = PevtExecutionStatus.Faulted;
                Diagnostic = new PevtRuntimeDiagnostic("PEVTR1101",
                    $"事件 `{EventId}` 正常结束，但清理失败：{sessionFailures[0].Message}",
                    CurrentLocation(), BuildCallStack(), innerException: sessionFailures[0]);
                return new PevtExecutionResult(Status, PevtSuspendReason.None, Diagnostic);
            }

            Status = PevtExecutionStatus.Completed;
            return new PevtExecutionResult(PevtExecutionStatus.Completed);
        }

        private PevtExecutionResult Fault(string id, string message, TextSpan span)
        {
            var diagnostic = new PevtRuntimeDiagnostic(id, message, LocationOf(span), BuildCallStack());
            return Fault(diagnostic);
        }

        private PevtExecutionResult Fault(PevtRuntimeDiagnostic diagnostic)
        {
            // 异常终止同样要逆序清理；清理期间的新异常作为附加信息，不覆盖最初异常。
            if (_command != null)
            {
                _command.CancelAndDispose();
                _command = null;
            }

            CancelPendingWait();
            _schedules.Clear();
            _async.ForceFinishAll();
            CollectUnobservedFailures();

            Cleanup.RunAll();
            if (_ownsEventSession)
                _services.Session.RestoreAll();

            Status = PevtExecutionStatus.Faulted;
            Diagnostic = diagnostic.CallStack.Count > 0
                ? diagnostic
                : new PevtRuntimeDiagnostic(diagnostic.Id, diagnostic.Message, diagnostic.Location, BuildCallStack(), diagnostic.InnerDiagnostic, diagnostic.InnerException);

            return new PevtExecutionResult(PevtExecutionStatus.Faulted, PevtSuspendReason.None, Diagnostic);
        }

        private TextSpan CurrentSpan()
        {
            if (_frames.Count == 0)
                return new TextSpan(0, 0);

            PevtFrame frame = _frames[_frames.Count - 1];
            return frame.Ip >= 0 && frame.Ip < _program.Code.Count ? _program.Code[frame.Ip].Span : frame.CallSpan;
        }

        private TextLocation CurrentLocation() => LocationOf(CurrentSpan());

        private TextLocation LocationOf(TextSpan span)
        {
            if (_program.Source == null)
                return null;
            int end = Math.Min(span.End, _program.Source.Length);
            int start = Math.Min(span.Start, end);
            return _program.Source.GetLocation(TextSpan.FromBounds(start, end));
        }

        /// <summary>从最内层到最外层构造调用栈：指令帧 → 事件块 → 外层事件。</summary>
        public IReadOnlyList<PevtCallFrame> BuildCallStack()
        {
            var stack = new List<PevtCallFrame>();

            if (_command != null)
                stack.Add(new PevtCallFrame(PevtCallFrameKind.Command, "@" + _command.Descriptor.Name, LocationOf(_command.Span)));

            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                PevtFrame frame = _frames[i];
                TextSpan span = frame.Ip >= 0 && frame.Ip < _program.Code.Count ? _program.Code[frame.Ip].Span : frame.CallSpan;
                stack.Add(new PevtCallFrame(
                    frame.Kind == PevtFrameKind.Event ? PevtCallFrameKind.Event : PevtCallFrameKind.Block,
                    frame.Name,
                    LocationOf(span)));
            }

            return new ReadOnlyCollection<PevtCallFrame>(stack);
        }
    }
}
