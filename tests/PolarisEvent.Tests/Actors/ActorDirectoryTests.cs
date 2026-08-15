using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// 最终人物 ID、目录合并、内置封闭与冲突候选。人物表与 `/event` 事件表分离，冲突守卫按来源区分
    /// 同源静态错误 PEVT9106 与跨源致命冲突 PEVTR4404。
    /// </summary>
    public class ActorDirectoryTests
    {
        private static ActorCatalog Catalog(string ns, params string[] localIds) =>
            new ActorCatalog(ns, 1, isBuiltIn: false, sourcePath: $"Actors/{ns}.pactor",
                actors: localIds.Select(id => new ActorDefinition(id, displayName: id)));

        [Theory]
        [InlineData("aic", "noel", "aic:noel")]
        [InlineData("example.mod", "iris", "example.mod:iris")]
        [InlineData("a", "b", "a:b")]
        public void ComposeId_JoinsNamespaceAndLocalId(string ns, string localId, string expected)
        {
            Assert.Equal(expected, ActorNaming.ComposeId(ns, localId));
            Assert.True(ActorNaming.IsValidActorId(expected));
        }

        [Theory]
        [InlineData("noel")] // 缺命名空间
        [InlineData(":noel")]
        [InlineData("aic:")]
        [InlineData("aic:noel:extra")]
        [InlineData("Aic:noel")] // 大写命名空间
        [InlineData("aic:Noel")] // 大写局部 ID
        [InlineData("")]
        [InlineData(null)]
        public void IsValidActorId_RejectsMalformedIds(string actorId) =>
            Assert.False(ActorNaming.IsValidActorId(actorId));

        [Fact]
        public void TryGetActor_UsesOrdinalComparison()
        {
            ActorDirectory directory = Build((Catalog("example.mod", "iris"), "ModA"));

            Assert.True(directory.TryGetActor("example.mod:iris", out _));
            Assert.False(directory.TryGetActor("example.mod:Iris", out _));
            Assert.False(directory.TryGetActor("Example.Mod:iris", out _));
        }

        [Fact]
        public void Merge_KeepsActorsFromEveryCatalog()
        {
            ActorDirectory directory = Build(
                (Catalog("mod.a", "iris", "lily"), "ModA"),
                (Catalog("mod.b", "iris"), "ModB"));

            Assert.Equal(3, directory.Actors.Count);
            Assert.Empty(directory.Conflicts);
            Assert.True(directory.Contains("mod.a:iris"));
            Assert.True(directory.Contains("mod.b:iris"));
        }

        [Fact]
        public void CrossOwnerDuplicate_IsRecordedAsPevtr4404AndMakesTheIdUnusable()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(Catalog("shared.ns", "iris"), "ModA");
            IReadOnlyList<ActorConflict> conflicts = builder.Add(Catalog("shared.ns", "iris"), "ModB");
            ActorDirectory directory = builder.Build();

            ActorConflict conflict = Assert.Single(conflicts);
            Assert.False(conflict.IsSameOwner);
            Assert.Equal("PEVTR4404", conflict.DiagnosticId);
            Assert.Equal("shared.ns:iris", conflict.ActorId);
            Assert.Equal("ModA", conflict.RetainedOwner);
            Assert.Equal("ModB", conflict.IgnoredOwner);
            Assert.Contains("shared.ns.pactor", conflict.RetainedSourcePath);

            // 先注册项只为稳定报告而保留，冲突人物不能用于新事件。
            Assert.False(directory.TryGetActor("shared.ns:iris", out _));
            Assert.Single(directory.Actors);
            Assert.Equal("PEVTR4404", Assert.Single(directory.CreateConflictDiagnostics()).Id);
        }

        [Fact]
        public void SameOwnerDuplicate_IsRecordedAsPevt9106()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(Catalog("mod.a", "iris"), "ModA");
            IReadOnlyList<ActorConflict> conflicts = builder.Add(Catalog("mod.a", "iris"), "ModA");

            ActorConflict conflict = Assert.Single(conflicts);
            Assert.True(conflict.IsSameOwner);
            Assert.Equal("PEVT9106", conflict.DiagnosticId);
        }

        [Fact]
        public void ActorConflicts_DoNotAffectUnrelatedActors()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(Catalog("shared.ns", "iris", "lily"), "ModA");
            builder.Add(Catalog("shared.ns", "iris"), "ModB");
            ActorDirectory directory = builder.Build();

            Assert.False(directory.Contains("shared.ns:iris"));
            Assert.True(directory.Contains("shared.ns:lily"));
        }

        [Fact]
        public void Builder_RejectsExternalCatalogOccupyingTheClosedBuiltinNamespace()
        {
            var forged = new ActorCatalog("aic", 1, isBuiltIn: false, sourcePath: "Evil.pactor",
                actors: new[] { new ActorDefinition("noel", displayName: "Fake") });

            var builder = new ActorDirectoryBuilder();
            Assert.Throws<ArgumentException>(() => builder.Add(forged, "EvilMod"));
        }

        [Fact]
        public void BuiltinCatalog_IsRegisteredBeforeExternalCatalogs()
        {
            ActorDirectoryBuilder builder = BuiltinActorCatalog.CreateDirectoryBuilder();
            builder.Add(Catalog("mod.a", "iris"), "ModA");
            ActorDirectory directory = builder.Build();

            Assert.Equal(BuiltinActorCatalog.Owner, directory.Actors[0].Owner);
            Assert.True(directory.Actors[0].Catalog.IsBuiltIn);
            Assert.Equal("ModA", directory.Actors.Last().Owner);
        }

        [Fact]
        public void ExternalCatalog_CannotOverrideABuiltinActor()
        {
            // 外部目录连 aic 命名空间都进不去，因此 aic:noel 的解析结果始终来自内置目录。
            ActorDirectoryBuilder builder = BuiltinActorCatalog.CreateDirectoryBuilder();
            var forged = new ActorCatalog("aic", 1, isBuiltIn: false, sourcePath: "Evil.pactor",
                actors: new[] { new ActorDefinition("noel", displayName: "Fake") });

            Assert.Throws<ArgumentException>(() => builder.Add(forged, "EvilMod"));

            ActorDirectory directory = builder.Build();
            Assert.True(directory.TryGetActor("aic:noel", out ActorRegistration noel));
            Assert.Equal(BuiltinActorCatalog.Owner, noel.Owner);
        }

        [Fact]
        public void LegacyPersonKeys_AreCollectedOnlyFromBuiltinCatalogs()
        {
            // 外部目录即便通过程序化构造带上短键，也不会进入短键表。
            var portrait = new ActorVisual("default", ActorVisualKind.Portrait,
                ActorVisualResource.FromPolarisResField("M.R.A"), legacyPerson: "n");
            var external = new ActorCatalog("mod.a", 1, isBuiltIn: false, sourcePath: "A.pactor",
                actors: new[] { new ActorDefinition("iris", displayName: "Iris", defaultPortraitId: "default", portraits: new[] { portrait }) });

            ActorDirectory directory = Build((external, "ModA"));

            Assert.False(directory.TryGetActorByLegacyPerson("n", out _));
            Assert.Empty(directory.LegacyPersonKeys);
        }

        [Fact]
        public void Build_ReturnsASnapshotThatLaterAddsCannotChange()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(Catalog("mod.a", "iris"), "ModA");
            ActorDirectory directory = builder.Build();

            Assert.Throws<InvalidOperationException>(() => builder.Add(Catalog("mod.b", "lily"), "ModB"));
            Assert.Single(directory.Actors);
        }

        [Fact]
        public void EmptyDirectory_ResolvesNothing()
        {
            Assert.Empty(ActorDirectory.Empty.Actors);
            Assert.Empty(ActorDirectory.Empty.Conflicts);
            Assert.False(ActorDirectory.Empty.TryGetActor("aic:noel", out _));
            Assert.False(ActorDirectory.Empty.TryGetActor(null, out _));
        }

        private static ActorDirectory Build(params (ActorCatalog Catalog, string Owner)[] entries)
        {
            var builder = new ActorDirectoryBuilder();
            foreach ((ActorCatalog catalog, string owner) in entries)
                builder.Add(catalog, owner);
            return builder.Build();
        }
    }
}
