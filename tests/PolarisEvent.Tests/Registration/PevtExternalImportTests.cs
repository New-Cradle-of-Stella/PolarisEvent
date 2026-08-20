using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Registration;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Registration
{
    /// <summary>
    /// 外部导入（磁盘目录与 PolarisTools 热重载通道共用的那条路径）：
    /// 与嵌入源同一套静态门、外部压嵌入的层级规则、整批替换语义，以及"外部导入不得和解掉
    /// 发布路径上的跨程序集致命冲突"这一条。
    /// </summary>
    public class PevtExternalImportTests
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

        private static PevtEmbeddedSource Embedded(string id, string path = null, string body = "end\n") =>
            PevtEmbeddedSource.Create(id, path ?? $"Events/{id}.pevt", $"id \"{id}\"\n{body}");

        private static PevtExternalSource External(string id, string path = null, string body = "end\n") =>
            PevtExternalSource.FromText(path ?? $"Events/{id}.pevt", $"id \"{id}\"\n{body}");

        // ---- 基本导入 ----

        [Fact]
        public void ExternalSourceLandsInTheVirtualEventSpace()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            PevtExternalApplyReport report = scanner.ApplyExternal(new[] { External("Opening") });

            Assert.Equal(1, report.SucceededCount);
            Assert.Equal(0, report.FailedCount);
            Assert.True(scanner.Events.TryGet("Opening", out PevtEventCandidate candidate));
            Assert.Equal(PevtEventOrigin.External, candidate.Origin);
            Assert.Equal(PevtRegistryScanner.ExternalOwner, candidate.Owner);
            Assert.Equal("/event/Opening.pevt", candidate.VirtualPath);
        }

        [Fact]
        public void TheEventIdComesFromTheSourceNotFromTheFileName()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            scanner.ApplyExternal(new[] { External("Real", "Events/WronglyNamed.pevt") });

            Assert.True(scanner.Events.Contains("Real"));
            Assert.False(scanner.Events.Contains("WronglyNamed"));
        }

        [Fact]
        public void StaticErrorsKeepTheEventOutAndPreserveTheDiagnostics()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            PevtExternalApplyReport report = scanner.ApplyExternal(new[]
            {
                External("Good"),
                PevtExternalSource.FromText("Events/Broken.pevt", "id \"Broken\"\n@no_such_command\nend\n"),
            });

            Assert.Equal(1, report.SucceededCount);
            PevtExternalLoadResult failure = Assert.Single(report.Failed);
            Assert.Equal(PevtExternalLoadFailure.StaticAnalysis, failure.Failure);
            Assert.NotEmpty(failure.Diagnostics);
            Assert.True(scanner.Events.Contains("Good"));
            Assert.False(scanner.Events.Contains("Broken"));
        }

        [Fact]
        public void InvalidUtf8IsRejectedWithItsOwnNumber()
        {
            // 0xFF 在 UTF-8 里永远不合法，因此不可能是"某个多字节字符的一部分"。
            var bytes = new List<byte>(Encoding.UTF8.GetBytes("id \"A\"\nend\n")) { 0xFF };

            PevtExternalLoadResult result = PevtExternalSourceLoader.Load(
                PevtExternalSource.FromBytes("Events/A.pevt", bytes.ToArray()));

            Assert.Equal(PevtExternalLoadFailure.InvalidSourceEncoding, result.Failure);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "PEVT9212");
        }

        [Fact]
        public void SourcesOverTheSizeLimitAreRejectedBeforeParsing()
        {
            var limits = new PevtEmbeddedSourceLimits(1024, 4);

            PevtExternalLoadResult result = PevtExternalSourceLoader.Load(External("A"), limits);

            Assert.Equal(PevtExternalLoadFailure.SourceTooLarge, result.Failure);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "PEVT9211");
        }

        [Fact]
        public void ByteOrderMarksAreStrippedSoTheHashMatchesTheEmbeddedOne()
        {
            string text = "id \"A\"\nend\n";
            byte[] withBom = new UTF8Encoding(true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(text)).ToArray();

            PevtExternalSource source = PevtExternalSource.FromBytes("Events/A.pevt", withBom);

            Assert.Equal(Encoding.UTF8.GetByteCount(text), source.ByteLength);
            Assert.Equal(PevtEmbeddedSource.Create("A", "Events/A.pevt", text).ContentHash, source.ContentHash);
        }

        // ---- 层级：外部压嵌入 ----

        [Fact]
        public void ExternalImportOverridesTheEmbeddedEventWithTheSameId()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Embedded("Opening", "Events/Shipped.pevt")), "ModA", "Mod A");
            scanner.Seal();

            scanner.ApplyExternal(new[] { External("Opening", "Events/WorkInProgress.pevt") });

            Assert.True(scanner.Events.TryGet("Opening", out PevtEventCandidate active));
            Assert.Equal(PevtEventOrigin.External, active.Origin);
            Assert.Equal("Events/WorkInProgress.pevt", active.SourcePath);

            // 覆盖不是冲突：`callevt` 判歧义看的是 FatalConflicts，覆盖不能污染那张表。
            Assert.Empty(scanner.Events.Conflicts);
            PevtEventOverride entry = Assert.Single(scanner.Events.Overrides);
            Assert.Equal("Opening", entry.EventId);
            Assert.Equal("Events/Shipped.pevt", entry.Shadowed.SourcePath);
            Assert.Contains("Events/WorkInProgress.pevt", entry.Describe());
        }

        [Fact]
        public void DroppingTheExternalImportRevivesTheEmbeddedEvent()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Embedded("Opening", "Events/Shipped.pevt")), "ModA");
            scanner.Seal();
            scanner.ApplyExternal(new[] { External("Opening", "Events/WorkInProgress.pevt") });

            Assert.Equal(1, scanner.ClearExternal());

            Assert.True(scanner.Events.TryGet("Opening", out PevtEventCandidate active));
            Assert.Equal(PevtEventOrigin.Embedded, active.Origin);
            Assert.Equal("Events/Shipped.pevt", active.SourcePath);
            Assert.Empty(scanner.Events.Overrides);
        }

        [Fact]
        public void ACrossAssemblyConflictStaysFatalEvenWhileAnExternalImportShadowsIt()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Embedded("Shared", "Events/FromA.pevt")), "ModA", "Mod A");
            scanner.Register(new EventRegistrar(Embedded("Shared", "Events/FromB.pevt")), "ModB", "Mod B");
            scanner.Seal();

            scanner.ApplyExternal(new[] { External("Shared", "Events/Mine.pevt") });

            // 作者本机恰好也导入了同名事件，不表示那两个模组的冲突被解决了。
            PevtEventConflict conflict = Assert.Single(scanner.Events.FatalConflicts);
            Assert.Equal("Shared", conflict.EventId);
            Assert.False(conflict.IsSameOwner);

            Assert.True(scanner.Events.TryGet("Shared", out PevtEventCandidate active));
            Assert.Equal(PevtEventOrigin.External, active.Origin);
        }

        [Fact]
        public void TwoExternalFilesWithTheSameIdWarnAndTheLaterOneWins()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            scanner.ApplyExternal(new[]
            {
                External("Dup", "Events/First.pevt"),
                External("Dup", "Events/Second.pevt"),
            });

            PevtEventConflict conflict = Assert.Single(scanner.Events.Conflicts);
            Assert.True(conflict.IsSameOwner);
            Assert.Contains("Events/First.pevt", conflict.Describe());
            Assert.Contains("Events/Second.pevt", conflict.Describe());

            Assert.True(scanner.Events.TryGet("Dup", out PevtEventCandidate active));
            Assert.Equal("Events/Second.pevt", active.SourcePath);
        }

        // ---- 整批替换 ----

        [Fact]
        public void ApplyReplacesTheWholeSetSoDeletedFilesDisappear()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();
            scanner.ApplyExternal(new[] { External("A"), External("B") });
            Assert.True(scanner.Events.Contains("B"));

            scanner.ApplyExternal(new[] { External("A") });

            Assert.True(scanner.Events.Contains("A"));
            Assert.False(scanner.Events.Contains("B"));
        }

        [Fact]
        public void ReimportingTheSameIdKeepsExactlyOneCandidate()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            scanner.ApplyExternal(new[] { External("A", body: "end\n") });
            scanner.ApplyExternal(new[] { External("A", body: "end\n") });
            scanner.ApplyExternal(new[] { External("A", body: "end\n") });

            Assert.Single(scanner.Events.Candidates);
            Assert.Empty(scanner.Events.Conflicts);
        }

        [Fact]
        public void ApplyingAnEmptySetDropsEverythingExternal()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Register(new EventRegistrar(Embedded("Kept")), "ModA");
            scanner.Seal();
            scanner.ApplyExternal(new[] { External("Temporary") });

            PevtExternalApplyReport report = scanner.ApplyExternal(new PevtExternalSource[0]);

            Assert.Equal(0, report.SucceededCount);
            Assert.False(scanner.Events.Contains("Temporary"));
            Assert.True(scanner.Events.Contains("Kept"));
        }

        [Fact]
        public void TheReportNamesTheFailingFile()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            PevtExternalApplyReport report = scanner.ApplyExternal(new[]
            {
                PevtExternalSource.FromText("Events/Broken.pevt", "id \"Broken\"\n@no_such_command\nend\n"),
            });

            Assert.Contains("Events/Broken.pevt", report.Describe());
            Assert.Empty(report.EventIds);
        }

        [Fact]
        public void ExternalFailuresStayOutOfThePublishedLoadFailureTable()
        {
            var scanner = new PevtRegistryScanner();
            scanner.Seal();

            scanner.ApplyExternal(new[]
            {
                PevtExternalSource.FromText("Events/Broken.pevt", "id \"Broken\"\n@no_such_command\nend\n"),
            });

            // Failures 是"随程序集分发的嵌入包加载失败"那张表，作者本机改坏的一行不该混进去。
            Assert.Empty(scanner.Events.Failures);
        }
    }
}
