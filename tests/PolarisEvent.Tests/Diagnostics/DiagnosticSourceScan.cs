using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Polaris.Pevt.Core.Tests.Diagnostics
{
    /// <summary>
    /// 扫仓库源码，找出"目录里登记了但生产代码里从来没出现过"的诊断编号。
    ///
    /// 这是计划要求的反向审计里最容易漏掉的一条：编号登记进目录很轻，真正发射它却可能一直没写。
    /// 用文本扫描而不是反射——发射点是散在各处的字符串常量，反射看不到。
    /// </summary>
    internal static class DiagnosticSourceScan
    {
        private static readonly string[] ExcludedFiles =
        {
            "DiagnosticCatalog.cs",
            "RuntimeDiagnosticCatalog.cs",
        };

        public static IReadOnlyCollection<string> WithoutProductionEmitter(IEnumerable<string> ids)
        {
            var pending = new HashSet<string>(ids, StringComparer.Ordinal);
            string root = FindRepoRoot();

            foreach (string file in EnumerateProductionSources(root))
            {
                string text = StripComments(File.ReadAllText(file));
                pending.RemoveWhere(id => text.Contains(id));
                if (pending.Count == 0)
                    break;
            }

            return pending;
        }

        /// <summary>
        /// 去掉注释再搜。仓库里大量文档注释会提到某个编号"由某某阶段负责"，把它们算成发射路径，
        /// 这道闸门就形同虚设——那正是最初漏掉 PEVT2415 的原因。
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

        private static IEnumerable<string> EnumerateProductionSources(string root)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/") || normalized.Contains("/tests/"))
                    continue;
                if (ExcludedFiles.Any(excluded => normalized.EndsWith("/" + excluded, StringComparison.Ordinal)))
                    continue;

                yield return file;
            }
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PolarisEvent.csproj")))
                    return dir.FullName;
            }

            throw new DirectoryNotFoundException("在祖先目录中找不到 PolarisEvent.csproj。");
        }
    }
}
