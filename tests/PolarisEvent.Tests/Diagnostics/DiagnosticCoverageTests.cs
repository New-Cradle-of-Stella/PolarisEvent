using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Diagnostics
{
    /// <summary>
    /// 补上那些"已经登记也已经有发射路径、但没有任何测试直接断言"的编号。
    ///
    /// 计划把「登记、发射、断言」列为三件独立的事，缺任何一件都不算交付；这个文件专门盯住第三件，
    /// 每条都用一个会真正触发它的最小源码来断言。
    /// </summary>
    public class DiagnosticCoverageTests
    {
        private static IReadOnlyList<Diagnostic> Compile(string source)
        {
            SourceText text = SourceText.FromUtf8(new UTF8Encoding(false).GetBytes(source), "coverage.pevt").Text;
            return PevtSourceCompiler.Compile(text, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable()).Diagnostics;
        }

        private static void AssertReports(string source, string expectedId) =>
            Assert.Contains(Compile(source), d => d.Id == expectedId);

        private static void AssertDoesNotReport(string source, string unexpectedId) =>
            Assert.DoesNotContain(Compile(source), d => d.Id == unexpectedId);

        // ---- 结束符后的多余参数 ----

        [Fact]
        public void Pevt2010_EndIfWithArguments()
        {
            AssertReports("id \"T\"\nif true\nendif 1\nend\n", "PEVT2010");
            AssertDoesNotReport("id \"T\"\nif true\nendif\nend\n", "PEVT2010");
        }

        [Fact]
        public void Pevt2104_EndWhileWithArguments()
        {
            AssertReports("id \"T\"\nwhile true\nendwhile 1\nend\n", "PEVT2104");
            AssertDoesNotReport("id \"T\"\nwhile true\nendwhile\nend\n", "PEVT2104");
        }

        [Fact]
        public void Pevt2414_EndSwitchWithArguments()
        {
            AssertReports("id \"T\"\nvar n : int = 1\nswitch n\ncase 1\nendswitch 1\nend\n", "PEVT2414");
            AssertDoesNotReport("id \"T\"\nvar n : int = 1\nswitch n\ncase 1\nendswitch\nend\n", "PEVT2414");
        }

        // ---- case 表达式的副作用 ----

        [Theory]
        [InlineData("@counter_get(\"s\", \"k\")")]
        [InlineData("_helper()")]
        [InlineData("(@counter_get(\"s\", \"k\"))")]
        [InlineData("1 + @counter_get(\"s\", \"k\")")]
        [InlineData("-@counter_get(\"s\", \"k\")")]
        public void Pevt2415_SideEffectingCaseExpression(string caseExpression)
        {
            string source = $@"id ""T""
block _helper() : int
var r : int = 1
return r
endblock
var n : int = 1
switch n
case {caseExpression}
default
endswitch
end
";
            AssertReports(source, "PEVT2415");
        }

        [Fact]
        public void Pevt2415_PlainCaseExpressionsAreFine()
        {
            AssertDoesNotReport(
                "id \"T\"\nvar n : int = 1\nconst k : int = 2\nswitch n\ncase 1\ncase k\ncase 1 + 2\ndefault\nendswitch\nend\n",
                "PEVT2415");
        }

        // ---- 声明形状 ----

        [Fact]
        public void Pevt6005_MissingTypeAnnotation()
        {
            AssertReports("id \"T\"\nvar n = 1\nend\n", "PEVT6005");
            AssertDoesNotReport("id \"T\"\nvar n : int = 1\nend\n", "PEVT6005");
        }

        [Fact]
        public void Pevt6011_MissingInitializerExpression()
        {
            AssertReports("id \"T\"\nvar n : int =\nend\n", "PEVT6011");
            AssertDoesNotReport("id \"T\"\nvar n : int\nend\n", "PEVT6011");
        }

        // ---- 异步调用的静态形状 ----

        [Fact]
        public void Pevt7202_IsUnreachableBecauseTheLexerMergesTheTwoKeywords()
        {
            // `async` 与 `block` 之间没有分隔符时，词法器把它们合成一个标识符 `asyncblock`，
            // 于是报的是 PEVT1201 而不是 PEVT7202。解析器里那道判断因此是防御性的死分支，
            // 这里把这个事实钉住：真出现分隔符缺失时用户看到的是哪一条。
            AssertReports("id \"T\"\nenable async\nasyncblock _work()\nendblock\nend\n", "PEVT1201");
            AssertDoesNotReport("id \"T\"\nenable async\nasync block _work()\nendblock\nend\n", "PEVT7202");
        }

        [Fact]
        public void Pevt7218_AggregateAwaitHandlerListMustBeIdentifiers()
        {
            AssertReports("id \"T\"\nenable async\nhandler a = @actor_move_start(\"x\", \"left\", 1)\nvar c : int = await all(a, 2)(p, q)\nend\n", "PEVT7218");
        }

        [Fact]
        public void Pevt7222_AggregateAwaitBindingListMustBeIdentifiers()
        {
            AssertReports("id \"T\"\nenable async\nhandler a = @actor_move_start(\"x\", \"left\", 1)\nvar c : int = await all(a)(1)\nend\n", "PEVT7222");
        }

        // ---- 覆盖率闸门 ----

        /// <summary>
        /// 明确记录哪些编号目前还没有生产发射路径，以及各自的唯一负责阶段。
        /// 清单之外再出现新的未发射编号时本测试会失败，避免"登记了就当交付"。
        /// </summary>
        [Fact]
        public void UnemittedStaticDiagnosticsAreExactlyTheOnesDeferredToLaterStages()
        {
            var expected = new HashSet<string>
            {
                // 通用规则，实现时被更具体编号取代（见静态诊断表同名小节）。
                "PEVT5006",

                // 功能阶段 E：异步调用的语义检查、await all/any 的参数与返回变量规则、exec 参数类型。
                "PEVT7203", "PEVT7219", "PEVT7221", "PEVT7223", "PEVT7224", "PEVT7225", "PEVT7403",

                // 功能阶段 F：`$raw cs` 的 C# 分析（计划 17 -> 39 交界）。
                "PEVT8007", "PEVT8008", "PEVT8009", "PEVT8010",
            };

            IReadOnlyCollection<string> actual = DiagnosticSourceScan.WithoutProductionEmitter(
                DiagnosticCatalog.All.Select(entry => entry.Id));

            Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal), actual.OrderBy(id => id, StringComparer.Ordinal));
        }

        /// <summary>运行诊断同理：未发射的编号必须全部属于尚未开工的功能阶段。</summary>
        [Fact]
        public void UnemittedRuntimeDiagnosticsAreExactlyTheOnesDeferredToLaterStages()
        {
            var expected = new HashSet<string>
            {
                // 功能阶段 E：exec、事件间调用与异步句柄。
                // PEVTR4301/4304 已经由公开宿主发射；4302/4303 要等 callevt 才有意义。
                "PEVTR1201", "PEVTR1202", "PEVTR1203",
                "PEVTR4302", "PEVTR4303",
                "PEVTR5001", "PEVTR5002", "PEVTR5003", "PEVTR5004", "PEVTR5005",

                // 功能阶段 D 的游戏侧适配器：视觉契约要等真实 PXLS pose/frame 才能核对。
                "PEVTR4405",

                // 功能阶段 F：原始桥与 C# 回调。
                "PEVTR4101", "PEVTR4102", "PEVTR4201",
            };

            IReadOnlyCollection<string> actual = DiagnosticSourceScan.WithoutProductionEmitter(
                RuntimeDiagnosticCatalog.All.Select(entry => entry.Id));

            Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal), actual.OrderBy(id => id, StringComparer.Ordinal));
        }
    }
}
