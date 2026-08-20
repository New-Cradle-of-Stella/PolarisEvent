using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Registration
{
    /// <summary>
    /// 全局人物空间，与 `/event` 事件表完全分离：两者分开 Seal 和查询，人物冲突不会覆盖事件，事件冲突也不会覆盖人物。
    /// 内置 `AliceInCradle.BuiltinActors.pactor` 由本类型在构造时先行登记，因此 `aic` 命名空间永远先于任何外部程序集占位。
    /// </summary>
    public sealed class PevtActorRegistry
    {
        private sealed class Entry
        {
            public ActorCatalog Catalog;
            public string Owner;
            public string CatalogHash;
        }

        /// <summary>一条排入的人物目录扩展（PEVT-E06）。按加入顺序保存，卸载按逆序移除。</summary>
        private sealed class ExtensionEntry
        {
            public ActorCatalogExtension Extension;
            public string Owner;
            public string SourceHash;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<ExtensionEntry> _extensions = new List<ExtensionEntry>();
        private readonly Dictionary<string, Func<object>> _visualAccessors = new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        private ActorDirectory _directory = ActorDirectory.Empty;

        public PevtActorRegistry()
        {
            _entries.Add(new Entry
            {
                Catalog = BuiltinActorCatalog.Catalog,
                Owner = BuiltinActorCatalog.Owner,
                CatalogHash = string.Empty,
            });

            Rebuild();
        }

        public bool IsSealed { get; private set; }

        /// <summary>当前生效的只读人物空间。发生跨来源冲突的 ID 在这里查不到。</summary>
        public ActorDirectory Directory => _directory;

        public IReadOnlyList<ActorConflict> Conflicts => _directory.Conflicts;

        /// <summary>跨来源致命冲突，映射到运行诊断 <c>PEVTR4404</c>。</summary>
        public IReadOnlyList<ActorConflict> FatalConflicts
        {
            get
            {
                var result = new List<ActorConflict>();
                foreach (ActorConflict conflict in _directory.Conflicts)
                {
                    if (!conflict.IsSameOwner)
                        result.Add(conflict);
                }

                return new ReadOnlyCollection<ActorConflict>(result);
            }
        }

        internal void Add(
            ActorCatalog catalog,
            string owner,
            string catalogHash,
            IReadOnlyDictionary<string, Func<object>> visualAccessors = null)
        {
            if (IsSealed)
                throw new InvalidOperationException("人物注册表已 Seal，不能继续注册。");
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            _entries.Add(new Entry { Catalog = catalog, Owner = owner ?? string.Empty, CatalogHash = catalogHash ?? string.Empty });

            if (visualAccessors != null)
            {
                foreach (KeyValuePair<string, Func<object>> accessor in visualAccessors)
                    _visualAccessors[accessor.Key] = accessor.Value;
            }

            Rebuild();
        }

        /// <summary>
        /// 排入一个人物目录扩展。扩展的目标人物可以来自任何已登记目录（含别的程序集），
        /// 所以它不在这里生效，而是在每次重建时按加入顺序统一应用。
        /// </summary>
        internal void AddExtension(ActorCatalogExtension extension, string owner, string sourceHash)
        {
            if (IsSealed)
                throw new InvalidOperationException("人物注册表已 Seal，不能继续注册。");
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));

            _extensions.Add(new ExtensionEntry
            {
                Extension = extension,
                Owner = owner ?? string.Empty,
                SourceHash = sourceHash ?? string.Empty,
            });

            Rebuild();
        }

        /// <summary>已应用的扩展记录，按应用顺序排列。被拒绝的内容不在这里，见 <see cref="ExtensionDiagnostics"/>。</summary>
        public IReadOnlyList<ActorExtensionRecord> Extensions => _directory.Extensions;

        /// <summary>被拒绝的扩展内容产生的静态目录诊断。</summary>
        public IReadOnlyList<Diagnostics.Diagnostic> ExtensionDiagnostics => _directory.ExtensionDiagnostics;

        /// <summary>
        /// 取一个视觉的延迟资源访问器，键为 <c>&lt;actorId&gt;/&lt;visualKey&gt;</c>。
        /// 只有 <c>polaris-res</c> 视觉有访问器；原版 <c>game-pxls</c> 借用由资源桥直接解析。
        /// </summary>
        public bool TryGetVisualAccessor(string actorId, string visualKey, out Func<object> accessor) =>
            _visualAccessors.TryGetValue((actorId ?? string.Empty) + "/" + (visualKey ?? string.Empty), out accessor);

        /// <summary>已登记的延迟访问器数量。</summary>
        public int VisualAccessorCount => _visualAccessors.Count;

        /// <summary>封闭注册。之后不能继续登记人物目录，但仍可以按 owner 卸载。</summary>
        public IReadOnlyList<ActorConflict> Seal()
        {
            IsSealed = true;
            return Conflicts;
        }

        /// <summary>撤销某个 owner 的人物目录并重新合并。内置目录不能被卸载。</summary>
        public int Unload(string owner)
        {
            string key = owner ?? string.Empty;
            if (string.Equals(key, BuiltinActorCatalog.Owner, StringComparison.Ordinal))
                throw new InvalidOperationException("内置人物目录不能卸载。");

            int removed = _entries.RemoveAll(entry => string.Equals(entry.Owner, key, StringComparison.Ordinal));

            // 扩展只追加 appearance，所以卸载它就是"重建时不再排入"——不需要"扩展前的人物快照"。
            // 逆序移除只为让卸载顺序与应用顺序对称，便于按 F8 上的 #order 对照。
            for (int i = _extensions.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_extensions[i].Owner, key, StringComparison.Ordinal))
                {
                    _extensions.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
                Rebuild();
            return removed;
        }

        private void Rebuild()
        {
            var builder = new ActorDirectoryBuilder();
            foreach (Entry entry in _entries)
                builder.Add(entry.Catalog, entry.Owner, entry.CatalogHash);

            // 扩展在全部基础目录之后才排入：目标人物可能来自最后一个加入的目录。
            foreach (ExtensionEntry extension in _extensions)
                builder.AddExtension(extension.Extension, extension.Owner, extension.SourceHash);

            _directory = builder.Build();
        }
    }
}
