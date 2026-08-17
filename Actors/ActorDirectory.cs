using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Diagnostics;

namespace Polaris.Pevt.Actors
{
    /// <summary>全局人物空间中的一条登记：最终 ID、人物资料、来源目录与来源所有者。</summary>
    public sealed class ActorRegistration
    {
        /// <summary>最终人物 ID <c>&lt;namespace&gt;:&lt;local-id&gt;</c>。</summary>
        public string ActorId { get; }

        public ActorDefinition Actor { get; }

        public ActorCatalog Catalog { get; }

        /// <summary>来源所有者：游戏侧为程序集名，工具侧为项目名。空串表示未知来源。</summary>
        public string Owner { get; }

        /// <summary>来源 `.pactor` 的目录哈希；只用于冲突报告，未提供时为空串。</summary>
        public string CatalogHash { get; }

        internal ActorRegistration(string actorId, ActorDefinition actor, ActorCatalog catalog, string owner, string catalogHash = null)
        {
            ActorId = actorId;
            Actor = actor;
            Catalog = catalog;
            Owner = owner ?? string.Empty;
            CatalogHash = catalogHash ?? string.Empty;
        }

        public override string ToString() => $"{ActorId} ({Owner})";
    }

    /// <summary>
    /// 一条最终人物 ID 冲突记录：同一所有者内的重复是静态错误 <c>PEVT9106</c>，不同所有者之间是加载期致命冲突 <c>PEVTR4404</c>。
    /// 两种情况都保留先注册项，只为让同一次启动的结果稳定，不表示后注册方被接受。
    /// </summary>
    public sealed class ActorConflict
    {
        /// <summary>跨所有者冲突在运行诊断表中的编号。</summary>
        public const string CrossOwnerDiagnosticId = "PEVTR4404";

        /// <summary>同所有者重复在静态诊断表中的编号。</summary>
        public const string SameOwnerDiagnosticId = "PEVT9106";

        public string ActorId { get; }

        public bool IsSameOwner { get; }

        public string RetainedOwner { get; }

        public string RetainedSourcePath { get; }

        public string IgnoredOwner { get; }

        public string IgnoredSourcePath { get; }

        /// <summary>保留方 `.pactor` 的目录哈希；未提供时为空串。</summary>
        public string RetainedCatalogHash { get; }

        /// <summary>忽略方 `.pactor` 的目录哈希；未提供时为空串。</summary>
        public string IgnoredCatalogHash { get; }

        internal ActorConflict(
            string actorId,
            bool isSameOwner,
            string retainedOwner,
            string retainedSourcePath,
            string ignoredOwner,
            string ignoredSourcePath,
            string retainedCatalogHash = null,
            string ignoredCatalogHash = null)
        {
            ActorId = actorId;
            IsSameOwner = isSameOwner;
            RetainedOwner = retainedOwner ?? string.Empty;
            RetainedSourcePath = retainedSourcePath ?? string.Empty;
            IgnoredOwner = ignoredOwner ?? string.Empty;
            IgnoredSourcePath = ignoredSourcePath ?? string.Empty;
            RetainedCatalogHash = retainedCatalogHash ?? string.Empty;
            IgnoredCatalogHash = ignoredCatalogHash ?? string.Empty;
        }

        /// <summary>冲突应使用的诊断编号；不允许调用方另造编号。</summary>
        public string DiagnosticId => IsSameOwner ? SameOwnerDiagnosticId : CrossOwnerDiagnosticId;

        public string Describe() =>
            IsSameOwner
                ? $"人物 ID `{ActorId}` 在 `{RetainedOwner}` 内重复：`{RetainedSourcePath}` 与 `{IgnoredSourcePath}`。"
                : $"人物 ID `{ActorId}` 被多个来源注册：`{RetainedOwner}` 的 `{RetainedSourcePath}` 与 `{IgnoredOwner}` 的 `{IgnoredSourcePath}`。";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// 合并后的只读人物空间。人物表与 `/event` 事件表分离，人物冲突不会影响事件登记
    /// （PEVT-嵌入注册与ID冲突规范.md 第 10 节）。
    /// </summary>
    public sealed class ActorDirectory
    {
        public IReadOnlyList<ActorRegistration> Actors { get; }

        public IReadOnlyList<ActorConflict> Conflicts { get; }

        private readonly Dictionary<string, ActorRegistration> _byId;
        private readonly Dictionary<string, ActorRegistration> _byLegacyPerson;
        private readonly HashSet<string> _conflictedIds;

        internal ActorDirectory(
            IList<ActorRegistration> actors,
            IList<ActorConflict> conflicts,
            Dictionary<string, ActorRegistration> byId,
            Dictionary<string, ActorRegistration> byLegacyPerson,
            HashSet<string> conflictedIds)
        {
            Actors = new ReadOnlyCollection<ActorRegistration>(actors);
            Conflicts = new ReadOnlyCollection<ActorConflict>(conflicts);
            _byId = byId;
            _byLegacyPerson = byLegacyPerson;
            _conflictedIds = conflictedIds;
        }

        public static ActorDirectory Empty { get; } = new ActorDirectoryBuilder().Build();

        /// <summary>
        /// 按最终人物 ID 查找。发生跨来源冲突的 ID 一律查不到——冲突人物不能用于新事件，
        /// 即使先注册项为了稳定报告被保留。
        /// </summary>
        public bool TryGetActor(string actorId, out ActorRegistration registration)
        {
            registration = null;
            if (actorId == null)
                return false;
            if (_conflictedIds.Contains(actorId))
                return false;
            return _byId.TryGetValue(actorId, out registration);
        }

