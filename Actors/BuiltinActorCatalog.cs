using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Polaris.Pevt.Diagnostics;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// Polaris 内置的原版固定人物目录。以 EmbeddedResource 随本程序集分发，工具侧与游戏侧读取同一份字节，
    /// 因此对 `aic:` 人物的解析结果必然一致；游戏侧仍然重新做完整校验，不信任任何预处理结果。
    /// </summary>
    public static class BuiltinActorCatalog
    {
        public const string ResourceFileName = "AliceInCradle.BuiltinActors.pactor";

        /// <summary>目录在诊断与冲突报告中的来源标识。</summary>
        public const string Owner = "Polaris";

        private static readonly object Gate = new object();
        private static ActorCatalogReadResult _cached;

        /// <summary>读取并缓存内置目录。多次调用返回同一个不可变结果。</summary>
        public static ActorCatalogReadResult Load()
        {
            if (_cached != null)
                return _cached;

            lock (Gate)
            {
                if (_cached == null)
                    _cached = ReadEmbedded();
            }

            return _cached;
        }

        /// <summary>已校验的内置目录。内置资源损坏属于分发事故，此时直接抛出而不是静默降级。</summary>
        public static ActorCatalog Catalog
        {
            get
            {
                ActorCatalogReadResult result = Load();
                if (result.Success)
                    return result.Catalog;

                string details = string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray());
                throw new InvalidOperationException($"内置人物目录 `{ResourceFileName}` 无法读取：{Environment.NewLine}{details}");
            }
        }

        /// <summary>以内置目录为第一个来源开始构建全局人物空间，保证 `aic` 先于外部程序集登记。</summary>
        public static ActorDirectoryBuilder CreateDirectoryBuilder()
        {
            var builder = new ActorDirectoryBuilder();
            builder.Add(Catalog, Owner);
            return builder;
        }

        private static ActorCatalogReadResult ReadEmbedded()
        {
            Assembly assembly = typeof(BuiltinActorCatalog).GetTypeInfo().Assembly;
            string resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(ResourceFileName, StringComparison.Ordinal));

            if (resourceName == null)
                throw new InvalidOperationException($"本程序集没有嵌入 `{ResourceFileName}`。");

            byte[] bytes;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (var buffer = new MemoryStream())
            {
                if (stream == null)
                    throw new InvalidOperationException($"无法打开嵌入资源 `{resourceName}`。");
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            return ActorCatalogReader.Read(bytes, ResourceFileName, ActorCatalogSourceKind.BuiltIn);
        }
    }
}
