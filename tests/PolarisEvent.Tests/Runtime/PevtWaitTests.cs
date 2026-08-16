using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 统一等待状态机：Tick 与 Cancel 幂等、终态不再推进、每种内置等待都能说明推进源。
    /// Core 里不存在 Unity yield 对象，所以这些测试完全在内存中跑。
    /// </summary>
    public class PevtWaitTests
    {
        private static PevtWaitContext At(long frame) => new PevtWaitContext(frame);

        private static void TickTo(PevtWait wait, long from, long to)
        {
            for (long frame = from; frame <= to; frame++)
                wait.Tick(At(frame));
        }

        [Fact]
        public void NewWaitStartsInCreatedAndBecomesPendingOnFirstTick()
        {
            var wait = new PevtFrameWait(2);

            Assert.Equal(PevtWaitState.Created, wait.State);
            wait.Tick(At(0));
            Assert.Equal(PevtWaitState.Pending, wait.State);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public void FrameWaitCompletesAfterExactlyTheRequestedFrames(int frames)
        {
            var wait = new PevtFrameWait(frames);

            for (int i = 0; i < frames; i++)
            {
                wait.Tick(At(i));
                Assert.False(wait.IsCompleted);
            }

            wait.Tick(At(frames));
            Assert.Equal(PevtWaitState.Succeeded, wait.State);
        }

        [Fact]
        public void FrameWaitRejectsNegativeFrames() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new PevtFrameWait(-1));

        [Fact]
        public void NextFrameWaitYieldsExactlyOnce()
        {
            var wait = new PevtNextFrameWait();

            wait.Tick(At(10));
            Assert.False(wait.IsCompleted);

            wait.Tick(At(11));
            Assert.Equal(PevtWaitState.Succeeded, wait.State);
        }

        [Fact]
        public void TickAfterCompletionIsANoOp()
        {
            var wait = new PevtFrameWait(0);
            wait.Tick(At(0));
            Assert.Equal(PevtWaitState.Succeeded, wait.State);

            wait.Tick(At(99));
            Assert.Equal(PevtWaitState.Succeeded, wait.State);
        }

        [Fact]
        public void CancelIsIdempotentAndCannotUndoATerminalState()
        {
            var completed = new PevtFrameWait(0);
            completed.Tick(At(0));
            completed.Cancel();
            completed.Cancel();
            Assert.Equal(PevtWaitState.Succeeded, completed.State);

            var pending = new PevtFrameWait(10);
            pending.Tick(At(0));
            pending.Cancel();
            Assert.Equal(PevtWaitState.Cancelling, pending.State);
            pending.Cancel();
            Assert.Equal(PevtWaitState.Cancelling, pending.State);

            pending.Tick(At(1));
            Assert.Equal(PevtWaitState.Cancelled, pending.State);

            // 取消完成后继续 Tick 也不会翻回 Pending。
            pending.Tick(At(2));
            Assert.Equal(PevtWaitState.Cancelled, pending.State);
        }

        [Fact]
        public void ReadingTheResultBeforeSuccessIsAnInternalError()
        {
            var wait = new PevtSignalWait<int>();
            Assert.Throws<InvalidOperationException>(() => wait.Result);

            wait.Signal(7);
            wait.Tick(At(0));
            Assert.Equal(7, wait.Result);
        }

        [Fact]
        public void SignalWaitOnlyHonoursTheFirstSignal()
        {
            var wait = new PevtSignalWait<string>();
            wait.Signal("first");
            wait.Signal("second");
            wait.Tick(At(0));

            Assert.Equal("first", wait.Result);
        }

        [Fact]
        public void SignalWaitCanFaultWithARuntimeDiagnostic()
        {
            var wait = new PevtSignalWait<int>();
            wait.Fault(new PevtRuntimeDiagnostic("PEVTR4001", "UI 会话失败"));
            wait.Tick(At(0));

            Assert.Equal(PevtWaitState.Faulted, wait.State);
            Assert.Equal("PEVTR4001", wait.Error.Id);
        }

        [Fact]
        public void ResourceWaitFailsWithPevtr4403()
        {
            var wait = new PevtResourceWait("img", () => PevtResourceStatus.Failed);
            wait.Tick(At(0));

            Assert.Equal(PevtWaitState.Faulted, wait.State);
            Assert.Equal("PEVTR4403", wait.Error.Id);
        }

        [Fact]
        public void InputWaitDistinguishesInputFromTimeout()
        {
            bool pressed = false;
            var byInput = new PevtInputWait(() => pressed, timeoutFrames: 10);
            byInput.Tick(At(0));
            pressed = true;
            byInput.Tick(At(1));
            Assert.True(byInput.Result);

            var byTimeout = new PevtInputWait(() => false, timeoutFrames: 3);
            TickTo(byTimeout, 0, 3);
            Assert.Equal(PevtWaitState.Succeeded, byTimeout.State);
            Assert.False(byTimeout.Result);
        }

        [Fact]
        public void InputWaitWithZeroTimeoutNeverTimesOut()
        {
            var wait = new PevtInputWait(() => false, timeoutFrames: 0);
            TickTo(wait, 0, 1000);

            Assert.False(wait.IsCompleted);
        }

        [Fact]
        public void MotionWaitReportsWhetherEverythingFinishedBeforeTheTimeout()
        {
            bool finished = false;
            var wait = new PevtMotionWait(() => finished, timeoutFrames: 5);
            wait.Tick(At(0));
            finished = true;
            wait.Tick(At(1));

            Assert.True(wait.Result);

            var timedOut = new PevtMotionWait(() => false, timeoutFrames: 2);
            TickTo(timedOut, 0, 2);
            Assert.False(timedOut.Result);
        }

        [Fact]
        public void PredicateWaitPollsUntilTheConditionHolds()
        {
            int calls = 0;
            var wait = new PevtPredicateWait(() => ++calls >= 3, "适配器状态");

            TickTo(wait, 0, 5);

            Assert.Equal(PevtWaitState.Succeeded, wait.State);
            Assert.Equal(3, calls);
            Assert.Equal("适配器状态", wait.ProgressSource);
        }

        [Fact]
        public void CompositeWaitCompletesOnlyWhenEveryMemberIsTerminal()
        {
            var a = new PevtFrameWait(1);
            var b = new PevtFrameWait(3);
            var composite = new PevtCompositeWait(new PevtWait[] { a, b });

            TickTo(composite, 0, 2);
            Assert.False(composite.IsCompleted);

            composite.Tick(At(3));
            Assert.Equal(PevtWaitState.Succeeded, composite.State);
            Assert.Equal(2, composite.Result);
        }

        [Fact]
        public void CompositeWaitFailsWhenAnyMemberFails()
        {
            var ok = new PevtFrameWait(0);
            var bad = new PevtResourceWait("x", () => PevtResourceStatus.Failed);
            var composite = new PevtCompositeWait(new PevtWait[] { ok, bad });

            composite.Tick(At(0));

            Assert.Equal(PevtWaitState.Faulted, composite.State);
            Assert.Equal("PEVTR4403", composite.Error.Id);
        }

        [Fact]
        public void CancellingACompositeCascadesToEveryMember()
        {
            var a = new PevtFrameWait(10);
            var b = new PevtFrameWait(10);
            var composite = new PevtCompositeWait(new PevtWait[] { a, b });

            composite.Tick(At(0));
            composite.Cancel();
            composite.Tick(At(1));

            Assert.Equal(PevtWaitState.Cancelled, composite.State);
            Assert.Equal(PevtWaitState.Cancelled, a.State);
            Assert.Equal(PevtWaitState.Cancelled, b.State);
        }

        [Fact]
        public void EveryBuiltinWaitDeclaresItsProgressSource()
        {
            PevtWait[] waits =
            {
                new PevtNextFrameWait(),
                new PevtFrameWait(1),
                new PevtPredicateWait(() => false),
                new PevtSignalWait<int>(),
                new PevtResourceWait("x", () => PevtResourceStatus.Loading),
                new PevtMotionWait(() => false),
                new PevtInputWait(() => false),
                new PevtCompositeWait(new PevtWait[] { new PevtFrameWait(1) }),
            };

            Assert.All(waits, wait => Assert.False(string.IsNullOrWhiteSpace(wait.ProgressSource)));
        }

        [Fact]
        public void ACompositeWithNoLiveMemberReportsThatItCannotProgress()
        {
            var member = new PevtFrameWait(0);
            var composite = new PevtCompositeWait(new PevtWait[] { member });

            member.Tick(At(0));
            Assert.True(member.IsCompleted);
            Assert.False(composite.HasProgressSource);
        }

        [Fact]
        public void CompositeRejectsNullMembers() =>
            Assert.Throws<ArgumentException>(() => new PevtCompositeWait(new PevtWait[] { null }));
    }
}
