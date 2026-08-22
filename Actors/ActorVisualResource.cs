using System;

namespace Polaris.Pevt.Actors
{
    /// <summary>视觉资源提供者，第一版只有两类。</summary>
    public enum ActorVisualProvider
    {
        /// <summary>借用游戏 StreamingAssets 内的原版 PXLS Bundle；仅 Polaris 内置目录可用。</summary>
        GamePxls,

        /// <summary>读取模组由 PolarisRes 自动绑定的资源字段；仅自定义目录可用。</summary>
        PolarisRes,
    }

    /// <summary>视觉资源的用途分类，决定 PolarisRes 字段允许的资源类型。</summary>
    public enum ActorVisualKind
    {
        /// <summary>地图像素角色，要求 <c>PxlsCharacterHandle</c>。</summary>
        WorldSprite,

        /// <summary>事件立绘，要求 <c>PxlsCharacterHandle</c>。</summary>
        Portrait,

        /// <summary>UI 头像，要求 <c>MImage</c>。</summary>
        UiPortrait,

        /// <summary>人物图标，要求 <c>MImage</c>。</summary>
        Icon,
    }

    /// <summary>
    /// 借用的原版视觉资源在事件之外的存活方式。原版把 <c>__ev_n</c> 之类的常驻立绘一直保留在内存中，
    /// 而大多数配角立绘随事件加载与释放，两者的清理边界不同，因此在目录里显式记录。
    /// </summary>
    public enum ActorVisualLifetime
    {
        /// <summary>随事件借用，事件结束、替换或异常时释放。缺省值。</summary>
        Event,

        /// <summary>常驻资源，事件结束时不撤销借用。</summary>
        Static,
    }

    /// <summary>
    /// 一条视觉资源引用：<see cref="ActorVisualProvider.GamePxls"/> 保存原版 Bundle 逻辑路径（<c>Asset</c>），
    /// <see cref="ActorVisualProvider.PolarisRes"/> 保存 C# 静态字段引用（<c>Resource</c>）。两者互斥，目录里不存在既是原版借用又是模组字段的资源。
    /// </summary>
    public sealed class ActorVisualResource : IEquatable<ActorVisualResource>
    {
        public ActorVisualProvider Provider { get; }

        /// <summary>原版 Bundle 逻辑路径与 PXLS 名，例如 <c>EvImg/__ev_n.pxls</c>；PolarisRes 引用时为 null。</summary>
        public string Asset { get; }

        /// <summary>PolarisRes 静态字段的点分路径，例如 <c>MyMod.Resources.IrisPortraitPxls</c>；原版借用时为 null。</summary>
        public string FieldReference { get; }

        private ActorVisualResource(ActorVisualProvider provider, string asset, string fieldReference)
        {
            Provider = provider;
            Asset = asset;
            FieldReference = fieldReference;
        }

        public static ActorVisualResource FromGameAsset(string asset)
        {
            if (string.IsNullOrEmpty(asset))
                throw new ArgumentException("原版资源路径不能为空。", nameof(asset));
            return new ActorVisualResource(ActorVisualProvider.GamePxls, asset, null);
        }

        public static ActorVisualResource FromPolarisResField(string fieldReference)
        {
            if (string.IsNullOrEmpty(fieldReference))
                throw new ArgumentException("PolarisRes 字段引用不能为空。", nameof(fieldReference));
            return new ActorVisualResource(ActorVisualProvider.PolarisRes, null, fieldReference);
        }

        /// <summary>字段引用中最后一段之前的部分，即声明该字段的类型全名；原版借用时为 null。</summary>
        public string DeclaringTypeName
        {
            get
            {
                if (FieldReference == null)
                    return null;
                int lastDot = FieldReference.LastIndexOf('.');
                return lastDot <= 0 ? null : FieldReference.Substring(0, lastDot);
            }
        }

        /// <summary>字段引用的最后一段，即字段名；原版借用时为 null。</summary>
        public string FieldName
        {
            get
            {
                if (FieldReference == null)
                    return null;
                int lastDot = FieldReference.LastIndexOf('.');
                return lastDot < 0 ? FieldReference : FieldReference.Substring(lastDot + 1);
            }
        }

        public bool Equals(ActorVisualResource other) =>
            other != null
            && Provider == other.Provider
            && string.Equals(Asset, other.Asset, StringComparison.Ordinal)
            && string.Equals(FieldReference, other.FieldReference, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as ActorVisualResource);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Provider;
                hash = (hash * 397) ^ (Asset?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (FieldReference?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString() =>
            Provider == ActorVisualProvider.GamePxls ? $"game-pxls:{Asset}" : $"polaris-res:{FieldReference}";
    }
}
