using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Loading;

namespace Polaris.Pevt.Registration
{
    /// <summary>
    /// 逐个扫描生成注册器，把嵌入事件与人物目录登记进两张分离的注册表。
    /// </summary>
    public sealed class PevtRegistryScanner
    {
        private readonly PevtEmbeddedSourceLimits _limits;
        private readonly BuiltinApiTable _builtinApi;
        private readonly HashSet<string> _scannedOwners = new HashSet<string>(StringComparer.Ordinal);

        public PevtEventRegistry Events { get; } = new PevtEventRegistry();

        public PevtActorRegistry Actors { get; } = new PevtActorRegistry();

        /// <param name="builtinApi">
        /// 绑定用的 API 表。默认使用权威 <see cref="CommandDescriptorCatalog.Builtin"/> 的投影，
        /// 不另建第二份注册表。
        /// </param>
        /// <param name="rawCsAnalyzer">
        /// <c>$raw cs</c> 的 C# 分析器；宿主接上它，嵌入源里的 C# 内容才会在扫描期一并重校
        /// （PEVT8007–8010）。为 null 时那四个编号不产生，其余静态门不受影响。
        /// </param>
        public PevtRegistryScanner(
            PevtEmbeddedSourceLimits limits = null,
            BuiltinApiTable builtinApi = null,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            _limits = limits ?? PevtEmbeddedSourceLimits.Default;
            _builtinApi = builtinApi ?? CommandDescriptorCatalog.Builtin.ToBuiltinApiTable();
            _rawCsAnalyzer = rawCsAnalyzer;
        }

        private readonly Runtime.Raw.IPevtRawCsAnalyzer _rawCsAnalyzer;

        /// <summary>已扫描过的 owner，按扫描顺序。</summary>
        public IReadOnlyCollection<string> ScannedOwners => _scannedOwners;

        /// <summary>
        /// 扫描一个程序集里带自动注册特性的注册器。人物目录先于事件登记，
        /// 这样同一程序集里的事件在加载时已经能看到自己的人物。
        /// </summary>
        public void ScanAssembly(Assembly assembly, string displayName = null, CancellationToken cancellationToken = default)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            string owner = assembly.GetName().Name;
            _scannedOwners.Add(owner);

