using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Components;
using Polaris.Event.Game;
using Polaris.Event.Game.Debugging;
using Polaris.Event.Game.Live;
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

        public override void Awake()
        {
            // 设置项文案必须早于 Start 阶段的设置项扫描注册，绑定配置时要用说明文字查表。
            PevtDebugStrings.Register();
        }

        /// <summary>
        /// 注册：先建注册表（构造时就登记了内置 <c>aic</c> 人物目录），
        /// 再扫描插件与组件程序集里的事件与人物 registrar，最后封闭注册。
        /// <para>
        /// 必须在 Start 而不是 Awake：组件的 Awake 跑在 PolarisCore 插件自己的 Awake 里，
        /// 此时 BepInEx 还没加载排在 Polaris 之后的模组程序集（依赖 Polaris 的模组必然排在后面），
        /// 在那里扫描只看得到 Polaris 自己，模组的事件一个都进不来。Start 阶段全部插件已完成 Awake，
        /// 名单才是完整的——Res / Lang / UI 的扫描同理都在 Start。
        /// </para>
        /// </summary>
        public override void Start()
        {
            _runtime = new PevtGameRuntime();

            // 只扫插件与 Polaris 组件，不扫整个 AppDomain：后者要连游戏本体和 Unity 的大程序集
            // 一起反射翻一遍，而 registrar 只可能出现在这两类程序集里。组件不带 BepInPlugin，
            // 漏掉它们内置事件就没了，所以两边都要。
            _runtime.Scan(PolarisAPI.Modules.PluginAssemblies
                .Concat(PolarisAPI.Modules.ComponentAssemblies)
                .Distinct());

            PevtScanReport report = _runtime.Seal();
            if (report.HasFatalConflicts)
                PolarisAPI.Errors.Report(new InvalidOperationException(report.DescribeFatalConflicts()), "PolarisEvent.Registration");
        }

        /// <summary>
        /// 每帧唯一的更新点，按固定顺序推进调度器。
        /// 调试页排在调度器之后：这一帧刚推进出来的状态就是它要显示的那一份。
        /// </summary>
        public override void Update()
        {
            // 外部导入的对账排在推进之前：本帧要是重新导入并重启了事件，紧接着的调度器推进
            // 就作用在新的那一份上，不会先白跑一帧旧的。
            if (_runtime != null)
                PevtLiveRuntime.Sync();

            _runtime?.Update();
            PevtDebugPage.Update();
        }

        /// <summary>最后应用镜头震动，避免本帧后续的原版摄像机定位覆盖震动偏移。</summary>
        public override void LateUpdate()
        {
            _runtime?.LateUpdate();
        }

        public override void Shutdown()
        {
            // 先收热重载通道：它的撤销会动注册表，必须赶在运行时被丢掉之前完成。
            PevtLiveRuntime.Shutdown();
            PevtDebugPage.Shutdown();
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
