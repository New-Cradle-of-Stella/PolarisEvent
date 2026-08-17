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
    ///
    /// 断言直接读两个目标输出的元数据，而不是把它们加载进测试宿主：<c>netstandard2.1</c> 那份
    /// 引用了 Assembly-CSharp、unsafeAssem 与 UnityEngine，在没装游戏的测试环境里根本加载不起来。
    /// 读元数据既能证明"前端干净"，也能证明"游戏侧确实接上了适配器"，而这正是本阶段最容易
    /// 悄悄退化的两件事——多一个 <c>using</c> 就会让 PolarisTools 编译不过。
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
        /// 旧执行链回归：除了功能阶段 F 的 <c>$raw cmd</c> 专用桥，游戏侧不得出现任何
        /// 原版 CMD 文本入口的痕迹。这里检查的是最终产物而不是源码，源码级扫描由构建脚本负责。
        /// </summary>
        [Fact]
        public void GameTarget_HasNoVanillaCommandTextEntryPoint()
        {
            AssemblyFacts gameTarget = Read("netstandard2.1");

            Assert.DoesNotContain("Polaris.Event.Game.PevtRawCmdBridge", gameTarget.TypeNames);
            Assert.DoesNotContain("EvReader", gameTarget.TypeReferences);
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
