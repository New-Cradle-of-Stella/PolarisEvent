using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Runtime
{
    /// <summary>调度器里的一个例程实例：ID 稳定递增，决定推进顺序。</summary>
    public sealed class PevtRoutineInstance
    {
        public long Id { get; }

        public PevtExecution Execution { get; }

        public PevtOwnershipNode Ownership { get; }

        /// <summary>本次 Tick 的结果；尚未推进过时为 null。</summary>
        public PevtExecutionResult LastResult { get; internal set; }

        /// <summary>结束时所处的帧号；未结束时为 -1。</summary>
        public long CompletedFrame { get; internal set; } = -1;

        internal PevtRoutineInstance(long id, PevtExecution execution, PevtOwnershipNode ownership)
        {
            Id = id;
            Execution = execution;
            Ownership = ownership;
        }

        public bool IsFinished => Execution.IsFinished;

        public override string ToString() => $"routine#{Id} {Execution.EventId} ({Execution.Status})";
    }

    /// <summary>
    /// 确定性调度器：每个更新帧按例程 ID 升序推进，同帧完成的多个例程也按 ID 升序汇报，因此同一份输入必然得到同一条执行轨迹。
    /// 所有权树与调度器共用同一批实例，事件结束、替换、异常和卸载都走同一条级联取消加逆序清理路径。
    /// </summary>
    public sealed class PevtScheduler
    {
        private readonly List<PevtRoutineInstance> _instances = new List<PevtRoutineInstance>();
        private readonly IPevtClock _clock;
        private long _nextId;

        public PevtOwnershipTree Ownership { get; } = new PevtOwnershipTree();

        public PevtScheduler(IPevtClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public IReadOnlyList<PevtRoutineInstance> Instances => new ReadOnlyCollection<PevtRoutineInstance>(_instances);

        /// <summary>当前仍在运行的实例，按 ID 升序。</summary>
        public IReadOnlyList<PevtRoutineInstance> Running
        {
            get
            {
                var result = new List<PevtRoutineInstance>();
                foreach (PevtRoutineInstance instance in _instances)
                {
                    if (!instance.IsFinished)
                        result.Add(instance);
                }

                return new ReadOnlyCollection<PevtRoutineInstance>(result);
            }
        }

        /// <summary>登记一个根事件执行实例，并在所有权树上建立它的根节点。</summary>
        public PevtRoutineInstance Register(PevtExecution execution)
        {
            if (execution == null)
                throw new ArgumentNullException(nameof(execution));

            PevtOwnershipNode node = Ownership.CreateRoot($"event {execution.EventId}");
            var instance = new PevtRoutineInstance(++_nextId, execution, node);
            _instances.Add(instance);
            return instance;
        }

        /// <summary>
        /// 推进一个更新帧：按 ID 升序推进全部未结束实例。
        /// 返回本帧结束的实例，同样按 ID 升序。
        /// </summary>
        public IReadOnlyList<PevtRoutineInstance> Tick()
        {
            var finished = new List<PevtRoutineInstance>();

            // 快照一份列表：推进过程中新登记的实例留到下一帧，避免同一帧内的登记顺序影响结果。
            var snapshot = new List<PevtRoutineInstance>(_instances);
            snapshot.Sort((a, b) => a.Id.CompareTo(b.Id));

            foreach (PevtRoutineInstance instance in snapshot)
            {
                if (instance.IsFinished)
                    continue;

                instance.LastResult = instance.Execution.Resume();

                if (!instance.IsFinished)
                    continue;

                instance.CompletedFrame = _clock.Frame;
                Ownership.ReleaseCascade(instance.Ownership);
                finished.Add(instance);
            }

            return new ReadOnlyCollection<PevtRoutineInstance>(finished);
        }

        /// <summary>停止一个实例：级联取消并逆序清理。已结束时是幂等的。</summary>
        public IReadOnlyList<Exception> Stop(PevtRoutineInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var failures = new List<Exception>(instance.Execution.Cancel());
            failures.AddRange(Ownership.ReleaseCascade(instance.Ownership));
            instance.CompletedFrame = _clock.Frame;
            return new ReadOnlyCollection<Exception>(failures);
        }

        /// <summary>
        /// 用新事件替换旧事件：必须先完成旧根事件的清理，再启动新事件，
        /// 否则两者的临时状态会互相覆盖。
        /// </summary>
        public PevtRoutineInstance Replace(PevtRoutineInstance current, PevtExecution replacement)
        {
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            if (current != null)
                Stop(current);

            return Register(replacement);
        }

        /// <summary>停止全部实例并释放所有权树。用于插件卸载。</summary>
        public IReadOnlyList<Exception> StopAll()
        {
            var failures = new List<Exception>();

            var snapshot = new List<PevtRoutineInstance>(_instances);
            snapshot.Sort((a, b) => b.Id.CompareTo(a.Id)); // 逆序停止：后启动的先清理。

            foreach (PevtRoutineInstance instance in snapshot)
            {
                if (!instance.IsFinished)
                    failures.AddRange(instance.Execution.Cancel());
            }

            failures.AddRange(Ownership.ReleaseAll());
            return new ReadOnlyCollection<Exception>(failures);
        }

        /// <summary>移除已结束实例的记录。不影响仍在运行的实例。</summary>
        public int PruneFinished() => _instances.RemoveAll(instance => instance.IsFinished);

        /// <summary>
        /// 只移除 ID 小于 <paramref name="keepFromId"/> 的已结束实例。
        /// 宿主保留最近一段结束历史供诊断查询，调度器必须跟着同一个窗口，
        /// 否则两边的实例集合会对不上。
        /// </summary>
        public int PruneFinished(long keepFromId) =>
            _instances.RemoveAll(instance => instance.IsFinished && instance.Id < keepFromId);
    }
}
