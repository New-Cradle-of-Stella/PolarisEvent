using System;
using System.Linq;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Loading
{
    /// <summary>
    /// 唯一前端编译入口。计划要求"任何公开构建入口必须强制执行它声称已经完成的阶段"，因此这里逐条
    /// 验证四道静态门（词法 / 语法 / 绑定 / 控制流）都真的跑过，而不是靠调用方记得依次调用。
    /// </summary>
    public class PevtSourceCompilerTests
    {
        private static SourceText Text(string source) =>
            SourceText.FromUtf8(new UTF8Encoding(false).GetBytes(source), "test.pevt").Text;

        private static PevtCompilation Compile(string source, BuiltinApiTable api = null) =>
            PevtSourceCompiler.Compile(Text(source), api);

        [Fact]
        public void CleanSource_ProducesAnImmutableDefinition()
        {
            PevtCompilation compilation = Compile("id \"Demo\"\nenable async\nvar n : int = 1\nend\n");

            Assert.True(compilation.Success);
            Assert.Empty(compilation.Diagnostics);

            PevtProgramDefinition definition = compilation.Definition;
            Assert.Equal("Demo", definition.EventId);
            Assert.True(definition.HasAsyncCapability);
            Assert.False(definition.HasCsCapability);
            Assert.Equal("n", Assert.Single(definition.TopLevelSymbols).Name);
            Assert.Equal(64, definition.SourceHash.Length);
        }

        [Theory]
        [InlineData("id \"A\"\nvar n : int = 1 ~ 2\nend\n", "PEVT1")] // 词法门
        [InlineData("id \"A\"\nif\nend\n", "PEVT2")] // 语法门
        [InlineData("id \"A\"\nvar n : int = \"text\"\nend\n", "PEVT")] // 绑定门
        [InlineData("id \"A\"\n", "PEVT4")] // 控制流门：没有 end
        public void EveryStaticGateRuns_WithoutTheCallerWiringThemUp(string source, string expectedIdPrefix)
        {
            PevtCompilation compilation = Compile(source);

            Assert.False(compilation.Success);
            Assert.Null(compilation.Definition);
            Assert.Contains(compilation.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith(expectedIdPrefix, StringComparison.Ordinal));
        }

        [Fact]
        public void UnregisteredBuiltinCall_IsRejectedByTheBindingGate()
        {
            // 空 API 表下 @say 未登记；换成登记过的表就通过，证明绑定门确实在这条路径里执行。
            Assert.False(Compile("id \"A\"\n@say(\"hi\")\nend\n").Success);

            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("say", false, new[] { new BuiltinParameter("text", PevtType.String) }, null));
            Assert.True(Compile("id \"A\"\n@say(\"hi\")\nend\n", table).Success);
        }

        [Fact]
        public void Warnings_DoNotBlockTheDefinition()
        {
            PevtCompilation compilation = Compile("id \"A\"\nif true\nendif\nend\n");

            Assert.True(compilation.Success);
            Assert.Contains(compilation.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
            Assert.DoesNotContain(compilation.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void SharedDiagnosticBagOverload_AppendsInsteadOfReplacing()
        {
            var bag = new DiagnosticBag();
            bag.AddWarning("PEVT2301", "调用方自己先放进去的一条。", null);

            PevtCompilation compilation = PevtSourceCompiler.Compile(Text("id \"A\"\nend\n"), bag);

            Assert.True(compilation.Success);
            Assert.Single(compilation.Diagnostics);
            Assert.Equal("PEVT2301", compilation.Diagnostics[0].Id);
        }

        [Fact]
        public void ByteOverload_ReportsEncodingFailureWithoutRunningTheLexer()
        {
            byte[] invalid = { 0x69, 0x64, 0xC0, 0x80 };

            PevtCompilation compilation = PevtSourceCompiler.Compile(invalid, "bad.pevt");

            Assert.False(compilation.Success);
            Assert.Null(compilation.Source);
            Assert.Null(compilation.Document);
            Assert.Equal("PEVT1009", Assert.Single(compilation.Diagnostics).Id);
        }

        [Fact]
        public void SameSourceCompilesDeterministically()
        {
            const string source = "id \"Demo\"\nvar n : int = 1\nend\n";

            PevtCompilation first = Compile(source);
            PevtCompilation second = Compile(source);

            Assert.Equal(first.Definition.SourceHash, second.Definition.SourceHash);
            Assert.Equal(
                first.Diagnostics.Select(d => d.Id + d.Message),
                second.Diagnostics.Select(d => d.Id + d.Message));
        }

        [Fact]
        public void NullArguments_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => PevtSourceCompiler.Compile((SourceText)null));
            Assert.Throws<ArgumentNullException>(() => PevtSourceCompiler.Compile((byte[])null, "a.pevt"));
            Assert.Throws<ArgumentNullException>(() => PevtSourceCompiler.Compile(Text("id \"A\"\nend\n"), (DiagnosticBag)null));
        }
    }
}
