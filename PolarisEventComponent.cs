using System;
using System.Collections.Generic;
using System.Reflection;
using Polaris.Components;
using Polaris.Event.Game;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event
{
    /// <summary>高级事件封装与内置事件内容的组件入口。</summary>
    public sealed class PolarisEventComponent : PolarisComponent
    {
        private static PevtGameRuntime _runtime;

        public override string Id => "PolarisEvent";

        public override int Order => 800;

        /// <summary>
        /// 组件初始化：先建注册表（构造时就登记了内置 <c>aic</c> 人物目录），
        /// 再扫描已加载程序集里的事件与人物 registrar，最后封闭注册。
        /// </summary>
        public override void Awake()
        {
            _runtime = new PevtGameRuntime();
            _runtime.Scan(AppDomain.CurrentDomain.GetAssemblies());

            PevtScanReport report = _runtime.Seal();
            if (report.HasFatalConflicts)
                PolarisAPI.Errors.Report(new InvalidOperationException(report.DescribeFatalConflicts()), "PolarisEvent.Registration");
        }

        /// <summary>每帧唯一的更新点，按固定顺序推进调度器。</summary>
        public override void Update() => _runtime?.Update();

        public override void Shutdown()
        {
            _runtime?.Shutdown();
            _runtime = null;
        }

        // ---- 公开入口 ----

        /// <summary>游戏侧运行时；组件尚未初始化时为 null。</summary>
        public static PevtGameRuntime Runtime => _runtime;

        /// <summary>当前根事件的只读视图；没有事件在跑时为 null。</summary>
        public static PevtEventInstance Current => _runtime?.Current;

        /// <summary>按事件 ID 启动一个已注册的 PEVT 事件。</summary>
        public static PevtEventInstance Start(string eventId) =>
            Require().Start(eventId);

        /// <summary>用另一个事件替换当前根事件；旧事件先完整清理。</summary>
        public static PevtEventInstance Change(string eventId) =>
            Require().Change(eventId);

        /// <summary>停止当前根事件并级联清理。</summary>
        public static IReadOnlyList<Exception> Stop() =>
            _runtime == null ? Array.AsReadOnly(Array.Empty<Exception>()) : _runtime.Stop();

        private static PevtGameRuntime Require() =>
            _runtime ?? throw new InvalidOperationException("PolarisEvent 组件尚未初始化，不能启动事件。");
    }
}
