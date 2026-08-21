using System;
using System.Collections.Generic;
using Polaris.Event.Game;
using Polaris.Pevt.Runtime;

namespace Polaris.Event
{
    /// <summary>PolarisEvent 游戏运行时的统一公开入口。</summary>
    public static partial class EventAPI
    {
        /// <summary>游戏侧运行时；组件尚未初始化时为 <c>null</c>。</summary>
        public static PevtGameRuntime Runtime => PolarisEventComponent.Runtime;

        /// <summary>当前根事件；没有事件在运行时为 <c>null</c>。</summary>
        public static PevtEventInstance Current => PolarisEventComponent.Current;

        /// <summary>按事件 ID 启动一个已注册事件。</summary>
        public static PevtEventInstance Start(string eventId) => PolarisEventComponent.Start(eventId);

        /// <summary>停止旧事件并用指定事件替换当前根事件。</summary>
        public static PevtEventInstance Change(string eventId) => PolarisEventComponent.Change(eventId);

        /// <summary>停止当前根事件并返回清理期间收集的异常。</summary>
        public static IReadOnlyList<Exception> Stop() => PolarisEventComponent.Stop();
    }
}
