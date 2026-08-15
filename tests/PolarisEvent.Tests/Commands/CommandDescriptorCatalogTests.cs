using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Commands
{
    /// <summary>
    /// 唯一权威 <c>@</c> API 目录。和诊断目录一样，快照就是规范文档本身——直接解析
    /// PEVT-内置事件语句表.md 的 API 表逐条核对，而不是在测试里再抄一份，否则两份副本谁改谁忘改
    /// 都发现不了。
    /// </summary>
    public class CommandDescriptorCatalogTests
    {
        private static readonly Regex ApiRow = new Regex(
            @"^\|\s*`@([A-Za-z_][A-Za-z0-9_]*)`\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|",
            RegexOptions.Compiled);

        private sealed class SpecRow
        {
            public string Name;
            public CommandWaitKind WaitKind;
            public PevtType? ReturnType;
            public List<(string Name, PevtType Type)> Parameters = new List<(string, PevtType)>();

            public string OverloadKey =>
                Name + "/" + Parameters.Count + string.Concat(Parameters.Select(p => ":" + p.Type.DisplayName()));
        }

        private static string FindRepoFile(string fileName)
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "doc", "design", fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"在祖先目录中找不到 {fileName}。");
        }

        private static PevtType ParseType(string text)
        {
            switch (text.Trim())
            {
                case "int": return PevtType.Int;
                case "float": return PevtType.Float;
                case "bool": return PevtType.Bool;
                case "char": return PevtType.Char;
                case "string": return PevtType.String;
                default: throw new FormatException($"规范中出现未知类型 `{text}`。");
            }
        }

        private static CommandWaitKind ParseWaitKind(string text)
        {
            switch (text.Trim())
            {
                case "立即": return CommandWaitKind.Immediate;
                case "查询": return CommandWaitKind.Query;
                case "同步等待":
                case "等待": return CommandWaitKind.Wait;
                case "等待／可并行": return CommandWaitKind.WaitParallel;
                default: throw new FormatException($"规范中出现未知执行方式 `{text}`。");
            }
        }

        private static IReadOnlyList<SpecRow> LoadSpecRows()
        {
            var rows = new List<SpecRow>();

            foreach (string line in File.ReadAllLines(FindRepoFile("PEVT-内置事件语句表.md")))
            {
                Match match = ApiRow.Match(line.TrimEnd());
                if (!match.Success)
                    continue;

                var row = new SpecRow
                {
                    Name = match.Groups[1].Value,
                    WaitKind = ParseWaitKind(match.Groups[2].Value),
                };

                string returnText = match.Groups[3].Value.Trim();
                if (returnText != "无")
                    row.ReturnType = ParseType(returnText.Trim('`'));

                string parameterText = match.Groups[4].Value.Trim();
                if (parameterText != "无")
                {
                    foreach (string part in parameterText.Trim('`').Split(','))
                    {
                        string[] halves = part.Split(':');
                        Assert.Equal(2, halves.Length);
                        row.Parameters.Add((halves[0].Trim(), ParseType(halves[1])));
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        [Fact]
        public void SpecDocument_ParsesIntoAReasonableNumberOfRows()
        {
            IReadOnlyList<SpecRow> rows = LoadSpecRows();

            Assert.True(rows.Count > 100, $"只解析到 {rows.Count} 条 API，正则可能与文档表格格式不再匹配。");
            Assert.DoesNotContain(rows, row => row.Name.EndsWith(CommandDescriptor.StartSuffix, StringComparison.Ordinal));
        }

        [Fact]
        public void Catalog_MatchesTheSpecDocumentExactly()
        {
            IReadOnlyList<SpecRow> specRows = LoadSpecRows();
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            Assert.Equal(specRows.Count, catalog.DeclaredDescriptors.Count);

            Dictionary<string, CommandDescriptor> byOverload =
                catalog.DeclaredDescriptors.ToDictionary(d => d.OverloadKey, StringComparer.Ordinal);

            foreach (SpecRow spec in specRows)
            {
                Assert.True(byOverload.TryGetValue(spec.OverloadKey, out CommandDescriptor descriptor),
                    $"目录里缺少规范中的 `@{spec.Name}`（重载键 {spec.OverloadKey}）。");

                Assert.Equal(spec.WaitKind, descriptor.WaitKind);
                Assert.Equal(spec.ReturnType, descriptor.ReturnType);
                Assert.Equal(
                    spec.Parameters.Select(p => p.Name),
                    descriptor.Parameters.Select(p => p.Name));
            }
        }

        [Fact]
        public void EveryCapabilityIdentifier_ExistsInTheCapabilitySpec()
        {
            string capabilitySpec = File.ReadAllText(FindRepoFile("PEVT-内置能力规范.md"));

            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.DeclaredDescriptors)
            {
                Assert.False(string.IsNullOrEmpty(descriptor.Capability), $"`@{descriptor.Name}` 没有登记能力标识。");
                Assert.True(capabilitySpec.Contains("`" + descriptor.Capability + "`"),
                    $"`@{descriptor.Name}` 的能力标识 `{descriptor.Capability}` 不在能力规范中。");
            }
        }

        // ---- 重载唯一性 ----

        [Fact]
        public void Overloads_AreUniquelyDeterminedByArityAndFullTypeList()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Descriptors)
                Assert.True(keys.Add(descriptor.OverloadKey), $"重载键重复：{descriptor.OverloadKey}");
        }

        [Fact]
        public void ChooseOverloads_ResolveByArgumentCount()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            Assert.Equal(3, catalog.Find("choose").Count);

            Assert.True(catalog.TryResolve("choose", Enumerable.Repeat(PevtType.String, 3).ToList(), out CommandDescriptor three));
            Assert.True(catalog.TryResolve("choose", Enumerable.Repeat(PevtType.String, 5).ToList(), out CommandDescriptor five));
            Assert.Equal(3, three.Parameters.Count);
            Assert.Equal(5, five.Parameters.Count);
            Assert.False(catalog.TryResolve("choose", Enumerable.Repeat(PevtType.String, 6).ToList(), out _));
            Assert.False(catalog.TryResolve("choose", new[] { PevtType.Int, PevtType.String, PevtType.String }, out _));
        }

        [Fact]
        public void AmbiguousOverload_FailsAtCatalogConstruction()
        {
            var duplicate = new[]
            {
                new CommandDescriptor("dup", new[] { new CommandParameter("a", PevtType.Int) }, null, CommandWaitKind.Immediate, CommandPriority.P0, "x"),
                new CommandDescriptor("dup", new[] { new CommandParameter("b", PevtType.Int) }, null, CommandWaitKind.Immediate, CommandPriority.P0, "x"),
            };

            ArgumentException error = Assert.Throws<ArgumentException>(() => new CommandDescriptorCatalog(duplicate));
            Assert.Contains("唯一确定", error.Message);
        }

        [Fact]
        public void SameNameDifferentArity_IsAccepted()
        {
            var catalog = new CommandDescriptorCatalog(new[]
            {
                new CommandDescriptor("ok", new[] { new CommandParameter("a", PevtType.Int) }, null, CommandWaitKind.Immediate, CommandPriority.P0, "x"),
                new CommandDescriptor("ok", new[] { new CommandParameter("a", PevtType.Int), new CommandParameter("b", PevtType.Int) }, null, CommandWaitKind.Immediate, CommandPriority.P0, "x"),
            });

            Assert.Equal(2, catalog.Find("ok").Count);
        }

        // ---- 并行能力与 _start 派生 ----

        [Fact]
        public void EveryParallelApi_HasADerivedStartVariantWithTheSameContract()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;
            List<CommandDescriptor> parallel = catalog.DeclaredDescriptors.Where(d => d.CanRunInParallel).ToList();

            Assert.NotEmpty(parallel);

            foreach (CommandDescriptor descriptor in parallel)
            {
                CommandDescriptor start = Assert.Single(catalog.Find(descriptor.StartName));

                Assert.True(start.IsAsync);
                Assert.Same(descriptor, start.ParallelSource);
                Assert.Equal(descriptor.ReturnType, start.ReturnType);
                Assert.Equal(descriptor.Priority, start.Priority);
                Assert.Equal(descriptor.Capability, start.Capability);

                // 不复制描述数据：形参列表就是同一批不可变实例。
                Assert.Equal(descriptor.Parameters.Count, start.Parameters.Count);
                for (int i = 0; i < descriptor.Parameters.Count; i++)
                    Assert.Same(descriptor.Parameters[i], start.Parameters[i]);
            }
        }

        [Fact]
        public void NonParallelApis_HaveNoStartVariant()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            foreach (CommandDescriptor descriptor in catalog.DeclaredDescriptors.Where(d => !d.CanRunInParallel))
            {
                Assert.Null(descriptor.StartName);
                Assert.Throws<InvalidOperationException>(() => descriptor.CreateStartVariant());
                Assert.Empty(catalog.Find(descriptor.Name + CommandDescriptor.StartSuffix));
            }
        }

        [Theory]
        [InlineData("say")]
        [InlineData("choose")]
        [InlineData("choice_show")]
        [InlineData("map_change")]
        [InlineData("autosave")]
        public void GlobalChannelApis_DoNotOfferAParallelVariant(string name)
        {
            // 对话、选择、地图切换和存档占用全局交互或场景切换通道，并行语义不稳定。
            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Find(name))
                Assert.False(descriptor.CanRunInParallel);

            Assert.Empty(CommandDescriptorCatalog.Builtin.Find(name + CommandDescriptor.StartSuffix));
        }

        [Fact]
        public void StartVariants_CannotBeRegisteredByHand()
        {
            Assert.Throws<ArgumentException>(() => new CommandDescriptor(
                "actor_move_start", Array.Empty<CommandParameter>(), null, CommandWaitKind.Wait, CommandPriority.P0, "x"));
        }

        // ---- 投影到绑定器 ----

        [Fact]
        public void ToBuiltinApiTable_IsTheOnlyBridgeAndCarriesDomains()
        {
            BuiltinApiTable table = CommandDescriptorCatalog.Builtin.ToBuiltinApiTable();

            BuiltinSignature say = Assert.Single(table.Find("say"));
            Assert.False(say.IsAsync);
            Assert.True(say.HasValidSignatureShape());
            Assert.Same(ParameterDomain.ActorId, say.Parameters[0].Domain);
            Assert.Null(say.Parameters[1].Domain);

            BuiltinSignature start = Assert.Single(table.Find("actor_move_start"));
            Assert.True(start.IsAsync);
            Assert.Same(ParameterDomain.ActorAnchor, start.Parameters[1].Domain);
        }

        [Fact]
        public void ToBuiltinApiTable_ReturnsAFreshTableEachTime()
        {
            BuiltinApiTable first = CommandDescriptorCatalog.Builtin.ToBuiltinApiTable();
            first.Register(new BuiltinSignature("injected", false, Array.Empty<BuiltinParameter>(), null));

            Assert.Empty(CommandDescriptorCatalog.Builtin.ToBuiltinApiTable().Find("injected"));
        }

        [Fact]
        public void EveryDescriptorHasAnOrdinaryTypeSignatureShape()
        {
            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Descriptors)
                Assert.True(descriptor.ToBuiltinSignature().HasValidSignatureShape(), descriptor.ToString());
        }

        // ---- 参数域 ----

        [Fact]
        public void ActorParametersCarryTheActorIdDomain()
        {
            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Descriptors)
            {
                foreach (CommandParameter parameter in descriptor.Parameters)
                {
                    if (parameter.Name == "actorId")
                        Assert.Same(ParameterDomain.ActorId, parameter.Domain);
                    if (parameter.Name == "appearanceId")
                        Assert.Same(ParameterDomain.ActorAppearance, parameter.Domain);
                    if (parameter.Name == "easing")
                        Assert.Same(ParameterDomain.Easing, parameter.Domain);
                    if (parameter.Name == "position")
                        Assert.Same(ParameterDomain.ActorAnchor, parameter.Domain);
                }
            }
        }

        [Fact]
        public void EasingDomain_HasTheClosedValueSetFixedByTheSpec()
        {
            Assert.Equal(
                new[] { "linear", "ease_in", "ease_out", "ease_in_out" },
                ParameterDomain.Easing.ClosedValues);

            // 封闭集也不产生静态错误：越界值是运行时参数错误。
            Assert.False(ParameterDomain.Easing.RejectsUnknownValues);
            Assert.Null(ParameterDomain.ActorId.ClosedValues);
        }

        // ---- 契约完整性 ----

        [Fact]
        public void QueryApis_AlwaysReturnAValue()
        {
            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Descriptors.Where(d => d.WaitKind == CommandWaitKind.Query))
                Assert.NotNull(descriptor.ReturnType);

            Assert.Throws<ArgumentException>(() => new CommandDescriptor(
                "bad", Array.Empty<CommandParameter>(), null, CommandWaitKind.Query, CommandPriority.P0, "x"));
        }

        [Fact]
        public void DescriptorRejectsDuplicateParameterNamesAndNonOrdinaryTypes()
        {
            Assert.Throws<ArgumentException>(() => new CommandDescriptor(
                "bad",
                new[] { new CommandParameter("a", PevtType.Int), new CommandParameter("a", PevtType.String) },
                null, CommandWaitKind.Immediate, CommandPriority.P0, "x"));

            Assert.Throws<ArgumentException>(() => new CommandParameter("h", PevtType.Handler));
            Assert.Throws<ArgumentException>(() => new CommandParameter("v", PevtType.Void));
            Assert.Throws<ArgumentException>(() => new CommandParameter("a", PevtType.Int, ParameterDomain.ActorId));
        }

        [Fact]
        public void PrioritiesCoverAllThreeTiers()
        {
            List<CommandDescriptor> declared = CommandDescriptorCatalog.Builtin.DeclaredDescriptors.ToList();

            Assert.Contains(declared, d => d.Priority == CommandPriority.P0);
            Assert.Contains(declared, d => d.Priority == CommandPriority.P1);
            Assert.Contains(declared, d => d.Priority == CommandPriority.P2);
            Assert.Equal(CommandPriority.P2, declared.Single(d => d.Name == "player_outfit").Priority);
            Assert.Equal(CommandPriority.P1, declared.Single(d => d.Name == "map_change").Priority);
        }

        [Fact]
        public void CatalogCollections_AreImmutable()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            Assert.Throws<NotSupportedException>(() => ((IList<CommandDescriptor>)catalog.Descriptors).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<CommandParameter>)catalog.Descriptors[0].Parameters).Clear());
        }
    }
}
