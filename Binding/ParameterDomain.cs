using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 参数域：一个 <c>@</c> 形参在其普通类型之上的取值语义，例如"这是一个人物 ID"。
    ///
    /// 参数域不是 PEVT 类型——语言的普通类型仍然只有 int、float、bool、char、string（实现总纲全局不变量）。
    /// 它只影响工具补全、快速信息与可选的静态提示，不改变绑定阶段的类型判定。
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
        /// 规范已经固定死的封闭取值集；null 表示取值集由处理器在运行期登记，静态侧看不全。
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

        /// <summary>缓动曲线。第一版取值集由规范固定（内置事件语句表「参数域」一节）。</summary>
        public static ParameterDomain Easing { get; } = new ParameterDomain("easing", PevtType.String, false,
            new[] { "linear", "ease_in", "ease_out", "ease_in_out" });

        /// <summary>`#RRGGBB` / `#RRGGBBAA`，或 PolarisEvent 登记的颜色名。</summary>
        public static ParameterDomain Color { get; } = new ParameterDomain("color", PevtType.String, false);

        /// <summary>受管图层 ID。</summary>
        public static ParameterDomain LayerId { get; } = new ParameterDomain("layer-id", PevtType.String, false);

        /// <summary>资源 ID（图像、CG、剪影等）。</summary>
        public static ParameterDomain AssetId { get; } = new ParameterDomain("asset-id", PevtType.String, false);

        public override string ToString() => Name;
    }
}
