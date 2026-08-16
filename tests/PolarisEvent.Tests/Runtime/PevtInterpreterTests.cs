using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 便携同步解释器：值语义、环境隔离、全部流程语句、表达式从左到右求值、运行诊断映射。
    /// 全程使用内存替身，不引用任何游戏程序集。
    /// </summary>
    public class PevtInterpreterTests
    {
        private static PevtTestHost Host() => new PevtTestHost();

        private static PevtExecution Run(PevtTestHost host, string body, out PevtExecutionResult result)
        {
            PevtExecution execution = host.Start("id \"T\"\n" + body);
            result = host.RunToCompletion(execution);

            // 走这条路径的测试都期望正常结束；失败时直接把诊断亮出来，而不是留下"没有槽位 x"。
            Assert.True(result.Status == PevtExecutionStatus.Completed,
                result.Diagnostic != null ? result.Diagnostic.Describe() : result.ToString());

            return execution;
        }

        private static PevtValue Read(PevtExecution execution, string name)
        {
            Assert.True(execution.RootEnvironment.TryGetSlot(name, out PevtSlot slot), $"没有槽位 `{name}`。");
            return slot.Value;
        }

        private static void AssertFault(string body, string expectedId)
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n" + body);
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal(expectedId, result.Diagnostic.Id);
        }

        // ---- 值与环境 ----

        [Fact]
        public void LiteralsAndDeclarations_ProduceTypedSlots()
        {
            PevtExecution execution = Run(Host(),
                "var i : int = 42\nvar f : float = 1.5\nvar b : bool = true\nvar c : char = 'x'\nvar s : string = \"hi\"\nend\n",
                out PevtExecutionResult result);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(42, Read(execution, "i").AsInt);
            Assert.Equal(1.5f, Read(execution, "f").AsFloat);
            Assert.True(Read(execution, "b").AsBool);
            Assert.Equal('x', Read(execution, "c").AsChar);
            Assert.Equal("hi", Read(execution, "s").AsString);
        }

        [Fact]
        public void UninitializedSlot_KeepsItsTypeButCannotBeRead()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nvar n : int\nn = 7\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal(7, Read(execution, "n").AsInt);
        }

        [Fact]
        public void UninitializedRead_IsAlreadyBlockedStatically()
        {
            // PEVT6003 在加载期就挡住了"确定未赋值就读取"，所以本阶段的源码走不到 PEVTR3002。
            // 解释器仍然保留这道运行期防线（见下一个测试），因为功能阶段 E 的异步异常、
            // 集合等待失败和 exec 会真正把未初始化的值送到读取点。
            PevtTestHost host = Host();
            var exception = Assert.Throws<System.InvalidOperationException>(
                () => host.Start("id \"T\"\nvar a : int\nvar b : int = a\nend\n"));

            Assert.Contains("PEVT6003", exception.Message);
        }

        [Fact]
        public void HandlersAreStoredSeparatelyFromOrdinaryValues()
        {
            // 句柄是运行时专用包装，不进入普通类型系统：同名的普通槽位与句柄槽位互不覆盖。
            var environment = new PevtEnvironment("T");
            environment.Declare("x", PevtType.Int, PevtSlotKind.Variable);
            environment.SetHandler("x", new PevtHandlerValue(7, 1, PevtType.Bool));

            Assert.True(environment.TryGetSlot("x", out PevtSlot slot));
            Assert.Equal(PevtType.Int, slot.DeclaredType);

            Assert.True(environment.TryGetHandler("x", out PevtHandlerValue handler));
            Assert.Equal(7, handler.RoutineId);
            Assert.Equal(PevtType.Bool, handler.ExpectedResultType);

            Assert.Equal(new[] { "x" }, environment.SlotNames);
            Assert.Equal(new[] { "x" }, environment.HandlerNames);
        }

        [Fact]
        public void PevtValueIsAReadOnlyValueTypeSoSnapshotsCannotAlias()
        {
            PevtValue a = PevtValue.FromInt(1);
            PevtValue b = a;

            Assert.True(typeof(PevtValue).IsValueType);
            Assert.Equal(a, b);
            Assert.NotEqual(a, PevtValue.FromInt(2));

            // 类型不同就不相等，即使底层表示可能相同。
            Assert.NotEqual(PevtValue.FromInt(1), PevtValue.FromFloat(1f));
            Assert.False(PevtValue.None.HasValue);
        }

        [Fact]
        public void PevtValueAccessorsRejectTheWrongType()
        {
            PevtValue value = PevtValue.FromInt(1);

            Assert.Throws<System.InvalidOperationException>(() => value.AsBool);
            Assert.Throws<System.InvalidOperationException>(() => value.AsString);
            Assert.Throws<System.ArgumentNullException>(() => PevtValue.FromString(null));
        }

        [Fact]
        public void SlotsAreOnlyWritableThroughTheInterpreter()
        {
            // 槽位写入是 internal 的：没有任何公开入口能绕过解释器改变量的值。
            Assert.Null(typeof(PevtSlot).GetMethod("Set", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
            Assert.Null(typeof(PevtSlot).GetProperty("Value").SetMethod);
        }

        [Fact]
        public void UninitializedSlot_RefusesToBeReadAtTheRuntimeLayer()
        {
            var environment = new PevtEnvironment("T");
            PevtSlot slot = environment.Declare("a", PevtType.Int, PevtSlotKind.Variable);

            Assert.False(slot.IsInitialized);
            Assert.Equal(PevtType.Int, slot.DeclaredType); // 未初始化变量仍然具有声明类型
            Assert.Throws<System.InvalidOperationException>(() => slot.Value);
        }

        [Fact]
        public void AssignmentTakesASnapshot_NotALiveBinding()
        {
            // 9.3 节的原样例：快照之后改动来源变量不影响已保存的值。
            PevtExecution execution = Run(Host(),
                "var source : int = 1\nconst snapshot : int = source\nsource = 2\nend\n",
                out _);

            Assert.Equal(2, Read(execution, "source").AsInt);
            Assert.Equal(1, Read(execution, "snapshot").AsInt);
        }

        [Fact]
        public void Pevtr3001_ExecutingTheSameDeclarationTwiceFails() =>
            AssertFault("var i : int = 0\n#top\ni = i + 1\nvar inner : int = i\nif i < 2\ngoto #top\nendif\nend\n", "PEVTR3001");

        [Fact]
        public void BlockEnvironmentsAreIsolatedFromTheEventAndFromEachOther()
        {
            const string source = @"id ""T""
block _bump(n : int) : int
var local : int = n + 1
return local
endblock
var outer : int = 10
var first : int = _bump(1)
var second : int = _bump(5)
end
";
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(source);
            host.RunToCompletion(execution);

            Assert.Equal(2, Read(execution, "first").AsInt);
            Assert.Equal(6, Read(execution, "second").AsInt);
            Assert.Equal(10, Read(execution, "outer").AsInt);

            // 块内的局部名不会泄漏到外层环境。
            Assert.False(execution.RootEnvironment.TryGetSlot("local", out _));
            Assert.False(execution.RootEnvironment.TryGetSlot("n", out _));
        }

        [Fact]
        public void EachBlockCallGetsAFreshEnvironment_SoDeclarationsDoNotCollide()
        {
            // 同一个声明在两次调用中各执行一次，不构成 PEVTR3001。
            const string source = @"id ""T""
block _make() : int
var value : int = 1
return value
endblock
var a : int = _make()
var b : int = _make()
end
";
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(source);
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(1, Read(execution, "a").AsInt);
            Assert.Equal(1, Read(execution, "b").AsInt);
        }

        // ---- 表达式 ----

        [Theory]
        [InlineData("1 + 2", 3)]
        [InlineData("7 - 3", 4)]
        [InlineData("3 * 4", 12)]
        [InlineData("7 / 2", 3)]
        [InlineData("7 % 2", 1)]
        [InlineData("-5", -5)]
        [InlineData("-2147483648", -2147483648)]
        [InlineData("2147483647", 2147483647)]
        public void IntegerArithmetic(string expression, int expected)
        {
            PevtExecution execution = Run(Host(), $"var r : int = {expression}\nend\n", out _);
            Assert.Equal(expected, Read(execution, "r").AsInt);
        }

        [Fact]
        public void ChainedExpressionsEvaluateStrictlyLeftToRight()
        {
            // 8.8 节：a + b * c 等价于 (a + b) * c，不套用 C# 优先级。
            PevtExecution execution = Run(Host(), "var r : int = 1 + 2 * 3\nend\n", out _);
            Assert.Equal(9, Read(execution, "r").AsInt);

            PevtExecution parenthesized = Run(Host(), "var r : int = 1 + (2 * 3)\nend\n", out _);
            Assert.Equal(7, Read(parenthesized, "r").AsInt);
        }

        [Fact]
        public void UnaryMinusAfterABinaryOperatorIsNegation()
        {
            PevtExecution execution = Run(Host(), "var a : int = 5\nvar b : int = 3\nvar r : int = a - -b\nend\n", out _);
            Assert.Equal(8, Read(execution, "r").AsInt);
        }

        [Fact]
        public void ExplicitConversionsProduceTheTargetType()
        {
            PevtExecution execution = Run(Host(),
                "var i : int = 3\nvar f : float = (float)i\nvar c : char = 'q'\nvar s : string = (string)c\nend\n", out _);

            Assert.Equal(3f, Read(execution, "f").AsFloat);
            Assert.Equal("q", Read(execution, "s").AsString);
            Assert.Equal(3, Read(execution, "i").AsInt); // 转换不改变原变量
        }

        [Theory]
        [InlineData("2147483647 + 1")]
        [InlineData("-2147483648 - 1")]
        [InlineData("2147483647 * 2")]
        public void Pevtr2001_IntegerOverflowIsChecked(string expression) =>
            AssertFault($"var r : int = {expression}\nend\n", "PEVTR2001");

        [Fact]
        public void Pevtr2001_NegatingIntMinValueOverflows() =>
            AssertFault("var a : int = -2147483648\nvar r : int = -a\nend\n", "PEVTR2001");

        [Theory]
        [InlineData("var z : int = 0\nvar r : int = 1 / z\nend\n")]
        [InlineData("var z : int = 0\nvar r : int = 1 % z\nend\n")]
        [InlineData("var z : float = 0.0\nvar r : float = 1.0 / z\nend\n")]
        public void Pevtr2002_DivisionByZero(string body) => AssertFault(body, "PEVTR2002");

        [Fact]
        public void Pevtr2003_NonFiniteFloatResult() =>
            // PEVT 的浮点字面量不接受指数形式，所以用连乘把结果推到 float 范围之外。
            AssertFault("var a : float = 1000000.0\nvar r : float = a * a * a * a * a * a * a\nend\n", "PEVTR2003");

        [Fact]
        public void FloatUnderflowToZeroIsNotAnError()
        {
            PevtExecution execution = Run(Host(),
                "var a : float = 0.0000001\nvar r : float = a * a * a * a * a * a * a\nend\n", out PevtExecutionResult result);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(0f, Read(execution, "r").AsFloat);
        }

        [Theory]
        [InlineData("1 == 1", true)]
        [InlineData("1 != 1", false)]
        [InlineData("1 < 2", true)]
        [InlineData("2 <= 2", true)]
        [InlineData("3 > 4", false)]
        [InlineData("4 >= 4", true)]
        public void Comparisons(string expression, bool expected)
        {
            PevtExecution execution = Run(Host(), $"var r : bool = {expression}\nend\n", out _);
            Assert.Equal(expected, Read(execution, "r").AsBool);
        }

        [Theory]
        [InlineData("true & false", false)]
        [InlineData("true | false", true)]
        [InlineData("true ^ true", false)]
        [InlineData("!false", true)]
        public void LogicalOperators(string expression, bool expected)
        {
            PevtExecution execution = Run(Host(), $"var r : bool = {expression}\nend\n", out _);
            Assert.Equal(expected, Read(execution, "r").AsBool);
        }

        [Fact]
        public void StringAndCharEqualityUsesOrdinalComparison()
        {
            PevtExecution execution = Run(Host(),
                "var a : bool = \"abc\" == \"abc\"\nvar b : bool = \"abc\" == \"ABC\"\nvar c : bool = 'x' == 'x'\nend\n", out _);

            Assert.True(Read(execution, "a").AsBool);
            Assert.False(Read(execution, "b").AsBool);
            Assert.True(Read(execution, "c").AsBool);
        }

        // ---- 流程 ----

        [Fact]
        public void IfElifElseSelectsExactlyOneBranch()
        {
            const string body = @"var n : int = 2
var r : string = """"
if n == 1
r = ""one""
elif n == 2
r = ""two""
elif n == 3
r = ""three""
else
r = ""other""
endif
end
";
            PevtExecution execution = Run(Host(), body, out _);
            Assert.Equal("two", Read(execution, "r").AsString);
        }

        [Fact]
        public void ElseRunsWhenNoBranchMatches()
        {
            PevtExecution execution = Run(Host(),
                "var n : int = 9\nvar r : string = \"\"\nif n == 1\nr = \"one\"\nelse\nr = \"other\"\nendif\nend\n", out _);
            Assert.Equal("other", Read(execution, "r").AsString);
        }

        [Fact]
        public void WhileLoopsUntilTheConditionIsFalse()
        {
            PevtExecution execution = Run(Host(),
                "var i : int = 0\nvar sum : int = 0\nwhile i < 5\nsum = sum + i\ni = i + 1\nendwhile\nend\n", out _);

            Assert.Equal(10, Read(execution, "sum").AsInt);
            Assert.Equal(5, Read(execution, "i").AsInt);
        }

        [Fact]
        public void WhileBodyMayRunZeroTimes()
        {
            PevtExecution execution = Run(Host(),
                "var i : int = 10\nvar count : int = 0\nwhile i < 5\ncount = count + 1\nendwhile\nend\n", out _);
            Assert.Equal(0, Read(execution, "count").AsInt);
        }

        [Fact]
        public void SwitchEvaluatesItsValueExactlyOnce()
        {
            // 计数器藏在一个 @ 处理器里：switch 值求值几次，处理器就被调用几次。
            int calls = 0;
            PevtTestHost host = Host();
            host.Command("counter_get", new[] { PevtType.String, PevtType.String }, (context, args) =>
            {
                calls++;
                context.Result.SetInt(2);
                return Empty();
            });

            const string body = @"var r : string = """"
switch @counter_get(""s"", ""k"")
case 1
r = ""one""
case 2
r = ""two""
case 3
r = ""three""
default
r = ""other""
endswitch
end
";
            PevtExecution execution = host.Start("id \"T\"\n" + body);
            host.RunToCompletion(execution);

            Assert.Equal("two", Read(execution, "r").AsString);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void SwitchFallsToDefaultWhenNoCaseMatches()
        {
            PevtExecution execution = Run(Host(),
                "var n : int = 9\nvar r : string = \"\"\nswitch n\ncase 1\nr = \"one\"\ndefault\nr = \"other\"\nendswitch\nend\n", out _);
            Assert.Equal("other", Read(execution, "r").AsString);
        }

        [Fact]
        public void SwitchWithoutAMatchAndWithoutDefaultSimplyFallsThrough()
        {
            PevtExecution execution = Run(Host(),
                "var n : int = 9\nvar r : string = \"none\"\nswitch n\ncase 1\nr = \"one\"\nendswitch\nend\n", out PevtExecutionResult result);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal("none", Read(execution, "r").AsString);
        }

        [Fact]
        public void SwitchGotoJumpsToTheMatchingCase()
        {
            const string body = @"var n : int = 1
var r : string = """"
switch n
case 1
r = ""one""
goto 3
case 2
r = ""two""
case 3
r = ""three""
endswitch
end
";
            PevtExecution execution = Run(Host(), body, out _);

            // case 1 先跑，goto 3 之后跳到 case 3，最终留下 three；case 2 从未执行。
            Assert.Equal("three", Read(execution, "r").AsString);
        }

        [Fact]
        public void LabelGotoJumpsBackwards()
        {
            PevtExecution execution = Run(Host(),
                "var i : int = 0\n#top\ni = i + 1\nif i < 3\ngoto #top\nendif\nend\n", out _);
            Assert.Equal(3, Read(execution, "i").AsInt);
        }

        [Fact]
        public void EndStopsExecutionImmediately()
        {
            PevtExecution execution = Run(Host(),
                "var r : int = 1\nif true\nend\nendif\nr = 2\nend\n", out PevtExecutionResult result);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(1, Read(execution, "r").AsInt);
        }

        [Fact]
        public void BlocksReturnValuesAndVoidBlocksRunForTheirEffects()
        {
            const string source = @"id ""T""
block _double(n : int) : int
var r : int = n * 2
return r
endblock
block _noop()
return
endblock
var v : int = _double(21)
_noop()
end
";
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(source);
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(42, Read(execution, "v").AsInt);
        }

        /// <summary>
        /// 生成 <paramref name="depth"/> 层互相调用的事件块。PEVT 静态禁止事件块递归（PEVT7115），
        /// 所以深帧只能靠一串各不相同的块，而块必须先定义后调用，因此最深的一层写在最前面。
        /// </summary>
        private static string NestedBlockChain(int depth)
        {
            var builder = new System.Text.StringBuilder("id \"T\"\n");
            builder.Append("block _b0(n : int) : int\nreturn n\nendblock\n");
            for (int i = 1; i < depth; i++)
                builder.Append($"block _b{i}(n : int) : int\nvar r : int = _b{i - 1}(n)\nreturn r\nendblock\n");

            builder.Append($"var v : int = _b{depth - 1}(7)\nend\n");
            return builder.ToString();
        }

        [Fact]
        public void BlockCallsUseExplicitFramesInsteadOfCSharpRecursion()
        {
            // 200 层嵌套：靠 C# 递归实现会直接吃掉宿主调用栈，靠显式帧栈只是多压 200 个帧。
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(maxCallDepth: 512) };
            PevtExecution execution = host.Start(NestedBlockChain(200));
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(7, Read(execution, "v").AsInt);
        }

        [Fact]
        public void Pevtr1003_CallDepthLimitIsEnforced()
        {
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(maxCallDepth: 8) };
            PevtExecution execution = host.Start(NestedBlockChain(40));
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR1003", result.Diagnostic.Id);
        }

        [Fact]
        public void CallDepthBoundary_ExactlyAtTheLimitStillRuns()
        {
            // 8 层块 + 1 层事件帧 = 9 个显式帧，因此上限 9 通过、8 失败。
            var atLimit = new PevtTestHost { Limits = new PevtBudgetLimits(maxCallDepth: 9) };
            Assert.Equal(PevtExecutionStatus.Completed,
                atLimit.RunToCompletion(atLimit.Start(NestedBlockChain(8))).Status);

            var overLimit = new PevtTestHost { Limits = new PevtBudgetLimits(maxCallDepth: 8) };
            Assert.Equal("PEVTR1003",
                overLimit.RunToCompletion(overLimit.Start(NestedBlockChain(8))).Diagnostic.Id);
        }

        // ---- 预算 ----

        [Fact]
        public void Pevtr1001_TotalStepBudgetStopsAnInfiniteLoop()
        {
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(stepsPerFrame: 1000, totalSteps: 5000) };
            PevtExecution execution = host.Start("id \"T\"\nvar i : int = 0\nwhile true\ni = i + 1\nendwhile\nend\n");
            PevtExecutionResult result = host.RunToCompletion(execution, maxFrames: 4096);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR1001", result.Diagnostic.Id);
        }

        [Fact]
        public void FrameStepBudgetYieldsWithoutFailing()
        {
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(stepsPerFrame: 8, totalSteps: 100_000) };
            PevtExecution execution = host.Start("id \"T\"\nvar i : int = 0\nwhile i < 50\ni = i + 1\nendwhile\nend\n");

            PevtExecutionResult first = host.Step(execution);
            Assert.Equal(PevtExecutionStatus.Suspended, first.Status);
            Assert.Equal(PevtSuspendReason.FrameBudget, first.SuspendReason);

            PevtExecutionResult final = host.RunToCompletion(execution, maxFrames: 512);
            Assert.Equal(PevtExecutionStatus.Completed, final.Status);
            Assert.Equal(50, Read(execution, "i").AsInt);
        }

        [Fact]
        public void BudgetBoundary_OneStepUnderTheLimitStillCompletes()
        {
            var host = new PevtTestHost { Limits = new PevtBudgetLimits(stepsPerFrame: 100_000, totalSteps: 100_000) };
            PevtExecution execution = host.Start("id \"T\"\nvar i : int = 0\nwhile i < 10\ni = i + 1\nendwhile\nend\n");
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);

            long used = execution.Budget.TotalSteps;
            var tight = new PevtTestHost { Limits = new PevtBudgetLimits(stepsPerFrame: 100_000, totalSteps: (int)used) };
            Assert.Equal(PevtExecutionStatus.Completed,
                tight.RunToCompletion(tight.Start("id \"T\"\nvar i : int = 0\nwhile i < 10\ni = i + 1\nendwhile\nend\n")).Status);

            var tooTight = new PevtTestHost { Limits = new PevtBudgetLimits(stepsPerFrame: 100_000, totalSteps: (int)used - 1) };
            PevtExecutionResult overrun = tooTight.RunToCompletion(
                tooTight.Start("id \"T\"\nvar i : int = 0\nwhile i < 10\ni = i + 1\nendwhile\nend\n"));
            Assert.Equal("PEVTR1001", overrun.Diagnostic.Id);
        }

        // ---- 只读跟踪 ----

        [Fact]
        public void CallStackReachesFromTheFailureBackToTheEvent()
        {
            const string source = @"id ""Story""
block _boom(n : int) : int
var r : int = n / 0
return r
endblock
var v : int = _boom(1)
end
";
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(source);
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal("PEVTR2002", result.Diagnostic.Id);
            Assert.NotNull(result.Diagnostic.Location);
            Assert.Equal(3, result.Diagnostic.Location.StartLine);

            List<PevtCallFrame> stack = result.Diagnostic.CallStack.ToList();
            Assert.Equal(PevtCallFrameKind.Block, stack[0].Kind);
            Assert.Equal("_boom", stack[0].Name);
            Assert.Equal(PevtCallFrameKind.Event, stack[1].Kind);
            Assert.Equal("Story", stack[1].Name);
            Assert.Contains("test.pevt", result.Diagnostic.Describe());
        }

        [Fact]
        public void ExecutionExposesReadOnlyFrameAndBudgetState()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nvar i : int = 1\nend\n");
            host.RunToCompletion(execution);

            Assert.Equal("T", execution.EventId);
            Assert.True(execution.Budget.TotalSteps > 0);
            Assert.Single(execution.Frames);
            Assert.Equal(PevtFrameKind.Event, execution.Frames[0].Kind);
        }

        // ---- 本阶段范围 ----

        [Theory]
        [InlineData("id \"T\"\nenable async\nhandler h = @actor_move_start(\"aic:noel\", \"left\", 3)\nend\n", "handler")]
        [InlineData("id \"T\"\ncallevt \"Other\"\nend\n", "callevt")]
        [InlineData("id \"T\"\nenable cs\n$raw cs'''return 1;'''\nend\n", "$raw cs")]
        public void ConstructsDeferredToLaterStagesAreRejectedAtCompileTime(string source, string expectedFragment)
        {
            PevtCompileResult result = Host().TryCompile(source);

            Assert.False(result.Success);
            Assert.Contains(result.UnsupportedFeatures, feature => feature.Contains(expectedFragment));
        }

        private static IEnumerator<PevtWait> Empty()
        {
            yield break;
        }
    }
}
