using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 同步 <c>@</c> 指令帧：实参先求值并快照、按已绑定描述创建指令帧、跨帧续跑、
    /// 结果只能提交一次、契约失败映射 PEVTR4001/4002、逆序清理。
    /// </summary>
    public class PevtCommandFrameTests
    {
        private static readonly PevtType[] SayTypes = { PevtType.String, PevtType.String };
        private static readonly PevtType[] WaitTypes = { PevtType.Int };
        private static readonly PevtType[] CounterTypes = { PevtType.String, PevtType.String };

        // ---- 参数快照与顺序 ----

        [Fact]
        public void ArgumentsAreEvaluatedAndSnapshottedBeforeTheRoutineRuns()
        {
            var observed = new List<string>();
            PevtTestHost host = new PevtTestHost();

            host.Command("say", SayTypes, (context, args) =>
            {
                observed.Add(args.String(0));
                observed.Add(args.String(1));
                return Empty();
            });

            PevtExecution execution = host.Start("id \"T\"\nvar who : string = \"aic:noel\"\n@say(who, \"hello\")\nwho = \"changed\"\nend\n");
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(new[] { "aic:noel", "hello" }, observed);
        }

        [Fact]
        public void AtomicMethodsRunInTheOrderTheRoutineWritesThem()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, SayRoutine);

            PevtExecution execution = host.Start("id \"T\"\n@say(\"aic:noel\", \"hi\")\nend\n");

            // 第一帧跑到 WaitAdvance；触发信号后才继续。
            host.Step(execution);
            Assert.NotNull(host.Dialogue.AdvanceSignal);
            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            Assert.Equal(
                new[] { "SelectSpeaker(aic:noel)", "OpenText(hi)", "WaitAdvance()", "CommitLog()" },
                host.Dialogue.Calls);
        }

        [Fact]
        public void ASyncCommandSuspendsTheWholeFlowUntilItFinishes()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, SayRoutine);

            PevtExecution execution = host.Start("id \"T\"\nvar after : int = 0\n@say(\"aic:noel\", \"hi\")\nafter = 1\nend\n");

            for (int i = 0; i < 5; i++)
            {
                PevtExecutionResult step = host.Step(execution);
                Assert.Equal(PevtExecutionStatus.Suspended, step.Status);
                Assert.Equal(PevtSuspendReason.Wait, step.SuspendReason);

                // 等待期间后续语句一步都不能推进。
                Assert.True(execution.RootEnvironment.TryGetSlot("after", out PevtSlot slot));
                Assert.Equal(0, slot.Value.AsInt);
            }

            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("after", out PevtSlot after));
            Assert.Equal(1, after.Value.AsInt);
        }

        [Fact]
        public void FrameWaitsResumeExactlyAfterTheRequestedFrameCount()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("wait", WaitTypes, (context, args) => WaitFrames(context, args.Int(0)));

            PevtExecution execution = host.Start("id \"T\"\n@wait(3)\nend\n");

            Assert.Equal(PevtExecutionStatus.Suspended, host.Step(execution).Status);
            Assert.Equal(PevtExecutionStatus.Suspended, host.Step(execution).Status);
            Assert.Equal(PevtExecutionStatus.Suspended, host.Step(execution).Status);
            Assert.Equal(PevtExecutionStatus.Completed, host.Step(execution).Status);
        }

        // ---- 返回值契约 ----

        [Fact]
        public void ValuedCommandsCommitTheirResultOnce()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("counter_get", CounterTypes, (context, args) =>
            {
                context.Result.SetInt(11);
                return Empty();
            });

            PevtExecution execution = host.Start("id \"T\"\nvar n : int = @counter_get(\"s\", \"k\")\nend\n");
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("n", out PevtSlot slot));
            Assert.Equal(11, slot.Value.AsInt);
        }

        [Fact]
        public void ResultSinkRejectsASecondCommit()
        {
            var sink = new PevtResultSink();
            sink.SetInt(1);

            Assert.Throws<InvalidOperationException>(() => sink.SetInt(2));
            Assert.Equal(1, sink.Value.AsInt);
        }

        [Fact]
        public void Pevtr4002_MissingResultForAValuedCommand()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("counter_get", CounterTypes, (context, args) => Empty());

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\nvar n : int = @counter_get(\"s\", \"k\")\nend\n"));

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4002", result.Diagnostic.Id);
        }

        [Fact]
        public void Pevtr4002_WrongResultTypeForAValuedCommand()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("counter_get", CounterTypes, (context, args) =>
            {
                context.Result.SetBool(true); // 描述目录说这里应该是 int
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\nvar n : int = @counter_get(\"s\", \"k\")\nend\n"));

            Assert.Equal("PEVTR4002", result.Diagnostic.Id);
            Assert.Contains("int", result.Diagnostic.Message);
        }

        [Fact]
        public void Pevtr4002_PureCallThatCommitsAResult()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, (context, args) =>
            {
                context.Result.SetInt(1);
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"a\", \"b\")\nend\n"));

            Assert.Equal("PEVTR4002", result.Diagnostic.Id);
            Assert.Contains("纯调用", result.Diagnostic.Message);
        }

        [Fact]
        public void Pevtr3003_NullStringResult()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("choice_show", new[] { PevtType.String, PevtType.String }, (context, args) =>
            {
                context.Result.SetString(null);
                return Empty();
            });

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\nvar k : string = @choice_show(\"p\", \"i\")\nend\n"));

            Assert.Equal("PEVTR3003", result.Diagnostic.Id);
        }

        // ---- 失败与清理 ----

        [Fact]
        public void Pevtr4001_ExceptionsFromAtomicMethodsAreTranslatedAtTheSchedulerBoundary()
        {
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, (context, args) => Throwing());

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"a\", \"b\")\nend\n"));

            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.IsType<InvalidTimeZoneException>(result.Diagnostic.InnerException);
        }

        [Fact]
        public void Pevtr4001_UnregisteredCommandHandler()
        {
            // 描述目录里有 @say，但这个宿主没有登记处理器。
            PevtTestHost host = new PevtTestHost();
            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"a\", \"b\")\nend\n"));

            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.Contains("没有登记处理器", result.Diagnostic.Message);
        }

        [Fact]
        public void CleanupRunsInReverseOrderWhenACommandFails()
        {
            var order = new List<string>();
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, (context, args) => FailingWithCleanup(context, order));

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"a\", \"b\")\nend\n"));

            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.Equal(new[] { "third", "second", "first" }, order);
        }

        [Fact]
        public void CleanupCanBeCancelledWhenTheStepSucceeds()
        {
            var order = new List<string>();
            PevtTestHost host = new PevtTestHost();
            host.Command("say", SayTypes, (context, args) => CleanupThenPop(context, order));

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"a\", \"b\")\nend\n"));

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Empty(order); // Abort 登记后又被撤销，正常路径上不该执行
        }

        [Fact]
        public void FailedWaitBecomesTheCommandFailure()
        {
            PevtTestHost host = new PevtTestHost();
            host.Resources.Missing.Add("image:missing.png");
            host.Command("image_show", new[] { PevtType.String, PevtType.String }, ImageShowRoutine);

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@image_show(\"layer\", \"missing.png\")\nend\n"));

            Assert.Equal("PEVTR4403", result.Diagnostic.Id);
        }

        [Fact]
        public void PendingResourceKeepsTheFlowSuspendedUntilItIsReleased()
        {
            PevtTestHost host = new PevtTestHost();
            host.Resources.Pending.Add("image:slow.png");
            host.Command("image_show", new[] { PevtType.String, PevtType.String }, ImageShowRoutine);

            PevtExecution execution = host.Start("id \"T\"\n@image_show(\"layer\", \"slow.png\")\nend\n");

            Assert.Equal(PevtSuspendReason.Wait, host.Step(execution).SuspendReason);
            Assert.Equal(PevtSuspendReason.Wait, host.Step(execution).SuspendReason);

            host.Resources.Release("image:slow.png");
            Assert.Equal(PevtExecutionStatus.Completed, host.RunToCompletion(execution).Status);
            Assert.Contains("SetContent(layer,slow.png)", host.Stage.Calls);
        }

        // ---- 人物解析 ----

        [Fact]
        public void ActorIdsResolveThroughTheStageBCatalog()
        {
            PevtTestHost host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            string resolved = null;

            host.Command("say", SayTypes, (context, args) =>
            {
                if (context.Services.ActorCatalog.TryResolve(args.String(0), out ActorRegistration registration))
                    resolved = registration.Actor.DisplayKey;
                return Empty();
            });

            host.RunToCompletion(host.Start("id \"T\"\n@say(\"aic:noel\", \"hi\")\nend\n"));

            Assert.Equal("Talker_Noel", resolved);
        }

        [Fact]
        public void Pevtr4401_UnknownActorIsARuntimeFailureNotAStaticOne()
        {
            PevtTestHost host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            host.Command("say", SayTypes, (context, args) => RequireActor(context, args));

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@say(\"other.mod:ghost\", \"hi\")\nend\n"));

            Assert.Equal("PEVTR4401", result.Diagnostic.Id);
        }

        // ---- 组合协程 ----

        private static IEnumerator<PevtWait> SayRoutine(PevtRoutineContext context, PevtArguments args)
        {
            context.Services.Dialogue.SelectSpeaker(args.String(0));
            context.Services.Dialogue.OpenText(args.String(1));
            yield return context.Services.Dialogue.WaitAdvance();
            context.Services.Dialogue.CommitLog();
        }

        private static IEnumerator<PevtWait> ImageShowRoutine(PevtRoutineContext context, PevtArguments args)
        {
            yield return context.Services.Resources.RequireImage(args.String(1));
            context.Services.Image.SetContent(args.String(0), args.String(1));
            context.Services.Image.SetVisible(args.String(0), true);
        }

        private static IEnumerator<PevtWait> WaitFrames(PevtRoutineContext context, int frames)
        {
            yield return context.Services.Clock.WaitFrames(frames);
        }

        private static IEnumerator<PevtWait> RequireActor(PevtRoutineContext context, PevtArguments args)
        {
            if (!context.Services.ActorCatalog.TryResolve(args.String(0), out ActorRegistration _))
                throw new PevtRoutineFailureException("PEVTR4401", $"人物 `{args.String(0)}` 不在全局人物目录中。");

            yield break;
        }

        private static IEnumerator<PevtWait> Throwing()
        {
            throw new InvalidTimeZoneException("原子方法炸了");
        }

        private static IEnumerator<PevtWait> FailingWithCleanup(PevtRoutineContext context, List<string> order)
        {
            context.Cleanup.Push("first", () => order.Add("first"));
            context.Cleanup.Push("second", () => order.Add("second"));
            context.Cleanup.Push("third", () => order.Add("third"));
            yield return new PevtFrameWait(0);
            throw new InvalidOperationException("中途失败");
        }

        private static IEnumerator<PevtWait> CleanupThenPop(PevtRoutineContext context, List<string> order)
        {
            context.Cleanup.Push("abort", () => order.Add("abort"));
            yield return new PevtFrameWait(0);
            context.Cleanup.Pop(); // 转场正常结束，撤销 Abort 登记
        }

        private static IEnumerator<PevtWait> Empty()
        {
            yield break;
        }

        [Fact]
        public void RoutineFailureExceptionRejectsUnknownDiagnosticIds()
        {
            Assert.Throws<ArgumentException>(() => new PevtRoutineFailureException("PEVTR0000", "x"));
            Assert.Throws<ArgumentException>(() => new PevtRoutineFailureException("PEVT9101", "静态编号不能当运行诊断用"));
        }
    }
}
