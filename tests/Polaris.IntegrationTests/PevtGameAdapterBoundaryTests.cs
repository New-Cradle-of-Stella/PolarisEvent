using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Polaris.Pevt.IntegrationTests
{
    /// <summary>
    /// 功能阶段 D 的程序集边界证明。
    /// </summary>
    public class PevtGameAdapterBoundaryTests
    {
        /// <summary>游戏侧必须实现的 14 个原子服务适配器加宿主入口。</summary>
        private static readonly string[] GameAdapters =
        {
            "PevtGameRuntime",
            "PevtGameHost",
            "PevtGameClock",
            "PevtGameActorCatalog",
            "PevtGameResources",
            "PevtGameDialogue",
            "PevtGameChoice",
            "PevtGamePortrait",
            "PevtGameImage",
            "PevtGameScreen",
            "PevtGameCamera",
            "PevtGameEffect",
            "PevtGameAudio",
            "PevtGameMusic",
            "PevtGameUi",
            "PevtGameInput",

            // 功能阶段 F：持久状态适配器与 `$raw cmd` 专用桥。
            "PevtGameState",
            "RawCmd.PevtGameRawCommandBridge",
            "RawCmd.PevtGameRawCommandSession",
        };

        private static readonly string[] GameAssemblies =
        {
            "Assembly-CSharp",
            "unsafeAssem",
        };

        [Fact]
        public void Frontend_HasNoGameAdapters()
        {
            AssemblyFacts frontend = Read("netstandard2.0");

            foreach (string adapter in GameAdapters)
                Assert.DoesNotContain("Polaris.Event.Game." + adapter, frontend.TypeNames);
        }

        [Fact]
        public void Frontend_DoesNotReferenceGameOrUnity()
        {
            AssemblyFacts frontend = Read("netstandard2.0");

            foreach (string reference in frontend.References)
            {
                Assert.False(
                    reference.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || reference.StartsWith("BepInEx", StringComparison.Ordinal)
                    || GameAssemblies.Contains(reference),
                    $"netstandard2.0 前端不能引用 `{reference}`：PolarisTools 要在没装游戏的机器上编译它。");
            }
        }

        [Fact]
        public void GameTarget_ContainsEveryAdapter()
        {
            AssemblyFacts gameTarget = Read("netstandard2.1");

            foreach (string adapter in GameAdapters)
                Assert.Contains("Polaris.Event.Game." + adapter, gameTarget.TypeNames);
        }

        [Fact]
        public void GameTarget_ReferencesVanillaEventAssemblies()
        {
            AssemblyFacts gameTarget = Read("netstandard2.1");

            foreach (string assembly in GameAssemblies)
                Assert.Contains(assembly, gameTarget.References);
        }

        /// <summary>
        /// 前端产物里不可能出现原版事件文本入口——它连游戏程序集都不引用。
        /// </summary>
        [Fact]
        public void Frontend_HasNoVanillaCommandTextEntryPoint()
        {
            AssemblyFacts frontend = Read("netstandard2.0");

            Assert.DoesNotContain("EvReader", frontend.TypeReferences);
            Assert.DoesNotContain("EV", frontend.TypeReferences);
        }

        /// <summary>
        /// 旧执行链回归的精确白名单。
        /// </summary>
        [Theory]
        [InlineData("EvReader")]
        [InlineData("EV.stack")]
        [InlineData("EV.readOneLine")]
        [InlineData("EV.setEventContent")]
        [InlineData("EV.getEventContent")]
        [InlineData("EV.unstackReader")]
        [InlineData("EV.getStacked")]
        public void VanillaEventTextEntryPointsOnlyAppearInTheRawCmdBridge(string token)
        {
            var offenders = new List<string>();

            foreach (string file in EnumerateProductionSources())
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Game/RawCmd/"))
                    continue;

                if (StripComments(File.ReadAllText(file)).Contains(token))
                    offenders.Add(normalized.Substring(normalized.IndexOf("/PolarisEvent/", StringComparison.Ordinal) + 1));
            }

            Assert.True(offenders.Count == 0,
                $"`{token}` 只允许出现在 Game/RawCmd，实际还出现在：{string.Join(", ", offenders)}");
        }

        /// <summary>旧默认执行链的痕迹必须为零，没有白名单。</summary>
        [Theory]
        [InlineData("commandText")]
        [InlineData(".phxx")]
        [InlineData("Polaris.Event.Compiler")]
        [InlineData("HppCompiler")]
        [InlineData("EventsDir")]
        [InlineData("Patch_EV_getEventContent")]
        public void TheOldExecutionChainLeavesNoTrace(string token)
        {
            foreach (string file in EnumerateProductionSources())
                Assert.False(StripComments(File.ReadAllText(file)).Contains(token), $"`{token}` 仍然出现在 {file}。");
        }

        /// <summary>
        /// 去掉注释再搜。仓库里大量文档注释正是在解释"这些入口为什么不许用"，把它们算成命中，
        /// 这道闸门就只会逼人删注释。
        /// </summary>
        private static string StripComments(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                        i++;
                    continue;
                }

                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                        i++;
                    i = Math.Min(i + 2, text.Length);
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            return builder.ToString();
        }

        private static IEnumerable<string> EnumerateProductionSources()
        {
            string root = RepositoryRoot();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/") || normalized.Contains("/tests/"))
                    continue;

                yield return file;
            }
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(PevtGameAdapterBoundaryTests).Assembly.Location));
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PolarisEvent.csproj")))
                directory = directory.Parent;

            Assert.NotNull(directory);
            return directory.FullName;
        }

        // ---- 元数据读取 ----

        private sealed class AssemblyFacts
        {
            public HashSet<string> TypeNames { get; } = new HashSet<string>(StringComparer.Ordinal);

            public HashSet<string> TypeReferences { get; } = new HashSet<string>(StringComparer.Ordinal);

            public HashSet<string> References { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private static AssemblyFacts Read(string targetFramework)
        {
            string path = LocateOutput(targetFramework);
            var facts = new AssemblyFacts();

            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream))
            {
                MetadataReader reader = peReader.GetMetadataReader();

                foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                {
                    TypeDefinition definition = reader.GetTypeDefinition(handle);
                    string ns = reader.GetString(definition.Namespace);
                    string name = reader.GetString(definition.Name);
                    facts.TypeNames.Add(string.IsNullOrEmpty(ns) ? name : ns + "." + name);
                }

                foreach (TypeReferenceHandle handle in reader.TypeReferences)
                    facts.TypeReferences.Add(reader.GetString(reader.GetTypeReference(handle).Name));

                foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
                    facts.References.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
            }

            return facts;
        }

        /// <summary>
        /// 从测试程序集的位置回溯到仓库根，再取对应目标的输出。
        /// 不硬编码 Debug/Release，跟着当前测试用的配置走。
        /// </summary>
        private static string LocateOutput(string targetFramework)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(PevtGameAdapterBoundaryTests).Assembly.Location));

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PolarisEvent.csproj")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            string configuration = Path.GetFileName(
                Path.GetDirectoryName(Path.GetDirectoryName(typeof(PevtGameAdapterBoundaryTests).Assembly.Location)));

            string path = Path.Combine(directory.FullName, "bin", configuration, targetFramework, "PolarisEvent.dll");
            Assert.True(File.Exists(path), $"找不到 {targetFramework} 目标的输出：{path}");
            return path;
        }
    }
}
