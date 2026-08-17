using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 单句柄 <c>await</c>。目标成功时返回它的普通值；失败或已被 <c>kill</c> 时产生
    /// <c>PEVTR5001</c>，并把原始异步异常作为内部原因保留（异步模型第 9 节）。
    /// </summary>
    public sealed class PevtHandlerWait : PevtWait<PevtValue>
    {
        private readonly PevtAsyncRoutine _routine;
        private readonly bool _wrapAsAwaitFailure;

        /// <param name="wrapAsAwaitFailure">
        /// true 是真正的 <c>await</c>：目标失败按 <c>PEVTR5001</c> 报，原异常作为内部原因。
        /// false 用于同步附着的构造（同步 <c>callevt</c>、<c>exec</c>），它们必须原样报出目标的诊断编号，
        /// 否则 PEVTR4xxx/PEVTR12xx 会被 5001 盖掉。
        /// </param>
        public PevtHandlerWait(PevtAsyncRoutine routine, bool wrapAsAwaitFailure = true)
        {
            _routine = routine ?? throw new ArgumentNullException(nameof(routine));
            _wrapAsAwaitFailure = wrapAsAwaitFailure;
        }

        public override string ProgressSource => "异步句柄 " + _routine.Description;

        /// <summary>子协程本身由调度器每帧推进；只有它内部的等待可能失去推进源。</summary>
        public override bool HasProgressSource => _routine.HasProgressSource;

        protected override void OnTick(PevtWaitContext context)
        {
            if (!_routine.IsFinished)
                return;

            _routine.Observed = true;

            if (_routine.State == PevtAsyncState.Succeeded)
            {
                CompleteSucceeded(_routine.Result);
                return;
            }

            if (!_wrapAsAwaitFailure && _routine.Error != null)
            {
                CompleteFaulted(_routine.Error);
                return;
            }

            CompleteFaulted(new PevtRuntimeDiagnostic("PEVTR5001",
                $"{_routine.Description} 以 {_routine.State} 结束，无法 await 它的结果。",
                _routine.Error?.Location, innerDiagnostic: _routine.Error));
        }
    }

    /// <summary>
    /// <c>await all</c>：全部目标进入终态后完成，结果是正常结束的数量（第 9 节）。
    /// 失败句柄不参与结果绑定，但同样算作"已观察"。
    /// </summary>
    public sealed class PevtAllHandlersWait : PevtWait<int>
    {
        private readonly IReadOnlyList<PevtAsyncRoutine> _routines;

        public PevtAllHandlersWait(IEnumerable<PevtAsyncRoutine> routines)
        {
            if (routines == null)
                throw new ArgumentNullException(nameof(routines));

            var copy = new List<PevtAsyncRoutine>();
            foreach (PevtAsyncRoutine routine in routines)
                copy.Add(routine ?? throw new ArgumentException("集合等待的成员不能为 null。", nameof(routines)));
            _routines = new ReadOnlyCollection<PevtAsyncRoutine>(copy);
        }

        public IReadOnlyList<PevtAsyncRoutine> Routines => _routines;

        public override string ProgressSource => "句柄列表（all）";

        public override bool HasProgressSource
        {
            get
            {
                foreach (PevtAsyncRoutine routine in _routines)
                {
                    if (!routine.HasProgressSource)
                        return false;
                }

                return true;
            }
        }

        protected override void OnTick(PevtWaitContext context)
        {
            int succeeded = 0;
            foreach (PevtAsyncRoutine routine in _routines)
            {
                if (!routine.IsFinished)
                    return;

                if (routine.State == PevtAsyncState.Succeeded)
                    succeeded++;
            }

            foreach (PevtAsyncRoutine routine in _routines)
                routine.Observed = true;

            CompleteSucceeded(succeeded);
        }
    }

    /// <summary>
    /// <c>await any</c>：每帧按输入列表顺序检查，第一个成功的句柄返回其序号（从 1 开始），随后请求取消其余句柄
    /// 并必须等它们全部进入终态才返回，否则未停下的动作会泄漏到后面的演出里；全部失败或被取消时返回 0。
    /// 同一帧多个句柄同时成功时取序号最小的那个，因此结果与调度顺序无关、可重现。
    /// </summary>
    public sealed class PevtAnyHandlersWait : PevtWait<int>
    {
        private readonly IReadOnlyList<PevtAsyncRoutine> _routines;
        private int _winner = -1;

        public PevtAnyHandlersWait(IEnumerable<PevtAsyncRoutine> routines)
        {
            if (routines == null)
                throw new ArgumentNullException(nameof(routines));

            var copy = new List<PevtAsyncRoutine>();
            foreach (PevtAsyncRoutine routine in routines)
                copy.Add(routine ?? throw new ArgumentException("集合等待的成员不能为 null。", nameof(routines)));
            _routines = new ReadOnlyCollection<PevtAsyncRoutine>(copy);
        }

        public IReadOnlyList<PevtAsyncRoutine> Routines => _routines;

        /// <summary>获胜句柄在输入列表中的从 1 开始的序号；尚未确定或全部失败时为 0。</summary>
        public int WinnerIndex => _winner < 0 ? 0 : _winner + 1;

        public override string ProgressSource => "句柄列表（any）";

        public override bool HasProgressSource
        {
            get
            {
                foreach (PevtAsyncRoutine routine in _routines)
                {
                    if (!routine.HasProgressSource)
                        return false;
                }

                return true;
            }
        }

        protected override void OnTick(PevtWaitContext context)
        {
            if (_winner < 0)
            {
                for (int i = 0; i < _routines.Count; i++)
                {
                    if (_routines[i].State == PevtAsyncState.Succeeded)
                    {
                        _winner = i;
                        break;
                    }
                }

                if (_winner >= 0)
                {
                    // 胜者已定：其余还在跑的立刻请求取消，但本帧不返回，等它们确认停下。
                    for (int i = 0; i < _routines.Count; i++)
                    {
                        if (i != _winner && !_routines[i].IsFinished)
                            _routines[i].RequestCancel();
                    }
                }
            }

            foreach (PevtAsyncRoutine routine in _routines)
            {
                if (!routine.IsFinished)
                    return;
            }

            foreach (PevtAsyncRoutine routine in _routines)
                routine.Observed = true;

            CompleteSucceeded(WinnerIndex);
        }
    }

    /// <summary>
    /// <c>kill</c> 使用的取消等待。目标真正停下（或取消超时）之前，当前同步流程不推进下一条语句。
    /// </summary>
    public sealed class PevtCancellationWait : PevtWait
    {
        private readonly PevtAsyncRoutine _routine;
        private bool _requested;

        public PevtCancellationWait(PevtAsyncRoutine routine)
        {
            _routine = routine ?? throw new ArgumentNullException(nameof(routine));
        }

        public override string ProgressSource => "取消确认 " + _routine.Description;

        public override bool HasProgressSource => true;

        protected override void OnTick(PevtWaitContext context)
        {
            if (!_requested)
            {
                _requested = true;
                _routine.RequestCancel();
            }

            if (!_routine.IsFinished)
                return;

            // 被 kill 掉的目标算作已观察：作者已经明确表示不再关心它的结果。
            _routine.Observed = true;
            CompleteSucceeded();
        }
    }
}
