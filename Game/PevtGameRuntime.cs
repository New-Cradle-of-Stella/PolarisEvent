using System;
using System.Collections.Generic;
using System.Reflection;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// PolarisEvent 的游戏侧运行时：注册、宿主、每帧更新点与事件模式切换都收在这里。
    ///
    /// 初始化顺序是固定的：先建 <see cref="PevtRegistryScanner"/>（它的构造函数就已经把内置
    /// <c>aic</c> 人物目录登记进去了），再扫描外部程序集的人物与事件 registrar，最后 Seal。
    /// 顺序反过来的话，外部目录就有机会抢占 <c>aic</c> 命名空间。
    /// </summary>
    public sealed class PevtGameRuntime
    {
        private readonly PevtGameClock _clock = new PevtGameClock();

        private PevtGameSessionServices _session;
        private bool _eventModeActive;

        public PevtGameRuntime()
        {
            Registry = new PevtRegistryScanner();
            Host = new PevtEventHost(Registry, _clock, CreateServices);
        }

        public PevtRegistryScanner Registry { get; }

        public PevtEventHost Host { get; }

        /// <summary>扫描结果；尚未 <see cref="Seal"/> 时为 null。</summary>
        public PevtScanReport Report { get; private set; }

        /// <summary>当前根事件；没有事件在跑时为 null。</summary>
        public PevtEventInstance Current => Host.Root;

        /// <summary>本次事件会话还没释放的原版资源租约数；没有事件在跑时为 0。供只读诊断查询。</summary>
        public int OutstandingLeaseCount => _session?.Resources.OutstandingLeaseCount ?? 0;

        /// <summary>本次事件会话登记的临时状态恢复项；没有事件在跑时为空。供只读诊断查询。</summary>
        public IReadOnlyList<string> PendingRestores =>
            _session?.Services.Session.PendingRestores ?? Array.AsReadOnly(Array.Empty<string>());

        // ---- 注册 ----

        /// <summary>扫描一批程序集里的事件与人物 registrar。必须在 <see cref="Seal"/> 之前调用。</summary>
        public void Scan(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                return;

            foreach (Assembly assembly in assemblies)
            {
                Assembly target = assembly;
                PevtGameHost.Guard("ScanAssembly", () => Registry.ScanAssembly(target));
            }
        }

        public PevtScanReport Seal()
        {
            Report = Registry.Seal();
            return Report;
        }

        // ---- 生命周期 ----

        /// <summary>按事件 ID 启动。目标不存在时抛 <see cref="PevtEventStartException"/>（PEVTR4301）。</summary>
        public PevtEventInstance Start(string eventId)
        {
            Stop();
            PevtEventInstance instance = Host.Start(eventId);
            EnterEventMode();
            return instance;
        }

        /// <summary>替换当前根事件。旧事件必须先完整清理，否则它的遮罩和 UI 会盖掉新事件刚设好的那一份。</summary>
        public PevtEventInstance Change(string eventId)
        {
            Stop();
            return Start(eventId);
        }

        /// <summary>停止当前根事件并级联清理。没有事件在跑时是空操作。</summary>
        public IReadOnlyList<Exception> Stop()
        {
            IReadOnlyList<Exception> failures = Host.Stop();
            FinishSession();
            return failures;
        }

        /// <summary>
        /// 宿主唯一的更新点。顺序固定：先推进时钟，再推进调度器；
        /// 反过来的话本帧建立的等待会用到上一帧的帧号，跨帧计时会差一帧。
        /// </summary>
        public void Update()
        {
            if (_session == null && !_eventModeActive)
                return;

            _clock.Advance();
            Host.Update();

            if (Host.Root == null)
                FinishSession();
        }

        /// <summary>插件卸载：停掉全部事件、清理会话并退出事件模式。</summary>
        public IReadOnlyList<Exception> Shutdown()
        {
            IReadOnlyList<Exception> failures = Host.Shutdown();
            FinishSession();
            return failures;
        }

        // ---- 内部 ----

        private PevtServices CreateServices(string eventId)
        {
            _session = new PevtGameSessionServices(Registry.Actors, _clock, eventId);
            return _session.Services;
        }

        private void EnterEventMode()
        {
            if (_eventModeActive)
                return;

            _eventModeActive = true;
            PevtGameHost.EnterEventMode();
        }

        /// <summary>
        /// 会话收尾。幂等：正常结束会从 <see cref="Update"/> 走到，异常终止和卸载会从
        /// <see cref="Stop"/> / <see cref="Shutdown"/> 走到，两边可能都触发。
        /// </summary>
        private void FinishSession()
        {
            if (_session != null)
            {
                PevtGameSessionServices session = _session;
                _session = null;
                session.Release();
            }

            if (!_eventModeActive)
                return;

            _eventModeActive = false;
            PevtGameHost.ExitEventMode();
        }
    }
}
