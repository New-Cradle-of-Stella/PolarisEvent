using Polaris.Pevt.Actors;
using Xunit;

namespace Polaris.Event.Tests.Actors
{
    /// <summary>
    /// 延迟视觉访问器的键格式。工具侧生成器与游戏侧资源桥各拼一遍字符串是这条规则最容易漂移的
    /// 地方，漂移的表现又是"模组自定义立绘静默不出现"，所以把格式钉死在测试里。
    /// </summary>
    public class ActorVisualKeyTests
    {
        [Fact]
        public void Portrait_UsesPrefixedLocalId()
        {
            Assert.Equal("portrait:default", ActorVisualKeys.Portrait("default"));
            Assert.Equal("ui:small", ActorVisualKeys.UiPortrait("small"));
        }

        [Fact]
        public void SingletonVisuals_HaveFixedKeys()
        {
            Assert.Equal("world", ActorVisualKeys.WorldSprite);
            Assert.Equal("icon", ActorVisualKeys.Icon);
        }

        [Theory]
        [InlineData(ActorVisualKind.WorldSprite, "ignored", "world")]
        [InlineData(ActorVisualKind.Icon, "ignored", "icon")]
        [InlineData(ActorVisualKind.Portrait, "bass", "portrait:bass")]
        [InlineData(ActorVisualKind.UiPortrait, "bust", "ui:bust")]
        public void For_MatchesPerKindFormat(ActorVisualKind kind, string visualId, string expected) =>
            Assert.Equal(expected, ActorVisualKeys.For(kind, visualId));

        /// <summary>ID 为 null 时仍然产生一个稳定的键，不抛异常——查不到访问器是正常结果。</summary>
        [Fact]
        public void NullId_StillProducesStableKey()
        {
            Assert.Equal("portrait:", ActorVisualKeys.Portrait(null));
            Assert.Equal("ui:", ActorVisualKeys.UiPortrait(null));
        }
    }
}
