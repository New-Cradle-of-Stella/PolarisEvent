using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 功能阶段 E：<c>callevt</c> 子事件。
    ///
    /// 目标存在性是运行期事实（全局不变量第 8 条），所以这里的重点是"编译得过、运行时才判定"，
    /// 以及四个失败编号各自对得上。
    /// </summary>
    public class PevtSubEventTests
    {
        private static readonly IReadOnlyList<PevtType> LogSignature = new[] { PevtType.String };

        /// <summary>一个把参数记进列表的 <c>@narrate</c>，用来观察子事件到底跑了没有。</summary>
        private static PevtTestHost HostWithLog(List<string> log)
        {
            var host = new PevtTestHost();
            host.Command("narrate", LogSignature, (context, args) => Record(log, args.String(0)));
            return host;
        }

        private static IEnumerator<PevtWait> Record(List<string> log, string text)
        {
            log.Add(text);
            yield break;
        }

        [Fact]
        public void SyncCallevtRunsTheChildBeforeTheNextStatement()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithLog(log);
            host.Event("Child", "id \"Child\"\n@narrate(\"child\")\nend\n");

            PevtExecution execution = host.Start("id \"T\"\n@narrate(\"before\")\ncallevt \"Child\"\n@narrate(\"after\")\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Equal(new[] { "before", "child", "after" }, log);
        }

        /// <summary>子事件失败时，报出来的必须是子事件自己的编号，不能被 await 的 PEVTR5001 盖掉。</summary>
        [Fact]
        public void SyncCallevtPropagatesTheChildDiagnosticUnchanged()
        {
            var host = new PevtTestHost();
            host.Command("narrate", LogSignature, Failing);
            host.Event("Child", "id \"Child\"\n@narrate(\"x\")\nend\n");

            PevtExecution execution = host.Start("id \"T\"\ncallevt \"Child\"\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR4401", execution.Diagnostic.Id);
        }

        [Fact]
        public void AsyncCallevtNeedsTheTargetToDeclareEnableAsync()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithLog(log);
            host.Event("Child", "id \"Child\"\nenable async\n@narrate(\"child\")\nend\n");

            PevtExecution execution = host.Start("id \"T\"\nenable async\nhandler h = callevt \"Child\"\nawait h\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Contains("child", log);
        }

        [Fact]
        public void Pevtr4303_AsyncCallevtOnANonAsyncTarget()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithLog(log);
            host.Event("Child", "id \"Child\"\n@narrate(\"child\")\nend\n");

            PevtExecution execution = host.Start("id \"T\"\nenable async\nhandler h = callevt \"Child\"\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR4303", execution.Diagnostic.Id);

            // 句柄声明没有完成初始化，目标一步都没跑。
            Assert.Empty(log);
        }

        [Fact]
        public void Pevtr4301_TargetNotInTheRegistry()
        {
            PevtTestHost host = HostWithLog(new List<string>());

            PevtExecution execution = host.Start("id \"T\"\ncallevt \"Missing\"\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal("PEVTR4301", execution.Diagnostic.Id);
        }

        [Fact]
        public void Pevtr4302_TargetHasSeveralSources()
        {
            PevtTestHost host = HostWithLog(new List<string>());
            host.Event("Child", "id \"Child\"\nend\n");
            host.SubEvents.Ambiguous.Add("Child");

            PevtExecution execution = host.Start("id \"T\"\ncallevt \"Child\"\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal("PEVTR4302", execution.Diagnostic.Id);
        }

        [Fact]
        public void Pevtr4304_TargetResolvesButCannotStart()
        {
            PevtTestHost host = HostWithLog(new List<string>());
            host.Event("Child", "id \"Child\"\nend\n");
            host.SubEvents.StartFailures.Add("Child");

            PevtExecution execution = host.Start("id \"T\"\ncallevt \"Child\"\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal("PEVTR4304", execution.Diagnostic.Id);
        }

        /// <summary>晚注册：调用方编译时目标还不存在，运行到那一行时才登记进来。</summary>
        [Fact]
        public void TargetRegisteredAfterTheCallerStartedIsStillFound()
        {
            var log = new List<string>();
            PevtTestHost host = HostWithLog(log);
            host.Command("wait", new[] { PevtType.Int }, WaitFrames);

            PevtExecution execution = host.Start("id \"T\"\n@wait(2)\ncallevt \"Late\"\nend\n");

            // 事件已经在跑了，这时候才把目标加进注册表。
            host.Step(execution);
            host.Event("Late", "id \"Late\"\n@narrate(\"late\")\nend\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, execution.Status);
            Assert.Contains("late", log);
        }

        /// <summary>子事件共享同一份预算，因此无限递归撞的是调用深度上限，而不是宿主栈。</summary>
        [Fact]
        public void Pevtr1003_RecursiveCallevtHitsTheSharedCallDepthLimit()
        {
            PevtTestHost host = HostWithLog(new List<string>());
            host.Limits = new PevtBudgetLimits(maxCallDepth: 4);
            host.Event("Loop", "id \"Loop\"\ncallevt \"Loop\"\nend\n");

            PevtExecution execution = host.Start("id \"T\"\ncallevt \"Loop\"\nend\n");
            host.RunToCompletion(execution, maxFrames: 512);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);

            // 最内层报 PEVTR1003，外层按"子事件失败就是本流程失败"逐层原样上报。
            Assert.Equal("PEVTR1003", execution.Diagnostic.Id);
        }

        private static IEnumerator<PevtWait> Failing(PevtRoutineContext context, PevtArguments args)
        {
            yield return new PevtFrameWait(0);
            throw new PevtRoutineFailureException("PEVTR4401", "测试用的子事件失败。");
        }

        private static IEnumerator<PevtWait> WaitFrames(PevtRoutineContext context, PevtArguments args)
        {
            yield return new PevtFrameWait(args.Int(0));
        }
    }
}
