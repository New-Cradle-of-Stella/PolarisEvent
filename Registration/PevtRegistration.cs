using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Loading;

namespace Polaris.Pevt.Registration
{
    /// <summary>
    /// 标记一个由 PolarisTools 生成的事件注册器，供扫描器发现。
    /// 生成类不能伪造来源程序集——来源由扫描器在创建注册上下文时固定。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PevtAutoRegistrationAttribute : Attribute
    {
    }

    /// <summary>标记一个由 PolarisTools 生成的人物目录注册器。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PevtActorAutoRegistrationAttribute : Attribute
    {
    }

    /// <summary>生成的事件注册器契约。一个注册器可以提交一个或多个嵌入包。</summary>
    public interface IPevtRegistrar
    {
        void Register(PevtRegistrationContext context);
    }

    /// <summary>生成的人物目录注册器契约。</summary>
    public interface IPevtActorRegistrar
    {
        void Register(PevtActorRegistrationContext context);
    }

    /// <summary>
    /// 事件注册上下文。由扫描器创建并固定所有者，注册器只能往里提交嵌入包，
    /// 拿不到修改所有者的入口。
    /// </summary>
    public sealed class PevtRegistrationContext
    {
        private readonly List<PevtEmbeddedSource> _submitted = new List<PevtEmbeddedSource>();

        /// <summary>来源标识，通常是程序集名。由扫描器固定。</summary>
        public string Owner { get; }

        /// <summary>模组的可识别名称，用于冲突报告；未提供时与 <see cref="Owner"/> 相同。</summary>
        public string DisplayName { get; }

        internal PevtRegistrationContext(string owner, string displayName)
        {
            Owner = owner ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Owner : displayName;
        }

        public void Register(PevtEmbeddedSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            _submitted.Add(source);
        }

        internal IReadOnlyList<PevtEmbeddedSource> Submitted => new ReadOnlyCollection<PevtEmbeddedSource>(_submitted);
    }

    /// <summary>
    /// 人物目录注册上下文。生成的 `.g.cs` 只提交已验证的不可变数据与延迟资源访问器，
    /// 不提交可执行 XML、任意 C# 方法名或原版 CMD 文本，因此这里接收的是构造好的
    /// <see cref="ActorCatalog"/> 而不是 XML 文本。
    /// </summary>
    public sealed class PevtActorRegistrationContext
    {
        private readonly List<ActorCatalogSubmission> _submitted = new List<ActorCatalogSubmission>();
        private readonly List<ActorExtensionSubmission> _submittedExtensions = new List<ActorExtensionSubmission>();

        public string Owner { get; }

        public string DisplayName { get; }

        internal PevtActorRegistrationContext(string owner, string displayName)
        {
            Owner = owner ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Owner : displayName;
        }

        /// <param name="catalogHash">`.pactor` 源文件的内容哈希，只用于冲突报告。</param>
        /// <param name="visualAccessors">
        /// 延迟资源访问器，键为 <c>&lt;actorId&gt;/&lt;visualKey&gt;</c>，值是 <c>Func&lt;object&gt;</c>——
        /// Core 看不到 <c>PxlsCharacterHandle</c> 或 <c>MImage</c>，只能原样存着交给游戏侧适配器取用。
        /// 注册期绝不调用这些访问器，扫描期不得触发资源加载。
        /// </param>
        public void Register(
            ActorCatalog catalog,
            string catalogHash = null,
            IReadOnlyDictionary<string, Func<object>> visualAccessors = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            _submitted.Add(new ActorCatalogSubmission(catalog, catalogHash ?? string.Empty, visualAccessors));
        }

        /// <summary>
        /// 提交一个人物目录增量扩展（PEVT-E06）。扩展只往**已登记**人物上追加 appearance，
        /// 因此它可以引用别的程序集注册的人物，也因此必须等全部目录都登记完才能判定目标是否存在。
        /// </summary>
        /// <param name="sourceHash">sidecar 源文件的内容哈希，只用于来源追踪。</param>
        public void RegisterExtension(ActorCatalogExtension extension, string sourceHash = null)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));
            _submittedExtensions.Add(new ActorExtensionSubmission(extension, sourceHash ?? string.Empty));
        }

        internal IReadOnlyList<ActorCatalogSubmission> Submitted =>
            new ReadOnlyCollection<ActorCatalogSubmission>(_submitted);

        internal IReadOnlyList<ActorExtensionSubmission> SubmittedExtensions =>
            new ReadOnlyCollection<ActorExtensionSubmission>(_submittedExtensions);
    }

    internal sealed class ActorExtensionSubmission
    {
        public ActorCatalogExtension Extension { get; }

        public string SourceHash { get; }

        public ActorExtensionSubmission(ActorCatalogExtension extension, string sourceHash)
        {
            Extension = extension;
            SourceHash = sourceHash ?? string.Empty;
        }
    }

    internal sealed class ActorCatalogSubmission
    {
        public ActorCatalog Catalog { get; }

        public string CatalogHash { get; }

        public IReadOnlyDictionary<string, Func<object>> VisualAccessors { get; }

        public ActorCatalogSubmission(
            ActorCatalog catalog,
            string catalogHash,
            IReadOnlyDictionary<string, Func<object>> visualAccessors = null)
        {
            Catalog = catalog;
            CatalogHash = catalogHash;
            VisualAccessors = visualAccessors ?? new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        }
    }
}
