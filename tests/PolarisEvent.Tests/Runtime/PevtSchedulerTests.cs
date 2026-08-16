using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 确定性调度、稳定递增 ID、同帧完成顺序、所有权树与级联清理。
    /// 事件结束、替换、异常和卸载走的是同一条清理路径，这里逐条验证。
    /// </summary>
    public class PevtSchedulerTests
    {
        private static readonly PevtType[] WaitTypes = { PevtType.Int };

        private static PevtTestHost HostWithWait()
        {
            var host = new PevtTestHost();
            host.Command("wait", WaitTypes, (context, args) => WaitFrames(context, args.Int(0)));
            return host;
        }

        private static IEnumerator<PevtWait> WaitFrames(PevtRoutineContext context, int frames)
        {
            yield return context.Services.Clock.WaitFrames(frames);
        }

        // ---- 调度顺序 ----

        [Fact]
        public void RoutineIdsAreStableAndIncreasing()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);

            PevtRoutineInstance first = scheduler.Register(host.Start("id \"A\"\nend\n"));
            PevtRoutineInstance second = scheduler.Register(host.Start("id \"B\"\nend\n"));
            PevtRoutineInstance third = scheduler.Register(host.Start("id \"C\"\nend\n"));

            Assert.True(first.Id < second.Id);
            Assert.True(second.Id < third.Id);
        }

        [Fact]
        public void EachFrameAdvancesInstancesInAscendingIdOrder()
        {
            var order = new List<string>();
            var host = new PevtTestHost();
            host.Command("say", new[] { PevtType.String, PevtType.String }, (context, args) =>
            {
                order.Add(context.EventId);
                return Empty();
            });

            var scheduler = new PevtScheduler(host.Clock);
            scheduler.Register(host.Start("id \"First\"\n@say(\"a\",\"b\")\nend\n"));
            scheduler.Register(host.Start("id \"Second\"\n@say(\"a\",\"b\")\nend\n"));
            scheduler.Register(host.Start("id \"Third\"\n@say(\"a\",\"b\")\nend\n"));

            scheduler.Tick();

            Assert.Equal(new[] { "First", "Second", "Third" }, order);
        }

        [Fact]
        public void InstancesFinishingInTheSameFrameAreReportedInIdOrder()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);

            PevtRoutineInstance a = scheduler.Register(host.Start("id \"A\"\n@wait(1)\nend\n"));
            PevtRoutineInstance b = scheduler.Register(host.Start("id \"B\"\n@wait(1)\nend\n"));

            scheduler.Tick();
            host.Clock.Advance();
            IReadOnlyList<PevtRoutineInstance> finished = scheduler.Tick();

            Assert.Equal(new[] { a.Id, b.Id }, finished.Select(instance => instance.Id));
            Assert.All(finished, instance => Assert.Equal(host.Clock.Frame, instance.CompletedFrame));
        }

        [Fact]
        public void SchedulingIsReproducible()
        {
            string[] RunOnce()
            {
                var order = new List<string>();
                var host = new PevtTestHost();
                host.Command("say", new[] { PevtType.String, PevtType.String }, (context, args) =>
                {
                    order.Add($"{context.EventId}@{host.Clock.Frame}");
                    return Empty();
                });
                host.Command("wait", WaitTypes, (context, args) => WaitFrames(context, args.Int(0)));

                var scheduler = new PevtScheduler(host.Clock);
                scheduler.Register(host.Start("id \"A\"\n@wait(2)\n@say(\"x\",\"y\")\nend\n"));
                scheduler.Register(host.Start("id \"B\"\n@say(\"x\",\"y\")\n@wait(1)\n@say(\"x\",\"y\")\nend\n"));

                for (int frame = 0; frame < 8; frame++)
                {
                    scheduler.Tick();
                    host.Clock.Advance();
                }

                return order.ToArray();
            }

            Assert.Equal(RunOnce(), RunOnce());
        }

        [Fact]
        public void InstancesRegisteredDuringAFrameStartOnTheNextFrame()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);
            scheduler.Register(host.Start("id \"A\"\n@wait(1)\nend\n"));

            scheduler.Tick();
            PevtRoutineInstance late = scheduler.Register(host.Start("id \"Late\"\nend\n"));

            Assert.Null(late.LastResult);

            host.Clock.Advance();
            scheduler.Tick();
            Assert.NotNull(late.LastResult);
        }

        // ---- 所有权树 ----

        [Fact]
        public void RootEventGetsAnOwnershipNodeThatIsReleasedOnCompletion()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);
            PevtRoutineInstance instance = scheduler.Register(host.Start("id \"A\"\nend\n"));

            Assert.Single(scheduler.Ownership.Roots);
            Assert.Equal(1, instance.Ownership.LiveCount);

            scheduler.Tick();

            Assert.True(instance.Ownership.IsReleased);
            Assert.Empty(scheduler.Ownership.Roots);
        }

        [Fact]
        public void OwnershipReleasesChildrenBeforeParentsAndInReverseRegistrationOrder()
        {
            var order = new List<string>();
            var tree = new PevtOwnershipTree();

            PevtOwnershipNode root = tree.CreateRoot("event", () => order.Add("root"));
            PevtOwnershipNode command = tree.Add(root, PevtOwnershipKind.CommandFrame, "@say", () => order.Add("command"));
            tree.Add(command, PevtOwnershipKind.Wait, "wait", () => order.Add("wait"));
            tree.Add(root, PevtOwnershipKind.Resource, "portrait", () => order.Add("portrait"));

            tree.ReleaseCascade(root);

            Assert.Equal(new[] { "portrait", "wait", "command", "root" }, order);
        }

        [Fact]
        public void OwnershipCountsLiveNodesAcrossTheWholeSubtree()
        {
            var tree = new PevtOwnershipTree();
            PevtOwnershipNode root = tree.CreateRoot("event");
            PevtOwnershipNode command = tree.Add(root, PevtOwnershipKind.CommandFrame, "@say");
            tree.Add(command, PevtOwnershipKind.Wait, "wait");
            tree.Add(root, PevtOwnershipKind.Resource, "portrait");

            Assert.Equal(4, root.LiveCount);

            tree.ReleaseCascade(command);
            Assert.Equal(2, root.LiveCount);
        }

        [Fact]
        public void AFailingReleaseDoesNotStopTheRest()
        {
            var order = new List<string>();
            var tree = new PevtOwnershipTree();

            PevtOwnershipNode root = tree.CreateRoot("event", () => order.Add("root"));
            tree.Add(root, PevtOwnershipKind.Resource, "bad", () => throw new InvalidOperationException("boom"));
            tree.Add(root, PevtOwnershipKind.Resource, "good", () => order.Add("good"));

            IReadOnlyList<Exception> failures = tree.ReleaseCascade(root);

            Assert.Single(failures);
            Assert.Equal(new[] { "good", "root" }, order);
        }

        // ---- 结束、替换、异常与卸载 ----

        [Fact]
        public void StoppingAnInstanceCancelsItsCommandAndRunsCleanupInReverse()
        {
            var order = new List<string>();
            var host = new PevtTestHost();
            host.Command("wait", WaitTypes, (context, args) => WaitWithCleanup(context, order, args.Int(0)));

            var scheduler = new PevtScheduler(host.Clock);
            PevtRoutineInstance instance = scheduler.Register(host.Start("id \"A\"\n@wait(100)\nend\n"));

            scheduler.Tick();
            Assert.NotNull(instance.Execution.CurrentCommand);

            scheduler.Stop(instance);

            Assert.Equal(PevtExecutionStatus.Cancelled, instance.Execution.Status);
            Assert.Equal(new[] { "inner", "outer" }, order);
            Assert.True(instance.Ownership.IsReleased);
        }

        [Fact]
        public void StopIsIdempotent()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);
            PevtRoutineInstance instance = scheduler.Register(host.Start("id \"A\"\n@wait(100)\nend\n"));

            scheduler.Tick();
            scheduler.Stop(instance);
            IReadOnlyList<Exception> second = scheduler.Stop(instance);

            Assert.Empty(second);
            Assert.Equal(PevtExecutionStatus.Cancelled, instance.Execution.Status);
        }

        [Fact]
        public void ReplaceFinishesTheOldEventBeforeStartingTheNewOne()
        {
            var order = new List<string>();
            var host = new PevtTestHost();
            host.Command("wait", WaitTypes, (context, args) => WaitWithCleanup(context, order, args.Int(0)));

            var scheduler = new PevtScheduler(host.Clock);
            PevtRoutineInstance old = scheduler.Register(host.Start("id \"Old\"\n@wait(100)\nend\n"));
            scheduler.Tick();

            PevtExecution replacement = host.Start("id \"New\"\nend\n");
            PevtRoutineInstance next = scheduler.Replace(old, replacement);

            Assert.Equal(PevtExecutionStatus.Cancelled, old.Execution.Status);
            Assert.Equal(new[] { "inner", "outer" }, order); // 旧事件的清理在新事件登记之前就跑完了
            Assert.True(next.Id > old.Id);
            Assert.Equal(PevtExecutionStatus.Created, next.Execution.Status);
        }

        [Fact]
        public void SessionTemporaryStateIsRestoredOnNormalCompletion()
        {
            var restored = new List<string>();
            var host = new PevtTestHost();
            host.Command("ui_visible", new[] { PevtType.Bool }, (context, args) =>
            {
                context.Services.Ui.SetGlobalVisible(args.Bool(0));
                context.Services.Session.RegisterRestore("ui", () => restored.Add("ui"));
                return Empty();
            });

            PevtExecution execution = host.Start("id \"A\"\n@ui_visible(false)\nend\n");
            Assert.Equal(PevtExecutionStatus.Completed, host.RunToCompletion(execution).Status);

            Assert.Equal(new[] { "ui" }, restored);
            Assert.Equal(0, host.Session.PendingRestoreCount);
        }

        [Fact]
        public void SessionTemporaryStateIsAlsoRestoredWhenTheEventFails()
        {
            var restored = new List<string>();
            var host = new PevtTestHost();
            host.Command("ui_visible", new[] { PevtType.Bool }, (context, args) =>
            {
                context.Services.Session.RegisterRestore("ui", () => restored.Add("ui"));
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(
                host.Start("id \"A\"\n@ui_visible(false)\nvar z : int = 0\nvar r : int = 1 / z\nend\n"));

            Assert.Equal("PEVTR2002", result.Diagnostic.Id);
            Assert.Equal(new[] { "ui" }, restored);
        }

        [Fact]
        public void Pevtr1101_CleanupFailureOnANormalEndBecomesThePrimaryDiagnostic()
        {
            var host = new PevtTestHost();
            host.Command("ui_visible", new[] { PevtType.Bool }, (context, args) =>
            {
                context.Services.Session.RegisterRestore("bad", () => throw new InvalidOperationException("恢复失败"));
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"A\"\n@ui_visible(false)\nend\n"));

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR1101", result.Diagnostic.Id);
        }

        [Fact]
        public void CleanupFailureDoesNotOverrideTheOriginalDiagnostic()
        {
            var host = new PevtTestHost();
            host.Command("ui_visible", new[] { PevtType.Bool }, (context, args) =>
            {
                context.Services.Session.RegisterRestore("bad", () => throw new InvalidOperationException("恢复失败"));
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(
                host.Start("id \"A\"\n@ui_visible(false)\nvar z : int = 0\nvar r : int = 1 / z\nend\n"));

            // 最初异常是除零，清理期间的失败不得把它换成 PEVTR1101。
            Assert.Equal("PEVTR2002", result.Diagnostic.Id);
        }

        [Fact]
        public void StopAllCancelsEverythingInReverseStartOrder()
        {
            var order = new List<string>();
            var host = new PevtTestHost();
            host.Command("wait", WaitTypes, (context, args) => WaitTagged(context, order, context.EventId, args.Int(0)));

            var scheduler = new PevtScheduler(host.Clock);
            scheduler.Register(host.Start("id \"A\"\n@wait(100)\nend\n"));
            scheduler.Register(host.Start("id \"B\"\n@wait(100)\nend\n"));
            scheduler.Tick();

            scheduler.StopAll();

            Assert.Equal(new[] { "B", "A" }, order);
            Assert.Empty(scheduler.Ownership.Roots);
            Assert.Empty(scheduler.Running);
        }

        [Fact]
        public void PruneFinishedKeepsRunningInstances()
        {
            PevtTestHost host = HostWithWait();
            var scheduler = new PevtScheduler(host.Clock);
            scheduler.Register(host.Start("id \"Done\"\nend\n"));
            PevtRoutineInstance running = scheduler.Register(host.Start("id \"Running\"\n@wait(100)\nend\n"));

            scheduler.Tick();

            Assert.Equal(1, scheduler.PruneFinished());
            Assert.Equal(new[] { running.Id }, scheduler.Instances.Select(instance => instance.Id));
        }

        private static IEnumerator<PevtWait> WaitWithCleanup(PevtRoutineContext context, List<string> order, int frames)
        {
            context.Cleanup.Push("outer", () => order.Add("outer"));
            context.Cleanup.Push("inner", () => order.Add("inner"));
            yield return context.Services.Clock.WaitFrames(frames);
        }

        private static IEnumerator<PevtWait> WaitTagged(PevtRoutineContext context, List<string> order, string tag, int frames)
        {
            context.Cleanup.Push(tag, () => order.Add(tag));
            yield return context.Services.Clock.WaitFrames(frames);
        }

        private static IEnumerator<PevtWait> Empty()
        {
            yield break;
        }
    }
}
