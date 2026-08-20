using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Loading;

namespace Polaris.Pevt.Registration
{
    /// <summary>
    /// 一个候选定义的来源层级。层级决定"谁盖谁"：外部导入的源永远压过随程序集分发的嵌入源，
    /// 否则作者一旦把事件打进程序集，就再也没法在游戏里迭代同一个 ID 了。
    /// 层级之内的冲突规则不受影响，仍按 PEVT-嵌入注册与ID冲突规范 第 6 节判定。
    /// </summary>
    public enum PevtEventOrigin
    {
        /// <summary>随模组程序集一起分发的嵌入包。发布路径只有这一种。</summary>
        Embedded = 0,

        /// <summary>作者本机导入的 `.pevt`：磁盘目录扫描或 PolarisTools 热重载通道推送。</summary>
        External = 1,
    }

    /// <summary>`/event` 空间中的一个候选定义。同一 ID 可以有多个候选，由冲突规则决定谁生效。</summary>
    public sealed class PevtEventCandidate
    {
        public string EventId { get; }

        public PevtProgramDefinition Definition { get; }

        public string Owner { get; }

        public string DisplayName { get; }

        public string SourcePath { get; }

        public string ContentHash { get; }

        /// <summary>注册顺序，从 0 开始递增。用于让同一次启动内的胜出者稳定。</summary>
        public int SequenceNumber { get; }

        /// <summary>来源层级。<see cref="PevtEventOrigin.External"/> 压过 <see cref="PevtEventOrigin.Embedded"/>。</summary>
        public PevtEventOrigin Origin { get; }

        internal PevtEventCandidate(
            string eventId,
            PevtProgramDefinition definition,
            string owner,
            string displayName,
            string sourcePath,
            string contentHash,
            int sequenceNumber,
            PevtEventOrigin origin = PevtEventOrigin.Embedded)
        {
            Origin = origin;
            EventId = eventId;
            Definition = definition;
            Owner = owner ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            SequenceNumber = sequenceNumber;
        }

        /// <summary>事件在 `/event` 内存虚拟空间中的路径。它不是磁盘路径，也不是 `.cmd` 目录。</summary>
        public string VirtualPath => PevtEventRegistry.GetVirtualPath(EventId);

        public override string ToString() => $"{VirtualPath} ({Owner}:{SourcePath})";
    }

    /// <summary>
    /// 一条事件 ID 冲突记录：跨程序集重复是致命冲突，同程序集重复只是警告且后注册覆盖先注册。
    /// 两种情况都必须同时列出两个项目相对源路径。
    /// </summary>
    public sealed class PevtEventConflict
    {
        public string EventId { get; }

        /// <summary>true 表示同一程序集内重复，按警告处理；false 表示跨程序集致命冲突。</summary>
        public bool IsSameOwner { get; }

        public PevtEventCandidate Retained { get; }

        public PevtEventCandidate Ignored { get; }

        internal PevtEventConflict(string eventId, bool isSameOwner, PevtEventCandidate retained, PevtEventCandidate ignored)
        {
            EventId = eventId;
            IsSameOwner = isSameOwner;
            Retained = retained;
            Ignored = ignored;
        }

        public string Describe() =>
            IsSameOwner
                ? $"事件 ID `{EventId}` 在 `{Retained.Owner}` 内重复：`{Ignored.SourcePath}` 覆盖了先前的 `{Retained.SourcePath}`。"
                : $"事件 ID `{EventId}` 被多个模组注册：保留 `{Retained.DisplayName}`（{Retained.Owner}）的 `{Retained.SourcePath}`（哈希 {Retained.ContentHash}），"
                  + $"忽略 `{Ignored.DisplayName}`（{Ignored.Owner}）的 `{Ignored.SourcePath}`（哈希 {Ignored.ContentHash}）。"
                  + "两个模组都是责任方，请修改其中一个 `.pevt` 的 `id`。";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// 一条"外部源盖住了嵌入源"的记录。它不是冲突：外部导入压过嵌入包是设计意图，
    /// 因此既不进 <see cref="PevtEventRegistry.Conflicts"/>，也不让 `callevt` 判成歧义目标。
    /// 单独列出来只是为了让作者一眼看到"现在跑的是哪一份"。
    /// </summary>
    public sealed class PevtEventOverride
    {
        public string EventId { get; }