            foreach (TypeInfo type in GetLoadableTypes(assembly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (type.GetCustomAttribute<PevtActorAutoRegistrationAttribute>() != null
                    && typeof(IPevtActorRegistrar).GetTypeInfo().IsAssignableFrom(type))
                {
                    RegisterActors((IPevtActorRegistrar)Activator.CreateInstance(type.AsType()), owner, displayName);
                }
            }

            foreach (TypeInfo type in GetLoadableTypes(assembly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (type.GetCustomAttribute<PevtAutoRegistrationAttribute>() != null
                    && typeof(IPevtRegistrar).GetTypeInfo().IsAssignableFrom(type))
                {
                    RegisterEvents((IPevtRegistrar)Activator.CreateInstance(type.AsType()), owner, displayName, cancellationToken);
                }
            }
        }

        /// <summary>不经过反射，直接登记一个注册器。宿主与测试用。</summary>
        public void Register(IPevtRegistrar registrar, string owner, string displayName = null, CancellationToken cancellationToken = default)
        {
            if (registrar == null)
                throw new ArgumentNullException(nameof(registrar));
            _scannedOwners.Add(owner ?? string.Empty);
            RegisterEvents(registrar, owner, displayName, cancellationToken);
        }

        /// <summary>不经过反射，直接登记一个人物目录注册器。宿主与测试用。</summary>
        public void Register(IPevtActorRegistrar registrar, string owner, string displayName = null)
        {
            if (registrar == null)
                throw new ArgumentNullException(nameof(registrar));
            _scannedOwners.Add(owner ?? string.Empty);
            RegisterActors(registrar, owner, displayName);
        }

        private void RegisterActors(IPevtActorRegistrar registrar, string owner, string displayName)
        {
            var context = new PevtActorRegistrationContext(owner, displayName);
            registrar.Register(context);

            foreach (ActorCatalogSubmission submission in context.Submitted)
                Actors.Add(submission.Catalog, context.Owner, submission.CatalogHash, submission.VisualAccessors);
        }

        private void RegisterEvents(IPevtRegistrar registrar, string owner, string displayName, CancellationToken cancellationToken)
        {
            var context = new PevtRegistrationContext(owner, displayName);
            registrar.Register(context);

            foreach (PevtEmbeddedSource source in context.Submitted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(
                    source, _limits, _builtinApi, cancellationToken, _rawCsAnalyzer);
                if (!result.Success)
                {
                    Events.AddFailure(context.Owner, result);
                    continue;
                }

                Events.AddCandidate(context.Owner, context.DisplayName, source, result.Definition);
            }
        }

        // ---- 外部导入 ----

        /// <summary>
        /// 外部导入统一使用的 owner。刻意用一个不可能是程序集名的名字（`#` 开头）：
        /// 它要和真实模组的 owner 明确区分，卸载时才能只撤销外部候选而不碰任何模组的注册。
        /// </summary>
        public const string ExternalOwner = "#external";

        /// <summary>冲突与覆盖报告里显示的外部来源名。</summary>
        public const string ExternalDisplayName = "外部导入";

        /// <summary>
        /// 加载一份外部源。用的是本扫描器自己的上限、API 表与 <c>$raw cs</c> 分析器，
        /// 因此外部导入与嵌入注册对同一份源码得到完全一致的 PEVTxxxx。
        /// </summary>
        public PevtExternalLoadResult LoadExternal(PevtExternalSource source, CancellationToken cancellationToken = default) =>
            PevtExternalSourceLoader.Load(source, _limits, _builtinApi, cancellationToken, _rawCsAnalyzer);

        /// <summary>
        /// 用一批外部源整批替换 <see cref="ExternalOwner"/> 名下的候选。
        /// 加载失败的文件不进入 `/event`，但它的诊断会留在返回的报告里——外部导入的失败是作者
        /// 正在改的那一行写错了，必须能立刻看到，因此不写进 <see cref="PevtEventRegistry.Failures"/>
        /// 那张"发布路径的加载失败"表里混淆两者。
        /// </summary>
        public PevtExternalApplyReport ApplyExternal(
            IReadOnlyList<PevtExternalSource> sources,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PevtExternalLoadResult>();
            var registrations = new List<PevtExternalRegistration>();

            if (sources != null)
            {
                foreach (PevtExternalSource source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (source == null)
                        continue;

                    PevtExternalLoadResult result = LoadExternal(source, cancellationToken);
                    results.Add(result);

                    if (result.Success)
                    {
                        registrations.Add(new PevtExternalRegistration(
                            result.Definition, source.DisplayPath, source.ContentHash));
                    }
                }
            }

            IReadOnlyList<PevtEventCandidate> registered =
                Events.ReplaceExternal(ExternalOwner, ExternalDisplayName, registrations);

            return new PevtExternalApplyReport(results, registered, Events.Overrides);
        }

        /// <summary>撤销全部外部候选，`/event` 回到只有嵌入源的状态。返回撤销的条数。</summary>
        public int ClearExternal() => Events.Unload(ExternalOwner);

        /// <summary>
        /// 封闭两张注册表。事件与人物分开 Seal，避免相同字符串在两个空间中互相影响；
        /// 返回值把两组冲突分开呈现，调用方据此产生致命报告。
        /// </summary>
        public PevtScanReport Seal()
        {
            IReadOnlyList<PevtEventConflict> eventConflicts = Events.Seal();
            IReadOnlyList<ActorConflict> actorConflicts = Actors.Seal();
            return new PevtScanReport(eventConflicts, actorConflicts, Events.Failures);
        }

        /// <summary>
        /// 反射拿类型时容忍部分类型加载失败：一个模组引用了缺失的可选依赖，不应该让整轮扫描失败。
        /// </summary>
        private static IEnumerable<TypeInfo> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.DefinedTypes;
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null).Select(type => type.GetTypeInfo());
            }
        }
    }

    /// <summary>一次外部导入的汇总结果。逐文件保留加载结果，成功与失败都能定位到具体路径。</summary>
    public sealed class PevtExternalApplyReport
    {
        /// <summary>逐个外部源的加载结果，顺序与传入顺序一致。</summary>
        public IReadOnlyList<PevtExternalLoadResult> Results { get; }

        /// <summary>本次登记进 `/event` 的候选。</summary>
        public IReadOnlyList<PevtEventCandidate> Registered { get; }

        /// <summary>本次生效后被外部源盖住的嵌入事件。</summary>
        public IReadOnlyList<PevtEventOverride> Overrides { get; }

        internal PevtExternalApplyReport(
            IReadOnlyList<PevtExternalLoadResult> results,
            IReadOnlyList<PevtEventCandidate> registered,
            IReadOnlyList<PevtEventOverride> overrides)
        {
            Results = results;
            Registered = registered;
            Overrides = overrides;
        }

        public int SucceededCount
        {
            get
            {
                int count = 0;
                foreach (PevtExternalLoadResult result in Results)
                {
                    if (result.Success)
                        count++;
                }

                return count;
            }
        }

        public int FailedCount => Results.Count - SucceededCount;

        /// <summary>失败的那些结果，供调试页与回执逐条展示。</summary>
        public IReadOnlyList<PevtExternalLoadResult> Failed
        {
            get
            {
                var failed = new List<PevtExternalLoadResult>();
                foreach (PevtExternalLoadResult result in Results)
                {
                    if (!result.Success)
                        failed.Add(result);
                }

                return failed;
            }
        }

        /// <summary>本次登记成功的事件 ID，按序数升序。</summary>
        public IReadOnlyList<string> EventIds
        {
            get
            {
                var ids = new List<string>();
                foreach (PevtEventCandidate candidate in Registered)
                    ids.Add(candidate.EventId);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        /// <summary>一行回执文本，直接送回 PolarisTools 或写进调试页页脚。</summary>
        public string Describe()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(SucceededCount).Append(" 个事件已导入");
            if (FailedCount > 0)
                builder.Append("，").Append(FailedCount).Append(" 个失败");
            if (Overrides.Count > 0)
                builder.Append("，").Append(Overrides.Count).Append(" 个盖住了嵌入版本");
            builder.Append("。");

            foreach (PevtExternalLoadResult result in Failed)
            {
                builder.Append(Environment.NewLine).Append(result.Source.DisplayPath).Append(": ");
                builder.Append(result.Diagnostics.Count > 0
                    ? result.Diagnostics[0].Id + " " + result.Diagnostics[0].Message
                    : result.Failure.ToString());
            }

            return builder.ToString();
        }

        public override string ToString() => Describe();
    }

    /// <summary>一次扫描的汇总结果。事件与人物冲突分表收集，互不影响。</summary>
    public sealed class PevtScanReport
    {
        public IReadOnlyList<PevtEventConflict> EventConflicts { get; }

        public IReadOnlyList<ActorConflict> ActorConflicts { get; }

        public IReadOnlyList<PevtEventLoadFailure> LoadFailures { get; }

        internal PevtScanReport(
            IReadOnlyList<PevtEventConflict> eventConflicts,
            IReadOnlyList<ActorConflict> actorConflicts,
            IReadOnlyList<PevtEventLoadFailure> loadFailures)
        {
            EventConflicts = eventConflicts;
            ActorConflicts = actorConflicts;
            LoadFailures = loadFailures;
        }

        /// <summary>是否存在必须作为致命报告上报的跨程序集冲突。</summary>
        public bool HasFatalConflicts =>
            EventConflicts.Any(conflict => !conflict.IsSameOwner) || ActorConflicts.Any(conflict => !conflict.IsSameOwner);

        /// <summary>把全部冲突汇总成一条报告，同时列出所有 ID 和涉及来源。</summary>
        public string DescribeFatalConflicts()
        {
            var lines = new List<string>();
            foreach (PevtEventConflict conflict in EventConflicts)
            {
                if (!conflict.IsSameOwner)
                    lines.Add("[事件] " + conflict.Describe());
            }

            foreach (ActorConflict conflict in ActorConflicts)
            {
                if (!conflict.IsSameOwner)
                    lines.Add($"[人物 {conflict.DiagnosticId}] " + conflict.Describe());
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
