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

        public string Owner { get; }

        public string DisplayName { get; }

        internal PevtActorRegistrationContext(string owner, string displayName)
        {
            Owner = owner ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Owner : displayName;
        }

        /// <summary><paramref name="catalogHash"/> 是 `.pactor` 源文件的内容哈希，只用于冲突报告。</summary>
        public void Register(ActorCatalog catalog, string catalogHash = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            _submitted.Add(new ActorCatalogSubmission(catalog, catalogHash ?? string.Empty));
        }

        internal IReadOnlyList<ActorCatalogSubmission> Submitted =>
            new ReadOnlyCollection<ActorCatalogSubmission>(_submitted);
    }

    internal sealed class ActorCatalogSubmission
    {
        public ActorCatalog Catalog { get; }

        public string CatalogHash { get; }

        public ActorCatalogSubmission(ActorCatalog catalog, string catalogHash)
        {
            Catalog = catalog;
            CatalogHash = catalogHash;
        }
    }
}
