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
    /// 把 DiagnosticCatalog 与仓库中的权威文档 PEVT-静态诊断表.md 逐项核对。快照就是文档本身——
    /// 不在测试里再抄一份 194 条诊断，那样两份副本谁改谁忘改都发现不了；直接解析文档表格才会真正防漂移。
    /// </summary>
    public class DiagnosticCatalogTests
    {
        private static readonly Regex RowPattern = new Regex(
            @"^\|\s*`(PEVT\d{4})`\s*\|\s*`([A-Za-z]+)`\s*\|\s*(Error|Warning)\s*\|\s*(.+?)\s*\|$",
            RegexOptions.Compiled);

        private sealed class SpecRow
        {
            public string Id;
            public string Name;
            public DiagnosticSeverity Severity;
            public string Message;
        }

        private static string FindRepoFile(string fileName)
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                    return candidate;

                candidate = Path.Combine(dir.FullName, "doc", "design", fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"在祖先目录中找不到 {fileName}，无法核对诊断目录快照。");
        }

        private static IReadOnlyList<SpecRow> LoadSpecRows()
        {
            string path = FindRepoFile("PEVT-静态诊断表.md");
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

        private static void AssertNoDuplicates(IEnumerable<(string Id, string Name)> entries, string source)
        {
            var ids = new HashSet<string>();
            var names = new HashSet<string>();
            foreach ((string id, string name) in entries)
            {
                Assert.True(ids.Add(id), $"{source}中编号重复: {id}");
                Assert.True(names.Add(name), $"{source}中名称重复: {name}");
            }
        }

        [Fact]
        public void SpecDocument_HasNoDuplicateIdsOrNames()
        {
            IReadOnlyList<SpecRow> rows = LoadSpecRows();
            Assert.True(rows.Count > 100, "解析到的诊断条目数量异常偏少，正则可能与文档表格格式不再匹配。");

            AssertNoDuplicates(rows.Select(row => (row.Id, row.Name)), "规范文档");
        }

        [Fact]
        public void Catalog_HasNoDuplicateIdsOrNames() =>
            AssertNoDuplicates(DiagnosticCatalog.All.Select(entry => (entry.Id, entry.Name)), "目录");

        [Fact]
        public void Catalog_MatchesSpecDocumentExactly()
        {
            IReadOnlyList<SpecRow> specRows = LoadSpecRows();
            Dictionary<string, SpecRow> specById = specRows.ToDictionary(row => row.Id);

            Assert.Equal(specRows.Count, DiagnosticCatalog.All.Count);

            foreach (DiagnosticDescriptor entry in DiagnosticCatalog.All)
            {
                Assert.True(specById.TryGetValue(entry.Id, out SpecRow spec), $"目录中的 {entry.Id} 不存在于规范文档。");
                Assert.Equal(spec.Name, entry.Name);
                Assert.Equal(spec.Severity, entry.Severity);
                Assert.Equal(spec.Message, entry.DefaultMessage);
            }
        }

        [Fact]
        public void Find_ResolvesKnownIdAndRejectsUnknownId()
        {
            DiagnosticDescriptor descriptor = DiagnosticCatalog.Find("PEVT1009");
            Assert.NotNull(descriptor);
            Assert.Equal("InvalidSourceEncoding", descriptor.Name);
            Assert.Equal(DiagnosticSeverity.Error, descriptor.Severity);

            Assert.Null(DiagnosticCatalog.Find("PEVT0000"));
            Assert.False(DiagnosticCatalog.TryFind("PEVT0000", out DiagnosticDescriptor missing));
            Assert.Null(missing);
        }
    }
}
