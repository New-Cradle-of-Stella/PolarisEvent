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
                default:
                    return t => t;
            }
        }
    }
}