        public bool Contains(string actorId) => TryGetActor(actorId, out _);

        /// <summary>
        /// 原版短键到公开人物的映射，只由内置目录填充。仅供 <c>$raw cmd</c> 桥与内置数据核对使用；
        /// 普通 <c>@</c> 参数不得接受短键。
        /// </summary>
        public bool TryGetActorByLegacyPerson(string legacyPerson, out ActorRegistration registration) =>
            _byLegacyPerson.TryGetValue(legacyPerson ?? string.Empty, out registration);

        public IEnumerable<string> LegacyPersonKeys => _byLegacyPerson.Keys;

        /// <summary>把冲突记录翻译成诊断。跨来源冲突使用 <c>PEVTR4404</c>，不重新编号。</summary>
        public IReadOnlyList<Diagnostic> CreateConflictDiagnostics()
        {
            var result = new List<Diagnostic>();
            foreach (ActorConflict conflict in Conflicts)
                result.Add(new Diagnostic(conflict.DiagnosticId, DiagnosticSeverity.Error, conflict.Describe(), null));
            return new ReadOnlyCollection<Diagnostic>(result);
        }

        public override string ToString() => $"{Actors.Count} actors, {Conflicts.Count} conflicts";
    }

    /// <summary>
    /// 按登记顺序合并多个 `.pactor` 目录。内置目录必须先加入，随后才是外部目录；
    /// 先注册的定义临时保留，后注册的定义忽略，使同一次启动内的结果稳定。
    /// </summary>
    public sealed class ActorDirectoryBuilder
    {
        private readonly List<ActorRegistration> _actors = new List<ActorRegistration>();
        private readonly List<ActorConflict> _conflicts = new List<ActorConflict>();
        private readonly Dictionary<string, ActorRegistration> _byId = new Dictionary<string, ActorRegistration>(ActorNaming.IdComparer);
        private readonly Dictionary<string, ActorRegistration> _byLegacyPerson = new Dictionary<string, ActorRegistration>(StringComparer.Ordinal);
        private readonly HashSet<string> _conflictedIds = new HashSet<string>(ActorNaming.IdComparer);
        private bool _sealed;

        /// <summary>
        /// 加入一个目录，<paramref name="owner"/> 由扫描器固定，目录自身不能伪造。
        /// 返回本次加入产生的冲突记录，调用方据此决定报静态错误还是致命加载冲突。
        /// </summary>
        public IReadOnlyList<ActorConflict> Add(ActorCatalog catalog, string owner, string catalogHash = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (_sealed)
                throw new InvalidOperationException("人物目录已 Seal，不能继续注册。");
            if (!catalog.IsBuiltIn && ActorNaming.IdComparer.Equals(catalog.Namespace, ActorNaming.BuiltInNamespace))
                throw new ArgumentException($"`{ActorNaming.BuiltInNamespace}` 是封闭命名空间，非内置目录不能占用。", nameof(catalog));

            var added = new List<ActorConflict>();

            foreach (ActorDefinition actor in catalog.Actors)
            {
                string actorId = catalog.GetActorId(actor);
                if (_byId.TryGetValue(actorId, out ActorRegistration existing))
                {
                    bool sameOwner = string.Equals(existing.Owner, owner ?? string.Empty, StringComparison.Ordinal);
                    var conflict = new ActorConflict(
                        actorId,
                        sameOwner,
                        existing.Owner,
                        existing.Catalog.SourcePath,
                        owner,
                        catalog.SourcePath,
                        existing.CatalogHash,
                        catalogHash);

                    _conflicts.Add(conflict);
                    added.Add(conflict);
                    if (!sameOwner)
                        _conflictedIds.Add(actorId);
                    continue;
                }

                var registration = new ActorRegistration(actorId, actor, catalog, owner, catalogHash);
                _byId[actorId] = registration;
                _actors.Add(registration);
                RegisterLegacyKeys(registration);
            }

            return added;
        }

        /// <summary>
        /// 原版短键只从内置目录收集：Actor 级短键用于 <c>_</c> 这类无 portrait 的固定说话人，
        /// Portrait 级短键用于 <c>n</c>/<c>nb</c>/<c>nb2</c> 这类同一人物的多套视觉。
        /// </summary>
        private void RegisterLegacyKeys(ActorRegistration registration)
        {
            if (!registration.Catalog.IsBuiltIn)
                return;

            if (registration.Actor.LegacyPerson != null)
                _byLegacyPerson[registration.Actor.LegacyPerson] = registration;

            foreach (ActorVisual portrait in registration.Actor.Portraits)
            {
                if (portrait.LegacyPerson != null)
                    _byLegacyPerson[portrait.LegacyPerson] = registration;
            }
        }

        /// <summary>Seal 之后不可继续注册；返回的目录是一份不随本构建器变化的只读快照。</summary>
        public ActorDirectory Build()
        {
            _sealed = true;
            return new ActorDirectory(
                new List<ActorRegistration>(_actors),
                new List<ActorConflict>(_conflicts),
                new Dictionary<string, ActorRegistration>(_byId, ActorNaming.IdComparer),
                new Dictionary<string, ActorRegistration>(_byLegacyPerson, StringComparer.Ordinal),
                new HashSet<string>(_conflictedIds, ActorNaming.IdComparer));
        }
    }
}
