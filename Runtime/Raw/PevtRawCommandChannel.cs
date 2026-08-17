using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Runtime.Raw
{
    /// <summary>
    /// 一次原版 EV 文本会话的宿主实现。
    /// </summary>
    public interface IPevtRawCommandBridge
    {
        /// <summary>
        /// 启动一次会话。返回 null 表示原版拒绝了这段文本（解析失败或无法压栈），
        /// 通道会把它翻成 PEVTR4101。
        /// </summary>
        IPevtRawCommandSession Begin(string rawText);
    }

    /// <summary>一次正在进行的原版 EV 文本会话。</summary>
    public interface IPevtRawCommandSession
    {
        /// <summary>原版是否已经读完这段文本（正常或异常）。</summary>
        bool IsFinished { get; }

        /// <summary>非空表示异常结束；通道会把它翻成 PEVTR4101 的消息。</summary>
        string FailureMessage { get; }

        /// <summary>请求提前结束，幂等。会话确认停下之前 <see cref="IsFinished"/> 仍为 false。</summary>
        void RequestCancel();

        /// <summary>
        /// 收尾：撤销这次会话占用的一切——内存事件内容、临时人物资料、原版栈上的 reader。
        /// 正常结束、失败和取消三条路径都会走到，且只会走到一次。
        /// </summary>
        void Release();
    }

    /// <summary>
    /// <c>$raw cmd</c> 的进程级通道。
    /// </summary>
    public sealed class PevtRawCommandChannel : IPevtRawCommands
    {
        private readonly IPevtRawCommandBridge _bridge;
        private readonly List<PevtRawCommandWait> _queue = new List<PevtRawCommandWait>();

        private PevtRawCommandWait _active;

        /// <summary>已经启动过的会话总数，供只读诊断与测试断言使用。</summary>
        public int StartedSessionCount { get; private set; }

        public PevtRawCommandChannel(IPevtRawCommandBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public bool IsBusy => _active != null;

        public int QueueLength => _queue.Count;

        /// <summary>排队中的原始文本，按先后顺序。只读诊断用。</summary>
        public IReadOnlyList<string> PendingCommands
        {
            get
            {
                var result = new List<string>(_queue.Count);
                foreach (PevtRawCommandWait wait in _queue)
                    result.Add(wait.CommandText);
                return new ReadOnlyCollection<string>(result);
            }
        }

        public PevtWait Submit(string rawText)
        {
            var wait = new PevtRawCommandWait(this, rawText ?? string.Empty);
            _queue.Add(wait);
            return wait;
        }

        /// <summary>
        /// 插件卸载或事件强制停止时的兜底清理。
        /// </summary>
        public IReadOnlyList<Exception> ReleaseAll()
        {
            var failures = new List<Exception>();

            var queued = new List<PevtRawCommandWait>(_queue);
            _queue.Clear();

            foreach (PevtRawCommandWait pending in queued)
            {
                try
                {
                    pending.ForceRelease();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            PevtRawCommandWait active = _active;
            if (active != null)
            {
                try
                {
                    active.ForceRelease();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _active = null;
            return new ReadOnlyCollection<Exception>(failures);
        }

        // ---- 由 PevtRawCommandWait 调用 ----

        private bool IsHead(PevtRawCommandWait wait) => _queue.Count > 0 && ReferenceEquals(_queue[0], wait);

        /// <summary>
        /// 队首尝试开跑。返回值区分三种情况：null 表示还没轮到（继续等待），
        /// 非 null 的 session 表示已开始，failure 非 null 表示原版拒绝。
        /// </summary>
        internal IPevtRawCommandSession TryStart(PevtRawCommandWait wait, out PevtRuntimeDiagnostic failure)
        {
            failure = null;

            ReclaimAbandonedSession();

            if (_active != null || !IsHead(wait))
                return null;

            _queue.RemoveAt(0);
            _active = wait;
            StartedSessionCount++;

            IPevtRawCommandSession session;
            try
            {
                session = _bridge.Begin(wait.CommandText);
            }
            catch (Exception ex)
            {
                _active = null;
                failure = new PevtRuntimeDiagnostic("PEVTR4101",
                    $"原版事件运行时拒绝了 `$raw cmd`：{ex.GetType().Name}: {ex.Message}", innerException: ex);
                return null;
            }

            if (session != null)
                return session;

            _active = null;
            failure = new PevtRuntimeDiagnostic("PEVTR4101", "原版事件运行时拒绝了 `$raw cmd` 的原始文本（解析失败或无法压栈）。");
            return null;
        }

        /// <summary>会话结束（成功、失败或取消）后放开通道。</summary>
        internal void Finish(PevtRawCommandWait wait)
        {
            if (ReferenceEquals(_active, wait))
                _active = null;
        }

        /// <summary>
        /// 回收被放弃的活动会话：拥有它的例程可能被 <c>kill</c>、异常终止或父级取消后再也不被推进，
        /// 活动槽会永久占着，后面排队的 <c>$raw cmd</c> 永远等不到自己。判据是等待自己的状态——已进入终态说明不再需要通道，
        /// 处于 <see cref="PevtWaitState.Cancelling"/> 说明取消请求已发给原版、没人还在等结果，直接释放。
        /// </summary>
        private void ReclaimAbandonedSession()
        {
            PevtRawCommandWait active = _active;
            if (active == null)
                return;

            if (active.IsCompleted)
            {
                _active = null;
                return;
            }

            if (active.State == PevtWaitState.Cancelling)
                active.ForceRelease();
        }

        /// <summary>还在排队时被取消：直接从队列里摘掉。</summary>
        internal void Dequeue(PevtRawCommandWait wait) => _queue.Remove(wait);
    }

    /// <summary>
    /// 一次 <c>$raw cmd</c> 的等待：先排队，轮到自己后启动原版会话，再等它读完。
    /// </summary>
    public sealed class PevtRawCommandWait : PevtWait
    {
        private readonly PevtRawCommandChannel _channel;
        private IPevtRawCommandSession _session;
        private bool _released;

        internal PevtRawCommandWait(PevtRawCommandChannel channel, string rawText)
        {
            _channel = channel;
            CommandText = rawText;
        }

        public string CommandText { get; }

        /// <summary>会话是否已经启动。</summary>
        public bool IsRunning => _session != null;

        public override string ProgressSource => _session != null ? "原版 EV 文本会话" : "原版 EV 文本会话队列";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_session == null)
            {
                _session = _channel.TryStart(this, out PevtRuntimeDiagnostic failure);
                if (failure != null)
                {
                    CompleteFaulted(failure);
                    return;
                }

                if (_session == null)
                    return; // 还没轮到。
            }

            if (!Poll(out string failureMessage))
                return;

            ReleaseSession();

            if (failureMessage != null)
                CompleteFaulted(new PevtRuntimeDiagnostic("PEVTR4101", $"`$raw cmd` 执行失败：{failureMessage}"));
            else
                CompleteSucceeded();
        }

        protected override void OnCancelRequested()
        {
            if (_session == null)
            {
                // 还在排队：摘掉即可，原版那边什么都没发生。
                _channel.Dequeue(this);
                return;
            }

            try
            {
                _session.RequestCancel();
            }
            catch (Exception)
            {
                // 取消请求本身失败也要继续走确认流程，最终由 Release 收尾。
            }
        }

        protected override void OnCancelTick(PevtWaitContext context)
        {
            if (_session != null && !Poll(out string _))
                return;

            ReleaseSession();
            CompleteCancelled();
        }

        /// <summary>
        /// 通道兜底清理：不等原版确认，直接请求停止、释放会话并把等待标成取消。
        /// </summary>
        internal void ForceRelease()
        {
            if (_session != null)
            {
                try
                {
                    _session.RequestCancel();
                }
                catch (Exception)
                {
                }
            }

            ReleaseSession();
            CompleteCancelled();
        }

        /// <summary>读一次会话状态。返回 true 表示已结束；<paramref name="failureMessage"/> 非空表示异常结束。</summary>
        private bool Poll(out string failureMessage)
        {
            failureMessage = null;
            if (_session == null)
                return true;

            try
            {
                failureMessage = _session.FailureMessage;
                return _session.IsFinished || failureMessage != null;
            }
            catch (Exception ex)
            {
                failureMessage = $"{ex.GetType().Name}: {ex.Message}";
                return true;
            }
        }

        private void ReleaseSession()
        {
            if (!_released && _session != null)
            {
                _released = true;
                try
                {
                    _session.Release();
                }
                catch (Exception)
                {
                    // Release 是收尾动作；它失败不改变本次等待的结果，只可能留下需要人工排查的原版状态。
                }
            }

            _channel.Finish(this);
        }
    }
}
