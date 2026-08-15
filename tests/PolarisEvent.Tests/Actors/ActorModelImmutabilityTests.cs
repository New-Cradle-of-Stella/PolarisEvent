using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// 深不可变门：人物目录模型不能只是 get-only 属性，还必须防御"调用者事后修改自己手里的 List"
    /// 这种别名攻击。每个持有集合的类型都用可变 <see cref="List{T}"/> 构造，构造后修改原集合再复查。
    /// </summary>
    public class ActorModelImmutabilityTests
    {
        private static ActorVisual Portrait(string id) =>
            new ActorVisual(id, ActorVisualKind.Portrait, ActorVisualResource.FromPolarisResField("M.R." + id));

        [Fact]
        public void ActorDefinition_MutatingOriginalListsAfterConstruction_DoesNotAffectActor()
        {
            var portraits = new List<ActorVisual> { Portrait("default") };
            var uiPortraits = new List<ActorVisual> { new ActorVisual("default", ActorVisualKind.UiPortrait, ActorVisualResource.FromPolarisResField("M.R.Ui")) };
            var appearances = new List<ActorAppearance> { new ActorAppearance("neutral", "default", "stand", "neutral") };
            var anchors = new List<ActorAnchor> { new ActorAnchor("spot", 1f, 2f) };

            var actor = new ActorDefinition(
                "iris",
                displayName: "Iris",
                defaultPortraitId: "default",
                portraits: portraits,
                uiPortraits: uiPortraits,
                appearances: appearances,
                anchors: anchors);

            portraits.Clear();
            portraits.Add(Portrait("injected"));
            uiPortraits.Clear();
            appearances.Clear();
            anchors.Clear();

            Assert.Single(actor.Portraits);
            Assert.Equal("default", actor.Portraits[0].Id);
            Assert.Single(actor.UiPortraits);
            Assert.Single(actor.Appearances);
            Assert.Single(actor.Anchors);
            Assert.False(actor.TryGetPortrait("injected", out _));
            Assert.Equal("default", actor.DefaultPortrait.Id);
        }

        [Fact]
        public void ActorCatalog_MutatingOriginalActorListAfterConstruction_DoesNotAffectCatalog()
        {
            var actors = new List<ActorDefinition> { new ActorDefinition("iris", displayName: "Iris") };

            var catalog = new ActorCatalog("example.mod", 1, isBuiltIn: false, sourcePath: "A.pactor", actors: actors);

            actors.Clear();
            actors.Add(new ActorDefinition("injected", displayName: "Injected"));

            Assert.Single(catalog.Actors);
            Assert.Equal("iris", catalog.Actors[0].LocalId);
            Assert.False(catalog.TryGetActorByLocalId("injected", out _));
        }

        [Fact]
        public void ActorDirectory_MutatingBuilderAfterBuild_DoesNotAffectSnapshot()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(new ActorCatalog("mod.a", 1, false, "A.pactor", new[] { new ActorDefinition("iris", displayName: "Iris") }), "ModA");

            ActorDirectory first = builder.Build();
            Assert.Throws<InvalidOperationException>(() =>
                builder.Add(new ActorCatalog("mod.b", 1, false, "B.pactor", new[] { new ActorDefinition("lily", displayName: "Lily") }), "ModB"));

            Assert.Single(first.Actors);
            Assert.False(first.Contains("mod.b:lily"));
        }

        [Fact]
        public void ExposedCollections_RejectMutationAttempts()
        {
            ActorCatalog catalog = BuiltinActorCatalog.Catalog;

            Assert.Throws<NotSupportedException>(() => ((IList<ActorDefinition>)catalog.Actors).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ActorVisual>)catalog.Actors.First(a => a.Portraits.Count > 0).Portraits).Clear());
        }

        [Fact]
        public void ActorDefinition_RejectsInconsistentConstructionInsteadOfSilentlyRepairing()
        {
            Assert.Throws<ArgumentException>(() => new ActorDefinition("iris"));
            Assert.Throws<ArgumentException>(() => new ActorDefinition("iris", displayName: "Iris", defaultPortraitId: "missing"));
            Assert.Throws<ArgumentException>(() => new ActorDefinition(
                "iris",
                displayName: "Iris",
                portraits: new[] { Portrait("default") },
                appearances: new[] { new ActorAppearance("a", "missing", "stand", "neutral") }));
            Assert.Throws<ArgumentException>(() => new ActorDefinition(
                "iris",
                displayName: "Iris",
                portraits: new[] { Portrait("default"), Portrait("default") }));
        }

        [Fact]
        public void ActorCatalog_RejectsInvalidNamespaceVersionOrDuplicateLocalId()
        {
            Assert.Throws<ArgumentException>(() => new ActorCatalog("Bad.Ns", 1, false, "A.pactor", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorCatalog("mod.a", 2, false, "A.pactor", null));
            Assert.Throws<ArgumentException>(() => new ActorCatalog("mod.a", 1, false, "A.pactor", new[]
            {
                new ActorDefinition("iris", displayName: "Iris"),
                new ActorDefinition("iris", displayName: "Iris 2"),
            }));
        }

        [Theory]
        [InlineData(float.NaN, 0f)]
        [InlineData(0f, float.NaN)]
        [InlineData(float.PositiveInfinity, 0f)]
        [InlineData(float.NegativeInfinity, 0f)]
        public void ActorAnchor_RejectsNonFiniteCoordinates(float x, float y) =>
            Assert.Throws<ArgumentException>(() => new ActorAnchor("spot", x, y));

        [Fact]
        public void ActorAnchor_RejectsHalfDeclaredEnterPosition()
        {
            Assert.Throws<ArgumentException>(() => new ActorAnchor("spot", 0f, 0f, enterX: 1f));
            Assert.Throws<ArgumentException>(() => new ActorAnchor("spot", 0f, 0f, enterY: 1f));
            Assert.Throws<ArgumentException>(() => new ActorAnchor("spot", 0f, 0f, enterX: float.NaN, enterY: 0f));
        }

        [Fact]
        public void ActorVisualResource_KeepsProviderAndPayloadMutuallyExclusive()
        {
            ActorVisualResource game = ActorVisualResource.FromGameAsset("EvImg/__ev_n.pxls");
            ActorVisualResource res = ActorVisualResource.FromPolarisResField("MyMod.Resources.Iris");

            Assert.Null(game.FieldReference);
            Assert.Null(game.DeclaringTypeName);
            Assert.Null(res.Asset);
            Assert.Equal("MyMod.Resources", res.DeclaringTypeName);
            Assert.Equal("Iris", res.FieldName);
            Assert.NotEqual(game, res);
            Assert.Equal(ActorVisualResource.FromGameAsset("EvImg/__ev_n.pxls"), game);
        }
    }
}
