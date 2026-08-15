using Polaris.Components;

namespace Polaris.Event
{
    /// <summary>高级事件封装与内置事件内容的组件入口。</summary>
    public sealed class PolarisEventComponent : PolarisComponent
    {
        public override string Id => "PolarisEvent";
        public override int Order => 800;
    }
}
