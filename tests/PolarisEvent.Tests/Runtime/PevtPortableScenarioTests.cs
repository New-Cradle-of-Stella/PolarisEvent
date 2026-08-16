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
    /// 功能阶段 C 的退出条件：不接任何游戏 API，用内存替身完整跑通一个真实形状的演出流程，
    /// 并覆盖多例程竞态与停滞检测。
    /// </summary>
    public class PevtPortableScenarioTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };

            host.Command("say", new[] { PevtType.String, PevtType.String }, Say);
            host.Command("narrate", new[] { PevtType.String }, Narrate);
            host.Command("actor_enter", new[] { PevtType.String, PevtType.String, PevtType.String, PevtType.Int }, ActorEnter);
            host.Command("actor_exit", new[] { PevtType.String, PevtType.Int }, ActorExit);
            host.Command("choose", new[] { PevtType.String, PevtType.String, PevtType.String }, Choose);
            host.Command("wait", new[] { PevtType.Int }, Wait);

            return host;
        }

        [Fact]
        public void AMinimalDialogueEventRunsEndToEndOnMemoryDoubles()
        {
            const string source = @"id ""Opening""
@actor_enter(""aic:noel"", ""left"", ""default"", 10)
@say(""aic:noel"", ""早上好"")
@narrate(""风穿过走廊。"")
var picked : int = @choose(""怎么办？"", ""留下"", ""离开"")
if picked == 1
@say(""aic:noel"", ""那就留下吧"")
else
@say(""aic:noel"", ""走吧"")
endif
@actor_exit(""aic:noel"", 10)
end
";
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(source);

            // 入场：10 帧的 Place 等待
            for (int i = 0; i <= 10; i++)
                Assert.Equal(PevtExecutionStatus.Suspended, host.Step(execution).Status);

            // 第一句对话
            Assert.Equal(PevtSuspendReason.Wait, host.Step(execution).SuspendReason);
            host.Dialogue.AdvanceSignal.Signal(0);
            host.Step(execution);

            // 旁白
            host.Dialogue.AdvanceSignal.Signal(0);
            host.Step(execution);

            // 选择：选第 2 项
            Assert.NotNull(host.Choice.PresentSignal);
            host.Choice.PresentSignal.Signal(2);
            host.Step(execution);

            // 分支里的对话
            host.Dialogue.AdvanceSignal.Signal(0);

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.True(execution.RootEnvironment.TryGetSlot("picked", out PevtSlot picked));
            Assert.Equal(2, picked.Value.AsInt);

            Assert.Equal(
                new[] { "SetAppearance(aic:noel,default)", "Place(aic:noel,left,10)", "Exit(aic:noel,10)" },
                host.Portrait.Calls);

            Assert.Contains("SelectSpeaker(aic:noel)", host.Dialogue.Calls);
            Assert.Contains("ClearSpeaker()", host.Dialogue.Calls);
            Assert.Contains("OpenText(走吧)", host.Dialogue.Calls);
            Assert.DoesNotContain("OpenText(那就留下吧)", host.Dialogue.Calls);
            Assert.Equal(new[] { "Reset()", "Begin(怎么办？)", "AddIndex(留下)", "AddIndex(离开)", "PresentIndex()", "Reset()" }, host.Choice.Calls);
        }

        [Fact]
        public void UnknownActorFailsAtRuntimeWithPevtr4401NotAtLoadTime()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@actor_enter(\"other.mod:ghost\", \"left\", \"default\", 1)\nend\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal("PEVTR4401", result.Diagnostic.Id);
            Assert.Empty(host.Portrait.Calls); // 校验失败时不得产生任何副作用
        }

        [Fact]
        public void UnknownAppearanceFailsWithPevtr4402BeforeAnySideEffect()
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = host.RunToCompletion(
                host.Start("id \"T\"\n@actor_enter(\"aic:noel\", \"left\", \"nonexistent\", 1)\nend\n"));

            Assert.Equal("PEVTR4402", result.Diagnostic.Id);
            Assert.Empty(host.Portrait.Calls);
        }

        [Fact]
        public void MultipleRoutinesInterleaveDeterministicallyAcrossFrames()
        {
            var timeline = new List<string>();
            PevtTestHost host = Host();
            host.Commands.Register("input_key_enabled", new[] { PevtType.String, PevtType.Bool },
                new DelegateRoutine((context, args) => Mark(timeline, $"{context.EventId}:{args.String(0)}@{host.Clock.Frame}")));

            var scheduler = new PevtScheduler(host.Clock);
            scheduler.Register(host.Start("id \"A\"\n@input_key_enabled(\"a1\", true)\n@wait(2)\n@input_key_enabled(\"a2\", true)\nend\n"));
            scheduler.Register(host.Start("id \"B\"\n@input_key_enabled(\"b1\", true)\n@wait(1)\n@input_key_enabled(\"b2\", true)\nend\n"));

            for (int frame = 0; frame < 6; frame++)
            {
                scheduler.Tick();
                host.Clock.Advance();
            }

            // A 与 B 在同一帧的动作严格按例程 ID 升序，等待时长不同也不会打乱这个顺序。
            Assert.Equal(new[] { "A:a1@0", "B:b1@0", "B:b2@1", "A:a2@2" }, timeline);
            Assert.Empty(scheduler.Running);
        }

        [Fact]
        public void Pevtr1002_AWaitWithNoProgressSourceIsReportedAsStalled()
        {
            PevtTestHost host = Host();
            host.Commands.Register("wait_input", new[] { PevtType.Int },
                new DelegateRoutine((context, args) => DeadWait(context)));

            PevtExecutionResult result = host.RunToCompletion(
                host.Start("id \"T\"\nvar ok : bool = @wait_input(0)\nend\n"), maxFrames: 32);

            Assert.Equal("PEVTR1002", result.Diagnostic.Id);
        }

        [Fact]
        public void Pevtr1002_AnEndlessWaitEventuallyTripsTheStallBudget()
        {
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(stallFrames: 5) };
            host.Command("wait", new[] { PevtType.Int }, Wait);

            PevtExecutionResult result = host.RunToCompletion(host.Start("id \"T\"\n@wait(1000)\nend\n"), maxFrames: 64);

            Assert.Equal("PEVTR1002", result.Diagnostic.Id);
        }

        [Fact]
        public void CallStackFromInsideACommandNamesTheCommandAndTheEvent()
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = host.RunToCompletion(
                host.Start("id \"Story\"\n@actor_enter(\"other.mod:ghost\", \"left\", \"default\", 1)\nend\n"));

            List<PevtCallFrame> stack = result.Diagnostic.CallStack.ToList();

            Assert.Equal(PevtCallFrameKind.Command, stack[0].Kind);
            Assert.Equal("@actor_enter", stack[0].Name);
            Assert.Equal(PevtCallFrameKind.Event, stack[1].Kind);
            Assert.Equal("Story", stack[1].Name);
            Assert.Equal(2, stack[0].Location.StartLine);
        }

        // ---- 组合协程：形状与同步指令中间层规范第 6/7 节一致 ----

        private static IEnumerator<PevtWait> Say(PevtRoutineContext context, PevtArguments args)
        {
            if (!context.Services.ActorCatalog.TryResolve(args.String(0), out ActorRegistration _))
                throw new PevtRoutineFailureException("PEVTR4401", $"人物 `{args.String(0)}` 不在全局人物目录中。");

            context.Services.Dialogue.SelectSpeaker(args.String(0));
            context.Services.Dialogue.OpenText(args.String(1));
            yield return context.Services.Dialogue.WaitAdvance();
            context.Services.Dialogue.CommitLog();
        }

        private static IEnumerator<PevtWait> Narrate(PevtRoutineContext context, PevtArguments args)
        {
            context.Services.Dialogue.ClearSpeaker();
            context.Services.Dialogue.OpenText(args.String(0));
            yield return context.Services.Dialogue.WaitAdvance();
            context.Services.Dialogue.CommitLog();
        }

        private static IEnumerator<PevtWait> ActorEnter(PevtRoutineContext context, PevtArguments args)
        {
            string actorId = args.String(0);
            string anchorId = args.String(1);
            string appearanceId = args.String(2);

            // 先完成全部校验，再产生任何副作用。
            if (!context.Services.ActorCatalog.TryResolve(actorId, out ActorRegistration registration))
                throw new PevtRoutineFailureException("PEVTR4401", $"人物 `{actorId}` 不在全局人物目录中。");
            if (!registration.Actor.TryGetPortrait(appearanceId, out ActorVisual _))
                throw new PevtRoutineFailureException("PEVTR4402", $"人物 `{actorId}` 没有登记 `{appearanceId}`。");
            if (!context.Services.ActorCatalog.TryResolveAnchor(actorId, anchorId, out ActorAnchor _))
                throw new PevtRoutineFailureException("PEVTR4402", $"人物 `{actorId}` 没有站位 `{anchorId}`。");

            yield return context.Services.Resources.RequirePortrait(actorId, appearanceId);
            context.Services.Portrait.SetAppearance(actorId, appearanceId);
            yield return context.Services.Portrait.Place(actorId, anchorId, args.Int(3));
        }

        private static IEnumerator<PevtWait> ActorExit(PevtRoutineContext context, PevtArguments args)
        {
            yield return context.Services.Portrait.Exit(args.String(0), args.Int(1));
        }

        private static IEnumerator<PevtWait> Choose(PevtRoutineContext context, PevtArguments args)
        {
            context.Services.Choice.Reset();
            context.Services.Choice.Begin(args.String(0));
            for (int i = 1; i < args.Count; i++)
                context.Services.Choice.AddIndex(args.String(i));

            PevtWait<int> wait = context.Services.Choice.PresentIndex();
            yield return wait;
            context.Result.SetInt(wait.Result);
            context.Services.Choice.Reset();
        }

        private static IEnumerator<PevtWait> Wait(PevtRoutineContext context, PevtArguments args)
        {
            yield return context.Services.Clock.WaitFrames(args.Int(0));
        }

        private static IEnumerator<PevtWait> Mark(List<string> timeline, string entry)
        {
            timeline.Add(entry);
            yield break;
        }

        /// <summary>一个明确宣布自己没有推进源的等待，用来验证停滞检测。</summary>
        private sealed class DeadEndWait : PevtWait<bool>
        {
            public override string ProgressSource => "无（测试用死等待）";

            public override bool HasProgressSource => false;

            protected override void OnTick(PevtWaitContext context)
            {
            }
        }

        private static IEnumerator<PevtWait> DeadWait(PevtRoutineContext context)
        {
            var wait = new DeadEndWait();
            yield return wait;
            context.Result.SetBool(wait.Result);
        }
    }
}
