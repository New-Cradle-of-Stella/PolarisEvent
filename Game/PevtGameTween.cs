using System;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 一段由 PEVT 自己推进的数值补间。
    /// </summary>
    internal sealed class PevtTweenWait : PevtWait
    {
        private readonly Action<float> _apply;
        private readonly Func<float, float> _easing;
        private readonly int _frames;
        private long _startFrame = -1;

        public PevtTweenWait(int frames, string easing, Action<float> apply)
        {
            _frames = frames < 0 ? 0 : frames;
            _easing = Resolve(easing);
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public override string ProgressSource => "图层补间";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_startFrame < 0)
                _startFrame = context.Frame;

            if (_frames == 0)
            {
                Apply(1f);
                CompleteSucceeded();
                return;
            }

            float linear = (float)(context.Frame - _startFrame) / _frames;
            if (linear >= 1f)
            {
                Apply(1f);
                CompleteSucceeded();
                return;
            }

            Apply(_easing(linear));
        }

        private void Apply(float t) => PevtGameHost.Guard("Tween", () => _apply(t));

        /// <summary>
        /// 除了历史遗留的四个通用名字（linear/ease_in/ease_out/ease_in_out，等价于 quad 族）和原版核对过的
        /// <c>zpow</c>，其余全部是 <c>easings.net</c> 标准缓动函数全集——sine/quad/cubic/quart/quint/expo/circ/back/elastic/bounce
        /// 十个族各三种（in/out/in-out），实现见 <see cref="PevtStandardEasing"/>。
        /// </summary>
        private static Func<float, float> Resolve(string easing)
        {
            switch (easing)
            {
                case "ease_in":
                    return t => t * t;
                case "ease_out":
                    return t => 1f - (1f - t) * (1f - t);
                case "ease_in_out":
                    return t => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                case "zpow":
                    // 与 ease_in 同值（原版 XX.X.ZPOW(t) = clamp01(t)²），但按规范独立登记。
                    return t => t * t;

                case "ease_in_sine": return PevtStandardEasing.InSine;
                case "ease_out_sine": return PevtStandardEasing.OutSine;
                case "ease_in_out_sine": return PevtStandardEasing.InOutSine;

                case "ease_in_quad": return PevtStandardEasing.InQuad;
                case "ease_out_quad": return PevtStandardEasing.OutQuad;
                case "ease_in_out_quad": return PevtStandardEasing.InOutQuad;

                case "ease_in_cubic": return PevtStandardEasing.InCubic;
                case "ease_out_cubic": return PevtStandardEasing.OutCubic;
                case "ease_in_out_cubic": return PevtStandardEasing.InOutCubic;

                case "ease_in_quart": return PevtStandardEasing.InQuart;
                case "ease_out_quart": return PevtStandardEasing.OutQuart;
                case "ease_in_out_quart": return PevtStandardEasing.InOutQuart;

                case "ease_in_quint": return PevtStandardEasing.InQuint;
                case "ease_out_quint": return PevtStandardEasing.OutQuint;
                case "ease_in_out_quint": return PevtStandardEasing.InOutQuint;

                case "ease_in_expo": return PevtStandardEasing.InExpo;
                case "ease_out_expo": return PevtStandardEasing.OutExpo;
                case "ease_in_out_expo": return PevtStandardEasing.InOutExpo;

                case "ease_in_circ": return PevtStandardEasing.InCirc;
                case "ease_out_circ": return PevtStandardEasing.OutCirc;
                case "ease_in_out_circ": return PevtStandardEasing.InOutCirc;

                case "ease_in_back": return PevtStandardEasing.InBack;
                case "ease_out_back": return PevtStandardEasing.OutBack;
                case "ease_in_out_back": return PevtStandardEasing.InOutBack;

                case "ease_in_elastic": return PevtStandardEasing.InElastic;
                case "ease_out_elastic": return PevtStandardEasing.OutElastic;
                case "ease_in_out_elastic": return PevtStandardEasing.InOutElastic;

                case "ease_in_bounce": return PevtStandardEasing.InBounce;
                case "ease_out_bounce": return PevtStandardEasing.OutBounce;
                case "ease_in_out_bounce": return PevtStandardEasing.InOutBounce;

                default:
                    return t => t;
            }
        }
    }
}
