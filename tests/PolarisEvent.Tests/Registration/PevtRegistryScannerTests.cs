using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Registration;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Registration
{
    /// <summary>
    /// 注册器、扫描器、只读注册表与冲突守卫。覆盖 PEVT-嵌入注册与ID冲突规范.md 第 3–7、10 节：
    /// 来源不可伪造、Ordinal 比较、事件与人物冲突分表、跨程序集致命 / 同程序集覆盖、Seal 与卸载。
    /// </summary>
    public class PevtRegistryScannerTests
    {
        private sealed class EventRegistrar : IPevtRegistrar
        {
            private readonly PevtEmbeddedSource[] _sources;

            public EventRegistrar(params PevtEmbeddedSource[] sources) => _sources = sources;

            public void Register(PevtRegistrationContext context)
            {
                foreach (PevtEmbeddedSource source in _sources)
                    context.Register(source);
            }
        }

        private sealed class ActorRegistrar : IPevtActorRegistrar
        {
            private readonly ActorCatalog _catalog;
            private readonly string _hash;

            public ActorRegistrar(ActorCatalog catalog, string hash = "hash") { _catalog = catalog; _hash = hash; }

            public void Register(PevtActorRegistrationContext context) => context.Register(_catalog, _hash);
        }

        private static PevtEmbeddedSource Event(string id, string path = null, string body = "end\n") =>
            PevtEmbeddedSource.Create(id, path ?? $"Events/{id}.pevt", $"id \"{id}\"\n{body}");

        private static ActorCatalog Catalog(string ns, params string[] localIds) =>
            new ActorCatalog(ns, 1, false, $"Actors/{ns}.pactor",
                localIds.Select(id => new ActorDefinition(id, displayName: id)));

        // ---- 基本注册 ----

        [Fact]
        public void ScannedEvent_LandsInTheVirtualEventSpace()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("Opening")), "ModA");
            PevtScanReport report = scanner.Seal();

            Assert.Empty(report.EventConflicts);
            Assert.Empty(report.LoadFailures);
            Assert.True(scanner.Events.TryGet("Opening", out PevtEventCandidate candidate));
            Assert.Equal("/event/Opening.pevt", candidate.VirtualPath);
            Assert.Equal("ModA", candidate.Owner);
            Assert.Equal("Events/Opening.pevt", candidate.SourcePath);
            Assert.Equal("Opening", candidate.Definition.EventId);
        }

        [Fact]
        public void EventIdsUseCaseSensitiveOrdinalComparison()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("Opening"), Event("opening")), "ModA");
            scanner.Seal();

            Assert.True(scanner.Events.Contains("Opening"));
            Assert.True(scanner.Events.Contains("opening"));
            Assert.Empty(scanner.Events.Conflicts);
        }

        [Fact]
        public void ChineseEventIdsCompareByRawUnicodeSequence()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("开场"), Event("结尾")), "ModA");
            scanner.Seal();

            Assert.True(scanner.Events.Contains("开场"));
            Assert.False(scanner.Events.Contains("开場"));
        }

        [Fact]
        public void RegistrarCannotForgeItsOwner()
        {
            // 注册上下文由扫描器创建；注册器只能提交载荷，拿不到修改 Owner 的入口。
            string observedOwner = null;
            var scanner = new PevtRegistryScanner();

            scanner.Register(new DelegatingRegistrar(context =>
            {
                observedOwner = context.Owner;
                context.Register(Event("Opening"));
            }), "RealOwner", "Real Mod");
            scanner.Seal();

            Assert.Equal("RealOwner", observedOwner);
            Assert.True(scanner.Events.TryGet("Opening", out PevtEventCandidate candidate));
            Assert.Equal("RealOwner", candidate.Owner);
            Assert.Equal("Real Mod", candidate.DisplayName);
            Assert.Null(typeof(PevtRegistrationContext).GetProperty("Owner").SetMethod);
        }

        private sealed class DelegatingRegistrar : IPevtRegistrar
        {
            private readonly Action<PevtRegistrationContext> _register;
            public DelegatingRegistrar(Action<PevtRegistrationContext> register) => _register = register;
            public void Register(PevtRegistrationContext context) => _register(context);
        }

        // ---- 加载失败 ----

        [Fact]
        public void CorruptedPackage_IsRecordedAsAFailureAndNeverEntersTheEventSpace()
        {
            PevtEmbeddedSource good = Event("Good");
            var broken = new PevtEmbeddedSource(1, "gzip-base64-v1", "Broken", "Events/Broken.pevt", 10, "deadbeef", "!!!");

            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(good, broken), "ModA");
            PevtScanReport report = scanner.Seal();

            Assert.True(scanner.Events.Contains("Good"));
            Assert.False(scanner.Events.Contains("Broken"));

            PevtEventLoadFailure failure = Assert.Single(report.LoadFailures);
            Assert.Equal("ModA", failure.Owner);
            Assert.Equal("Events/Broken.pevt", failure.SourcePath);
            Assert.Equal(PevtEmbeddedLoadFailure.InvalidPayloadEncoding, failure.Result.Failure);
        }

        [Fact]
        public void StaticAnalysisIsRerunOnTheGameSideAndCanRejectAPackage()
        {
            // 工具侧可能用了不同的 API 表；游戏侧重新绑定，未登记的 @ 直接拒绝。
            PevtEmbeddedSource source = PevtEmbeddedSource.Create("Talk", "Events/Talk.pevt", "id \"Talk\"\n@not_registered(\"x\")\nend\n");

            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(source), "ModA");
            PevtScanReport report = scanner.Seal();

            Assert.False(scanner.Events.Contains("Talk"));
            Assert.Equal(PevtEmbeddedLoadFailure.StaticAnalysis, Assert.Single(report.LoadFailures).Result.Failure);
        }

        [Fact]
        public void ScannerBindsAgainstTheAuthoritativeDescriptorCatalogByDefault()
        {
            PevtEmbeddedSource source = PevtEmbeddedSource.Create(
                "Talk", "Events/Talk.pevt", "id \"Talk\"\n@say(\"aic:noel\", \"hi\")\nend\n");

            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(source), "ModA");
            scanner.Seal();

            Assert.True(scanner.Events.Contains("Talk"));
            Assert.NotEmpty(CommandDescriptorCatalog.Builtin.Find("say"));
        }

        // ---- 事件冲突 ----

        [Fact]
        public void CrossAssemblyDuplicate_IsFatalAndKeepsTheFirstRegistration()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("Shared", "Events/FromA.pevt")), "ModA", "Mod A");
            scanner.Register(new EventRegistrar(Event("Shared", "Events/FromB.pevt")), "ModB", "Mod B");
            PevtScanReport report = scanner.Seal();

            Assert.True(report.HasFatalConflicts);
            PevtEventConflict conflict = Assert.Single(report.EventConflicts);
            Assert.False(conflict.IsSameOwner);
            Assert.Equal("ModA", conflict.Retained.Owner);
            Assert.Equal("ModB", conflict.Ignored.Owner);

            string description = conflict.Describe();
            Assert.Contains("Events/FromA.pevt", description);
            Assert.Contains("Events/FromB.pevt", description);
            Assert.Contains("Mod A", description);
            Assert.Contains("Mod B", description);
            Assert.Contains("两个模组都是责任方", description);

            // 先注册的定义仍然生效，使同一次启动内的结果稳定。
            Assert.True(scanner.Events.TryGet("Shared", out PevtEventCandidate active));
            Assert.Equal("ModA", active.Owner);
        }

        [Fact]
        public void IdenticalContentDoesNotExemptACrossAssemblyConflict()
        {
            PevtEmbeddedSource a = Event("Shared", "Events/Same.pevt");
            PevtEmbeddedSource b = Event("Shared", "Events/Same.pevt");
            Assert.Equal(a.ContentHash, b.ContentHash);

            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(a), "ModA");
            scanner.Register(new EventRegistrar(b), "ModB");

            Assert.Single(scanner.Seal().EventConflicts);
        }

        [Fact]
        public void SameAssemblyDuplicate_IsAWarningAndTheLastRegistrationWins()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(
                Event("Dup", "Events/First.pevt"),
                Event("Dup", "Events/Second.pevt", "var n : int = 1\nend\n")), "ModA");
            PevtScanReport report = scanner.Seal();

            PevtEventConflict conflict = Assert.Single(report.EventConflicts);
            Assert.True(conflict.IsSameOwner);
            Assert.False(report.HasFatalConflicts);

            // 警告必须同时列出两个项目相对源路径。
            Assert.Contains("Events/First.pevt", conflict.Describe());
            Assert.Contains("Events/Second.pevt", conflict.Describe());

            Assert.True(scanner.Events.TryGet("Dup", out PevtEventCandidate active));
            Assert.Equal("Events/Second.pevt", active.SourcePath);
        }

        [Fact]
        public void AllConflictsAreCollectedAndSummarisedOnceAtSeal()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("A"), Event("B")), "ModA");
            scanner.Register(new EventRegistrar(Event("A"), Event("B")), "ModB");
            PevtScanReport report = scanner.Seal();

            Assert.Equal(2, report.EventConflicts.Count);

            string summary = report.DescribeFatalConflicts();
            Assert.Contains("`A`", summary);
            Assert.Contains("`B`", summary);
            Assert.Contains("ModA", summary);
            Assert.Contains("ModB", summary);
        }

        [Fact]
        public void ConflictsAppearingAfterSealAreReportedIndividually()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("Shared", "Events/FromA.pevt")), "ModA");
            PevtScanReport report = scanner.Seal();
            Assert.Empty(report.EventConflicts);

            PevtEmbeddedSource late = Event("Shared", "Events/FromLate.pevt");
            PevtEmbeddedLoadResult loaded = PevtEmbeddedSourceLoader.Load(late, null, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            IReadOnlyList<PevtEventConflict> immediate = scanner.Events.RegisterLate("ModLate", "Late Mod", late, loaded.Definition);

            PevtEventConflict conflict = Assert.Single(immediate);
            Assert.Equal("ModLate", conflict.Ignored.Owner);
            Assert.True(scanner.Events.TryGet("Shared", out PevtEventCandidate active));
            Assert.Equal("ModA", active.Owner);
        }

        [Fact]
        public void RegisterLateBeforeSealIsRejected()
        {
            var registry = new PevtEventRegistry();
            Assert.Throws<InvalidOperationException>(() => registry.RegisterLate("ModA", "A", Event("X"), null));
        }

        // ---- 卸载 ----

        [Fact]
        public void UnloadRemovesOnlyThatOwnerAndRevivesTheDefinitionItHadDisplaced()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("Shared", "Events/FromA.pevt"), Event("OnlyA")), "ModA");
            scanner.Register(new EventRegistrar(Event("Shared", "Events/FromB.pevt")), "ModB");
            scanner.Seal();

            Assert.Equal("ModA", scanner.Events.ActiveEvents.Single(e => e.EventId == "Shared").Owner);

            Assert.Equal(2, scanner.Events.Unload("ModA"));

            Assert.False(scanner.Events.Contains("OnlyA"));
            Assert.True(scanner.Events.TryGet("Shared", out PevtEventCandidate revived));
            Assert.Equal("ModB", revived.Owner);
            Assert.Empty(scanner.Events.Conflicts);
        }

        [Fact]
        public void UnloadingAnUnknownOwnerChangesNothing()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("A")), "ModA");
            scanner.Seal();

            Assert.Equal(0, scanner.Events.Unload("Nobody"));
            Assert.True(scanner.Events.Contains("A"));
        }

        // ---- 人物注册 ----

        [Fact]
        public void BuiltinActorsAreRegisteredBeforeAnyExternalCatalog()
        {
            var scanner = new PevtRegistryScanner();

            Assert.True(scanner.Actors.Directory.TryGetActor("aic:noel", out ActorRegistration noel));
            Assert.Equal(BuiltinActorCatalog.Owner, noel.Owner);

            scanner.Register(new ActorRegistrar(Catalog("example.mod", "iris")), "ExampleMod");
            scanner.Seal();

            Assert.True(scanner.Actors.Directory.Contains("example.mod:iris"));
            Assert.Equal(BuiltinActorCatalog.Owner, scanner.Actors.Directory.Actors[0].Owner);
        }

        [Fact]
        public void CrossAssemblyActorConflictMapsToPevtr4404AndDisablesTheId()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new ActorRegistrar(Catalog("shared.ns", "iris"), "hash-a"), "ModA");
            scanner.Register(new ActorRegistrar(Catalog("shared.ns", "iris"), "hash-b"), "ModB");
            PevtScanReport report = scanner.Seal();

            ActorConflict conflict = Assert.Single(report.ActorConflicts);
            Assert.Equal("PEVTR4404", conflict.DiagnosticId);
            Assert.Equal("shared.ns:iris", conflict.ActorId);
            Assert.Equal("hash-a", conflict.RetainedCatalogHash);
            Assert.Equal("hash-b", conflict.IgnoredCatalogHash);

            // 冲突人物不能用于新事件。
            Assert.False(scanner.Actors.Directory.Contains("shared.ns:iris"));
            Assert.True(report.HasFatalConflicts);
        }

        [Fact]
        public void ActorAndEventConflictsAreCollectedInSeparateTables()
        {
            // 同一个字符串既是事件 ID 又是人物局部 ID 时，两个空间互不影响。
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Event("iris")), "ModA");
            scanner.Register(new EventRegistrar(Event("iris")), "ModB");
            scanner.Register(new ActorRegistrar(Catalog("mod.a", "iris")), "ModA");
            PevtScanReport report = scanner.Seal();

            Assert.Single(report.EventConflicts);
            Assert.Empty(report.ActorConflicts);
            Assert.True(scanner.Actors.Directory.Contains("mod.a:iris"));
            Assert.True(scanner.Events.Contains("iris"));
        }

        [Fact]
        public void ActorRegistryRejectsRegistrationAfterSeal()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            Assert.Throws<InvalidOperationException>(() =>
                scanner.Register(new ActorRegistrar(Catalog("mod.a", "iris")), "ModA"));
        }

        [Fact]
        public void UnloadingAnActorOwnerRevivesTheCatalogItHadDisplaced()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new ActorRegistrar(Catalog("shared.ns", "iris")), "ModA");
            scanner.Register(new ActorRegistrar(Catalog("shared.ns", "iris")), "ModB");
            scanner.Seal();

            Assert.False(scanner.Actors.Directory.Contains("shared.ns:iris"));

            Assert.Equal(1, scanner.Actors.Unload("ModB"));

            Assert.True(scanner.Actors.Directory.TryGetActor("shared.ns:iris", out ActorRegistration revived));
            Assert.Equal("ModA", revived.Owner);
            Assert.Empty(scanner.Actors.Conflicts);
        }

        [Fact]
        public void BuiltinActorCatalogCannotBeUnloaded()
        {
            var scanner = new PevtRegistryScanner();
            Assert.Throws<InvalidOperationException>(() => scanner.Actors.Unload(BuiltinActorCatalog.Owner));
            Assert.True(scanner.Actors.Directory.Contains("aic:noel"));
        }

        [Fact]
        public void ExternalCatalogCannotOccupyTheClosedBuiltinNamespace()
        {
            var forged = new ActorCatalog("aic", 1, false, "Evil.pactor",
                new[] { new ActorDefinition("noel", displayName: "Fake") });

            var scanner = new PevtRegistryScanner();
            Assert.Throws<ArgumentException>(() => scanner.Register(new ActorRegistrar(forged), "EvilMod"));
        }

        // ---- 程序集扫描 ----

        [Fact]
        public void ScanAssemblyDiscoversAttributedRegistrarsAndFixesTheOwner()
        {
            var scanner = new PevtRegistryScanner();
            scanner.ScanAssembly(typeof(PevtRegistryScannerTests).Assembly);
            scanner.Seal();

            string owner = typeof(PevtRegistryScannerTests).Assembly.GetName().Name;

            Assert.True(scanner.Events.TryGet("ScannedByAttribute", out PevtEventCandidate candidate));
            Assert.Equal(owner, candidate.Owner);
            Assert.True(scanner.Actors.Directory.TryGetActor("scan.test:probe", out ActorRegistration actor));
            Assert.Equal(owner, actor.Owner);
            Assert.Contains(owner, scanner.ScannedOwners);
        }

        [PevtAutoRegistration]
        internal sealed class DiscoveredEventRegistrar : IPevtRegistrar
        {
            public void Register(PevtRegistrationContext context) =>
                context.Register(PevtEmbeddedSource.Create(
                    "ScannedByAttribute", "Events/Scanned.pevt", "id \"ScannedByAttribute\"\nend\n"));
        }

        [PevtActorAutoRegistration]
        internal sealed class DiscoveredActorRegistrar : IPevtActorRegistrar
        {
            public void Register(PevtActorRegistrationContext context) =>
                context.Register(new ActorCatalog("scan.test", 1, false, "Actors/Probe.pactor",
                    new[] { new ActorDefinition("probe", displayName: "Probe") }), "probe-hash");
        }
    }
}
