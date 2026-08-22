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

        /// <summary>
        /// 已应用到本人物上的扩展，按应用顺序排列（PEVT-E06），卸载须按逆序进行。
        /// 空列表表示这个人物完全来自基础目录。
        /// </summary>
        public IReadOnlyList<ActorExtensionRecord> Extensions { get; }

        internal ActorRegistration(
            string actorId,
            ActorDefinition actor,
            ActorCatalog catalog,
            string owner,
            string catalogHash = null,
            IReadOnlyList<ActorExtensionRecord> extensions = null)
        {
            ActorId = actorId;
            Actor = actor;
            Catalog = catalog;
            Owner = owner ?? string.Empty;
            CatalogHash = catalogHash ?? string.Empty;
            Extensions = extensions ?? Array.AsReadOnly(Array.Empty<ActorExtensionRecord>());
        }

        /// <summary>
        /// 一个 appearance 是从哪来的：基础目录还是某个扩展。F8 的 Source/Ownership 面板用它，
        /// 因此这里回答的是"来源"，而不是"值"。
        /// </summary>
        public bool TryGetAppearanceSource(string appearanceId, out ActorExtensionRecord extension)
        {
            extension = null;
            if (appearanceId == null)
                return false;

            // 后应用的扩展排在后面；同一个 ID 不可能被两个扩展占用（重复会在应用时被拒），
            // 所以顺序查找就是唯一答案。
            foreach (ActorExtensionRecord record in Extensions)
            {
                foreach (string id in record.AppearanceIds)
                {
                    if (ActorNaming.IdComparer.Equals(id, appearanceId))
                    {
                        extension = record;
                        return true;
                    }
                }
            }

            return false;
        }

        public override string ToString() =>
            Extensions.Count == 0 ? $"{ActorId} ({Owner})" : $"{ActorId} ({Owner}, +{Extensions.Count} extension(s))";
    }

    /// <summary>
    /// 一次已应用的人物目录扩展（PEVT-E06）：谁、从哪个文件、往哪个人物上追加了哪些 appearance。
    ///
    /// 扩展只追加，所以这条记录同时就是卸载指令——把这些 appearance ID 拿掉即可，
    /// 不需要"扩展前的人物是什么样"这种快照。
    /// </summary>
    public sealed class ActorExtensionRecord
    {
        public string ActorId { get; }

        /// <summary>来源所有者：游戏侧为程序集名，工具侧为项目名。</summary>
        public string Owner { get; }

        /// <summary>来源 sidecar 的项目相对路径。</summary>
        public string SourcePath { get; }

        /// <summary>来源 sidecar 的内容哈希；未提供时为空串。</summary>
        public string SourceHash { get; }

        /// <summary>应用顺序，从 0 开始递增。卸载按逆序进行。</summary>
        public int Order { get; }

        public IReadOnlyList<string> AppearanceIds { get; }

        internal ActorExtensionRecord(
            string actorId, string owner, string sourcePath, string sourceHash, int order, IList<string> appearanceIds)
        {
            ActorId = actorId ?? string.Empty;
            Owner = owner ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            SourceHash = sourceHash ?? string.Empty;
            Order = order;
            AppearanceIds = new ReadOnlyCollection<string>(appearanceIds ?? new List<string>());
        }

        public string Describe() =>
            $"#{Order} `{Owner}` 的 `{SourcePath}` 向 `{ActorId}` 追加了 {AppearanceIds.Count} 个 appearance："
            + string.Join("、", AppearanceIds);

        public override string ToString() => Describe();
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
    /// 合并后的只读人物空间。人物表与 `/event` 事件表分离，人物冲突不会影响事件登记。
    /// </summary>
    public sealed class ActorDirectory
    {
        public IReadOnlyList<ActorRegistration> Actors { get; }

        public IReadOnlyList<ActorConflict> Conflicts { get; }

        /// <summary>按应用顺序排列的全部已应用扩展（PEVT-E06）。</summary>
        public IReadOnlyList<ActorExtensionRecord> Extensions { get; }

        /// <summary>被拒绝的扩展内容产生的静态目录诊断。</summary>
        public IReadOnlyList<Diagnostic> ExtensionDiagnostics { get; }

        private readonly Dictionary<string, ActorRegistration> _byId;
        private readonly Dictionary<string, ActorRegistration> _byLegacyPerson;
        private readonly HashSet<string> _conflictedIds;

        internal ActorDirectory(
            IList<ActorRegistration> actors,
            IList<ActorConflict> conflicts,
            Dictionary<string, ActorRegistration> byId,
            Dictionary<string, ActorRegistration> byLegacyPerson,
            HashSet<string> conflictedIds,
            IList<ActorExtensionRecord> extensions = null,
            IList<Diagnostic> extensionDiagnostics = null)
        {
            Actors = new ReadOnlyCollection<ActorRegistration>(actors);
            Conflicts = new ReadOnlyCollection<ActorConflict>(conflicts);
            Extensions = new ReadOnlyCollection<ActorExtensionRecord>(extensions ?? new List<ActorExtensionRecord>());
            ExtensionDiagnostics = new ReadOnlyCollection<Diagnostic>(extensionDiagnostics ?? new List<Diagnostic>());
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

        public override string ToString() =>
            $"{Actors.Count} actors, {Conflicts.Count} conflicts, {Extensions.Count} extension(s)";
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
        private readonly List<PendingExtension> _pendingExtensions = new List<PendingExtension>();
        private bool _sealed;

        /// <summary>扩展目标不是已登记公开人物 ID。</summary>
        private const string UnknownExtensionTargetId = "PEVT9119";

        /// <summary>扩展试图占用一个已经存在的 appearance ID。</summary>
        private const string DuplicateExtensionAppearanceId = "PEVT9120";

        /// <summary>扩展的 appearance 引用了目标人物没有登记的 portrait。</summary>
        private const string UnknownVisualReferenceId = "PEVT9114";

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

        /// <summary>
        /// 排入一个人物目录扩展（PEVT-E06）。它不立刻生效：扩展的目标人物可能来自还没加入的目录，
        /// 所以全部扩展统一在 <see cref="Build"/> 里按排入顺序应用，"目标不存在"才是确定的结论。
        /// </summary>
        public void AddExtension(ActorCatalogExtension extension, string owner, string sourceHash = null)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));
            if (_sealed)
                throw new InvalidOperationException("人物目录已 Seal，不能继续注册。");

            _pendingExtensions.Add(new PendingExtension
            {
                Extension = extension,
                Owner = owner ?? string.Empty,
                SourceHash = sourceHash ?? string.Empty,
            });
        }

        /// <summary>Seal 之后不可继续注册；返回的目录是一份不随本构建器变化的只读快照。</summary>
        public ActorDirectory Build()
        {
            _sealed = true;

            var actors = new List<ActorRegistration>(_actors);
            var byId = new Dictionary<string, ActorRegistration>(_byId, ActorNaming.IdComparer);
            var byLegacyPerson = new Dictionary<string, ActorRegistration>(_byLegacyPerson, StringComparer.Ordinal);
            var records = new List<ActorExtensionRecord>();
            var diagnostics = new List<Diagnostic>();

            ApplyExtensions(actors, byId, byLegacyPerson, records, diagnostics);

            return new ActorDirectory(
                actors,
                new List<ActorConflict>(_conflicts),
                byId,
                byLegacyPerson,
                new HashSet<string>(_conflictedIds, ActorNaming.IdComparer),
                records,
                diagnostics);
        }

        /// <summary>
        /// 按排入顺序应用扩展。每个 appearance 独立判定：一个被拒不影响同一段里其余的，
        /// 因为"这一个 ID 撞了"不该让作者的另外三个外观一起消失。
        /// </summary>
        private void ApplyExtensions(
            List<ActorRegistration> actors,
            Dictionary<string, ActorRegistration> byId,
            Dictionary<string, ActorRegistration> byLegacyPerson,
            List<ActorExtensionRecord> records,
            List<Diagnostic> diagnostics)
        {
            int order = 0;

            foreach (PendingExtension pending in _pendingExtensions)
            {
                foreach (ActorExtensionDefinition definition in pending.Extension.Extensions)
                {
                    if (!byId.TryGetValue(definition.ActorId, out ActorRegistration registration)
                        || _conflictedIds.Contains(definition.ActorId))
                    {
                        diagnostics.Add(new Diagnostic(
                            UnknownExtensionTargetId,
                            DiagnosticSeverity.Error,
                            $"扩展目标人物 `{definition.ActorId}` 不是已登记的公开人物 ID"
                            + $"（来自 `{pending.Owner}` 的 `{pending.Extension.SourcePath}`）。",
                            definition.Location));
                        continue;
                    }

                    var accepted = new List<ActorAppearance>();
                    var acceptedIds = new List<string>();

                    foreach (ActorAppearance appearance in definition.Appearances)
                    {
                        if (registration.Actor.TryGetAppearance(appearance.Id, out ActorAppearance _)
                            || ContainsId(acceptedIds, appearance.Id))
                        {
                            diagnostics.Add(new Diagnostic(
                                DuplicateExtensionAppearanceId,
                                DiagnosticSeverity.Error,
                                $"人物 `{definition.ActorId}` 已经有 appearance `{appearance.Id}`；"
                                + "扩展只能追加，不能覆盖已有 appearance。",
                                appearance.Location));
                            continue;
                        }

                        if (!registration.Actor.TryGetPortrait(appearance.PortraitId, out ActorVisual _))
                        {
                            diagnostics.Add(new Diagnostic(
                                UnknownVisualReferenceId,
                                DiagnosticSeverity.Error,
                                $"appearance `{appearance.Id}` 引用了人物 `{definition.ActorId}` 没有登记的 portrait "
                                + $"`{appearance.PortraitId}`。",
                                appearance.Location));
                            continue;
                        }

                        accepted.Add(appearance);
                        acceptedIds.Add(appearance.Id);
                    }

                    if (accepted.Count == 0)
                        continue;

                    var record = new ActorExtensionRecord(
                        definition.ActorId, pending.Owner, pending.Extension.SourcePath, pending.SourceHash, order++, acceptedIds);

                    var extensions = new List<ActorExtensionRecord>(registration.Extensions) { record };
                    var updated = new ActorRegistration(
                        registration.ActorId,
                        WithAdditionalAppearances(registration.Actor, accepted),
                        registration.Catalog,
                        registration.Owner,
                        registration.CatalogHash,
                        new ReadOnlyCollection<ActorExtensionRecord>(extensions));

                    Replace(actors, byId, byLegacyPerson, registration, updated);
                    records.Add(record);
                }
            }
        }

        private static bool ContainsId(List<string> ids, string id)
        {
            foreach (string candidate in ids)
            {
                if (ActorNaming.IdComparer.Equals(candidate, id))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 人物资料是深不可变的，所以"追加 appearance"就是照原样重建一份、多带几个外观。
        /// 重建走的是同一个构造函数，因此 appearance→portrait 引用与 ID 唯一性仍由它统一把关。
        /// </summary>
        private static ActorDefinition WithAdditionalAppearances(ActorDefinition actor, IEnumerable<ActorAppearance> additional)
        {
            var appearances = new List<ActorAppearance>(actor.Appearances);
            appearances.AddRange(additional);

            return new ActorDefinition(
                actor.LocalId,
                actor.DisplayKey,
                actor.DisplayName,
                actor.Voice,
                actor.Color,
                actor.Icon,
                actor.DefaultPortraitId,
                actor.LegacyPerson,
                actor.WorldSprite,
                actor.Portraits,
                actor.UiPortraits,
                appearances,
                actor.Anchors,
                actor.Location);
        }

        /// <summary>
        /// 换掉一条登记。原版短键表存的是登记引用，所以指向旧登记的键必须一起换过来，
        /// 否则 `$raw cmd` 桥会继续看到扩展之前的人物。
        /// </summary>
        private static void Replace(
            List<ActorRegistration> actors,
            Dictionary<string, ActorRegistration> byId,
            Dictionary<string, ActorRegistration> byLegacyPerson,
            ActorRegistration existing,
            ActorRegistration updated)
        {
            int index = actors.IndexOf(existing);
            if (index >= 0)
                actors[index] = updated;

            byId[updated.ActorId] = updated;

            var legacyKeys = new List<string>();
            foreach (KeyValuePair<string, ActorRegistration> entry in byLegacyPerson)
            {
                if (ReferenceEquals(entry.Value, existing))
                    legacyKeys.Add(entry.Key);
            }

            foreach (string key in legacyKeys)
                byLegacyPerson[key] = updated;
        }

        private sealed class PendingExtension
        {
            public ActorCatalogExtension Extension;
            public string Owner;
            public string SourceHash;
        }
    }
}
