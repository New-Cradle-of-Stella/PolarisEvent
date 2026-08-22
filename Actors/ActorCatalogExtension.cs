using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 一个已校验的人物目录增量扩展（PEVT-E06）：只往已登记人物上**追加** appearance，
    /// 不能声明命名空间、新建人物、改人物元数据或覆盖已有 appearance。
    /// 这样加载顺序不影响已有内容的含义，卸载也只是把追加的 appearance ID 拿掉。
    /// </summary>
    public sealed class ActorCatalogExtension
    {
        /// <summary>第一版扩展 sidecar 唯一支持的 <c>Version</c>。</summary>
        public const int SupportedVersion = 1;

        /// <summary>扩展 sidecar 根元素固定的 XML 命名空间，与 `.pactor` 相同。</summary>
        public const string XmlNamespace = ActorCatalog.XmlNamespace;

        public int Version { get; }

        /// <summary>项目相对路径；程序化构造时可为空串。</summary>
        public string SourcePath { get; }

        public IReadOnlyList<ActorExtensionDefinition> Extensions { get; }

        public ActorCatalogExtension(int version, string sourcePath, IEnumerable<ActorExtensionDefinition> extensions)
        {
            if (version != SupportedVersion)
                throw new ArgumentOutOfRangeException(nameof(version), version, "第一版扩展 sidecar 只支持 Version=1。");

            Version = version;
            SourcePath = sourcePath ?? string.Empty;

            var copy = new List<ActorExtensionDefinition>();
            if (extensions != null)
            {
                foreach (ActorExtensionDefinition extension in extensions)
                {
                    if (extension == null)
                        throw new ArgumentException("扩展列表中不能包含 null。", nameof(extensions));
                    copy.Add(extension);
                }
            }

            Extensions = new ReadOnlyCollection<ActorExtensionDefinition>(copy);
        }

        /// <summary>本扩展一共追加多少个 appearance。</summary>
        public int AppearanceCount
        {
            get
            {
                int count = 0;
                foreach (ActorExtensionDefinition extension in Extensions)
                    count += extension.Appearances.Count;
                return count;
            }
        }

        public override string ToString() => $"{Extensions.Count} actor extension(s), {AppearanceCount} appearance(s)";
    }

    /// <summary>针对一个已登记公开人物 ID 的追加内容。</summary>
    public sealed class ActorExtensionDefinition
    {
        /// <summary>目标人物的**最终**公开 ID <c>&lt;namespace&gt;:&lt;local-id&gt;</c>。</summary>
        public string ActorId { get; }

        public IReadOnlyList<ActorAppearance> Appearances { get; }

        public TextLocation Location { get; }

        public ActorExtensionDefinition(string actorId, IEnumerable<ActorAppearance> appearances, TextLocation location = null)
        {
            if (string.IsNullOrEmpty(actorId))
                throw new ArgumentException("扩展目标人物 ID 不能为空。", nameof(actorId));

            ActorId = actorId;
            Location = location;

            var copy = new List<ActorAppearance>();
            if (appearances != null)
            {
                foreach (ActorAppearance appearance in appearances)
                {
                    if (appearance == null)
                        throw new ArgumentException("appearance 列表中不能包含 null。", nameof(appearances));
                    copy.Add(appearance);
                }
            }

            Appearances = new ReadOnlyCollection<ActorAppearance>(copy);
        }

        public override string ToString() => $"{ActorId} (+{Appearances.Count})";
    }

    /// <summary>扩展 sidecar 的读取结果。</summary>
    public sealed class ActorCatalogExtensionReadResult
    {
        /// <summary>校验通过的扩展；有错误时为 null。</summary>
        public ActorCatalogExtension Extension { get; }

        public SourceText Source { get; }

        public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }

        public bool Success => Extension != null;

        internal ActorCatalogExtensionReadResult(
            ActorCatalogExtension extension,
            SourceText source,
            IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
        {
            Extension = extension;
            Source = source;
            Diagnostics = diagnostics;
        }
    }
}
