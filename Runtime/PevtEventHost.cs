using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Registration;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 一个正在运行（或已经结束）的事件实例的只读视图。
    ///
    /// 调用方拿到它只能查询，不能直接推进或改状态——推进由宿主的更新点统一负责，
    /// 否则两个调用方各推一次就会破坏"每帧按例程 ID 升序"的确定性。
    /// </summary>
    public sealed class PevtEventInstance
    {
        private readonly PevtRoutineInstance _instance;

        internal PevtEventInstance(PevtRoutineInstance instance, string owner)
        {
            _instance = instance;
            Owner = owner ?? string.Empty;
        }

        /// <summary>调度器分配的稳定递增 ID。</summary>
        public long Id => _instance.Id;

        public string EventId => _instance.Execution.EventId;

        /// <summary>注册该事件的来源程序集；直接由源码启动时为空串。</summary>
        public string Owner { get; }

        public PevtExecutionStatus Status => _instance.Execution.Status;

        public bool IsFinished => _instance.Execution.IsFinished;

        /// <summary>终止原因；正常结束或仍在运行时为 null。</summary>
        public PevtRuntimeDiagnostic Diagnostic => _instance.Execution.Diagnostic;

        /// <summary>结束时所处的帧号；未结束时为 -1。</summary>
        public long CompletedFrame => _instance.CompletedFrame;

        /// <summary>当前源码调用栈快照：事件 → 事件块 → 指令帧。</summary>
        public IReadOnlyList<PevtCallFrame> CallStack => _instance.Execution.BuildCallStack();

        /// <summary>当前正在执行的 <c>@</c> 名称；没有时为 null。</summary>
        public string CurrentCommand => _instance.Execution.CurrentCommand?.Descriptor.Name;

        /// <summary>当前等待的推进源，供停滞排查使用；没有等待时为 null。</summary>
        public string CurrentWaitSource => _instance.Execution.CurrentCommand?.CurrentWait?.ProgressSource;

        /// <summary>该事件在所有权树上的根节点。</summary>
        public PevtOwnershipNode Ownership => _instance.Ownership;

        /// <summary>本次执行已经消耗的指令步数。</summary>
        public long TotalSteps => _instance.Execution.Budget.TotalSteps;

        internal PevtRoutineInstance Routine => _instance;

        public override string ToString() => $"{EventId}#{Id} ({Status})";
    }

    /// <summary>
    /// PolarisEvent 的公开事件宿主：唯一的 <c>Start</c>/<c>Change</c>/<c>Stop</c> 入口和唯一的更新点。
    ///
    /// 插件初始化顺序固定为"先登记内置人物，再扫描人物与事件 registrar"，因此 <c>aic</c> 命名空间
    /// 永远先占位；这个顺序由 <see cref="PevtRegistryScanner"/> 的构造保证，宿主只负责接过它的结果。
    /// </summary>
    public sealed class PevtEventHost : IPevtSubEventProvider
    {
        private readonly PevtScheduler _scheduler;
        private readonly PevtCommandRegistry _commands;
        private readonly Func<string, PevtServices> _servicesFactory;
        private readonly Dictionary<long, PevtEventInstance> _instances = new Dictionary<long, PevtEventInstance>();
        private readonly Dictionary<string, PevtCompiledProgram> _compiled = new Dictionary<string, PevtCompiledProgram>(StringComparer.Ordinal);

        /// <summary>已结束事件实例的保留条数上限，只影响事后诊断的可查窗口。</summary>
        public const int FinishedHistoryLimit = 32;

        private PevtEventInstance _root;

        public PevtRegistryScanner Registry { get; }

        public PevtBudgetLimits Limits { get; }

        /// <param name="servicesFactory">
        /// 每次启动事件时为它建立一套服务。每个事件必须拿到自己的 <see cref="PevtEventSession"/>，
        /// 否则上一个事件的临时状态恢复表会被下一个事件继承。
        /// </param>
        public PevtEventHost(
            PevtRegistryScanner registry,
            IPevtClock clock,
            Func<string, PevtServices> servicesFactory,
            PevtCommandRegistry commands = null,
            PevtBudgetLimits limits = null)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _servicesFactory = servicesFactory ?? throw new ArgumentNullException(nameof(servicesFactory));
            _commands = commands ?? Routines.P0CommandRoutines.CreateRegistry(CommandDescriptorCatalog.Builtin);
            Limits = limits ?? PevtBudgetLimits.Default;
            _scheduler = new PevtScheduler(clock ?? throw new ArgumentNullException(nameof(clock)));
        }

        /// <summary>当前的根事件；没有事件在跑时为 null。</summary>
        public PevtEventInstance Root => _root != null && !_root.IsFinished ? _root : null;

        /// <summary>
        /// 当前保留的实例（含已结束的），按 ID 升序。
        ///
        /// 已结束实例只保留最近 <see cref="FinishedHistoryLimit"/> 条：它们的价值是事后查诊断，
        /// 而一次游戏会话里事件会启动成千上万次，无上限保留就是一条稳定增长的内存占用。
        /// </summary>
        public IReadOnlyList<PevtEventInstance> Instances
        {
            get
            {
                var ids = new List<long>(_instances.Keys);
                ids.Sort();

                var result = new List<PevtEventInstance>(ids.Count);
                foreach (long id in ids)
                    result.Add(_instances[id]);

                return new ReadOnlyCollection<PevtEventInstance>(result);
            }
        }

        /// <summary>所有权树只读视图。</summary>
        public IReadOnlyList<PevtOwnershipNode> OwnershipRoots => _scheduler.Ownership.Roots;

        /// <summary>按事件 ID 启动一个已注册事件。找不到目标时 PEVTR4301。</summary>
        public PevtEventInstance Start(string eventId)
        {
            if (!Registry.Events.TryGet(eventId, out PevtEventCandidate candidate))
            {
                throw new PevtEventStartException(new PevtRuntimeDiagnostic("PEVTR4301",
                    $"`/event/{eventId}.pevt` 不在当前运行时注册表中。"));
            }

            return StartCore(candidate.Definition, candidate.Owner);
        }

        /// <summary>直接启动一个已经通过全部静态门的程序定义。宿主自测与工具预览用。</summary>
        public PevtEventInstance Start(PevtProgramDefinition definition, string owner = null)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            return StartCore(definition, owner);
        }

        /// <summary>
        /// 用另一个事件替换当前根事件。必须先完成旧根事件的清理再启动新事件，
        /// 否则旧事件的遮罩、镜头和 UI 临时状态会盖掉新事件刚设好的那一份。
        /// </summary>
        public PevtEventInstance Change(string eventId)
        {
            Stop();
            return Start(eventId);
        }

        /// <summary>停止当前根事件并级联清理。没有事件在跑时是空操作。</summary>
        public IReadOnlyList<Exception> Stop()
        {
            if (_root == null || _root.IsFinished)
            {
                _root = null;
                return Array.AsReadOnly(Array.Empty<Exception>());
            }

            IReadOnlyList<Exception> failures = _scheduler.Stop(_root.Routine);
            _root = null;
            return failures;
        }

        /// <summary>
        /// 宿主更新点。每帧调用一次，按固定顺序推进调度器；返回本帧结束的事件，按 ID 升序。
        /// </summary>
        public IReadOnlyList<PevtEventInstance> Update()
        {
            var finished = new List<PevtEventInstance>();
            foreach (PevtRoutineInstance routine in _scheduler.Tick())
            {
                if (_instances.TryGetValue(routine.Id, out PevtEventInstance instance))
                    finished.Add(instance);
            }

            if (_root != null && _root.IsFinished)
                _root = null;

            PruneFinished();
            return new ReadOnlyCollection<PevtEventInstance>(finished);
        }

        /// <summary>停止全部事件并释放所有权树。插件卸载时调用。</summary>
        public IReadOnlyList<Exception> Shutdown()
        {
            IReadOnlyList<Exception> failures = _scheduler.StopAll();
            _root = null;
            _instances.Clear();
            return failures;
        }

        /// <summary>按 ID 查一个实例。</summary>
        public bool TryGetInstance(long id, out PevtEventInstance instance) => _instances.TryGetValue(id, out instance);

        /// <summary>
        /// 把已结束实例的保留量压回 <see cref="FinishedHistoryLimit"/>，最旧的先丢。
        ///
        /// 必须由更新点主动调用：调度器与宿主都按 ID 保存实例，每条还挂着执行状态、所有权节点和
        /// 一份编译产物引用。当前根事件即使已经结束也先留着，让 <see cref="Root"/> 与
        /// <see cref="Update"/> 的"本帧结束了哪些事件"在同一帧内保持一致。
        /// </summary>
        private void PruneFinished()
        {
            var finished = new List<long>();
            foreach (KeyValuePair<long, PevtEventInstance> entry in _instances)
            {
                if (entry.Value.IsFinished && !ReferenceEquals(entry.Value, _root))
                    finished.Add(entry.Key);
            }

            if (finished.Count <= FinishedHistoryLimit)
                return;

            finished.Sort();
            for (int i = 0; i < finished.Count - FinishedHistoryLimit; i++)
                _instances.Remove(finished[i]);

            _scheduler.PruneFinished(_instances.Count > 0 ? MinKey() : long.MaxValue);
        }

        private long MinKey()
        {
            long min = long.MaxValue;
            foreach (long id in _instances.Keys)
            {
                if (id < min)
                    min = id;
            }

            return min;
        }

        private PevtEventInstance StartCore(PevtProgramDefinition definition, string owner)
        {
            PevtCompiledProgram program = GetOrCompile(definition);
            var execution = new PevtExecution(program, _servicesFactory(program.EventId), _commands, Limits)
            {
                // callevt 的目标解析要查注册表，而解释器不认识注册表；宿主自己就是那个解析器。
                SubEvents = this,
            };

            PevtRoutineInstance routine = _scheduler.Register(execution);
            var instance = new PevtEventInstance(routine, owner);
            _instances[routine.Id] = instance;
            _root = instance;
            return instance;
        }

        /// <summary>
        /// 编译结果按事件 ID 缓存。编译是纯函数，同一份定义反复启动不必重编；
        /// 缓存键用事件 ID 加内容哈希，源码换了就重编。
        /// </summary>
        private PevtCompiledProgram GetOrCompile(PevtProgramDefinition definition)
        {
            string key = definition.EventId + "@" + definition.SourceHash;
            if (_compiled.TryGetValue(key, out PevtCompiledProgram cached))
                return cached;

            PevtCompileResult result = PevtCompiledProgram.Compile(definition, _commands.Catalog);
            if (!result.Success)
            {
                throw new PevtEventStartException(new PevtRuntimeDiagnostic("PEVTR4304",
                    $"事件 `{definition.EventId}` 使用了当前运行时尚未支持的构造：{string.Join("、", result.UnsupportedFeatures)}"));
            }

            _compiled[key] = result.Program;
            return result.Program;
        }

        // ---- IPevtSubEventProvider ----

        /// <summary>
        /// <c>callevt</c> 的运行时目标解析。
        ///
        /// 跨来源冲突的 ID 先判 <see cref="PevtSubEventStatus.Ambiguous"/>：那种情况下"保留先注册项"
        /// 只是为了让同一次启动的结果稳定，并不表示它是唯一合法目标，不能拿它当调用目标。
        /// </summary>
        public PevtSubEventStatus TryResolve(string eventId, out PevtCompiledProgram program, out bool declaresAsync)
        {
            program = null;
            declaresAsync = false;

            foreach (PevtEventConflict conflict in Registry.Events.FatalConflicts)
            {
                if (string.Equals(conflict.EventId, eventId, StringComparison.Ordinal))
                    return PevtSubEventStatus.Ambiguous;
            }

            if (!Registry.Events.TryGet(eventId, out PevtEventCandidate candidate))
                return PevtSubEventStatus.NotFound;

            try
            {
                program = GetOrCompile(candidate.Definition);
            }
            catch (PevtEventStartException)
            {
                return PevtSubEventStatus.StartFailed;
            }

            declaresAsync = candidate.Definition.HasAsyncCapability;
            return PevtSubEventStatus.Found;
        }
    }

    /// <summary>启动事件失败。诊断编号来自运行诊断表，不另造。</summary>
    public sealed class PevtEventStartException : Exception
    {
        public PevtRuntimeDiagnostic Diagnostic { get; }

        public PevtEventStartException(PevtRuntimeDiagnostic diagnostic)
            : base(diagnostic?.Message ?? "事件启动失败。")
        {
            Diagnostic = diagnostic;
        }
    }
}
