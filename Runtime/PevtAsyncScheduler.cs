using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime
{
    /// <summary>异步协程的生命周期状态。后三个是终态。</summary>
    public enum PevtAsyncState
    {
        Running,

        /// <summary>已请求取消，正在等待底层等待源确认停止。</summary>
        Cancelling,

        Succeeded,

        Faulted,

        Cancelled,
    }

    /// <summary>推进一次异步协程的结果。</summary>
    internal enum PevtAsyncStep
    {
        Waiting,
        Progressed,
        Completed,
        Faulted,
    }

    /// <summary>
    /// 一个异步协程的底层驱动。
    /// </summary>
    internal interface IPevtAsyncDriver
    {
        /// <summary>本驱动当前产出且尚未完成的等待；没有时为 null。供停滞检测查询推进源。</summary>
        PevtWait CurrentWait { get; }

        PevtAsyncStep Advance(PevtWaitContext context, out PevtRuntimeDiagnostic error);

        /// <summary>正常结束时的普通返回值契约校验。违反契约返回 <c>PEVTR5002</c>。</summary>
        PevtRuntimeDiagnostic TakeResult(out PevtValue result, out bool hasResult);

        /// <summary>一次性的取消请求。必须幂等。</summary>
        void RequestCancel();

        /// <summary>确认底层是否已经停下。返回 true 表示可以进入终态。</summary>
        bool ConfirmCancel(PevtWaitContext context);

        /// <summary>处置迭代器/子实例并逆序执行显式清理。</summary>
        IReadOnlyList<Exception> Dispose();
    }

    /// <summary>
    /// 一个由事件拥有的异步协程。
    /// </summary>
    public sealed class PevtAsyncRoutine
    {
        private readonly IPevtAsyncDriver _driver;

        /// <summary>请求取消后已经等了多少帧，用于 <c>PEVTR5003</c> 的取消宽限。</summary>
        private int _cancellingFrames;

        public long Id { get; }

        /// <summary>诊断展示用的来源描述，例如 <c>@actor_move_start</c> 或 <c>callevt "intro"</c>。</summary>
        public string Description { get; }

        /// <summary>预期普通返回类型；null 表示无返回值。</summary>
        public PevtType? ExpectedResultType { get; }

        public PevtAsyncState State { get; private set; } = PevtAsyncState.Running;

        public PevtValue Result { get; private set; }

        public bool HasResult { get; private set; }

        public PevtRuntimeDiagnostic Error { get; private set; }

        /// <summary>失败或取消是否已经通过 <c>await</c>/<c>kill</c> 观察过。</summary>
        public bool Observed { get; internal set; }

        internal PevtAsyncRoutine(long id, string description, PevtType? expectedResultType, IPevtAsyncDriver driver)
        {
            Id = id;
            Description = description ?? string.Empty;
            ExpectedResultType = expectedResultType;
            _driver = driver;
        }

        public bool IsFinished =>
            State == PevtAsyncState.Succeeded || State == PevtAsyncState.Faulted || State == PevtAsyncState.Cancelled;

        /// <summary>当前等待的推进源；正在被调度器逐帧推进但没有具体等待时为"更新帧"。</summary>
        public string CurrentWaitSource =>
            IsFinished ? null : _driver.CurrentWait?.ProgressSource ?? "更新帧";

        /// <summary>本协程是否还有推进源。子协程本身由调度器每帧推进，只有它内部的等待可能失去推进源。</summary>
        public bool HasProgressSource
        {
            get
            {
                if (IsFinished)
                    return true;

                PevtWait wait = _driver.CurrentWait;
                return wait == null || wait.HasProgressSource;
            }
        }

        /// <summary>当前子协程是否正合法地等待玩家输入。</summary>
        public bool AllowsIndefiniteWait => !IsFinished && _driver.CurrentWait?.AllowsIndefiniteWait == true;

        /// <summary>PEVT <c>status</c> 的取值：0 未结束，1 成功，2 失败或已取消。</summary>
        public int StatusCode
        {
            get
            {
                switch (State)
                {
                    case PevtAsyncState.Succeeded:
                        return 1;
                    case PevtAsyncState.Faulted:
                    case PevtAsyncState.Cancelled:
                        return 2;
                    default:
                        return 0;
                }
            }
        }

        /// <summary>推进一个更新帧。已进入终态时是空操作。</summary>
        internal void Tick(PevtWaitContext context, int cancelGraceFrames)
        {
            if (IsFinished)
                return;

            if (State == PevtAsyncState.Cancelling)
            {
                TickCancelling(context, cancelGraceFrames);
                return;
            }

            // 一帧内允许连续走多步无等待的原子步骤，但一旦产出等待或结束就交回调度器。
            while (true)
            {
                PevtAsyncStep step = _driver.Advance(context, out PevtRuntimeDiagnostic error);

                switch (step)
                {
                    case PevtAsyncStep.Waiting:
                        return;

                    case PevtAsyncStep.Progressed:
                        continue;

                    case PevtAsyncStep.Faulted:
                        Finish(PevtAsyncState.Faulted, error);
                        return;

                    case PevtAsyncStep.Completed:
                    default:
                    {
                        PevtRuntimeDiagnostic contractError = _driver.TakeResult(out PevtValue result, out bool hasResult);
                        if (contractError != null)
                        {
                            Finish(PevtAsyncState.Faulted, contractError);
                            return;
                        }

                        Result = result;
                        HasResult = hasResult;
                        Finish(PevtAsyncState.Succeeded, null);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 取消确认阶段。底层等待源可能立刻停下，也可能要几帧；宽限期内继续确认，
        /// 超时后按 <c>PEVTR5003</c> 强制断开，但仍然要走完处置与显式清理（第 10 节）。
        /// </summary>
        private void TickCancelling(PevtWaitContext context, int cancelGraceFrames)
        {
            if (_driver.ConfirmCancel(context))
            {
                Finish(PevtAsyncState.Cancelled, Error);
                return;
            }

            if (++_cancellingFrames < cancelGraceFrames)
                return;

            Error = new PevtRuntimeDiagnostic("PEVTR5003",
                $"{Description} 在取消宽限 {cancelGraceFrames} 帧内没能停下"
                + $"（等待源：{_driver.CurrentWait?.ProgressSource ?? "未知"}）。");
            Finish(PevtAsyncState.Cancelled, Error);
        }

        /// <summary>请求取消。幂等；已结束时不改变既有结果或异常（第 10 节）。</summary>
        internal void RequestCancel()
        {
            if (IsFinished || State == PevtAsyncState.Cancelling)
                return;

            State = PevtAsyncState.Cancelling;
            _cancellingFrames = 0;

            try
            {
                _driver.RequestCancel();
            }
            catch (Exception)
            {
                // 取消动作本身失败不阻止后续确认；真正停不下来会在宽限超时后报 PEVTR5003。
            }
        }

        /// <summary>立即结束并清理，不等取消确认。用于事件结束、替换和卸载这种没有下一帧的场合。</summary>
        internal IReadOnlyList<Exception> ForceFinish()
        {
            if (IsFinished)
                return Array.AsReadOnly(Array.Empty<Exception>());

            RequestCancel();
            IReadOnlyList<Exception> failures = _driver.Dispose();
            State = PevtAsyncState.Cancelled;
            return failures;
        }

        private void Finish(PevtAsyncState state, PevtRuntimeDiagnostic error)
        {
            _driver.Dispose();
            Error = error;
            State = state;
        }

        public override string ToString() => $"async#{Id} {Description} ({State})";
    }

    /// <summary>
    /// 一个事件拥有的子协程调度器。
    /// 与根事件调度器同一套规则：ID 稳定递增，每帧按 ID 升序推进，因此同一份输入必然得到同一条
    /// 执行轨迹——"同一帧多个句柄同时成功"这种竞争也就有了确定的解决顺序。
    /// </summary>
    public sealed class PevtAsyncScheduler
    {
        /// <summary>取消宽限帧数。超过后按 PEVTR5003 强制断开等待源。</summary>
        public const int CancelGraceFrames = 120;

        private readonly List<PevtAsyncRoutine> _routines = new List<PevtAsyncRoutine>();
        private long _nextId;

        public IReadOnlyList<PevtAsyncRoutine> Routines => new ReadOnlyCollection<PevtAsyncRoutine>(_routines);

        public int RunningCount
        {
            get
            {
                int count = 0;
                foreach (PevtAsyncRoutine routine in _routines)
                {
                    if (!routine.IsFinished)
                        count++;
                }

                return count;
            }
        }

        internal PevtAsyncRoutine Register(string description, PevtType? expectedResultType, IPevtAsyncDriver driver)
        {
            var routine = new PevtAsyncRoutine(++_nextId, description, expectedResultType, driver);
            _routines.Add(routine);
            return routine;
        }

        public bool TryGet(long id, out PevtAsyncRoutine routine)
        {
            foreach (PevtAsyncRoutine candidate in _routines)
            {
                if (candidate.Id == id)
                {
                    routine = candidate;
                    return true;
                }
            }

            routine = null;
            return false;
        }

        /// <summary>推进一个更新帧，按 ID 升序。</summary>
        internal void Tick(PevtWaitContext context)
        {
            // 快照一份：推进过程中新登记的子协程留到下一帧，避免登记顺序影响结果。
            var snapshot = new List<PevtAsyncRoutine>(_routines);
            foreach (PevtAsyncRoutine routine in snapshot)
                routine.Tick(context, CancelGraceFrames);
        }

        /// <summary>
        /// 取消并清理全部未结束子协程，按 ID 逆序——后启动的先停（第 10 节）。
        /// 事件 <c>end</c>、替换、异常结束和卸载都走这里。
        /// </summary>
        internal IReadOnlyList<Exception> ForceFinishAll()
        {
            var failures = new List<Exception>();
            for (int i = _routines.Count - 1; i >= 0; i--)
                failures.AddRange(_routines[i].ForceFinish());

            return new ReadOnlyCollection<Exception>(failures);
        }

        /// <summary>
        /// 失败、但从来没有被 <c>await</c> 或 <c>kill</c> 观察过的子协程。事件结束时逐条产生
        /// <c>PEVTR5005</c> 警告；这条诊断不反向中断已经继续执行的事件（第 11 节）。
        /// </summary>
        internal IReadOnlyList<PevtAsyncRoutine> UnobservedFailures()
        {
            var result = new List<PevtAsyncRoutine>();
            foreach (PevtAsyncRoutine routine in _routines)
            {
                if (routine.State == PevtAsyncState.Faulted && !routine.Observed)
                    result.Add(routine);
            }

            return new ReadOnlyCollection<PevtAsyncRoutine>(result);
        }
    }
}
