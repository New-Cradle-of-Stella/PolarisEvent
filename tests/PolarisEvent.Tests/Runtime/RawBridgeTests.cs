using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Raw;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 两个受控逃生口：<c>$raw cmd</c> 的原版会话通道，和 <c>$raw cs</c> 的受信任 C# 执行器。
    /// </summary>
    public class RawBridgeTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            host.WithBuiltinRoutines();
            return host;
        }

        private static PevtValue Slot(PevtExecution execution, string name)
        {
            Assert.True(execution.RootEnvironment.TryGetSlot(name, out PevtSlot slot), $"环境里没有 `{name}`。");
            return slot.Value;
        }

        // ==== $raw cmd ====

        [Fact]
        public void RawCommandRunsOneVanillaSessionAndReleasesIt()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n$raw cmd'''MSG a'''\nend\n");

            host.Step(execution);
            Assert.Equal(new[] { "MSG a" }, host.RawCommandBridge.Started);
            Assert.True(host.RawCommands.IsBusy);

            host.RawCommandBridge.FinishCurrent();
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.False(host.RawCommands.IsBusy);
            Assert.Equal(1, host.RawCommandBridge.ReleaseCount);
        }

        [Fact]
        public void RawCommandKeepsTheOriginalNewlinesOfAMultiLineBlock()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n$raw cmd'''\nMSG a\nMSG b\n'''\nend\n");

            host.Step(execution);

            Assert.Equal(new[] { "\nMSG a\nMSG b\n" }, host.RawCommandBridge.Started);
        }

        /// <summary>12.4 节：<c>\'''</c> 在提交前折回 <c>'''</c>。</summary>
        [Fact]
        public void RawCommandUnescapesTheDelimiterBeforeSubmitting()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n$raw cmd'''MSG \\''' x'''\nend\n");

            host.Step(execution);

            Assert.Equal(new[] { "MSG ''' x" }, host.RawCommandBridge.Started);
        }

        /// <summary>同一时间只能有一个原版会话：第二条排队，前一条结束后才开始。</summary>
        [Fact]
        public void ASecondRawCommandQueuesBehindTheRunningSession()
        {
            var bridge = new FakeRawCommandBridge();
            var channel = new PevtRawCommandChannel(bridge);

            PevtWait first = channel.Submit("A");
            PevtWait second = channel.Submit("B");

            Tick(first, second, frame: 0);

            Assert.Equal(new[] { "A" }, bridge.Started);
            Assert.True(channel.IsBusy);
            Assert.Equal(1, channel.QueueLength);
            Assert.Equal(new[] { "B" }, channel.PendingCommands);

            bridge.FinishCurrent();
            Tick(first, second, frame: 1);

            Assert.Equal(PevtWaitState.Succeeded, first.State);
            Assert.Equal(new[] { "A", "B" }, bridge.Started);
            Assert.Equal(0, channel.QueueLength);

            bridge.FinishCurrent();
            Tick(first, second, frame: 2);

            Assert.Equal(PevtWaitState.Succeeded, second.State);
            Assert.False(channel.IsBusy);
            Assert.Equal(2, bridge.ReleaseCount);
        }

        [Fact]
        public void Pevtr4101_WhenTheVanillaRuntimeRejectsTheText()
        {
            PevtTestHost host = Host();
            host.RawCommandBridge.Rejected.Add("BAD");

            PevtExecution execution = host.Start("id \"T\"\n$raw cmd'''BAD'''\nend\n");
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4101", result.Diagnostic.Id);
            Assert.False(host.RawCommands.IsBusy);
        }

        [Fact]
        public void Pevtr4101_WhenTheSessionEndsWithAFailure()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n$raw cmd'''MSG a'''\nend\n");

            host.Step(execution);
            host.RawCommandBridge.FinishCurrent("原版拒绝了 MSG");
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal("PEVTR4101", result.Diagnostic.Id);
            Assert.Contains("原版拒绝了 MSG", result.Diagnostic.Message);
            Assert.Equal(1, host.RawCommandBridge.ReleaseCount);
        }

        /// <summary>排队中被取消：直接退出队列，原版那边什么都没发生。</summary>
        [Fact]
        public void CancellingAQueuedRawCommandNeverStartsASession()
        {
            var bridge = new FakeRawCommandBridge();
            var channel = new PevtRawCommandChannel(bridge);

            PevtWait first = channel.Submit("A");
            PevtWait second = channel.Submit("B");
            Tick(first, second, frame: 0);

            second.Cancel();
            Tick(second, frame: 1);

            Assert.Equal(PevtWaitState.Cancelled, second.State);
            Assert.Equal(new[] { "A" }, bridge.Started);
            Assert.Equal(0, channel.QueueLength);
        }

        /// <summary>已经在跑时被取消：先向原版请求停止，等它确认停下再释放。</summary>
        [Fact]
        public void CancellingARunningRawCommandWaitsForTheVanillaConfirmation()
        {
            var bridge = new FakeRawCommandBridge();
            var channel = new PevtRawCommandChannel(bridge);

            PevtWait wait = channel.Submit("A");
            Tick(wait, frame: 0);

            FakeRawCommandSession session = bridge.Current;
            wait.Cancel();
            Tick(wait, frame: 1);

            Assert.True(session.CancelRequested);
            Assert.False(session.Released);
            Assert.Equal(PevtWaitState.Cancelling, wait.State);

            session.Finish(null);
            Tick(wait, frame: 2);

            Assert.Equal(PevtWaitState.Cancelled, wait.State);
            Assert.True(session.Released);
            Assert.False(channel.IsBusy);
        }

        /// <summary>
        /// 插件卸载兜底：被强制结束的例程可能再也不会被 Tick，通道必须能自己把活动会话收掉。
        /// </summary>
        [Fact]
        public void ReleaseAllTearsDownTheRunningSessionAndTheQueue()
        {
            var bridge = new FakeRawCommandBridge();
            var channel = new PevtRawCommandChannel(bridge);

            PevtWait first = channel.Submit("A");
            PevtWait second = channel.Submit("B");
            Tick(first, second, frame: 0);

            FakeRawCommandSession session = bridge.Current;
            Assert.Empty(channel.ReleaseAll());

            Assert.True(session.CancelRequested);
            Assert.True(session.Released);
            Assert.Equal(PevtWaitState.Cancelled, first.State);
            Assert.Equal(PevtWaitState.Cancelled, second.State);
            Assert.False(channel.IsBusy);
            Assert.Equal(0, channel.QueueLength);
        }

        /// <summary>
        /// 活动会话被放弃时通道必须能自己回收。
        /// </summary>
        [Fact]
        public void AnAbandonedCancellingSessionIsReclaimedForTheNextInLine()
        {
            var bridge = new FakeRawCommandBridge();
            var channel = new PevtRawCommandChannel(bridge);

            PevtWait first = channel.Submit("A");
            PevtWait second = channel.Submit("B");
            Tick(first, second, frame: 0);

            FakeRawCommandSession abandoned = bridge.Current;

            // 取消之后再也不 Tick first——模拟例程被强制结束。
            first.Cancel();
            Assert.Equal(PevtWaitState.Cancelling, first.State);

            Tick(second, frame: 1);

            Assert.True(abandoned.Released);
            Assert.Equal(PevtWaitState.Cancelled, first.State);
            Assert.Equal(new[] { "A", "B" }, bridge.Started);
            Assert.True(channel.IsBusy);
        }

        /// <summary>失败的编译不进缓存：否则另一个文件里相同的代码会拿到指向别人文件的诊断位置。</summary>
        [Fact]
        public void FailedCompilationsAreNotCached()
        {
            PevtTestHost host = Host();
            const string bad = "id \"T\"\nenable cs\n$raw cs'''not csharp;'''\nend\n";

            Diagnostic first = host.CompileFrontend(bad, "a.pevt").Diagnostics.First(d => d.Id == "PEVT8007");
            Diagnostic second = host.CompileFrontend(bad, "b.pevt").Diagnostics.First(d => d.Id == "PEVT8007");

            Assert.Equal("a.pevt", first.Location.FilePath);
            Assert.Equal("b.pevt", second.Location.FilePath);
        }

        /// <summary>没人用 <c>$raw cs</c> 时不该把整套 C# 编译器拉进进程。</summary>
        [Fact]
        public void TheCompilerIsOnlyCreatedOnFirstUse()
        {
            var lazy = new PevtLazyRawCsCompiler(() => new CountingCompiler());
            var executor = new PevtRawCsExecutor(lazy);

            Assert.False(lazy.IsCreated);

            executor.GetOrCompile(new PevtRawCsRequest("return 1;"));

            Assert.True(lazy.IsCreated);
        }

        /// <summary>宿主没接通道时不能冒充 PEVTR4101——那会把"没接线"说成"原版拒绝"。</summary>
        [Fact]
        public void RawCommandWithoutAChannelIsAHostWiringError()
        {
            PevtTestHost host = Host();
            var bare = new PevtServices(host.Clock, new PevtEventSession("T"));
            var execution = new PevtExecution(host.Compile("id \"T\"\n$raw cmd'''MSG a'''\nend\n"), bare, host.Commands);

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR9001", result.Diagnostic.Id);
        }

        // ==== $raw cs ====

        [Fact]
        public void RawCsExpressionReturnsAValueSnapshot()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nenable cs\nvar n : int = $raw cs'''return 41 + 1;'''\nend\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(42, Slot(execution, "n").AsInt);
        }

        [Theory]
        [InlineData("int", "return 1;", "1")]
        [InlineData("float", "return 1.5f;", "1.5")]
        [InlineData("bool", "return true;", "true")]
        [InlineData("char", "return 'x';", "x")]
        [InlineData("string", "return \"hi\";", "hi")]
        public void AllFiveOrdinaryTypesCanBeReturned(string type, string code, string expected)
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start($"id \"T\"\nenable cs\nvar v : {type} = $raw cs'''{code}'''\nend\n");

            host.RunToCompletion(execution);

            Assert.Equal(expected, Slot(execution, "v").ToString());
        }

        /// <summary>12.2 节：传入的是值副本，C# 侧改动不反写 PEVT 变量。</summary>
        [Fact]
        public void ArgumentsArePassedByValueAndNeverWrittenBack()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\n" +
                "var count : int = 5\n" +
                "var doubled : int = $raw cs (count)'''\ncount += 1;\nreturn count * 2;\n'''\n" +
                "end\n");

            host.RunToCompletion(execution);

            Assert.Equal(5, Slot(execution, "count").AsInt);
            Assert.Equal(12, Slot(execution, "doubled").AsInt);
        }

        [Fact]
        public void StatementFormDiscardsTheValueAndDoesNotNeedEveryPathToReturn()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\n" +
                "var n : int = 1\n" +
                "$raw cs (n)'''\nif (n > 0) { return n; }\n'''\n" +
                "end\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
        }

        /// <summary>编译缓存：同一段代码 + 同一组参数只编译一次。</summary>
        [Fact]
        public void CompilationsAreCachedAcrossRepeatedExecutions()
        {
            PevtTestHost host = Host();
            int before = host.RawCs.CompileCount;

            // 循环体里用赋值而不是声明：同一个声明在同一个环境里只能执行一次（PEVTR3001）。
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\n" +
                "var acc : int = 0\n" +
                "var i : int = 0\n" +
                "while i < 3\n" +
                "acc = $raw cs'''return 7;'''\n" +
                "i = i + 1\n" +
                "endwhile\n" +
                "end\n");
            host.RunToCompletion(execution);

            Assert.Equal(7, Slot(execution, "acc").AsInt);

            // 一次加载期分析 + 一次运行期查询共用同一个缓存条目。
            Assert.Equal(before + 1, host.RawCs.CompileCount);
            Assert.True(host.RawCs.CacheCount >= 1);
        }

        /// <summary>参数类型不同必须是两个缓存条目，不能错命中。</summary>
        [Fact]
        public void CacheKeyIncludesParameterNamesAndTypes()
        {
            var compiler = new CountingCompiler();
            var executor = new PevtRawCsExecutor(compiler);

            var a = new PevtRawCsRequest("return 1;", new[] { new PevtRawCsParameter("x", PevtType.Int) });
            var b = new PevtRawCsRequest("return 1;", new[] { new PevtRawCsParameter("x", PevtType.Float) });
            var c = new PevtRawCsRequest("return 1;", new[] { new PevtRawCsParameter("y", PevtType.Int) });

            executor.GetOrCompile(a);
            executor.GetOrCompile(a);
            executor.GetOrCompile(b);
            executor.GetOrCompile(c);

            Assert.Equal(3, compiler.Calls);
            Assert.Equal(3, executor.CacheCount);
        }

        [Fact]
        public void CacheKeyIncludesTheReferenceSetAndLanguageVersion()
        {
            var one = new PevtRoslynRawCsCompiler(new[] { typeof(object).Assembly });
            var two = new PevtRoslynRawCsCompiler(new[] { typeof(object).Assembly, typeof(Enumerable).Assembly });

            Assert.NotEqual(one.CacheScope, two.CacheScope);
            Assert.Contains(PevtRoslynRawCsCompiler.DefaultLanguageVersion.ToString(), one.CacheScope);
        }

        // ---- 静态诊断 ----

        private static IReadOnlyList<Diagnostic> Diagnose(string source) => Host().CompileFrontend(source).Diagnostics;

        private static void AssertReports(string source, string id) =>
            Assert.Contains(Diagnose(source), d => d.Id == id);

        [Fact]
        public void Pevt8007_ContentIsNotValidCSharp()
        {
            AssertReports("id \"T\"\nenable cs\n$raw cs'''this is not c#'''\nend\n", "PEVT8007");
        }

        [Fact]
        public void Pevt8008_ReturnTypeOutsideTheFiveOrdinaryTypes()
        {
            AssertReports("id \"T\"\nenable cs\nvar v : int = $raw cs'''return new object();'''\nend\n", "PEVT8008");
        }

        [Fact]
        public void Pevt8009_TwoValueReturnsWithDifferentPevtTypes()
        {
            AssertReports(
                "id \"T\"\nenable cs\nvar v : int = $raw cs'''\nif (1 > 0) { return 1; }\nreturn \"x\";\n'''\nend\n",
                "PEVT8009");
        }

        /// <summary>
        /// 条件必须是运行时值：<c>if (1 &gt; 0)</c> 是编译期常量，C# 会认定末尾不可达而不报 CS0161，
        /// 那就测不到 PEVT8010 了。
        /// </summary>
        [Fact]
        public void Pevt8010_ExpressionFormWithAReachableExitThatReturnsNothing()
        {
            AssertReports(
                "id \"T\"\nenable cs\nvar n : int = 1\nvar v : int = $raw cs (n)'''\nif (n > 0) { return 1; }\n'''\nend\n",
                "PEVT8010");
        }

        [Fact]
        public void Pevt8006_ExpressionFormWithNoValueReturnAtAll()
        {
            AssertReports("id \"T\"\nenable cs\nvar v : int = $raw cs'''var a = 1;'''\nend\n", "PEVT8006");
        }

        [Fact]
        public void Pevt8015_RawCsWithoutEnableCs()
        {
            AssertReports("id \"T\"\n$raw cs'''var a = 1;'''\nend\n", "PEVT8015");
        }

        [Fact]
        public void DiagnosticsAreMappedBackIntoTheRawBlock()
        {
            Diagnostic diagnostic = Diagnose("id \"T\"\nenable cs\n$raw cs'''\nnot csharp;\n'''\nend\n")
                .First(d => d.Id == "PEVT8007");

            Assert.NotNull(diagnostic.Location);

            // 原始文本块从第 3 行的 `'''` 之后开始，所以位置必须落在第 4 行（1 基）。
            Assert.Equal(4, diagnostic.Location.StartLine);
        }

        [Fact]
        public void ValidRawCsProducesNoStaticDiagnostics()
        {
            Assert.DoesNotContain(
                Diagnose("id \"T\"\nenable cs\nvar v : int = $raw cs'''return 1;'''\nend\n"),
                d => d.Severity == DiagnosticSeverity.Error);
        }

        // ---- 运行失败契约 ----

        [Fact]
        public void Pevtr4102_WhenTheCSharpBlockThrows()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\n$raw cs'''throw new InvalidOperationException(\"boom\");'''\nend\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4102", result.Diagnostic.Id);
            Assert.Contains("boom", result.Diagnostic.Describe());
        }

        [Fact]
        public void Pevtr3003_WhenTheBlockReturnsANullString()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\nvar s : string = $raw cs'''return (string)null;'''\nend\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal("PEVTR3003", result.Diagnostic.Id);
        }

        /// <summary>
        /// 能力检查不依赖 C# 分析器：即使宿主完全没接分析器，缺 <c>enable cs</c> 仍然是加载期 PEVT8015，程序根本到不了运行时。
        /// 运行时那道 <c>HasCsCapability</c> 闸门因此是纯防御，这条断言把"防御不是唯一防线"钉住。
        /// </summary>
        [Fact]
        public void TheCsCapabilityGateDoesNotDependOnTheAnalyzer()
        {
            PevtCompilation withoutAnalyzer = PevtSourceCompiler.Compile(
                SourceText.FromUtf8(
                    new System.Text.UTF8Encoding(false).GetBytes("id \"T\"\n$raw cs'''var a = 1;'''\nend\n"),
                    "t.pevt").Text,
                CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            Assert.False(withoutAnalyzer.Success);
            Assert.Contains(withoutAnalyzer.Diagnostics, d => d.Id == "PEVT8015");
        }

        /// <summary>宿主没接执行器时报 PEVTR9001，而不是把它当成 C# 抛异常（PEVTR4102）。</summary>
        [Fact]
        public void RawCsWithoutAnExecutorIsAHostWiringError()
        {
            PevtTestHost host = Host();
            var bare = new PevtServices(host.Clock, new PevtEventSession("T"));
            var execution = new PevtExecution(
                host.Compile("id \"T\"\nenable cs\n$raw cs'''var a = 1;'''\nend\n"), bare, host.Commands);

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR9001", result.Diagnostic.Id);
        }

        /// <summary><c>exec</c> 片段继承宿主的 <c>enable cs</c>，不能自己扩大能力。</summary>
        [Fact]
        public void ExecFragmentsInheritTheCsCapability()
        {
            PevtTestHost enabled = Host();
            PevtExecution ok = enabled.Start(
                "id \"T\"\nenable cs\nexec(\"$raw cs'''var a = 1;'''\")\nend\n");
            Assert.Equal(PevtExecutionStatus.Completed, enabled.RunToCompletion(ok).Status);

            PevtTestHost disabled = Host();
            PevtExecution rejected = disabled.Start("id \"T\"\nexec(\"$raw cs'''var a = 1;'''\")\nend\n");
            PevtExecutionResult result = disabled.RunToCompletion(rejected);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR1201", result.Diagnostic.Id);
        }

        /// <summary>
        /// 片段里的 <c>$raw cs</c> 也要过 C# 分析：不合法的 C# 是片段校验失败（PEVTR1201），
        /// 而不是等到执行点才以 PEVTR4102 冒出来。
        /// </summary>
        [Fact]
        public void InvalidCSharpInsideAnExecFragmentFailsFragmentValidation()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\nenable cs\nexec(\"$raw cs'''not csharp;'''\")\nend\n");

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR1201", result.Diagnostic.Id);
            Assert.Contains("PEVT8007", result.Diagnostic.Describe());
        }

        // ---- 辅助 ----

        private static void Tick(PevtWait wait, long frame) => wait.Tick(new PevtWaitContext(frame));

        private static void Tick(PevtWait first, PevtWait second, long frame)
        {
            var context = new PevtWaitContext(frame);
            first.Tick(context);
            second.Tick(context);
        }

        /// <summary>只数编译次数的编译器替身，用来断言缓存键。</summary>
        private sealed class CountingCompiler : IPevtRawCsCompiler
        {
            public int Calls { get; private set; }

            public string CacheScope => "counting";

            public PevtRawCsCompilation Compile(PevtRawCsRequest request)
            {
                Calls++;
                return new PevtRawCsCompilation(PevtType.Int, _ => 1);
            }
        }
    }
}
