using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Registration
{
    /// <summary>
    /// 全局人物空间。与 `/event` 事件表完全分离：人物冲突不会覆盖事件，事件冲突也不会覆盖人物，
    /// 两者分开 Seal 和查询（PEVT-嵌入注册与ID冲突规范.md 第 10 节）。
    ///
    /// 内置 `AliceInCradle.BuiltinActors.pactor` 由本类型在构造时先行登记，因此 `aic` 命名空间
    /// 永远先于任何外部程序集占位。
    /// </summary>
    public sealed class PevtActorRegistry
    {
        private sealed class Entry
        {
            public ActorCatalog Catalog;
            public string Owner;
            public string CatalogHash;
        }

        private readonly List<Entry> _entries = new List<Entry>();
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
        /// 取一个视觉的延迟资源访问器，键为 <c>&lt;actorId&gt;/&lt;visualKey&gt;</c>。
        /// 只有 <c>polaris-res</c> 视觉有访问器；原版 <c>game-pxls</c> 借用由资源桥直接解析。
        ///
        /// 本方法只把委托交出去，不调用它——真正解析要等首次演出。
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
            if (removed > 0)
                Rebuild();
            return removed;
        }

        private void Rebuild()
        {
            var builder = new ActorDirectoryBuilder();
            foreach (Entry entry in _entries)
                builder.Add(entry.Catalog, entry.Owner, entry.CatalogHash);
            _directory = builder.Build();
        }
    }
}
