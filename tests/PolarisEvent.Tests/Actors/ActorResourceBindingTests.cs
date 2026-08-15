using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Diagnostics;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// PEVT9111 / PEVT9117 / PEVT9118 的发射路径。这三条不由 XML 语法决定，而由资源字段的静态事实决定，
    /// 因此 Core 提供唯一判定，工具侧与游戏侧共用；这里同时验证合法字段不会误报任一编号。
    /// </summary>
    public class ActorResourceBindingTests
    {
        private static ActorResourceFieldInfo Field(
            string typeName = ActorResourceBinding.PxlsCharacterHandleTypeName,
            bool isStatic = true,
            bool isAccessible = true,
            bool hasResourceAttribute = true,
            bool hasFolderAttribute = true) =>
            new ActorResourceFieldInfo(typeName, isStatic, isAccessible, hasResourceAttribute, hasFolderAttribute);

        private static string[] Validate(ActorVisualKind kind, ActorResourceFieldInfo field, ActorVisualResource resource = null)
        {
            var bag = new DiagnosticBag();
            ActorResourceBinding.Validate(
                resource ?? ActorVisualResource.FromPolarisResField("MyMod.Resources.Iris"),
                kind,
                field,
                null,
                bag);
            return bag.ToReadOnly().Select(d => d.Id).ToArray();
        }

        [Theory]
        [InlineData(ActorVisualKind.WorldSprite, ActorResourceType.PxlsCharacterHandle)]
        [InlineData(ActorVisualKind.Portrait, ActorResourceType.PxlsCharacterHandle)]
        [InlineData(ActorVisualKind.UiPortrait, ActorResourceType.MImage)]
        [InlineData(ActorVisualKind.Icon, ActorResourceType.MImage)]
        public void RequiredResourceType_FollowsTheVisualKind(ActorVisualKind kind, ActorResourceType expected) =>
            Assert.Equal(expected, ActorResourceBinding.GetRequiredResourceType(kind));

        [Theory]
        [InlineData(ActorVisualKind.WorldSprite, ActorResourceBinding.PxlsCharacterHandleTypeName)]
        [InlineData(ActorVisualKind.Portrait, "Polaris.Res.PxlsCharacterHandle")]
        [InlineData(ActorVisualKind.UiPortrait, ActorResourceBinding.MImageTypeName)]
        [InlineData(ActorVisualKind.Icon, "Polaris.UI.MImage")]
        public void ValidField_ProducesNoDiagnostics(ActorVisualKind kind, string typeName) =>
            Assert.Empty(Validate(kind, Field(typeName)));

        [Theory]
        [InlineData(ActorVisualKind.Portrait, ActorResourceBinding.MImageTypeName)]
        [InlineData(ActorVisualKind.WorldSprite, ActorResourceBinding.MImageTypeName)]
        [InlineData(ActorVisualKind.UiPortrait, ActorResourceBinding.PxlsCharacterHandleTypeName)]
        [InlineData(ActorVisualKind.Icon, ActorResourceBinding.PxlsCharacterHandleTypeName)]
        [InlineData(ActorVisualKind.Portrait, "MyPxlsCharacterHandle")]
        [InlineData(ActorVisualKind.Portrait, "")]
        public void Pevt9111_ReportsResourceTypeMismatch(ActorVisualKind kind, string typeName) =>
            Assert.Contains("PEVT9111", Validate(kind, Field(typeName)));

        [Fact]
        public void Pevt9117_ReportsEveryUnmetBindingCondition()
        {
            Assert.Contains("PEVT9117", Validate(ActorVisualKind.Portrait, Field(isStatic: false)));
            Assert.Contains("PEVT9117", Validate(ActorVisualKind.Portrait, Field(isAccessible: false)));
            Assert.Contains("PEVT9117", Validate(ActorVisualKind.Portrait, Field(hasResourceAttribute: false)));
            Assert.Contains("PEVT9117", Validate(ActorVisualKind.Portrait, Field(hasFolderAttribute: false)));

            // 多个条件同时不满足时全部报告，不在第一条就停下。
            string[] all = Validate(ActorVisualKind.Portrait, Field(
                typeName: ActorResourceBinding.MImageTypeName,
                isStatic: false,
                isAccessible: false,
                hasResourceAttribute: false,
                hasFolderAttribute: false));
            Assert.Equal(4, all.Count(id => id == "PEVT9117"));
            Assert.Contains("PEVT9111", all);
        }

        [Fact]
        public void Pevt9118_DowngradesToWarningWhenTheFieldCannotBeResolvedYet()
        {
            var bag = new DiagnosticBag();
            ActorResourceBinding.Validate(
                ActorVisualResource.FromPolarisResField("MyMod.Resources.Iris"),
                ActorVisualKind.Portrait,
                null,
                null,
                bag);

            Diagnostic diagnostic = Assert.Single(bag.ToReadOnly());
            Assert.Equal("PEVT9118", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.False(bag.HasErrors);
        }

        [Fact]
        public void GamePxlsReferences_AreNotSubjectToFieldBinding()
        {
            string[] ids = Validate(
                ActorVisualKind.Portrait,
                Field(typeName: ActorResourceBinding.MImageTypeName, isStatic: false),
                ActorVisualResource.FromGameAsset("EvImg/__ev_n.pxls"));

            Assert.Empty(ids);
        }

        [Fact]
        public void BuiltinCatalogVisuals_NeverRequireFieldBinding()
        {
            var bag = new DiagnosticBag();
            foreach (ActorDefinition actor in BuiltinActorCatalog.Catalog.Actors)
            {
                foreach (ActorVisual visual in actor.Portraits)
                    ActorResourceBinding.Validate(visual.Resource, visual.Kind, null, null, bag);
                if (actor.WorldSprite != null)
                    ActorResourceBinding.Validate(actor.WorldSprite.Resource, actor.WorldSprite.Kind, null, null, bag);
            }

            Assert.Empty(bag.ToReadOnly());
        }
    }
}
