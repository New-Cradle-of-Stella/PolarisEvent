using System;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 一条已校验的人物视觉登记：世界像素角色、事件立绘或 UI 头像。
    /// 三类共用同一形状、只用 <see cref="Kind"/> 区分，避免为纯数据差异造出三套并行类型；构造后深不可变。
    /// </summary>
    public sealed class ActorVisual
    {
        /// <summary>人物内该分类下唯一的局部 ID。<c>WorldSprite</c> 没有 Id 属性，固定为 <c>default</c>。</summary>
        public string Id { get; }

        public ActorVisualKind Kind { get; }

        public ActorVisualResource Resource { get; }

        /// <summary>原版 CMD 短键；只有内置目录可以声明，外部目录声明时读取器发 PEVT9115。null 表示未声明。</summary>
        public string LegacyPerson { get; }

        public ActorVisualLifetime Lifetime { get; }

        /// <summary>该视觉在 `.pactor` 源文件中的位置，供工具跳转与诊断使用；程序化构造时可为 null。</summary>
        public TextLocation Location { get; }

        public ActorVisual(
            string id,
            ActorVisualKind kind,
            ActorVisualResource resource,
            string legacyPerson = null,
            ActorVisualLifetime lifetime = ActorVisualLifetime.Event,
            TextLocation location = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("视觉 ID 不能为空。", nameof(id));

            Id = id;
            Kind = kind;
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            LegacyPerson = legacyPerson;
            Lifetime = lifetime;
            Location = location;
        }

        public override string ToString() => $"{Kind} {Id} -> {Resource}";
    }

    /// <summary>
    /// 可读外观名到 PXLS pose/frame 的数据映射。不执行表达式，也不能调用 C#；
    /// <see cref="PortraitId"/> 必须指向同一人物下已登记的 portrait。
    /// </summary>
    public sealed class ActorAppearance
    {
        public string Id { get; }

        public string PortraitId { get; }

        public string Pose { get; }

        public string Frame { get; }

        public TextLocation Location { get; }

        public ActorAppearance(string id, string portraitId, string pose, string frame, TextLocation location = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("appearance ID 不能为空。", nameof(id));
            if (string.IsNullOrEmpty(portraitId))
                throw new ArgumentException("appearance 必须引用一个 portrait。", nameof(portraitId));
            if (string.IsNullOrEmpty(pose))
                throw new ArgumentException("appearance 必须提供 pose。", nameof(pose));
            if (string.IsNullOrEmpty(frame))
                throw new ArgumentException("appearance 必须提供 frame。", nameof(frame));

            Id = id;
            PortraitId = portraitId;
            Pose = pose;
            Frame = frame;
            Location = location;
        }

        public override string ToString() => $"{Id} -> {PortraitId}/{Pose}/{Frame}";
    }

    /// <summary>
    /// 人物专用站位。坐标由 PolarisEvent 直接保存，不在运行时翻译回原版 <c>L/R/C</c> 组合键再交给原版解释器。
    /// </summary>
    public sealed class ActorAnchor
    {
        public string Id { get; }

        public float X { get; }

        public float Y { get; }

        /// <summary>入场坐标；未声明时为 null，此时入场直接使用 <see cref="X"/>/<see cref="Y"/>。</summary>
        public float? EnterX { get; }

        public float? EnterY { get; }

        public TextLocation Location { get; }

        public ActorAnchor(string id, float x, float y, float? enterX = null, float? enterY = null, TextLocation location = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("anchor ID 不能为空。", nameof(id));
            if (!IsFinite(x) || !IsFinite(y))
                throw new ArgumentException("anchor 坐标必须是有限值。");
            if (enterX.HasValue != enterY.HasValue)
                throw new ArgumentException("EnterX 与 EnterY 必须同时声明或同时省略。");
            if (enterX.HasValue && (!IsFinite(enterX.Value) || !IsFinite(enterY.Value)))
                throw new ArgumentException("anchor 入场坐标必须是有限值。");

            Id = id;
            X = x;
            Y = y;
            EnterX = enterX;
            EnterY = enterY;
            Location = location;
        }

        /// <summary>netstandard2.0 没有 <c>float.IsFinite</c>，这里自己判定 NaN 与无穷。</summary>
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public override string ToString() =>
            EnterX.HasValue ? $"{Id} ({X}, {Y}) enter ({EnterX}, {EnterY})" : $"{Id} ({X}, {Y})";
    }
}
