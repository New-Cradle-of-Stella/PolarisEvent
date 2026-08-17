using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 功能阶段 E：异步调用、句柄、集合等待与取消。
    ///
    /// 全部用内存替身跑，因此断言的是调度语义本身——谁先完成、谁被取消、结果怎么绑定——
    /// 而不是某个游戏适配器的行为。
    /// </summary>
    public class PevtAsyncTests
    {
        private static readonly IReadOnlyList<PevtType> MoveSignature =
            new[] { PevtType.String, PevtType.String, PevtType.Int };

        private const string Prelude = "id \"T\"\nenable async\n";

        /// <summary>一个跨 <c>frames</c> 帧的 <c>@actor_move</c>：同步调用与 <c>_start</c> 共用它。</summary>
        private static PevtTestHost HostWithMove(List<string> log = null)
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, (context, args) => Move(context, args, log));
            host.Command("wait", new[] { PevtType.Int }, WaitFrames);
            return host;
        }

        private static IEnumerator<PevtWait> Move(PevtRoutineContext context, PevtArguments args, List<string> log)
        {
            log?.Add("start:" + args.String(0));
            yield return new PevtFrameWait(args.Int(2));
            log?.Add("done:" + args.String(0));
        }

        // ---- 启动与 status ----

        [Fact]
        public void AsyncStartReturnsImmediatelyAndTheEventKeepsRunning()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithMove(log);

            // 立绘要动 20 帧，事件只等 2 帧就结束：status 一直是 0，事件不为它阻塞。
            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 20)\n"
                + "@wait(2)\n"
                + "var s : int = status h\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(0, execution.RootEnvironment.SlotValue("s").AsInt);

            // 协程确实跑起来了，但事件没有等它跑完。
            Assert.Contains("start:a", log);
            Assert.DoesNotContain("done:a", log);
        }

        [Fact]
        public void AwaitOnASucceededHandleReportsStatusOne()
        {
            PevtTestHost host = HostWithMove();

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 2)\n"
                + "await h\n"
                + "var s : int = status h\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(1, execution.RootEnvironment.SlotValue("s").AsInt);
        }

        /// <summary>异步协程即使在创建当帧就完成，也会产生一个有效句柄，status 立即为 1（第 7 节）。</summary>
        [Fact]
        public void ZeroFrameAsyncCallStillProducesAValidHandle()
        {
            PevtTestHost host = HostWithMove();

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 0)\n"
                + "await h\n"
                + "var s : int = status h\n"
                + "end\n");

            host.RunToCompletion(execution);
            Assert.Equal(1, execution.RootEnvironment.SlotValue("s").AsInt);
        }

        // ---- 异步块与返回值 ----

        [Fact]
        public void AsyncBlockCarriesItsReturnValueThroughAwait()
        {
            var host = new PevtTestHost();

            PevtExecution execution = host.Start(Prelude
                + "async block _answer() : int\n"
                + "var v : int = 42\n"
                + "return v\n"
                + "endblock\n"
                + "handler h = _answer()\n"
                + "var got : int = await h\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(42, execution.RootEnvironment.SlotValue("got").AsInt);
        }

        [Fact]
        public void SyncAndAsyncCallsShareTheSameBlockDefinition()
        {
            var host = new PevtTestHost();

            // 同一个块既被同步调用又被异步启动；两条路径必须得到同一个值。
            PevtExecution execution = host.Start(Prelude
                + "async block _answer() : int\n"
                + "var v : int = 7\n"
                + "return v\n"
                + "endblock\n"
                + "handler h = _answer()\n"
                + "var viaAsync : int = await h\n"
                + "end\n");

            host.RunToCompletion(execution);
            Assert.Equal(7, execution.RootEnvironment.SlotValue("viaAsync").AsInt);
        }

        // ---- 失败与观察 ----

        [Fact]
        public void Pevtr5001_AwaitingAFailedHandleFaultsWithTheOriginalCauseAttached()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, Failing);

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 1)\n"
                + "await h\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR5001", execution.Diagnostic.Id);
            Assert.Equal("PEVTR4401", execution.Diagnostic.InnerDiagnostic.Id);
        }

        /// <summary>异步失败不回溯到启动语句：事件照常跑完，只留一条 PEVTR5005 警告（第 11 节）。</summary>
        [Fact]
        public void Pevtr5005_UnobservedAsyncFailureIsAWarningNotATermination()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, Failing);

            host.Command("wait", new[] { PevtType.Int }, WaitFrames);
            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 1)\n"
                + "@wait(3)\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Single(execution.Warnings);
            Assert.Equal("PEVTR5005", execution.Warnings[0].Id);
        }

        [Fact]
        public void AwaitingAFailureMarksItObservedSoNoWarningIsRaised()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, Failing);

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 1)\n"
                + "await h\n"
                + "end\n");

            host.RunToCompletion(execution);
            Assert.Empty(execution.Warnings);
        }

        // ---- kill ----

        [Fact]
        public void KillStopsTheRoutineAndStatusBecomesTwo()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithMove(log);

            // 先给调度器一帧让协程真的开始，再 kill——否则测的只是"还没启动就被丢掉"。
            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 100)\n"
                + "@wait(2)\n"
                + "kill h\n"
                + "var s : int = status h\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(2, execution.RootEnvironment.SlotValue("s").AsInt);

            // 协程被停在等待里，业务代码没有跑到结束那一步。
            Assert.Contains("start:a", log);
            Assert.DoesNotContain("done:a", log);
        }

        /// <summary>对已经结束的目标 kill 立即成功，且不改写它的结果（第 10 节）。</summary>
        [Fact]
        public void KillOnAFinishedHandleIsANoOp()
        {
            PevtTestHost host = HostWithMove();

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 0)\n"
                + "await h\n"
                + "kill h\n"
                + "var s : int = status h\n"
                + "end\n");

            host.RunToCompletion(execution);
            Assert.Equal(1, execution.RootEnvironment.SlotValue("s").AsInt);
        }

        [Fact]
        public void EventEndKillsRoutinesItStillOwns()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithMove(log);

            PevtExecution execution = host.Start(Prelude
                + "handler h = @actor_move_start(\"a\", \"left\", 100)\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(0, execution.AsyncRoutines.RunningCount);
            Assert.DoesNotContain("done:a", log);
        }

        // ---- await all / any ----

        [Fact]
        public void AwaitAllReturnsTheNumberOfNormalCompletions()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, (context, args) =>
                args.String(0) == "bad" ? Failing(context, args) : Move(context, args, null));

            PevtExecution execution = host.Start(Prelude
                + "handler a = @actor_move_start(\"a\", \"left\", 1)\n"
                + "handler b = @actor_move_start(\"bad\", \"left\", 1)\n"
                + "handler c = @actor_move_start(\"c\", \"left\", 3)\n"
                + "var n : int = await all(a, b, c)()\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(2, execution.RootEnvironment.SlotValue("n").AsInt);
        }

        /// <summary>失败句柄对应的绑定变量保持未初始化，真读到它才报 PEVTR3002（第 9 节）。</summary>
        [Fact]
        public void AwaitAllLeavesBindingsOfFailedHandlesUninitialized()
        {
            var host = new PevtTestHost();

            PevtExecution execution = host.Start(Prelude
                + "async block _ok() : int\n"
                + "var v : int = 5\n"
                + "return v\n"
                + "endblock\n"
                + "async block _bad() : int\n"
                + "@actor_move(\"x\", \"left\", 0)\n"
                + "var v : int = 6\n"
                + "return v\n"
                + "endblock\n"
                + "handler a = _ok()\n"
                + "handler b = _bad()\n"
                + "var n : int = await all(a, b)(x, y)\n"
                + "var readBad : int = y\n"
                + "end\n");
            host.Command("actor_move", MoveSignature, Failing);

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR3002", execution.Diagnostic.Id);
        }

        [Fact]
        public void AwaitAnyReturnsTheFirstNormalCompletionByListOrder()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, (context, args) => Move(context, args, null));

            // 三个句柄同帧就绪（都是 0 帧），按列表顺序取序号最小的那个。
            PevtExecution execution = host.Start(Prelude
                + "handler a = @actor_move_start(\"a\", \"left\", 0)\n"
                + "handler b = @actor_move_start(\"b\", \"left\", 0)\n"
                + "var which : int = await any(a, b)()\n"
                + "end\n");

            host.RunToCompletion(execution);
            Assert.Equal(1, execution.RootEnvironment.SlotValue("which").AsInt);
        }

        [Fact]
        public void AwaitAnyCancelsTheLosersBeforeReturning()
        {
            var log = new List<string>();
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, (context, args) => Move(context, args, log));

            PevtExecution execution = host.Start(Prelude
                + "handler fast = @actor_move_start(\"fast\", \"left\", 1)\n"
                + "handler slow = @actor_move_start(\"slow\", \"left\", 100)\n"
                + "var which : int = await any(fast, slow)()\n"
                + "var s : int = status slow\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(1, execution.RootEnvironment.SlotValue("which").AsInt);
            Assert.Equal(2, execution.RootEnvironment.SlotValue("s").AsInt);
            Assert.DoesNotContain("done:slow", log);
        }

        [Fact]
        public void AwaitAnyReturnsZeroWhenEverythingFails()
        {
            var host = new PevtTestHost();
            host.Command("actor_move", MoveSignature, Failing);

            PevtExecution execution = host.Start(Prelude
                + "handler a = @actor_move_start(\"a\", \"left\", 1)\n"
                + "handler b = @actor_move_start(\"b\", \"left\", 1)\n"
                + "var which : int = await any(a, b)()\n"
                + "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(0, execution.RootEnvironment.SlotValue("which").AsInt);
        }

        // ---- 辅助 ----

        private static IEnumerator<PevtWait> Failing(PevtRoutineContext context, PevtArguments args)
        {
            yield return new PevtFrameWait(0);
            throw new PevtRoutineFailureException("PEVTR4401", "测试用的异步失败。");
        }

        private static IEnumerator<PevtWait> WaitFrames(PevtRoutineContext context, PevtArguments args)
        {
            yield return new PevtFrameWait(args.Int(0));
        }
    }
}
