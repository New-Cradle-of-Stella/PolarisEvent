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
