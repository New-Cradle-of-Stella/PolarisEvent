using System;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// <c>@camera_move</c> 的 <c>targetId</c> 种类（PEVT-E02）。
    /// </summary>
    public enum PevtCameraTargetKind
    {
        /// <summary>跟随玩家。<c>x</c>/<c>y</c> 不参与定位。</summary>
        Player,

        /// <summary>使用显式坐标。</summary>
        Point,

        /// <summary>按地图实体键解析并跟随该实体。</summary>
        Entity,

        /// <summary>按地图标签点解析。</summary>
        Anchor,
    }

    /// <summary>
    /// 解析好的镜头目标。
    ///
    /// 解析规则放在 Core 而不是游戏适配器里，是因为"哪些写法合法"是语言的事：
    /// 组合处理器要按它报参数错，适配器要按它决定用哪条原版入口，两边必须是同一份判定，
    /// 否则会出现"静态/组合层放行、适配器当成别的东西"这种最难查的错。
    /// </summary>
    public readonly struct PevtCameraTarget
    {
        /// <summary>地图实体目标前缀。</summary>
        public const string EntityPrefix = "entity:";

        /// <summary>地图标签点目标前缀。</summary>
        public const string AnchorPrefix = "anchor:";

        /// <summary>跟随玩家的固定写法。</summary>
        public const string PlayerTarget = "player";

        /// <summary>使用显式坐标的固定写法。</summary>
        public const string PointTarget = "point";

        private PevtCameraTarget(PevtCameraTargetKind kind, string key, bool isLegacyAnchor)
        {
            Kind = kind;
            Key = key;
            IsLegacyAnchor = isLegacyAnchor;
        }

        public PevtCameraTargetKind Kind { get; }

        /// <summary>实体键或标签点 ID；<c>player</c>/<c>point</c> 时为空串。</summary>
        public string Key { get; }

        /// <summary>
        /// 是否是不带前缀的旧写法。旧 PEVT 源码里的裸标签点必须继续可用，
        /// 但新代码应该显式写 <c>anchor:</c>，所以这一位保留下来供工具提示。
        /// </summary>
        public bool IsLegacyAnchor { get; }

        /// <summary>
        /// 解析一个 <c>targetId</c>。
        ///
        /// 空串、已知前缀后面没有键、以及带未知前缀（形如 <c>foo:bar</c>）都判为不合法：
        /// 未知前缀几乎一定是 <c>entity:</c>/<c>anchor:</c> 的拼写错误，静默当成标签点会让
        /// 镜头默默停在原地，比报错难查得多。
        /// </summary>
        public static bool TryParse(string targetId, out PevtCameraTarget target)
        {
            target = default(PevtCameraTarget);
            if (string.IsNullOrEmpty(targetId))
                return false;

            if (string.Equals(targetId, PlayerTarget, StringComparison.Ordinal))
            {
                target = new PevtCameraTarget(PevtCameraTargetKind.Player, string.Empty, false);
                return true;
            }

            if (string.Equals(targetId, PointTarget, StringComparison.Ordinal))
            {
                target = new PevtCameraTarget(PevtCameraTargetKind.Point, string.Empty, false);
                return true;
            }

            if (targetId.StartsWith(EntityPrefix, StringComparison.Ordinal))
                return TryPrefixed(PevtCameraTargetKind.Entity, targetId, EntityPrefix.Length, out target);

            if (targetId.StartsWith(AnchorPrefix, StringComparison.Ordinal))
                return TryPrefixed(PevtCameraTargetKind.Anchor, targetId, AnchorPrefix.Length, out target);

            if (targetId.IndexOf(':') >= 0)
                return false;

            target = new PevtCameraTarget(PevtCameraTargetKind.Anchor, targetId, true);
            return true;
        }

        private static bool TryPrefixed(PevtCameraTargetKind kind, string targetId, int prefixLength, out PevtCameraTarget target)
        {
            target = default(PevtCameraTarget);

            string key = targetId.Substring(prefixLength);
            if (key.Length == 0)
                return false;

            target = new PevtCameraTarget(kind, key, false);
            return true;
        }

        public override string ToString() => Kind switch
        {
            PevtCameraTargetKind.Player => PlayerTarget,
            PevtCameraTargetKind.Point => PointTarget,
            PevtCameraTargetKind.Entity => EntityPrefix + Key,
            _ => AnchorPrefix + Key,
        };
    }
}
