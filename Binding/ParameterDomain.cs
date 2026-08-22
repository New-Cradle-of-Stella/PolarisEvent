using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 参数域：一个 <c>@</c> 形参在其普通类型之上的取值语义，例如"这是一个人物 ID"。
    /// </summary>
    public sealed class ParameterDomain
    {
        /// <summary>域名，用于诊断消息与工具展示。</summary>
        public string Name { get; }

        /// <summary>域所依附的普通类型。</summary>
        public PevtType UnderlyingType { get; }

        /// <summary>
        /// 取值不在已知集合中时是否构成静态错误。人物、外观与站位的存在性通常是运行期事实
        /// （跨模组人物可以晚于当前 `.pevt` 所在项目注册），因此这三个域一律为 false：
        /// 未知值只影响补全，不产生静态错误。
        /// </summary>
        public bool RejectsUnknownValues { get; }

        /// <summary>
        /// 已经固定死的封闭取值集；null 表示取值集由处理器在运行期登记，静态侧看不全。
        /// 即使是封闭集，未登记值也只是运行时参数错误，不是静态诊断——见 <see cref="RejectsUnknownValues"/>。
        /// </summary>
        public IReadOnlyList<string> ClosedValues { get; }

        public ParameterDomain(string name, PevtType underlyingType, bool rejectsUnknownValues, IEnumerable<string> closedValues = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("参数域名称不能为空。", nameof(name));
            if (!underlyingType.IsOrdinaryType())
                throw new ArgumentException("参数域只能依附于五种普通类型之一。", nameof(underlyingType));

            Name = name;
            UnderlyingType = underlyingType;
            RejectsUnknownValues = rejectsUnknownValues;

            if (closedValues != null)
            {
                var copy = new List<string>(closedValues);
                ClosedValues = new ReadOnlyCollection<string>(copy);
            }
        }

        /// <summary>最终人物 ID <c>&lt;namespace&gt;:&lt;local-id&gt;</c>。</summary>
        public static ParameterDomain ActorId { get; } = new ParameterDomain("actor-id", PevtType.String, false);

        /// <summary>人物内已登记的 appearance ID。</summary>
        public static ParameterDomain ActorAppearance { get; } = new ParameterDomain("actor-appearance", PevtType.String, false);

        /// <summary>内置语义站位名或人物专用 anchor ID。</summary>
        public static ParameterDomain ActorAnchor { get; } = new ParameterDomain("actor-anchor", PevtType.String, false);

        /// <summary>人物内已登记的 portrait ID。</summary>
        public static ParameterDomain ActorPortrait { get; } = new ParameterDomain("actor-portrait", PevtType.String, false);

        /// <summary>人物内已登记的 UI portrait ID。</summary>
        public static ParameterDomain ActorUiPortrait { get; } = new ParameterDomain("actor-ui-portrait", PevtType.String, false);

        /// <summary>表情符号 ID。来自 PolarisEvent 的通用登记表，`.pactor` 不定义表情。</summary>
        public static ParameterDomain ActorEmote { get; } = new ParameterDomain("actor-emote", PevtType.String, false);

        /// <summary>
        /// 缓动曲线：<c>zpow</c>（PEVT-E04）数值上与 <c>ease_in</c> 相同，但独立登记，不当作其别名。
        /// 其余全部是 <c>easings.net</c> 标准缓动函数全集，曲线实现见 <c>Game/PevtStandardEasing.cs</c>。
        /// </summary>
        public static ParameterDomain Easing { get; } = new ParameterDomain("easing", PevtType.String, false, new[]
        {
            "linear", "ease_in", "ease_out", "ease_in_out", "zpow",

            "ease_in_sine", "ease_out_sine", "ease_in_out_sine",
            "ease_in_quad", "ease_out_quad", "ease_in_out_quad",
            "ease_in_cubic", "ease_out_cubic", "ease_in_out_cubic",
            "ease_in_quart", "ease_out_quart", "ease_in_out_quart",
            "ease_in_quint", "ease_out_quint", "ease_in_out_quint",
            "ease_in_expo", "ease_out_expo", "ease_in_out_expo",
            "ease_in_circ", "ease_out_circ", "ease_in_out_circ",
            "ease_in_back", "ease_out_back", "ease_in_out_back",
            "ease_in_elastic", "ease_out_elastic", "ease_in_out_elastic",
            "ease_in_bounce", "ease_out_bounce", "ease_in_out_bounce",
        });

        /// <summary>`#RRGGBB` / `#RRGGBBAA`，或 PolarisEvent 登记的颜色名。</summary>
        public static ParameterDomain Color { get; } = new ParameterDomain("color", PevtType.String, false);

        /// <summary>受管图层 ID。</summary>
        public static ParameterDomain LayerId { get; } = new ParameterDomain("layer-id", PevtType.String, false);

        /// <summary>资源 ID（图像、CG、剪影等）。</summary>
        public static ParameterDomain AssetId { get; } = new ParameterDomain("asset-id", PevtType.String, false);

        /// <summary>
        /// 直接显示给玩家看的文本，在进入处理器前经 <see cref="Runtime.IPevtLocalization"/> 解析：<c>&amp;</c> 开头按本地化键查表，<c>&amp;&amp;</c> 开头脱转义，其余原样。
        /// 取值不可能"未知"，因此永远不产生静态诊断。
        /// </summary>
        public static ParameterDomain Text { get; } = new ParameterDomain("text", PevtType.String, false);

        /// <summary>
        /// 已登记只读游戏查询键（PEVT-E01）。取值集由宿主在运行期登记，静态侧看不全，
        /// 因此未知键只影响补全，不产生静态错误——真正的判定在 PEVTR4501。
        /// </summary>
        public static ParameterDomain GameQueryKey { get; } = new ParameterDomain("game-query-key", PevtType.String, false);

        /// <summary>
        /// 镜头目标（PEVT-E02）：<c>player</c>、<c>point</c>、<c>entity:&lt;key&gt;</c>、<c>anchor:&lt;id&gt;</c>，
        /// 以及兼容旧源码的裸标签点 ID。前缀是封闭集，但具体实体键与标签点属于地图运行期事实，
        /// 因此这个域不产生静态错误——解析规则见 <see cref="Runtime.PevtCameraTarget"/>。
        /// </summary>
        public static ParameterDomain CameraTarget { get; } = new ParameterDomain("camera-target", PevtType.String, false);

        public override string ToString() => Name;
    }
}
