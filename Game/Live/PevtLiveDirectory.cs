using System;
using System.Collections.Generic;
using System.IO;
using Polaris.Pevt.Live;
using Polaris.Pevt.Loading;

namespace Polaris.Event.Game.Live
{
    /// <summary>一次目录扫描的结果：能读的都变成外部源，读不了的单独列出来。</summary>
    internal sealed class PevtLiveDirectoryScan
    {
        internal PevtLiveDirectoryScan(
            string directory,
            bool exists,
            IReadOnlyList<PevtExternalSource> sources,
            IReadOnlyList<string> unreadable,
            bool truncated)
        {
            Directory = directory;
            Exists = exists;
            Sources = sources;
            Unreadable = unreadable;
            Truncated = truncated;
        }

        internal string Directory { get; }

        internal bool Exists { get; }

        internal IReadOnlyList<PevtExternalSource> Sources { get; }

        /// <summary>读盘失败的文件，每条形如 `路径: 原因`。它们不进入 `/event`，但必须让作者看到。</summary>
        internal IReadOnlyList<string> Unreadable { get; }

        /// <summary>命中 <see cref="PevtLiveProtocol.MaxFiles"/> 上限后截断了。</summary>
        internal bool Truncated { get; }
    }

    /// <summary>
    /// 外部 `.pevt` 目录的解析与读取。
    /// <para>
    /// 这里只负责"哪些文件、字节是什么"，一行 PEVT 语法都不认识——解析与校验全部由
    /// <see cref="Polaris.Pevt.Registration.PevtRegistryScanner.ApplyExternal"/> 走共享语言核心完成。
    /// </para>
    /// </summary>
    internal static class PevtLiveDirectory
    {
        /// <summary>默认目录名，落在 <c>BepInEx/Polaris/</c> 之下。</summary>
        internal const string FolderName = "pevt";

        internal const string Extension = ".pevt";

        /// <summary>
        /// 解析生效目录。设置项留空时用默认目录；填了就按填的走，并展开 <c>%VAR%</c>——
        /// 作者常把它指向自己的工程目录，写成 <c>%USERPROFILE%\source\...</c> 比绝对路径好带。
        /// </summary>
        internal static string Resolve(string configured)
        {
            string trimmed = (configured ?? string.Empty).Trim().Trim('"');
            if (trimmed.Length == 0)
                return Path.Combine(PolarisAPI.Paths.StateDir, FolderName);

            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(trimmed));
            }
            catch (Exception)
            {
                // 路径写得不合法（非法字符、过长）时原样返回，让扫描阶段报"目录不存在"而不是在这里抛。
                return trimmed;
            }
        }

        /// <summary>幂等建出默认目录，好让作者能在文件管理器里找到该往哪儿放 `.pevt`。</summary>
        internal static void EnsureDefaultDirectory()
        {
            PevtGameHost.Guard("Live.EnsureDirectory", () =>
                Directory.CreateDirectory(Path.Combine(PolarisAPI.Paths.StateDir, FolderName)));
        }

        /// <summary>
        /// 扫描目录下的全部 `.pevt`。按完整路径的序数序遍历，使同一份磁盘内容每次得到相同的登记顺序
        /// ——顺序决定重复 ID 的胜负，随文件系统枚举顺序漂移的话，同一个目录两次启动可能跑的不是同一份事件。
        /// </summary>
        internal static PevtLiveDirectoryScan Scan(string directory)
        {
            var sources = new List<PevtExternalSource>();
            var unreadable = new List<string>();

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return new PevtLiveDirectoryScan(directory, false, sources, unreadable, false);

            var paths = new List<string>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*" + Extension, SearchOption.AllDirectories))
                {
                    if (!IsBuildOutput(directory, path))
                        paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                unreadable.Add(directory + ": " + ex.Message);
                return new PevtLiveDirectoryScan(directory, true, sources, unreadable, false);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);

            bool truncated = paths.Count > PevtLiveProtocol.MaxFiles;
            int count = truncated ? PevtLiveProtocol.MaxFiles : paths.Count;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    sources.Add(PevtExternalSource.FromBytes(
                        ToDisplayPath(directory, paths[i]), File.ReadAllBytes(paths[i])));
                }
                catch (Exception ex)
                {
                    unreadable.Add(ToDisplayPath(directory, paths[i]) + ": " + ex.Message);
                }
            }

            return new PevtLiveDirectoryScan(directory, true, sources, unreadable, truncated);
        }

        /// <summary>
        /// 跳过 <c>bin</c> / <c>obj</c> 里的副本。作者通常把导入目录直接指向 PolarisTools 的工程目录，
        /// 而 `.pevt` 会作为内容项被拷进输出目录；不跳过的话同一个事件 ID 会以两份路径出现，
        /// 白白撞出一条同 owner 重复警告。
        /// </summary>
        private static bool IsBuildOutput(string root, string path)
        {
            string relative = ToDisplayPath(root, path);
            foreach (string segment in relative.Split('/'))
            {
                if (string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>目录相对路径，统一写成 `/` 分隔；不在目录之下时退回文件名。</summary>
        private static string ToDisplayPath(string root, string path)
        {
            try
            {
                string rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    rootFull += Path.DirectorySeparatorChar;

                string full = Path.GetFullPath(path);
                if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(rootFull.Length).Replace('\\', '/');

                return Path.GetFileName(full);
            }
            catch (Exception)
            {
                return Path.GetFileName(path) ?? string.Empty;
            }
        }
    }
}
