using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// 内置固定人物目录的 golden 测试。规范第 7.1 节把 `__vp_person.dat` 的 18 个稳定说话人键归并为 16 个公开 profile，
    /// 这里逐条钉死映射，任何一条改动都必须先改规范。
    /// </summary>
    public class BuiltinActorCatalogTests
    {
        /// <summary>原键 -> (公开人物 ID, portrait)。portrait 为 null 表示该键没有事件立绘。</summary>
        private static readonly (string LegacyKey, string ActorId, string PortraitId)[] StableLegacyKeys =
        {
            ("_", "aic:narrator", null),
            ("n", "aic:noel", "default"),
            ("nb", "aic:noel", "bass"),
            ("nb2", "aic:noel", "epbench"),
            ("v", "aic:laevi", "default"),
            ("p", "aic:primula", "default"),
            ("i", "aic:ixia", "default"),
            ("t", "aic:nightingale", "default"),
            ("d", "aic:tilde", "default"),
            ("l", "aic:alma", "default"),
            ("f", "aic:noel-father", "default"),
            ("g", "aic:mepha", "default"),
            ("s", "aic:ostrea", "default"),
            ("w", "aic:walross", "default"),
            ("bt", "aic:barten", "default"),
            ("so", "aic:tigrina", "default"),
            ("a", "aic:alice", "default"),
            ("fh", "aic:first-human", "default"),
        };

        [Fact]
        public void BuiltinCatalog_LoadsWithoutDiagnostics()
        {
            ActorCatalogReadResult result = BuiltinActorCatalog.Load();

            Assert.Empty(result.Diagnostics);
            Assert.True(result.Success);
            Assert.True(result.Catalog.IsBuiltIn);
            Assert.Equal("aic", result.Catalog.Namespace);
            Assert.Equal(1, result.Catalog.Version);
        }

        [Fact]
        public void BuiltinCatalog_Declares16PublicProfilesForThe18StableLegacyKeys()
        {
            ActorCatalog catalog = BuiltinActorCatalog.Catalog;

            Assert.Equal(18, StableLegacyKeys.Length);
            Assert.Equal(16, StableLegacyKeys.Select(entry => entry.ActorId).Distinct().Count());
            Assert.Equal(16, catalog.Actors.Count);

            IEnumerable<string> expected = StableLegacyKeys.Select(entry => entry.ActorId).Distinct().OrderBy(id => id, ActorNaming.IdComparer);
            IEnumerable<string> actual = catalog.Actors.Select(catalog.GetActorId).OrderBy(id => id, ActorNaming.IdComparer);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(LegacyKeyRows))]
        public void BuiltinCatalog_MapsEachStableLegacyKeyToItsPublicProfile(string legacyKey, string actorId, string portraitId)
        {
            ActorDirectory directory = BuiltinActorCatalog.CreateDirectoryBuilder().Build();

            Assert.True(directory.TryGetActorByLegacyPerson(legacyKey, out ActorRegistration registration), $"缺少原版短键 `{legacyKey}`。");
            Assert.Equal(actorId, registration.ActorId);

            if (portraitId == null)
            {
                Assert.Equal(legacyKey, registration.Actor.LegacyPerson);
                Assert.Empty(registration.Actor.Portraits);
                return;
            }

            Assert.True(registration.Actor.TryGetPortrait(portraitId, out ActorVisual portrait));
            Assert.Equal(legacyKey, portrait.LegacyPerson);
            Assert.Equal(ActorVisualProvider.GamePxls, portrait.Resource.Provider);
        }

        public static IEnumerable<object[]> LegacyKeyRows() =>
            StableLegacyKeys.Select(entry => new object[] { entry.LegacyKey, entry.ActorId, entry.PortraitId });

        [Fact]
        public void BuiltinCatalog_ExposesExactlyTheStableLegacyKeys()
        {
            ActorDirectory directory = BuiltinActorCatalog.CreateDirectoryBuilder().Build();

            Assert.Equal(
                StableLegacyKeys.Select(entry => entry.LegacyKey).OrderBy(key => key, ActorNaming.IdComparer),
                directory.LegacyPersonKeys.OrderBy(key => key, ActorNaming.IdComparer));
        }

        [Fact]
        public void Noel_HasExactlyThreeFixedPortraits()
        {
            ActorCatalog catalog = BuiltinActorCatalog.Catalog;

            Assert.True(catalog.TryGetActor("aic:noel", out ActorDefinition noel));
            Assert.Equal(
                new[] { "default", "bass", "epbench" },
                noel.Portraits.Select(portrait => portrait.Id));
            Assert.Equal("default", noel.DefaultPortraitId);
            Assert.Equal("EvImg/__ev_n.pxls", noel.DefaultPortrait.Resource.Asset);
            Assert.Equal("EvImg/__ev_n_bass.pxls", noel.Portraits[1].Resource.Asset);
            Assert.Equal("EvImg/__ev_n_epbench.pxls", noel.Portraits[2].Resource.Asset);
        }

        [Fact]
        public void Narrator_IsADialogueOnlyProfileWithoutVisuals()
        {
            ActorCatalog catalog = BuiltinActorCatalog.Catalog;

            Assert.True(catalog.TryGetActor("aic:narrator", out ActorDefinition narrator));
            Assert.Empty(narrator.Portraits);
            Assert.Null(narrator.WorldSprite);
            Assert.Null(narrator.DefaultPortraitId);
            Assert.Equal("_", narrator.LegacyPerson);
        }

        [Fact]
        public void BuiltinCatalog_UsesOnlyGamePxlsResources()
        {
            ActorCatalog catalog = BuiltinActorCatalog.Catalog;

            IEnumerable<ActorVisualResource> resources = catalog.Actors
                .SelectMany(actor => actor.Portraits.Concat(actor.UiPortraits).Concat(actor.WorldSprite != null ? new[] { actor.WorldSprite } : new ActorVisual[0]))
                .Select(visual => visual.Resource)
                .Concat(catalog.Actors.Where(actor => actor.Icon != null).Select(actor => actor.Icon));

            Assert.All(resources, resource =>
            {
                Assert.Equal(ActorVisualProvider.GamePxls, resource.Provider);
                Assert.Null(resource.FieldReference);
            });
        }

        [Fact]
        public void BuiltinCatalog_DoesNotLeakTalkerReplaceTemporaryKeys()
        {
            ActorDirectory directory = BuiltinActorCatalog.CreateDirectoryBuilder().Build();

            // 7.2 节列出的临时键必须完全不在全局人物表里。
            foreach (string temporary in new[] { "ann", "b", "cane", "cm", "cn", "dev", "fd", "ff", "fm", "ma", "mb", "mc", "mob", "ow", "pp", "st", "tc", "x", "xa", "xb" })
            {
                Assert.False(directory.TryGetActorByLegacyPerson(temporary, out _), $"临时键 `{temporary}` 不应进入全局人物表。");
                Assert.False(directory.Contains("aic:" + temporary));
            }
        }

        [Fact]
        public void BuiltinCatalog_LoadIsCachedAndStable()
        {
            Assert.Same(BuiltinActorCatalog.Load(), BuiltinActorCatalog.Load());
            Assert.Same(BuiltinActorCatalog.Catalog, BuiltinActorCatalog.Catalog);
        }
    }
}
