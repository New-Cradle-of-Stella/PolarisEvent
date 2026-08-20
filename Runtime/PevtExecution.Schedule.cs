using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 一条已排入但尚未触发的调度项（PEVT-E05）。这条记录本身就是"卸载指令"：
    /// 事件结束、异常或取消时把它从列表里丢掉即可，不需要"排入前的快照"，
    /// 因为它还没有产生任何效果——目标块要到到期那一帧才真正启动。
    /// </summary>
    public sealed class PevtScheduledItem
    {
        public string TimelineId { get; }

        /// <summary>到期时的 <see cref="IPevtClock.Frame"/>；到达或超过这个帧号就算到期。</summary>
        public long DueFrame { get; }

        public PevtBlockInfo Block { get; }

        public TextSpan Span { get; }

        internal PevtScheduledItem(string timelineId, long dueFrame, PevtBlockInfo block, TextSpan span)
        {
            TimelineId = timelineId;
            DueFrame = dueFrame;
            Block = block;
            Span = span;
        }

        public override string ToString() => $"#{TimelineId} due@{DueFrame} -> _{Block.Name}()";
    }

    public sealed partial class PevtExecution
    {
        /// <summary>尚未触发的调度项，按排入顺序。</summary>
        private readonly List<PevtScheduledItem> _schedules = new List<PevtScheduledItem>();

        /// <summary>只读快照，供 F8 的 Async 页面展示。</summary>
        public IReadOnlyList<PevtScheduledItem> Schedules => new ReadOnlyCollection<PevtScheduledItem>(_schedules);

        /// <summary>
        /// 检查全部尚未触发的项，到期的按排入顺序启动。和 <c>_async.Tick</c> 一样在 <see cref="Resume"/>
        /// 开头统一推进一次，这样同一帧里 <c>@wait_motion</c>/F8 看到的是这一帧已经启动过的状态。
        /// </summary>
        private void TickSchedules()
        {
            if (_schedules.Count == 0)
                return;

            long currentFrame = _services.Clock.Frame;
            List<PevtScheduledItem> due = null;

            foreach (PevtScheduledItem item in _schedules)
            {
                if (item.DueFrame <= currentFrame)
                    (due ??= new List<PevtScheduledItem>()).Add(item);
            }

            if (due == null)
                return;

            foreach (PevtScheduledItem item in due)
                _schedules.Remove(item);

            foreach (PevtScheduledItem item in due)
                LaunchScheduledBlock(item);
        }

        /// <summary>
        /// 启动一个到期（或被 <c>flush schedules</c> 提前触发）的项。做法和 <c>async block</c> 完全一样
        /// （见 <see cref="StartAsyncBlock"/>）：新建一个子执行实例，把块正文当根帧跑，登记进同一个
        /// 异步调度器——触发之后它就是一个普通的、事件所有的异步协程，`Stop`/异常清理不需要认识"它曾经是调度项"。
        /// </summary>
        private void LaunchScheduledBlock(PevtScheduledItem item)
        {
            var child = new PevtExecution(_program, _services, _commands, Budget, SubEvents, DynamicDepth, TotalDepth);
            PevtRuntimeDiagnostic error = child.EnterBlockAsRoot(item.Block, System.Array.Empty<PevtValue>(), item.Span);
            if (error != null)
            {
                _warnings.Add(error);
                return;
            }

            _async.Register("_" + item.Block.Name, item.Block.ReturnType, new ExecutionDriver(child, "_" + item.Block.Name));
        }

        private PevtExecutionResult ExecuteScheduleAfter(PevtFrame frame, PevtInstruction instruction)
        {
            PevtValue framesValue = Pop(frame);
            int frames = framesValue.AsInt;

            if (frames < 0)
                return Fault("PEVTR6001", $"`schedule` 的 frames 求值结果为负数，实际为 {frames}。", instruction.Span);

            if (!_program.TryGetBlock(instruction.HandlerName, out PevtBlockInfo block))
                return Fault("PEVTR9001", $"未定义的事件块 `{instruction.HandlerName}`。", instruction.Span);

            _schedules.Add(new PevtScheduledItem(instruction.Name, _services.Clock.Frame + frames, block, instruction.Span));
            frame.Ip++;
            return null;
        }

        /// <summary><c>flush schedules</c>：立即启动全部尚未触发的项，已经启动过的不受影响。</summary>
        private PevtExecutionResult ExecuteFlushSchedules(PevtFrame frame, PevtInstruction instruction)
        {
            if (_schedules.Count > 0)
            {
                var pending = new List<PevtScheduledItem>(_schedules);
                _schedules.Clear();

                foreach (PevtScheduledItem item in pending)
                    LaunchScheduledBlock(item);
            }

            frame.Ip++;
            return null;
        }

        /// <summary><c>clear schedules</c>：丢弃全部尚未触发的项，不停止已经启动的动作。</summary>
        private PevtExecutionResult ExecuteClearSchedules(PevtFrame frame, PevtInstruction instruction)
        {
            _schedules.Clear();
            frame.Ip++;
            return null;
        }
    }
}
