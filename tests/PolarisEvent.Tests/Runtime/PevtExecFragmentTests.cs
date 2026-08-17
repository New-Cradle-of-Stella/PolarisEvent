using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 功能阶段 E：<c>exec</c> 动态片段。
    ///
    /// 片段的静态校验在运行时完整重做一遍，因此这里既要证明合法片段真的能读写授权的外层变量，
    /// 也要证明非法片段拿到的是 PEVTR12xx 而不是一个含糊的内部错误。
    /// </summary>
    public class PevtExecFragmentTests
    {
        private static readonly IReadOnlyList<PevtType> LogSignature = new[] { PevtType.String };

        private static PevtTestHost Host(List<string> log = null)
        {
            var host = new PevtTestHost();
            host.Command("narrate", LogSignature, (context, args) =>
            {
                log?.Add(args.String(0));
                return Empty();
            });
            return host;
        }

        private static IEnumerator<PevtWait> Empty()
        {
            yield break;
        }

        /// <summary>断言正常结束，失败时把诊断带进消息——不然只能看到 "Completed != Faulted"。</summary>
        private static void AssertCompleted(PevtExecution execution) =>
            Assert.True(execution.Status == PevtExecutionStatus.Completed,
                $"期望正常结束，实际 {execution.Status}：{execution.Diagnostic?.Id} {execution.Diagnostic?.Message}"
                + $" / inner={execution.Diagnostic?.InnerDiagnostic?.Message}");

        // ---- 外层变量 ----

        [Fact]
        public void FragmentCanReadAnAuthorizedOuterVariable()
        {
            var log = new List<string>();
            PevtTestHost host = Host(log);

            PevtExecution execution = host.Start(
                "id \"T\"\nvar msg : string = \"from-host\"\nexec(\"@narrate(msg)\")\nend\n");

            host.RunToCompletion(execution);

            AssertCompleted(execution);
            Assert.Equal(new[] { "from-host" }, log);
        }

        [Fact]
        public void FragmentCanWriteAnAuthorizedOuterVariable()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start(
                "id \"T\"\nvar n : int = 1\nexec(\"n = 9\")\nend\n");

            host.RunToCompletion(execution);

            AssertCompleted(execution);
            Assert.Equal(9, execution.RootEnvironment.SlotValue("n").AsInt);
        }

        /// <summary>片段里新声明的变量只活在临时环境里，片段结束就没了。</summary>
        [Fact]
        public void FragmentLocalVariablesDoNotLeakIntoTheHost()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start(
                "id \"T\"\nexec(\"var temp : int = 3\")\nend\n");

            host.RunToCompletion(execution);

            AssertCompleted(execution);
            Assert.False(execution.RootEnvironment.TryGetSlot("temp", out _));
        }

        /// <summary>常量不在授权范围内：片段看不见它，因此按"名称不存在"处理。</summary>
        [Fact]
        public void FragmentCannotSeeHostConstants()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start(
                "id \"T\"\nconst k : int = 5\nexec(\"@narrate(\\\"x\\\")\\nvar copy : int = k\")\nend\n");

            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR1201", execution.Diagnostic.Id);
        }

        // ---- 失败路径 ----

        [Fact]
        public void Pevtr1201_FragmentThatFailsStaticValidationKeepsTheStaticReasonAttached()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start("id \"T\"\nexec(\"var x : int = \\\"text\\\"\")\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR1201", execution.Diagnostic.Id);
            Assert.NotNull(execution.Diagnostic.InnerDiagnostic);
        }

        [Theory]
        [InlineData("end")]
        [InlineData("#label")]
        [InlineData("goto #label")]
        [InlineData("enable async")]
        [InlineData("block _b()\nendblock")]
        public void Pevtr1202_FragmentsCannotUseForbiddenStatements(string fragment)
        {
            PevtTestHost host = Host();

            string escaped = fragment.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            PevtExecution execution = host.Start("id \"T\"\nexec(\"" + escaped + "\")\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR1202", execution.Diagnostic.Id);
        }

        [Fact]
        public void Pevtr1203_NestedExecStopsAtTheDepthLimit()
        {
            PevtTestHost host = Host();

            // 每层片段都再 exec 一层：第 MaxDynamicDepth+1 层被拒。
            string innermost = "@narrate(\"deep\")";
            string source = innermost;
            for (int i = 0; i <= PevtExecution.MaxDynamicDepth; i++)
                source = "exec(\"" + source.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")";

            PevtExecution execution = host.Start("id \"T\"\n" + source + "\nend\n");
            host.RunToCompletion(execution, maxFrames: 512);

            Assert.Equal(PevtExecutionStatus.Faulted, execution.Status);
            Assert.Equal("PEVTR1203", execution.Diagnostic.Id);
        }

        /// <summary>
        /// <c>exec</c> 的实参数量由静态门把住（PEVT7402），根本到不了运行时——
        /// 运行时那道"恰好一个 string"的检查是防御性的，不是这条规则的归属地。
        /// </summary>
        [Fact]
        public void Pevt7402_ExecArityIsAStaticError()
        {
            PevtTestHost host = Host();

            PevtCompileResult result = null;
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => result = host.TryCompile("id \"T\"\nexec(\"@narrate(\\\"a\\\")\", \"extra\")\nend\n"));

            Assert.Null(result);
            Assert.Contains("PEVT7402", error.Message);
        }

        /// <summary>片段的步数计入同一份总预算，不是另开一份（计划：exec 独立深度与步数预算并入统一预算）。</summary>
        [Fact]
        public void FragmentStepsCountAgainstTheSharedTotalBudget()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start("id \"T\"\nvar n : int = 0\nexec(\"n = 1\")\nend\n");
            host.RunToCompletion(execution);

            // 宿主自己只有几条指令；片段的声明与赋值也记在同一个账本上。
            Assert.True(execution.Budget.TotalSteps > 4, $"总步数只有 {execution.Budget.TotalSteps}，片段的步数没有计入。");
        }
    }
}
