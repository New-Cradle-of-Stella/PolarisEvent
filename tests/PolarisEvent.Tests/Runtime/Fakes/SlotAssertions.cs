using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime.Fakes
{
    /// <summary>读一个已初始化槽位的值。断言用，读不到就直接失败，省掉每处一个 TryGetSlot。</summary>
    public static class SlotAssertions
    {
        public static PevtValue SlotValue(this PevtEnvironment environment, string name)
        {
            Assert.True(environment.TryGetSlot(name, out PevtSlot slot), $"环境里没有槽位 `{name}`。");
            Assert.True(slot.IsInitialized, $"槽位 `{name}` 尚未初始化。");
            return slot.Value;
        }
    }
}
