using System;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>PolarisRes 字段允许承载的资源类型。第一版只有两种。</summary>
    public enum ActorResourceType
    {
        /// <summary>PXLS 角色句柄，用于 WorldSprite 与 Portrait。</summary>
        PxlsCharacterHandle,

        /// <summary>UI 图片，用于 UiPortrait 与 Icon。</summary>
        MImage,
    }

    /// <summary>
    /// 一个 PolarisRes 静态资源字段的中立描述。Core 是 netstandard 前端，不引用游戏程序集也不做反射，
    /// 字段事实由调用方（工具侧源码扫描、游戏侧注册器）填好后传进来，判定规则只有这一份。
    /// </summary>
    public sealed class ActorResourceFieldInfo
    {
        /// <summary>字段的实际类型名。可以是简单名或全名，判定时按最后一段比较。</summary>
        public string FieldTypeName { get; }

        public bool IsStatic { get; }

        /// <summary>字段可见性是否允许自动绑定（public 或对 PolarisRes 可见的 internal）。</summary>
        public bool IsAccessible { get; }

        /// <summary>字段是否带 <c>[PolarisResource]</c>。</summary>
        public bool HasResourceAttribute { get; }

        /// <summary>字段所属类型是否带 <c>[PolarisResourceFolder]</c>。</summary>
        public bool DeclaringTypeHasResourceFolderAttribute { get; }

        public ActorResourceFieldInfo(
            string fieldTypeName,
            bool isStatic,
            bool isAccessible,
            bool hasResourceAttribute,
            bool declaringTypeHasResourceFolderAttribute)
        {
            FieldTypeName = fieldTypeName ?? string.Empty;
            IsStatic = isStatic;
            IsAccessible = isAccessible;
            HasResourceAttribute = hasResourceAttribute;
            DeclaringTypeHasResourceFolderAttribute = declaringTypeHasResourceFolderAttribute;
        }
    }

    /// <summary>
    /// `.pactor` 资源字段的绑定判定：类型是否匹配视觉分类（PEVT9111）、字段是否满足自动绑定条件
    /// （PEVT9117），以及编辑器暂时读不到字段时的降级警告（PEVT9118）。
    /// </summary>
    public static class ActorResourceBinding
    {
        private const string TypeMismatch = "PEVT9111";
        private const string FieldNotBindable = "PEVT9117";
        private const string SourceUnavailable = "PEVT9118";

        public const string ResourceAttributeName = "PolarisResource";
        public const string ResourceFolderAttributeName = "PolarisResourceFolder";

        public const string PxlsCharacterHandleTypeName = "PxlsCharacterHandle";
        public const string MImageTypeName = "MImage";

        /// <summary>某个视觉分类要求的资源类型。</summary>
        public static ActorResourceType GetRequiredResourceType(ActorVisualKind kind)
        {
            switch (kind)
            {
                case ActorVisualKind.WorldSprite:
                case ActorVisualKind.Portrait:
                    return ActorResourceType.PxlsCharacterHandle;
                case ActorVisualKind.UiPortrait:
                case ActorVisualKind.Icon:
                    return ActorResourceType.MImage;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的视觉分类。");
            }
        }

        public static string GetTypeName(ActorResourceType type) =>
            type == ActorResourceType.PxlsCharacterHandle ? PxlsCharacterHandleTypeName : MImageTypeName;

        /// <summary>
        /// 校验一条 <c>polaris-res</c> 引用。<paramref name="field"/> 为 null 表示当前上下文暂时解析不到该字段，
        /// 只降级为 PEVT9118 警告；<c>game-pxls</c> 引用不走本判定，原版资源的存在性由游戏侧资源桥在运行期确认。
        /// </summary>
        public static void Validate(
            ActorVisualResource resource,
            ActorVisualKind kind,
            ActorResourceFieldInfo field,
            TextLocation location,
            DiagnosticBag diagnostics)
        {
            if (resource == null)
                throw new ArgumentNullException(nameof(resource));
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            if (resource.Provider != ActorVisualProvider.PolarisRes)
                return;

            if (field == null)
            {
                diagnostics.AddWarning(
                    SourceUnavailable,
                    $"暂时无法解析资源字段 `{resource.FieldReference}`，本次不校验其类型与绑定条件。",
                    location);
                return;
            }

            if (!field.IsStatic)
            {
                diagnostics.AddError(
                    FieldNotBindable,
                    $"资源字段 `{resource.FieldReference}` 不是 static，无法自动绑定。",
                    location);
            }

            if (!field.IsAccessible)
            {
                diagnostics.AddError(
                    FieldNotBindable,
                    $"资源字段 `{resource.FieldReference}` 的可见性不允许自动绑定。",
                    location);
            }

            if (!field.HasResourceAttribute)
            {
                diagnostics.AddError(
                    FieldNotBindable,
                    $"资源字段 `{resource.FieldReference}` 缺少 `[{ResourceAttributeName}]`。",
                    location);
            }

            if (!field.DeclaringTypeHasResourceFolderAttribute)
            {
                diagnostics.AddError(
                    FieldNotBindable,
                    $"资源字段 `{resource.FieldReference}` 所属类型缺少 `[{ResourceFolderAttributeName}]`。",
                    location);
            }

            ActorResourceType required = GetRequiredResourceType(kind);
            if (!MatchesTypeName(field.FieldTypeName, GetTypeName(required)))
            {
                diagnostics.AddError(
                    TypeMismatch,
                    $"资源字段 `{resource.FieldReference}` 的类型是 `{field.FieldTypeName}`，但 {kind} 要求 `{GetTypeName(required)}`。",
                    location);
            }
        }

        /// <summary>按类型名最后一段比较，这样 `Polaris.Res.MImage` 与 `MImage` 都能匹配，同时不接受 `MyMImage`。</summary>
        private static bool MatchesTypeName(string actual, string expected)
        {
            if (string.IsNullOrEmpty(actual))
                return false;

            int lastDot = actual.LastIndexOf('.');
            string simple = lastDot < 0 ? actual : actual.Substring(lastDot + 1);
            return string.Equals(simple, expected, StringComparison.Ordinal);
        }
    }
}
