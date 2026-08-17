using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Routines;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 公开宿主：Start / Change / Stop、固定更新点、只读查询与插件卸载。
    /// Change 的关键约定是"先完成旧根事件清理再启动新事件"，这里用会话恢复顺序把它钉住。
    /// </summary>
    public class PevtEventHostTests
    {
        private sealed class Registrar : IPevtRegistrar
        {
            private readonly PevtEmbeddedSource[] _sources;

            public Registrar(params PevtEmbeddedSource[] sources) => _sources = sources;

            public void Register(PevtRegistrationContext context)
            {
                foreach (PevtEmbeddedSource source in _sources)
                    context.Register(source);
            }
        }

        private sealed class Fixture
        {
            public FakeClock Clock { get; } = new FakeClock();

            public FakeDialogue Dialogue { get; } = new FakeDialogue();

            public FakeStage Stage { get; } = new FakeStage();

            public FakeResources Resources { get; } = new FakeResources();

            public FakePortrait Portrait { get; } = new FakePortrait();

            public FakeChoice Choice { get; } = new FakeChoice();

            public List<string> StartedSessions { get; } = new List<string>();

            public PevtRegistryScanner Scanner { get; }

            public PevtEventHost Host { get; }

            public Fixture(params PevtEmbeddedSource[] events)
            {
                Scanner = new PevtRegistryScanner(null, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());
                if (events.Length > 0)
                    Scanner.Register(new Registrar(events), "TestMod", "Test Mod");
                Scanner.Seal();

                ActorDirectory actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build();

                Host = new PevtEventHost(
                    Scanner,
                    Clock,
                    eventId =>
                    {
                        StartedSessions.Add(eventId);
                        return new PevtServices(
                            Clock, new PevtEventSession(eventId),
                            new FakeActorCatalogService(actors),
                            Resources, Dialogue, Choice, Portrait,
                            Stage, Stage, Stage, Stage, Stage, Stage, Stage, Stage);
                    },
                    P0CommandRoutines.CreateRegistry());
            }

            public void Advance(int frames = 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    Host.Update();
                    Clock.Advance();
                }
            }

            public void RunToCompletion(int maxFrames = 128)
            {
                for (int i = 0; i < maxFrames; i++)
                {
                    Host.Update();
                    Clock.Advance();
                    if (Host.Root == null)
                        return;
                }

                throw new InvalidOperationException("事件没有在给定帧数内结束。");
            }
        }

        private static PevtEmbeddedSource Event(string id, string body) =>
            PevtEmbeddedSource.Create(id, $"Events/{id}.pevt", $"id \"{id}\"\n{body}end\n");

        // ---- Start ----

        [Fact]
        public void StartRunsARegisteredEventThroughTheUpdatePoint()
        {
            var fixture = new Fixture(Event("Opening", "@ui_visible(false)\n"));

            PevtEventInstance instance = fixture.Host.Start("Opening");

            Assert.Equal("Opening", instance.EventId);
            Assert.Equal("TestMod", instance.Owner);
            Assert.Equal(PevtExecutionStatus.Created, instance.Status);
            Assert.Same(instance, fixture.Host.Root);

            IReadOnlyList<PevtEventInstance> finished = fixture.Host.Update();

            Assert.Equal(new[] { instance.Id }, finished.Select(i => i.Id));
            Assert.Equal(PevtExecutionStatus.Completed, instance.Status);
            Assert.Null(fixture.Host.Root);
        }

        [Fact]
        public void Pevtr4301_StartingAnUnregisteredEventId()
        {
            var fixture = new Fixture();

            PevtEventStartException error = Assert.Throws<PevtEventStartException>(() => fixture.Host.Start("Nope"));

            Assert.Equal("PEVTR4301", error.Diagnostic.Id);
            Assert.Null(fixture.Host.Root);
        }

        [Fact]
        public void Pevtr4304_StartingAnEventThatUsesConstructsThisRuntimeCannotRunYet()
        {
            // 用功能阶段 F 的原始桥：异步与 callevt 从功能阶段 E 起已经能跑了。
            var fixture = new Fixture(Event("Raw", "enable cs\n$raw cs'''return 1;'''\n"));

            // 载入期能通过，是运行时编译阶段拒绝的。
            Assert.True(fixture.Scanner.Events.Contains("Raw"));

            PevtEventStartException error = Assert.Throws<PevtEventStartException>(() => fixture.Host.Start("Raw"));
            Assert.Equal("PEVTR4304", error.Diagnostic.Id);
        }

        [Fact]
        public void EachStartGetsItsOwnSession()
        {
            var fixture = new Fixture(Event("A", "@ui_visible(false)\n"));

            fixture.Host.Start("A");
            fixture.RunToCompletion();
            fixture.Host.Start("A");
            fixture.RunToCompletion();

            Assert.Equal(new[] { "A", "A" }, fixture.StartedSessions);

            // 两次运行各自恢复一次，不会因为共用会话而少恢复或多恢复。
            Assert.Equal(2, fixture.Stage.Calls.Count(c => c == "SetGlobalVisible(True)"));
        }

        // ---- Change ----

        [Fact]
        public void ChangeFinishesTheOldEventsCleanupBeforeStartingTheNewOne()
        {
            var fixture = new Fixture(
                Event("Old", "@ui_visible(false)\n@wait(100)\n"),
                Event("New", "@status_visible(false)\n"));

            fixture.Host.Start("Old");
            fixture.Advance();
            Assert.Contains("SetGlobalVisible(False)", fixture.Stage.Calls);
            Assert.DoesNotContain("SetGlobalVisible(True)", fixture.Stage.Calls);

            PevtEventInstance next = fixture.Host.Change("New");

            // 旧事件的恢复必须排在新事件的第一个副作用之前。
            int restoreIndex = fixture.Stage.Calls.IndexOf("SetGlobalVisible(True)");
            Assert.True(restoreIndex >= 0, "旧事件的临时状态没有恢复。");
            Assert.DoesNotContain("SetStatusVisible(False)", fixture.Stage.Calls.Take(restoreIndex + 1));

            Assert.Equal("New", next.EventId);
            Assert.Same(next, fixture.Host.Root);
        }

        [Fact]
        public void ChangeCancelsTheOldEventEvenWhileItIsWaiting()
        {
            var fixture = new Fixture(
                Event("Old", "@wait(100)\n"),
                Event("New", "@ui_visible(false)\n"));

            PevtEventInstance old = fixture.Host.Start("Old");
            fixture.Advance();
            Assert.Equal(PevtExecutionStatus.Suspended, old.Status);

            fixture.Host.Change("New");

            Assert.Equal(PevtExecutionStatus.Cancelled, old.Status);
            Assert.True(old.Ownership.IsReleased);
        }

        // ---- Stop 与卸载 ----

        [Fact]
        public void StopCancelsTheRootEventAndReleasesOwnership()
        {
            var fixture = new Fixture(Event("A", "@ui_visible(false)\n@wait(100)\n"));

            PevtEventInstance instance = fixture.Host.Start("A");
            fixture.Advance();

            fixture.Host.Stop();

            Assert.Equal(PevtExecutionStatus.Cancelled, instance.Status);
            Assert.Contains("SetGlobalVisible(True)", fixture.Stage.Calls);
            Assert.True(instance.Ownership.IsReleased);
            Assert.Null(fixture.Host.Root);
        }

        [Fact]
        public void StopIsANoOpWhenNothingIsRunning()
        {
            var fixture = new Fixture();
            Assert.Empty(fixture.Host.Stop());
        }

        [Fact]
        public void ShutdownStopsEverythingAndClearsTheOwnershipTree()
        {
            var fixture = new Fixture(Event("A", "@ui_visible(false)\n@wait(100)\n"));

            fixture.Host.Start("A");
            fixture.Advance();

            fixture.Host.Shutdown();

            Assert.Empty(fixture.Host.OwnershipRoots);
            Assert.Null(fixture.Host.Root);
            Assert.Contains("SetGlobalVisible(True)", fixture.Stage.Calls);
        }

        // ---- 只读查询 ----

        [Fact]
        public void InstanceExposesReadOnlyRuntimeState()
        {
            var fixture = new Fixture(Event("Story", "@say(\"aic:noel\", \"hi\")\n"));

            PevtEventInstance instance = fixture.Host.Start("Story");
            fixture.Advance();

            Assert.Equal("say", instance.CurrentCommand);
            Assert.Equal("对话推进", instance.CurrentWaitSource);
            Assert.True(instance.TotalSteps > 0);
            Assert.False(instance.IsFinished);

            List<PevtCallFrame> stack = instance.CallStack.ToList();
            Assert.Equal(PevtCallFrameKind.Command, stack[0].Kind);
            Assert.Equal("@say", stack[0].Name);
            Assert.Equal("Story", stack[1].Name);

            Assert.Equal(1, instance.Ownership.LiveCount);
        }

        [Fact]
        public void FailedEventKeepsItsDiagnosticForInspection()
        {
            var fixture = new Fixture(Event("Bad", "@say(\"other.mod:ghost\", \"hi\")\n"));

            PevtEventInstance instance = fixture.Host.Start("Bad");
            fixture.RunToCompletion();

            Assert.Equal(PevtExecutionStatus.Faulted, instance.Status);
            Assert.Equal("PEVTR4401", instance.Diagnostic.Id);
            Assert.Equal(fixture.Clock.Frame - 1, instance.CompletedFrame);
            Assert.Contains("Bad", instance.Diagnostic.Describe());
        }

        [Fact]
        public void TryGetInstanceFindsEventsByScheduledId()
        {
            var fixture = new Fixture(Event("A", "@ui_visible(false)\n"));
            PevtEventInstance started = fixture.Host.Start("A");

            Assert.True(fixture.Host.TryGetInstance(started.Id, out PevtEventInstance found));
            Assert.Same(started, found);
            Assert.False(fixture.Host.TryGetInstance(9999, out _));
        }

        [Fact]
        public void CompiledProgramsAreReusedAcrossStarts()
        {
            var fixture = new Fixture(Event("A", "@ui_visible(false)\n"));

            fixture.Host.Start("A");
            fixture.RunToCompletion();
            fixture.Host.Start("A");
            fixture.RunToCompletion();

            Assert.Equal(2, fixture.Host.Instances.Count);
            Assert.All(fixture.Host.Instances, instance => Assert.Equal(PevtExecutionStatus.Completed, instance.Status));
        }

        // ---- 端到端 ----

        [Fact]
        public void AMinimalDialogueEventRunsThroughTheHostFromRegistryToCleanup()
        {
            var fixture = new Fixture(Event("Opening", @"@ui_visible(false)
@actor_enter(""aic:noel"", ""left"", ""default"", 0)
@say(""aic:noel"", ""早上好"")
@actor_exit(""aic:noel"", 0)
"));

            PevtEventInstance instance = fixture.Host.Start("Opening");
            fixture.Advance();

            Assert.Equal("say", instance.CurrentCommand);
            fixture.Dialogue.AdvanceSignal.Signal(0);
            fixture.RunToCompletion();

            Assert.Equal(PevtExecutionStatus.Completed, instance.Status);
            Assert.Equal(
                new[] { "SetAppearance(aic:noel,default)", "Place(aic:noel,left,0)", "Exit(aic:noel,0)" },
                fixture.Portrait.Calls);
            Assert.Equal(new[] { "SetGlobalVisible(False)", "SetGlobalVisible(True)" }, fixture.Stage.Calls);
            Assert.True(instance.Ownership.IsReleased);
        }
    }
}
