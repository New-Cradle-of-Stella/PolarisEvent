using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Runtime
{
    /// <summary>等待对象的状态机（异步协程与等待模型第 3 节）。后三个是终态。</summary>
    public enum PevtWaitState
    {
        /// <summary>已建立，尚未被调度器接管。</summary>
        Created,

        /// <summary>条件尚未满足。</summary>
        Pending,

        /// <summary>已请求取消，正在等待底层确认停止。</summary>
        Cancelling,

        /// <summary>正常完成。</summary>
        Succeeded,

        /// <summary>等待源报错。</summary>
        Faulted,

        /// <summary>因 kill、事件终止或父级取消而停止。</summary>
        Cancelled,
    }

    /// <summary>推进一次等待所需的最小上下文。刻意不含任何 Unity 或游戏类型。</summary>
    public sealed class PevtWaitContext
    {
        /// <summary>当前 PolarisEvent 更新帧号，单调递增。</summary>
        public long Frame { get; }

        public PevtWaitContext(long frame) => Frame = frame;
    }

    /// <summary>
    /// 全部跨帧等待的统一基类。PEVT 的每一次跨帧停顿都必须表现为一个可取消、可诊断的 PevtWait；
    /// Core 里不存在 Unity <c>YieldInstruction</c>，也没有"带字符串 kind 的万能等待"。
    /// </summary>
    public abstract class PevtWait
    {
        public PevtWaitState State { get; private set; } = PevtWaitState.Created;

        /// <summary>失败原因；只有 <see cref="PevtWaitState.Faulted"/> 时非 null。</summary>
        public PevtRuntimeDiagnostic Error { get; private set; }

        /// <summary>
        /// 本等待靠什么推进，供 PEVTR1002 停滞检测使用。每个等待类型都必须能说明自己的推进源。
        /// </summary>
        public abstract string ProgressSource { get; }

        /// <summary>是否已进入终态。</summary>
        public bool IsCompleted =>
            State == PevtWaitState.Succeeded || State == PevtWaitState.Faulted || State == PevtWaitState.Cancelled;

        /// <summary>本等待当前是否还有推进源。返回 false 且未完成时，调度器报 PEVTR1002。</summary>
        public virtual bool HasProgressSource => true;

        /// <summary>
        /// 是否允许等待源无限期保持 Pending。只应由玩家输入这类“等多久都合法”的等待开启；
        /// 资源、动作和状态轮询仍受全局停滞预算保护。
        /// </summary>
        public virtual bool AllowsIndefiniteWait => false;

        /// <summary>由调度器调用一次，把 <see cref="PevtWaitState.Created"/> 转成 <see cref="PevtWaitState.Pending"/>。</summary>
        public void Attach()
        {
            if (State == PevtWaitState.Created)
                State = PevtWaitState.Pending;
        }

        /// <summary>推进一次。完成后再调用是空操作。</summary>
        public void Tick(PevtWaitContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (IsCompleted)
                return;
            if (State == PevtWaitState.Created)
                Attach();

            if (State == PevtWaitState.Cancelling)
                OnCancelTick(context);
            else
                OnTick(context);
        }

        /// <summary>请求取消。幂等：已处于终态时不产生副作用。</summary>
        public void Cancel()
        {
            if (IsCompleted || State == PevtWaitState.Cancelling)
                return;

            State = PevtWaitState.Cancelling;
            OnCancelRequested();
        }

        protected abstract void OnTick(PevtWaitContext context);

        /// <summary>取消请求已发出、底层尚未确认时每帧调用。默认立即确认取消。</summary>
        protected virtual void OnCancelTick(PevtWaitContext context) => CompleteCancelled();

        /// <summary>收到取消请求时的一次性动作。默认什么都不做。</summary>
        protected virtual void OnCancelRequested()
        {
        }

        protected void CompleteSucceeded()
        {
            if (!IsCompleted)
                State = PevtWaitState.Succeeded;
        }

        protected void CompleteFaulted(PevtRuntimeDiagnostic error)
        {
            if (IsCompleted)
                return;
            Error = error ?? throw new ArgumentNullException(nameof(error));
            State = PevtWaitState.Faulted;
        }

        protected void CompleteCancelled()
        {
            if (!IsCompleted)
                State = PevtWaitState.Cancelled;
        }

        public override string ToString() => $"{GetType().Name}({State})";
    }

    /// <summary>有值等待。读取未 <see cref="PevtWaitState.Succeeded"/> 的结果是内部错误。</summary>
    public abstract class PevtWait<T> : PevtWait
    {
        private T _result;

        public T Result =>
            State == PevtWaitState.Succeeded
                ? _result
                : throw new InvalidOperationException($"等待尚未成功完成（当前 {State}），不能读取结果。");

        protected void CompleteSucceeded(T result)
        {
            _result = result;
            CompleteSucceeded();
        }
    }

    /// <summary>让出一次调度即完成。替代原版 <c>WAIT_1</c>，也用于每帧步数预算用尽时的内部让步。</summary>
    public sealed class PevtNextFrameWait : PevtWait
    {
        private long _attachedFrame = -1;

        public override string ProgressSource => "更新帧";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_attachedFrame < 0)
            {
                _attachedFrame = context.Frame;
                return;
            }

            if (context.Frame > _attachedFrame)
                CompleteSucceeded();
        }
    }

    /// <summary>等待指定逻辑帧数。<c>frames</c> 为 0 时在首次 Tick 就完成。</summary>
    public sealed class PevtFrameWait : PevtWait
    {
        private readonly int _frames;
        private long _startFrame = -1;

        public PevtFrameWait(int frames)
        {
            if (frames < 0)
                throw new ArgumentOutOfRangeException(nameof(frames), frames, "等待帧数不能为负数。");
            _frames = frames;
        }

        public int Frames => _frames;

        public override string ProgressSource => "更新帧";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_startFrame < 0)
                _startFrame = context.Frame;

            if (context.Frame - _startFrame >= _frames)
                CompleteSucceeded();
        }
    }

    /// <summary>
    /// 轮询受信任适配器的状态。谓词由 C# 侧登记，不向 PEVT 脚本暴露任意谓词。
    /// </summary>
    public sealed class PevtPredicateWait : PevtWait
    {
        private readonly Func<bool> _predicate;
        private readonly string _source;
        private readonly bool _allowsIndefiniteWait;

        public PevtPredicateWait(
            Func<bool> predicate,
            string progressSource = "状态轮询",
            bool allowsIndefiniteWait = false)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _source = progressSource;
            _allowsIndefiniteWait = allowsIndefiniteWait;
        }

        public override string ProgressSource => _source;

        public override bool AllowsIndefiniteWait => _allowsIndefiniteWait;

        protected override void OnTick(PevtWaitContext context)
        {
            if (_predicate())
                CompleteSucceeded();
        }
    }

    /// <summary>
    /// 等待一个已登记的一次性 C# 信号：UI 关闭、选择结果、动画回调等。
    /// 信号由适配器通过 <see cref="Signal"/> / <see cref="Fault"/> 推进，不靠轮询。
    /// </summary>
    public sealed class PevtSignalWait<T> : PevtWait<T>
    {
        private readonly string _source;
        private bool _signalled;
        private T _pending;
        private PevtRuntimeDiagnostic _pendingError;

        public PevtSignalWait(string progressSource = "一次性信号") => _source = progressSource;

        public override string ProgressSource => _source;

        /// <summary>适配器在信号到达时调用。重复调用只有第一次生效。</summary>
        public void Signal(T result)
        {
            if (_signalled || IsCompleted)
                return;
            _signalled = true;
            _pending = result;
        }

        /// <summary>适配器在信号源失败时调用。</summary>
        public void Fault(PevtRuntimeDiagnostic error)
        {
            if (_signalled || IsCompleted)
                return;
            _signalled = true;
            _pendingError = error ?? throw new ArgumentNullException(nameof(error));
        }

        protected override void OnTick(PevtWaitContext context)
        {
            if (!_signalled)
                return;

            if (_pendingError != null)
                CompleteFaulted(_pendingError);
            else
                CompleteSucceeded(_pending);
        }
    }

    /// <summary>资源加载票据等待。资源缺失时以 PEVTR4403 失败。</summary>
    public sealed class PevtResourceWait : PevtWait
    {
        private readonly Func<PevtResourceStatus> _poll;
        private readonly string _resourceId;

        public PevtResourceWait(string resourceId, Func<PevtResourceStatus> poll)
        {
            _resourceId = resourceId ?? string.Empty;
            _poll = poll ?? throw new ArgumentNullException(nameof(poll));
        }

        public string ResourceId => _resourceId;

        public override string ProgressSource => "资源加载票据";

        protected override void OnTick(PevtWaitContext context)
        {
            switch (_poll())
            {
                case PevtResourceStatus.Ready:
                    CompleteSucceeded();
                    break;
                case PevtResourceStatus.Failed:
                    CompleteFaulted(new PevtRuntimeDiagnostic("PEVTR4403", $"资源 `{_resourceId}` 加载失败。"));
                    break;
            }
        }
    }

    /// <summary>资源票据的三种状态。</summary>
    public enum PevtResourceStatus
    {
        Loading,
        Ready,
        Failed,
    }

    /// <summary>
    /// 等待一个或一组受管动作票据完成，<c>timeoutFrames</c> 为 0 表示不超时。
    /// 超时不算失败，而是以"未全部完成"成功返回。
    /// </summary>
    public sealed class PevtMotionWait : PevtWait<bool>
    {
        private readonly Func<bool> _allFinished;
        private readonly int _timeoutFrames;
        private long _startFrame = -1;

        public PevtMotionWait(Func<bool> allFinished, int timeoutFrames = 0)
        {
            _allFinished = allFinished ?? throw new ArgumentNullException(nameof(allFinished));
            if (timeoutFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutFrames), timeoutFrames, "超时帧数不能为负数。");
            _timeoutFrames = timeoutFrames;
        }

        public override string ProgressSource => "受管动作票据";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_startFrame < 0)
                _startFrame = context.Frame;

            if (_allFinished())
            {
                CompleteSucceeded(true);
                return;
            }

            if (_timeoutFrames > 0 && context.Frame - _startFrame >= _timeoutFrames)
                CompleteSucceeded(false);
        }
    }

    /// <summary>等待玩家确认或指定受控按键，<c>timeoutFrames</c> 为 0 表示无超时。结果表示是否由输入结束，而不是超时。</summary>
    public sealed class PevtInputWait : PevtWait<bool>
    {
        private readonly Func<bool> _pressed;
        private readonly int _timeoutFrames;
        private long _startFrame = -1;

        public PevtInputWait(Func<bool> pressed, int timeoutFrames = 0)
        {
            _pressed = pressed ?? throw new ArgumentNullException(nameof(pressed));
            if (timeoutFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutFrames), timeoutFrames, "超时帧数不能为负数。");
            _timeoutFrames = timeoutFrames;
        }

        public override string ProgressSource => "输入服务";

        // 显式 timeoutFrames 由本等待自己处理；0 的语言语义就是永久等待玩家输入。
        public override bool AllowsIndefiniteWait => true;

        protected override void OnTick(PevtWaitContext context)
        {
            if (_startFrame < 0)
                _startFrame = context.Frame;

            if (_pressed())
            {
                CompleteSucceeded(true);
                return;
            }

            if (_timeoutFrames > 0 && context.Frame - _startFrame >= _timeoutFrames)
                CompleteSucceeded(false);
        }
    }

    /// <summary>
    /// 组合等待：全部成员进入终态后才完成。任一成员失败时整体失败并保留首个失败原因；
    /// 取消会级联到全部成员。
    /// </summary>
    public sealed class PevtCompositeWait : PevtWait<int>
    {
        private readonly IReadOnlyList<PevtWait> _members;

        public PevtCompositeWait(IEnumerable<PevtWait> members)
        {
            if (members == null)
                throw new ArgumentNullException(nameof(members));

            var copy = new List<PevtWait>();
            foreach (PevtWait member in members)
                copy.Add(member ?? throw new ArgumentException("组合等待的成员不能为 null。", nameof(members)));
            _members = new ReadOnlyCollection<PevtWait>(copy);
        }

        public IReadOnlyList<PevtWait> Members => _members;

        public override string ProgressSource => "组合等待";

        public override bool HasProgressSource
        {
            get
            {
                foreach (PevtWait member in _members)
                {
                    if (!member.IsCompleted && member.HasProgressSource)
                        return true;
                }

                return _members.Count == 0;
            }
        }

        public override bool AllowsIndefiniteWait
        {
            get
            {
                foreach (PevtWait member in _members)
                {
                    if (!member.IsCompleted && member.AllowsIndefiniteWait)
                        return true;
                }

                return false;
            }
        }

        protected override void OnTick(PevtWaitContext context)
        {
            int succeeded = 0;
            bool allCompleted = true;
            PevtRuntimeDiagnostic firstError = null;

            foreach (PevtWait member in _members)
            {
                member.Tick(context);

                if (!member.IsCompleted)
                {
                    allCompleted = false;
                    continue;
                }

                if (member.State == PevtWaitState.Succeeded)
                    succeeded++;
                else if (member.State == PevtWaitState.Faulted && firstError == null)
                    firstError = member.Error;
            }

            if (!allCompleted)
                return;

            if (firstError != null)
                CompleteFaulted(firstError);
            else
                CompleteSucceeded(succeeded);
        }

        protected override void OnCancelRequested()
        {
            foreach (PevtWait member in _members)
                member.Cancel();
        }

        protected override void OnCancelTick(PevtWaitContext context)
        {
            foreach (PevtWait member in _members)
            {
                if (!member.IsCompleted)
                    member.Tick(context);
            }

            foreach (PevtWait member in _members)
            {
                if (!member.IsCompleted)
                    return;
            }

            CompleteCancelled();
        }
    }
}
