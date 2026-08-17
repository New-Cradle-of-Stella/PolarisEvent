using System;
using System.Collections.Generic;
using System.Reflection;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Raw;

namespace Polaris.Event.Game
{
    /// <summary>
    /// PolarisEvent 的游戏侧运行时：注册、宿主、每帧更新点与事件模式切换都收在这里。
    /// </summary>
    public sealed class PevtGameRuntime
    {
        private readonly PevtGameClock _clock = new PevtGameClock();

        private PevtGameSessionServices _session;
        private bool _eventModeActive;

        public PevtGameRuntime()
        {
            // 两个逃生口都是进程级的：原版 EV 是单套静态执行器，所以会话通道必须跨事件共享；
            // `$raw cs` 的编译缓存同理，每个事件一份等于每次启动事件都重编一遍同一段 C#。
            RawCommands = new PevtRawCommandChannel(new RawCmd.PevtGameRawCommandBridge());

            // Roslyn 延迟加载：整套编译器程序集只在真的遇到第一个 `$raw cs` 时才进入进程。
            // 绝大多数事件一行 `$raw cs` 都没有，没理由让它们为这个逃生口付启动成本。
            RawCs = new PevtRawCsExecutor(new PevtLazyRawCsCompiler(
                () => PevtRoslynRawCsCompiler.FromLoadedAssemblies()));

            // 扫描器拿到同一个执行器当分析器：嵌入源在游戏侧重校时，`$raw cs` 的 C# 内容也要过
            // PEVT8007–8010，而且那一次编译的结果直接进缓存，运行时不必再编一遍。
            Registry = new PevtRegistryScanner(null, null, RawCs);

            Host = new PevtEventHost(Registry, _clock, CreateServices);
        }

        /// <summary><c>$raw cmd</c> 的进程级原版会话通道。</summary>
        public PevtRawCommandChannel RawCommands { get; }

        /// <summary><c>$raw cs</c> 的受信任执行器，同时也是加载期的 C# 分析器。</summary>
        public PevtRawCsExecutor RawCs { get; }

        public PevtRegistryScanner Registry { get; }

        public PevtEventHost Host { get; }

        /// <summary>扫描结果；尚未 <see cref="Seal"/> 时为 null。</summary>
        public PevtScanReport Report { get; private set; }

        /// <summary>当前根事件；没有事件在跑时为 null。</summary>
        public PevtEventInstance Current => Host.Root;

        /// <summary>宿主自己的帧号，与 PEVT 的等待同一单位。供只读诊断查询。</summary>
        public long Frame => _clock.Frame;

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
            var failures = new List<Exception>(Host.Stop());

            // 必须排在 FinishSession 之前：ExitEventMode 以 `EV.isActive()` 为闸门决定要不要恢复 GMain / GHandle。
            // 一个还留在原版栈上的 raw reader 会让它误判成"原版事件在跑"而跳过恢复，游戏就停在暂停状态。
            failures.AddRange(RawCommands.ReleaseAll());

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
            var failures = new List<Exception>(Host.Shutdown());

            // 同 Stop：先把原版栈上的 reader 收掉，再让 ExitEventMode 判断要不要恢复游戏主循环。
            failures.AddRange(RawCommands.ReleaseAll());

            FinishSession();
            return failures;
        }

        // ---- 内部 ----

        private PevtServices CreateServices(string eventId)
        {
            _session = new PevtGameSessionServices(Registry.Actors, _clock, eventId, RawCommands, RawCs);
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
