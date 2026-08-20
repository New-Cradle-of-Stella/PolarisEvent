using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Routines;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// PEVT-E01：任意已登记游戏值的只读查询。
    /// 这些用例盯住三件事——键名不是白名单、四个返回类型各自的转换规则、以及每一种失败都有专属编号。
    /// </summary>
    public class GameQueryRoutineTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost();
            host.WithBuiltinRoutines();
            return host;
        }

        private static PevtExecutionResult Run(PevtTestHost host, string body, int maxFrames = 64) =>
            host.RunToCompletion(host.Start("id \"T\"\n" + body + "end\n"), maxFrames);

        private static void RunExpectingFault(PevtTestHost host, string body, string expectedId)
        {
            PevtExecutionResult result = Run(host, body);
            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal(expectedId, result.Diagnostic.Id);
        }

        /// <summary>把一次查询的结果存进变量再断言，比在 <c>@say</c> 里拼字符串直观。</summary>
        private static PevtValue ValueOf(PevtTestHost host, string declaration)
        {
            PevtExecution execution = host.Start("id \"T\"\n" + declaration + "\nend\n");
            PevtExecutionResult result = host.RunToCompletion(execution);
            Assert.Equal(PevtExecutionStatus.Completed, result.Status);

            Assert.True(execution.RootEnvironment.TryGetSlot("v", out PevtSlot slot));
            Assert.True(slot.IsInitialized);
            return slot.Value;
        }

        // ---- 登记完整性 ----

        [Fact]
        public void EveryReturnTypeIsRegisteredForEveryArity()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;
            PevtCommandRegistry registry = PevtBuiltinRoutines.CreateRegistry();

            foreach (string name in new[] { "game_read_int", "game_read_float", "game_read_bool", "game_read_string" })
            {
                IReadOnlyList<CommandDescriptor> overloads = catalog.Find(name);
                Assert.Equal(BuiltinCommandDescriptors.MaxQueryArguments + 1, overloads.Count);

                for (int arity = 1; arity <= BuiltinCommandDescriptors.MaxQueryArguments + 1; arity++)
                {
                    Assert.True(
                        catalog.TryResolve(name, Enumerable.Repeat(PevtType.String, arity).ToList(), out CommandDescriptor descriptor),
                        $"`@{name}` 缺少 {arity} 参数重载。");

                    // 查询类必定有返回值，而且不能派生出 `_start` 变体：只读查询没有"并行"语义。
                    Assert.Equal(CommandWaitKind.Query, descriptor.WaitKind);
                    Assert.True(descriptor.ReturnType.HasValue);
                    Assert.Null(descriptor.StartName);
                    Assert.True(registry.TryGetRoutine(descriptor, out _));
                }
            }

            // 键的参数域必须是查询键域，其余参数没有域——它们只是这个键的查询参数。
            Assert.True(catalog.TryResolve("game_read_int", new[] { PevtType.String, PevtType.String }, out CommandDescriptor two));
            Assert.Same(ParameterDomain.GameQueryKey, two.Parameters[0].Domain);
            Assert.Null(two.Parameters[1].Domain);
        }

        [Fact]
        public void QueryNamesAreNotAWhitelist_AnyRegisteredKeyReads()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["SOME_MOD_ADDED_THIS"] = 7d;

            Assert.Equal(7, ValueOf(host, "var v : int = @game_read_int(\"SOME_MOD_ADDED_THIS\")").AsInt);
        }

        // ---- 四种目标类型 ----

        [Fact]
        public void NumberKeyReadsAsIntFloatBoolAndString()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["N"] = 3d;

            Assert.Equal(3, ValueOf(host, "var v : int = @game_read_int(\"N\")").AsInt);
            Assert.Equal(3f, ValueOf(host, "var v : float = @game_read_float(\"N\")").AsFloat);
            Assert.True(ValueOf(host, "var v : bool = @game_read_bool(\"N\")").AsBool);
            Assert.Equal("3", ValueOf(host, "var v : string = @game_read_string(\"N\")").AsString);
        }

        [Fact]
        public void FractionalNumberReadsAsFloatButNotAsInt()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["F"] = 2.5d;

            Assert.Equal(2.5f, ValueOf(host, "var v : float = @game_read_float(\"F\")").AsFloat);
            RunExpectingFault(host, "var v : int = @game_read_int(\"F\")\n", "PEVTR4503");
        }

        [Fact]
        public void ZeroIsFalseAndAnyNonZeroIsTrue()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["OFF"] = 0d;
            host.GameQuery.Numbers["ON"] = 1d;
            host.GameQuery.Numbers["NEGATIVE"] = -1d;

            Assert.False(ValueOf(host, "var v : bool = @game_read_bool(\"OFF\")").AsBool);
            Assert.True(ValueOf(host, "var v : bool = @game_read_bool(\"ON\")").AsBool);
            Assert.True(ValueOf(host, "var v : bool = @game_read_bool(\"NEGATIVE\")").AsBool);
        }

        [Fact]
        public void TextKeyReadsAsStringAndParsesIntoTheOtherTypes()
        {
            PevtTestHost host = Host();
            host.GameQuery.Texts["TITLE"] = "森の記憶";
            host.GameQuery.Texts["COUNT"] = "42";
            host.GameQuery.Texts["FLAG"] = "true";

            Assert.Equal("森の記憶", ValueOf(host, "var v : string = @game_read_string(\"TITLE\")").AsString);
            Assert.Equal(42, ValueOf(host, "var v : int = @game_read_int(\"COUNT\")").AsInt);
            Assert.True(ValueOf(host, "var v : bool = @game_read_bool(\"FLAG\")").AsBool);
        }

        [Fact]
        public void TextResultThatIsNotANumberOrBooleanFailsWithAConversionDiagnostic()
        {
            PevtTestHost host = Host();
            host.GameQuery.Texts["TITLE"] = "森の記憶";

            RunExpectingFault(host, "var v : int = @game_read_int(\"TITLE\")\n", "PEVTR4503");
            RunExpectingFault(host, "var v : float = @game_read_float(\"TITLE\")\n", "PEVTR4503");
            RunExpectingFault(host, "var v : bool = @game_read_bool(\"TITLE\")\n", "PEVTR4503");
        }

        [Fact]
        public void IntConversionRejectsValuesOutsideThe32BitRange()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["HUGE"] = 5e9d;

            RunExpectingFault(host, "var v : int = @game_read_int(\"HUGE\")\n", "PEVTR4503");
        }

        // ---- 查询参数 ----

        [Fact]
        public void ArgumentsReachTheQueryTableVerbatimAndAreNotConcatenated()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["GAR"] = 0.5d;

            Run(host, "var v : float = @game_read_float(\"GAR\", \"1\", \"2 + 3\", \"x\")\n");

            // 三个实参各自作为一个参数到达，"2 + 3" 原样传过去而不是被求成 5，
            // 也没有被拼成 "GAR(1,2 + 3,x)" 这样的一段表达式。
            Assert.Equal(new[] { "TryRead(GAR,1,2 + 3,x)" }, host.GameQuery.Calls);
        }

        [Fact]
        public void WrongArgumentCountForAKeyReportsAnArgumentDiagnostic()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["NEEDS_TWO"] = 1d;
            host.GameQuery.RequiredArgumentCounts["NEEDS_TWO"] = 2;

            RunExpectingFault(host, "var v : int = @game_read_int(\"NEEDS_TWO\", \"a\")\n", "PEVTR4502");

            host = Host();
            host.GameQuery.Numbers["NEEDS_TWO"] = 4d;
            host.GameQuery.RequiredArgumentCounts["NEEDS_TWO"] = 2;
            Assert.Equal(4, ValueOf(host, "var v : int = @game_read_int(\"NEEDS_TWO\", \"a\", \"b\")").AsInt);
        }

        [Fact]
        public void EmptyKeyIsAnArgumentError()
        {
            PevtTestHost host = Host();
            RunExpectingFault(host, "var v : int = @game_read_int(\"\")\n", "PEVTR4502");
        }

        // ---- 失败路径 ----

        [Fact]
        public void UnknownKeyReportsItsOwnDiagnostic()
        {
            PevtTestHost host = Host();
            RunExpectingFault(host, "var v : int = @game_read_int(\"NOPE\")\n", "PEVTR4501");
        }

        [Fact]
        public void AHostWithoutAQueryTableFailsTheSameWayAsAMissingKey()
        {
            var host = new PevtTestHost();
            host.WithBuiltinRoutines();
            host.UseGameQuery(null);

            RunExpectingFault(host, "var v : int = @game_read_int(\"ANY\")\n", "PEVTR4501");
        }

        [Fact]
        public void AQueryTableThatThrowsDoesNotSurfaceAsAnInternalError()
        {
            PevtTestHost host = Host();
            host.GameQuery.Numbers["BOOM"] = 1d;
            host.GameQuery.ThrowingKeys.Add("BOOM");

            // 宿主的异常必须落在查询这一族自己的编号上，而不是 PEVTR9001（解释器状态损坏）。
            RunExpectingFault(host, "var v : int = @game_read_int(\"BOOM\")\n", "PEVTR4501");
        }

        // ---- 只读 ----

        [Fact]
        public void ThereIsNoWriteSideToTheQueryFamily()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            foreach (string name in new[] { "game_write_int", "game_write", "game_set", "game_call", "game_eval" })
                Assert.Empty(catalog.Find(name));

            // 整族的能力标识只有一个，而且是 read。
            foreach (CommandDescriptor descriptor in catalog.DeclaredDescriptors)
            {
                if (descriptor.Name.StartsWith("game_", StringComparison.Ordinal))
                    Assert.Equal("game.query.read", descriptor.Capability);
            }
        }

        // ---- F8 可观察性 ----

        [Fact]
        public void SuccessfulQueryIsRecordedWithKeyArgumentsRawResultAndTargetType()
        {
            PevtGameQueryLog.Shared.Clear();

            PevtTestHost host = Host();
            host.GameQuery.Numbers["DANGER"] = 12d;
            host.Clock.Advance();

            Run(host, "var v : int = @game_read_int(\"DANGER\", \"bonus\")\n");

            PevtGameQueryTrace trace = PevtGameQueryLog.Shared.Last;
            Assert.NotNull(trace);
            Assert.True(trace.IsSuccess);
            Assert.Equal("DANGER", trace.Key);
            Assert.Equal(new[] { "bonus" }, trace.Arguments);
            Assert.Equal(PevtType.Int, trace.TargetType);
            Assert.Equal("T", trace.EventId);
            Assert.True(trace.Value.HasValue);
            Assert.Equal(12d, trace.Value.Value.Number);
            Assert.Equal(PevtQueryValueKind.Number, trace.Value.Value.Kind);
            Assert.Equal("@game_read_int(\"DANGER\", \"bonus\")", trace.Call);
            Assert.Null(trace.DiagnosticId);
        }

        [Fact]
        public void ConversionFailureKeepsBothWhatWasReadAndWhyItCouldNotBeUsed()
        {
            PevtGameQueryLog.Shared.Clear();

            PevtTestHost host = Host();
            host.GameQuery.Numbers["RATIO"] = 0.25d;

            RunExpectingFault(host, "var v : int = @game_read_int(\"RATIO\")\n", "PEVTR4503");

            IReadOnlyList<PevtGameQueryTrace> recent = PevtGameQueryLog.Shared.Recent;
            Assert.Equal(2, recent.Count);

            Assert.True(recent[0].IsSuccess);
            Assert.Equal(0.25d, recent[0].Value.Value.Number);

            Assert.False(recent[1].IsSuccess);
            Assert.Equal("PEVTR4503", recent[1].DiagnosticId);
            Assert.Contains("不是整数值", recent[1].Failure);
            Assert.Equal(0.25d, recent[1].Value.Value.Number);

            Assert.Equal(2, PevtGameQueryLog.Shared.TotalCount);
            Assert.Equal(1, PevtGameQueryLog.Shared.FailureCount);
        }

        [Fact]
        public void UnknownKeyIsRecordedWithoutARawResult()
        {
            PevtGameQueryLog.Shared.Clear();

            PevtTestHost host = Host();
            RunExpectingFault(host, "var v : bool = @game_read_bool(\"MISSING\")\n", "PEVTR4501");

            PevtGameQueryTrace trace = PevtGameQueryLog.Shared.Last;
            Assert.False(trace.IsSuccess);
            Assert.Equal("PEVTR4501", trace.DiagnosticId);
            Assert.Equal(PevtType.Bool, trace.TargetType);
            Assert.False(trace.Value.HasValue);
        }

        [Fact]
        public void TheLogDropsTheOldestEntryInsteadOfGrowing()
        {
            PevtGameQueryLog.Shared.Clear();

            PevtTestHost host = Host();
            host.GameQuery.Numbers["N"] = 1d;

            var body = new System.Text.StringBuilder();
            for (int i = 0; i < PevtGameQueryLog.Capacity + 5; i++)
                body.Append("var v").Append(i).Append(" : int = @game_read_int(\"N\")\n");

            Run(host, body.ToString());

            Assert.Equal(PevtGameQueryLog.Capacity, PevtGameQueryLog.Shared.Recent.Count);
            Assert.Equal(PevtGameQueryLog.Capacity + 5, PevtGameQueryLog.Shared.TotalCount);
        }
    }
}
