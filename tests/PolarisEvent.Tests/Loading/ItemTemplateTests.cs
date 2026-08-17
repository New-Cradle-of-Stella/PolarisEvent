using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Loading
{
    /// <summary>
    /// PolarisTools 的 <c>.pevt</c> / <c>.pactor</c> 项目模板必须自己就能通过共享 Core——模板是作者接触 PEVT 的第一份代码，
    /// 一个语法错误会让人以为语言坏了。模板住在并排的 PolarisTools 仓库里，找不到那个目录时跳过而不是失败。
    /// </summary>
    public class ItemTemplateTests
    {
        /// <summary>模板里的 VS 占位符。替换成合法值之后才谈得上"是不是合法 PEVT"。</summary>
        private static string Substitute(string text) => text
            .Replace("$fileinputname$", "TemplateEvent")
            .Replace("$safeprojectname$", "examplemod");

        public static IEnumerable<object[]> PevtTemplates()
        {
            string? root = ToolsTemplateRoot();
            if (root == null)
                yield break;

            foreach (string file in Directory.EnumerateFiles(root, "*.pevt", SearchOption.AllDirectories))
                yield return new object[] { file };
        }

        public static IEnumerable<object[]> PactorTemplates()
        {
            string? root = ToolsTemplateRoot();
            if (root == null)
                yield break;

            foreach (string file in Directory.EnumerateFiles(root, "*.pactor", SearchOption.AllDirectories))
                yield return new object[] { file };
        }

        [Theory]
        [MemberData(nameof(PevtTemplates))]
        public void EveryPevtTemplateCompilesCleanly(string path)
        {
            string source = Substitute(File.ReadAllText(path));

            SourceTextLoadResult loaded = SourceText.FromUtf8(new UTF8Encoding(false).GetBytes(source), path);
            Assert.True(loaded.Success, "模板不是合法 UTF-8：" + path);

            PevtCompilation compilation = PevtSourceCompiler.Compile(
                loaded.Text, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            Assert.True(compilation.Success, Describe(path, compilation.Diagnostics));
            Assert.DoesNotContain(compilation.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Theory]
        [MemberData(nameof(PactorTemplates))]
        public void EveryPactorTemplateReadsCleanly(string path)
        {
            string xml = Substitute(File.ReadAllText(path));

            ActorCatalogReadResult result = ActorCatalogReader.ReadText(xml, path, ActorCatalogSourceKind.External);

            // 资源引用指向的是模板项目里并不存在的 C# 字段，因此只要求"目录本身读得出来、没有结构错误"。
            IReadOnlyList<Diagnostic> structural = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            Assert.True(result.Success, Describe(path, structural));
            Assert.Empty(structural);
        }

        /// <summary>没有任何模板时说明取模板的路径写错了——那本身就是回归。</summary>
        [Fact]
        public void TemplatesAreDiscoverableWhenPolarisToolsIsClonedSideBySide()
        {
            string? root = ToolsTemplateRoot();
            if (root == null)
                return;

            Assert.NotEmpty(Directory.EnumerateFiles(root, "*.pevt", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.EnumerateFiles(root, "*.pactor", SearchOption.AllDirectories));
        }

        private static string Describe(string path, IEnumerable<Diagnostic> diagnostics) =>
            path + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

        /// <summary>并排 PolarisTools 仓库里的模板目录；没有并排 clone 时为 null。</summary>
        private static string? ToolsTemplateRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, "PolarisEvent.csproj")))
                    continue;

                // dir = .../Polaris/PolarisEvent → 兄弟目录 .../PolarisTools
                DirectoryInfo? polaris = dir.Parent;
                if (polaris?.Parent == null)
                    return null;

                string candidate = Path.Combine(polaris.Parent.FullName, "PolarisTools", "ItemTemplates", "Polaris");
                return Directory.Exists(candidate) ? candidate : null;
            }

            return null;
        }
    }
}