        /// <summary>当前生效的外部候选。</summary>
        public PevtEventCandidate Active { get; }

        /// <summary>被盖住的嵌入候选。</summary>
        public PevtEventCandidate Shadowed { get; }

        internal PevtEventOverride(string eventId, PevtEventCandidate active, PevtEventCandidate shadowed)
        {
            EventId = eventId;
            Active = active;
            Shadowed = shadowed;
        }

        public string Describe() =>
            $"事件 ID `{EventId}` 由外部导入的 `{Active.SourcePath}` 生效，"
            + $"`{Shadowed.DisplayName}`（{Shadowed.Owner}）随程序集分发的 `{Shadowed.SourcePath}` 暂时被盖住。";

        public override string ToString() => Describe();
    }

    /// <summary>一个嵌入包没有进入 `/event` 的原因。保留来源信息与具体诊断，不降级为原版 EV。</summary>
    public sealed class PevtEventLoadFailure
    {
        public string Owner { get; }

        public string DeclaredId { get; }

        public string SourcePath { get; }

        public PevtEmbeddedLoadResult Result { get; }

        internal PevtEventLoadFailure(string owner, PevtEmbeddedLoadResult result)
        {
            Owner = owner ?? string.Empty;
            DeclaredId = result.Source.DeclaredId;
            SourcePath = result.Source.SourcePath;
            Result = result;
        }

        public override string ToString() => $"{Owner}:{SourcePath} -> {Result.Failure}";
    }

    /// <summary>
    /// 一条待登记的外部候选：已经通过全部静态门的定义，加上展示用路径与内容哈希。
    /// 注册表不认识"文件"这个概念，因此外部导入的读盘与通道细节全部留在调用方。
    /// </summary>
    public sealed class PevtExternalRegistration
    {
        public PevtExternalRegistration(PevtProgramDefinition definition, string sourcePath, string contentHash)
        {
            Definition = definition;
            SourcePath = sourcePath ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
        }

        public PevtProgramDefinition Definition { get; }

        public string SourcePath { get; }

        public string ContentHash { get; }
    }

    /// <summary>
    /// `/event` 运行时虚拟事件空间，由 PolarisEvent 在内存中管理，不是原版 `StreamingAssets/evt`、`.cmd` 目录或磁盘缓存。
    /// 本类型只保存候选、按冲突规则派生生效集合并支持按 owner 卸载；卸载后重新派生，原本因冲突被忽略的定义可以重新生效。
    /// </summary>
    public sealed class PevtEventRegistry
    {
        private readonly List<PevtEventCandidate> _candidates = new List<PevtEventCandidate>();
        private readonly List<PevtEventLoadFailure> _failures = new List<PevtEventLoadFailure>();
        private Dictionary<string, PevtEventCandidate> _active = new Dictionary<string, PevtEventCandidate>(StringComparer.Ordinal);
        private List<PevtEventConflict> _conflicts = new List<PevtEventConflict>();
        private List<PevtEventOverride> _overrides = new List<PevtEventOverride>();
        private int _nextSequence;

        /// <summary>事件在虚拟空间中的路径。ID 区分大小写，索引使用序数比较。</summary>
        public static string GetVirtualPath(string eventId) => "/event/" + eventId + ".pevt";

        public bool IsSealed { get; private set; }

        /// <summary>当前生效的事件，按事件 ID 索引。</summary>
        public IReadOnlyCollection<PevtEventCandidate> ActiveEvents => new ReadOnlyCollection<PevtEventCandidate>(new List<PevtEventCandidate>(_active.Values));

        public IReadOnlyList<PevtEventCandidate> Candidates => new ReadOnlyCollection<PevtEventCandidate>(_candidates);

        public IReadOnlyList<PevtEventConflict> Conflicts => new ReadOnlyCollection<PevtEventConflict>(_conflicts);

        /// <summary>当前被外部导入盖住的嵌入事件。信息性记录，不是冲突。</summary>
        public IReadOnlyList<PevtEventOverride> Overrides => new ReadOnlyCollection<PevtEventOverride>(_overrides);

