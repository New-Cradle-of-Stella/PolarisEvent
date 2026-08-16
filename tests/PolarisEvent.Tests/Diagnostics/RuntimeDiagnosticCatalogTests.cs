using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Polaris.Pevt.Diagnostics;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Diagnostics
{
    /// <summary>
    /// 把 RuntimeDiagnosticCatalog 与 PEVT-运行诊断表.md 逐项核对，做法与静态诊断目录一致：
    /// 快照就是文档本身，不在测试里再抄一份。
    /// </summary>
    public class RuntimeDiagnosticCatalogTests
    {
        private static readonly Regex RowPattern = new Regex(
            @"^\|\s*`(PEVTR\d{4})`\s*\|\s*`([A-Za-z]+)`\s*\|\s*Runtime (Error|Warning)\s*\|\s*(.+?)\s*\|$",
            RegexOptions.Compiled);

        private sealed class SpecRow
        {
            public string Id;
            public string Name;
            public DiagnosticSeverity Severity;
            public string Message;
        }

        private static IReadOnlyList<SpecRow> LoadSpecRows()
        {
            string path = null;
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "doc", "design", "PEVT-运行诊断表.md");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            Assert.NotNull(path);

            var rows = new List<SpecRow>();
            foreach (string line in File.ReadAllLines(path))
            {
                Match match = RowPattern.Match(line.TrimEnd());
                if (!match.Success)
                    continue;

                rows.Add(new SpecRow
                {
                    Id = match.Groups[1].Value,
                    Name = match.Groups[2].Value,
                    Severity = match.Groups[3].Value == "Error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    Message = match.Groups[4].Value,
                });
            }

            return rows;
        }

        [Fact]
        public void Catalog_MatchesTheSpecDocumentExactly()
        {
            IReadOnlyList<SpecRow> specRows = LoadSpecRows();
            Assert.True(specRows.Count > 25, $"只解析到 {specRows.Count} 条运行诊断，正则可能与文档格式不再匹配。");
            Assert.Equal(specRows.Count, RuntimeDiagnosticCatalog.All.Count);

            Dictionary<string, SpecRow> specById = specRows.ToDictionary(row => row.Id);
            foreach (DiagnosticDescriptor entry in RuntimeDiagnosticCatalog.All)
            {
                Assert.True(specById.TryGetValue(entry.Id, out SpecRow spec), $"目录中的 {entry.Id} 不存在于规范文档。");
                Assert.Equal(spec.Name, entry.Name);
                Assert.Equal(spec.Severity, entry.Severity);
                Assert.Equal(spec.Message, entry.DefaultMessage);
            }
        }

        [Fact]
        public void Catalog_HasNoDuplicateIdsOrNames()
        {
            var ids = new HashSet<string>();
            var names = new HashSet<string>();

            foreach (DiagnosticDescriptor entry in RuntimeDiagnosticCatalog.All)
            {
                Assert.True(ids.Add(entry.Id), $"编号重复: {entry.Id}");
                Assert.True(names.Add(entry.Name), $"名称重复: {entry.Name}");
            }
        }

        [Fact]
        public void RuntimeAndStaticCatalogsAreIndependentNumberingSpaces()
        {
            // PEVTR 与 PEVT 是两套编号：同一个四位数字在两张表里是两条不同的诊断。
            Assert.Null(RuntimeDiagnosticCatalog.Find("PEVT9001"));
            Assert.Null(DiagnosticCatalog.Find("PEVTR9001"));
            Assert.NotNull(RuntimeDiagnosticCatalog.Find("PEVTR9001"));
            Assert.NotNull(DiagnosticCatalog.Find("PEVT9001"));
        }

        [Fact]
        public void Require_RejectsUnknownIdsSoRuntimeCannotInventNumbers()
        {
            Assert.Throws<ArgumentException>(() => RuntimeDiagnosticCatalog.Require("PEVTR0000"));
            Assert.Equal("ExecutionBudgetExceeded", RuntimeDiagnosticCatalog.Require("PEVTR1001").Name);
        }

        [Fact]
        public void Pevtr5005_IsTheOnlyWarning()
        {
            List<DiagnosticDescriptor> warnings =
                RuntimeDiagnosticCatalog.All.Where(entry => entry.Severity == DiagnosticSeverity.Warning).ToList();

            Assert.Equal("PEVTR5005", Assert.Single(warnings).Id);
        }
    }
}
