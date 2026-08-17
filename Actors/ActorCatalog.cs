using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 目录的来源可信级别。只有 Polaris 自己嵌入的内置目录才能声明 <c>BuiltIn="true"</c> 或占用
    /// <c>aic</c> 命名空间；这个判定由调用方（宿主扫描器 / 工具生成器）提供，不能从 XML 内容自证。
    /// </summary>
    public enum ActorCatalogSourceKind
    {
        /// <summary>模组或项目提供的外部目录。</summary>
        External,

        /// <summary>Polaris 自身嵌入的内置固定目录。</summary>
        BuiltIn,
    }

    /// <summary>
    /// 一个已完整校验的 `.pactor` 人物目录，构造函数复制人物列表且人物元素本身不可变，因此整体深不可变。
    /// 最终人物 ID 由 <see cref="Namespace"/> 与人物局部 ID 组成，比较固定使用序数比较。
    /// </summary>
    public sealed class ActorCatalog
    {
        /// <summary>第一版 `.pactor` 唯一支持的 <c>Version</c>。</summary>
        public const int SupportedVersion = 1;

        /// <summary>`.pactor` 根元素固定的 XML 命名空间。</summary>
        public const string XmlNamespace = "urn:polaris:pevt:actors:v1";

        public string Namespace { get; }

        public int Version { get; }

        public bool IsBuiltIn { get; }

        /// <summary>项目相对 `.pactor` 路径；不嵌入开发机绝对路径。程序化构造时可为空串。</summary>
        public string SourcePath { get; }

        public IReadOnlyList<ActorDefinition> Actors { get; }

        private readonly Dictionary<string, ActorDefinition> _byLocalId;

        public ActorCatalog(
            string catalogNamespace,
            int version,
            bool isBuiltIn,
            string sourcePath,
            IEnumerable<ActorDefinition> actors)
        {
            if (!ActorNaming.IsValidNamespace(catalogNamespace))
                throw new ArgumentException($"目录命名空间 `{catalogNamespace}` 不合法。", nameof(catalogNamespace));
            if (version != SupportedVersion)
                throw new ArgumentOutOfRangeException(nameof(version), version, "第一版 `.pactor` 只支持 Version=1。");

            Namespace = catalogNamespace;
            Version = version;
            IsBuiltIn = isBuiltIn;
            SourcePath = sourcePath ?? string.Empty;

            var copy = new List<ActorDefinition>();
            if (actors != null)
            {
                foreach (ActorDefinition actor in actors)
                {
                    if (actor == null)
                        throw new ArgumentException("人物列表中不能包含 null。", nameof(actors));
                    copy.Add(actor);
                }
            }

            Actors = new ReadOnlyCollection<ActorDefinition>(copy);

            _byLocalId = new Dictionary<string, ActorDefinition>(ActorNaming.IdComparer);
            foreach (ActorDefinition actor in Actors)
            {
                if (_byLocalId.ContainsKey(actor.LocalId))
                    throw new ArgumentException($"人物局部 ID `{actor.LocalId}` 在同一目录内重复。", nameof(actors));
                _byLocalId[actor.LocalId] = actor;
            }
        }

        /// <summary>人物在全局人物空间中的最终 ID：<c>&lt;namespace&gt;:&lt;local-id&gt;</c>。</summary>
        public string GetActorId(ActorDefinition actor)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));
            return ActorNaming.ComposeId(Namespace, actor.LocalId);
        }

        public bool TryGetActorByLocalId(string localId, out ActorDefinition actor) =>
            _byLocalId.TryGetValue(localId ?? string.Empty, out actor);

        /// <summary>按最终 ID 查找。命名空间不匹配时直接返回 false，不做任何回退猜测。</summary>
        public bool TryGetActor(string actorId, out ActorDefinition actor)
        {
            actor = null;
            if (!ActorNaming.TrySplitId(actorId, out string ns, out string localId))
                return false;
            if (!ActorNaming.IdComparer.Equals(ns, Namespace))
                return false;
            return _byLocalId.TryGetValue(localId, out actor);
        }

        public override string ToString() => $"{Namespace} ({Actors.Count} actors)";
    }

    /// <summary>`.pactor` 读取结果。<see cref="Catalog"/> 在出现 Error 级诊断时为 null。</summary>
    public sealed class ActorCatalogReadResult
    {
        public ActorCatalog Catalog { get; }

        public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }

        /// <summary>读取器实际解码出的源文本；编码失败时为 null。工具侧用它定位诊断。</summary>
        public SourceText Source { get; }

        public bool Success => Catalog != null;

        internal ActorCatalogReadResult(ActorCatalog catalog, SourceText source, IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
        {
            Catalog = catalog;
            Source = source;
            Diagnostics = diagnostics;
        }
    }
}