        public IReadOnlyList<PevtEventLoadFailure> Failures => new ReadOnlyCollection<PevtEventLoadFailure>(_failures);

        /// <summary>跨程序集致命冲突。它们不阻止先注册者生效，但必须作为致命报告上报。</summary>
        public IReadOnlyList<PevtEventConflict> FatalConflicts
        {
            get
            {
                var result = new List<PevtEventConflict>();
                foreach (PevtEventConflict conflict in _conflicts)
                {
                    if (!conflict.IsSameOwner)
                        result.Add(conflict);
                }

                return new ReadOnlyCollection<PevtEventConflict>(result);
            }
        }

        /// <summary><c>callevt "ID"</c> 在真正执行时查询这里。</summary>
        public bool TryGet(string eventId, out PevtEventCandidate candidate) =>
            _active.TryGetValue(eventId ?? string.Empty, out candidate);

        public bool Contains(string eventId) => TryGet(eventId, out _);

        internal void AddFailure(string owner, PevtEmbeddedLoadResult result) =>
            _failures.Add(new PevtEventLoadFailure(owner, result));

        internal PevtEventCandidate AddCandidate(
            string owner,
            string displayName,
            PevtEmbeddedSource source,
            PevtProgramDefinition definition) =>
            AddCandidate(owner, displayName, source.SourcePath, source.ContentHash, definition, PevtEventOrigin.Embedded);

        internal PevtEventCandidate AddCandidate(
            string owner,
            string displayName,
            string sourcePath,
            string contentHash,
            PevtProgramDefinition definition,
            PevtEventOrigin origin)
        {
            var candidate = new PevtEventCandidate(
                definition.EventId, definition, owner, displayName,
                sourcePath, contentHash, _nextSequence++, origin);
            _candidates.Add(candidate);
            return candidate;
        }

        /// <summary>扫描结束时封闭注册并派生生效集合。返回本次汇总出的全部冲突。</summary>
        public IReadOnlyList<PevtEventConflict> Seal()
        {
            IsSealed = true;
            Rebuild();
            return Conflicts;
        }

        /// <summary>
        /// 扫描封闭之后新加入的注册。规范要求这种情况"立即单独上报"，因此本方法只返回本次新增的冲突，
        /// 不重复报告 Seal 时已经汇总过的那些。
        /// </summary>
        public IReadOnlyList<PevtEventConflict> RegisterLate(
            string owner,
            string displayName,
            PevtEmbeddedSource source,
            PevtProgramDefinition definition)
        {
            if (!IsSealed)
                throw new InvalidOperationException("注册表尚未 Seal，应使用扫描器的正常注册路径。");

            int before = _conflicts.Count;
            AddCandidate(owner, displayName, source, definition);
            Rebuild();

            var added = new List<PevtEventConflict>();
            for (int i = before; i < _conflicts.Count; i++)
                added.Add(_conflicts[i]);
            return new ReadOnlyCollection<PevtEventConflict>(added);
        }

        /// <summary>撤销某个 owner 的全部注册，并重新派生。原本被它挤掉的定义可以重新生效。</summary>
        public int Unload(string owner)
        {
            string key = owner ?? string.Empty;
            int removed = _candidates.RemoveAll(candidate => string.Equals(candidate.Owner, key, StringComparison.Ordinal));
            _failures.RemoveAll(failure => string.Equals(failure.Owner, key, StringComparison.Ordinal));
            if (removed > 0)
                Rebuild();
            return removed;
        }

        /// <summary>
        /// 整批替换某个 owner 名下的外部候选：先撤销它此前登记的全部候选，再登记这一批，最后重新派生一次。
        /// <para>
        /// 必须是"整批替换"而不是"逐个追加"。热重载的语义是"磁盘上现在有这些文件"，作者删掉一个
        /// `.pevt` 之后它就该从 `/event` 里消失；逐个追加做不到这件事，只会让上一轮的事件一直赖在表里。
        /// 同理也只 Rebuild 一次，避免中间态被别人看见。
        /// </para>
        /// </summary>
        public IReadOnlyList<PevtEventCandidate> ReplaceExternal(
            string owner,
            string displayName,
            IEnumerable<PevtExternalRegistration> registrations)
        {
            string key = owner ?? string.Empty;
            _candidates.RemoveAll(candidate => string.Equals(candidate.Owner, key, StringComparison.Ordinal));

            var added = new List<PevtEventCandidate>();
            if (registrations != null)
            {
                foreach (PevtExternalRegistration registration in registrations)
                {
                    if (registration == null || registration.Definition == null)
                        continue;

                    added.Add(AddCandidate(
                        key, displayName, registration.SourcePath, registration.ContentHash,
                        registration.Definition, PevtEventOrigin.External));
                }
            }

            Rebuild();
            return new ReadOnlyCollection<PevtEventCandidate>(added);
        }

        /// <summary>
        /// 派生生效集合。分两层：先在嵌入层内部按规范第 6 节定胜负，再让外部导入层整体压上去。
        /// <para>
        /// 分层而不是把所有候选放在一起排序，是为了让"外部导入"不污染发布路径的判定：
        /// 两个模组抢同一个 ID 仍然是致命冲突，不会因为作者本机恰好导入了同名事件而被悄悄和解掉；
        /// 反过来外部源盖住嵌入源也不算冲突，只记一条 <see cref="PevtEventOverride"/>。
        /// </para>
        /// </summary>
        private void Rebuild()
        {
            var conflicts = new List<PevtEventConflict>();
            Dictionary<string, PevtEventCandidate> embedded = ResolveTier(PevtEventOrigin.Embedded, conflicts);
            Dictionary<string, PevtEventCandidate> external = ResolveTier(PevtEventOrigin.External, conflicts);

            var active = new Dictionary<string, PevtEventCandidate>(embedded, StringComparer.Ordinal);
            var overrides = new List<PevtEventOverride>();

            // 按候选的登记顺序遍历而不是遍历字典，这样 Overrides 的次序是稳定的。
            foreach (PevtEventCandidate candidate in _candidates)
            {
                if (candidate.Origin != PevtEventOrigin.External)
                    continue;

                if (!external.TryGetValue(candidate.EventId, out PevtEventCandidate winner)
                    || !ReferenceEquals(winner, candidate))
                {
                    continue;
                }

                if (active.TryGetValue(candidate.EventId, out PevtEventCandidate shadowed))
                    overrides.Add(new PevtEventOverride(candidate.EventId, candidate, shadowed));

                active[candidate.EventId] = candidate;
            }

            _active = active;
            _conflicts = conflicts;
            _overrides = overrides;
        }

        /// <summary>
        /// 在一个来源层级内部按注册顺序定胜负：
        /// 同一 ID 下第一个出现的 owner 胜出（跨程序集冲突时先注册者临时保留，使结果稳定）；
        /// 该 owner 内部的重复由后注册者覆盖先注册者，并记为警告级冲突。
        /// </summary>
        private Dictionary<string, PevtEventCandidate> ResolveTier(
            PevtEventOrigin origin,
            List<PevtEventConflict> conflicts)
        {
            var active = new Dictionary<string, PevtEventCandidate>(StringComparer.Ordinal);
            var winningOwner = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (PevtEventCandidate candidate in _candidates)
            {
                if (candidate.Origin != origin)
                    continue;

                if (!active.TryGetValue(candidate.EventId, out PevtEventCandidate current))
                {
                    active[candidate.EventId] = candidate;
                    winningOwner[candidate.EventId] = candidate.Owner;
                    continue;
                }

                if (string.Equals(winningOwner[candidate.EventId], candidate.Owner, StringComparison.Ordinal))
                {
                    // 同程序集重复：记录警告，后注册覆盖先注册，两个源路径都要出现在报告里。
                    conflicts.Add(new PevtEventConflict(candidate.EventId, true, current, candidate));
                    active[candidate.EventId] = candidate;
                    continue;
                }

                // 跨程序集重复：致命冲突，保留先注册者。不因两份内容哈希相同而豁免。
                conflicts.Add(new PevtEventConflict(candidate.EventId, false, current, candidate));
            }

            return active;
        }
    }
}
