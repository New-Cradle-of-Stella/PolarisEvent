using System;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 标准缓动函数全集（<c>easings.net</c> 那一套业界通用公式）：sine/quad/cubic/quart/quint/expo/circ/back/elastic/bounce
    /// 十个族，每族 in/out/in-out 三种。全部只接受归一化输入 <c>t∈[0,1]</c>，不引用 Unity 或原版类型，
    /// 因此和原版 <c>XX.X</c> 的反射式缓动完全独立——<see cref="PevtTweenWait"/> 用它们直接在 C# 里逐帧插值。
    /// </summary>
    internal static class PevtStandardEasing
    {
        // ---- Sine ----
        public static float InSine(float t) => 1f - (float)Math.Cos(t * Math.PI / 2);
        public static float OutSine(float t) => (float)Math.Sin(t * Math.PI / 2);
        public static float InOutSine(float t) => -((float)Math.Cos(Math.PI * t) - 1f) / 2f;

        // ---- Quad（等价于既有 ease_in/ease_out/ease_in_out，独立注册供显式按族选择） ----
        public static float InQuad(float t) => t * t;
        public static float OutQuad(float t) => 1f - Pow2(1f - t);
        public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Pow2(-2f * t + 2f) / 2f;

        // ---- Cubic ----
        public static float InCubic(float t) => t * t * t;
        public static float OutCubic(float t) => 1f - Pow3(1f - t);
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Pow3(-2f * t + 2f) / 2f;

        // ---- Quart ----
        public static float InQuart(float t) => Pow4(t);
        public static float OutQuart(float t) => 1f - Pow4(1f - t);
        public static float InOutQuart(float t) => t < 0.5f ? 8f * Pow4(t) : 1f - Pow4(-2f * t + 2f) / 2f;

        // ---- Quint ----
        public static float InQuint(float t) => Pow5(t);
        public static float OutQuint(float t) => 1f - Pow5(1f - t);
        public static float InOutQuint(float t) => t < 0.5f ? 16f * Pow5(t) : 1f - Pow5(-2f * t + 2f) / 2f;

        // ---- Expo ----
        public static float InExpo(float t) => t <= 0f ? 0f : (float)Math.Pow(2, 10 * t - 10);
        public static float OutExpo(float t) => t >= 1f ? 1f : 1f - (float)Math.Pow(2, -10 * t);

        public static float InOutExpo(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? (float)Math.Pow(2, 20 * t - 10) / 2f
                : (2f - (float)Math.Pow(2, -20 * t + 10)) / 2f;
        }

        // ---- Circ ----
        public static float InCirc(float t) => 1f - (float)Math.Sqrt(1 - Pow2(t));
        public static float OutCirc(float t) => (float)Math.Sqrt(1 - Pow2(t - 1f));

        public static float InOutCirc(float t) => t < 0.5f
            ? (1f - (float)Math.Sqrt(1 - Pow2(2f * t))) / 2f
            : ((float)Math.Sqrt(1 - Pow2(-2f * t + 2f)) + 1f) / 2f;

        // ---- Back ----
        private const float BackC1 = 1.70158f;
        private const float BackC2 = BackC1 * 1.525f;
        private const float BackC3 = BackC1 + 1f;

        public static float InBack(float t) => BackC3 * t * t * t - BackC1 * t * t;
        public static float OutBack(float t) => 1f + BackC3 * Pow3(t - 1f) + BackC1 * Pow2(t - 1f);

        public static float InOutBack(float t) => t < 0.5f
            ? Pow2(2f * t) * ((BackC2 + 1f) * 2f * t - BackC2) / 2f
            : (Pow2(2f * t - 2f) * ((BackC2 + 1f) * (t * 2f - 2f) + BackC2) + 2f) / 2f;

        // ---- Elastic ----
        private static readonly float ElasticC4 = (float)(2 * Math.PI / 3);
        private static readonly float ElasticC5 = (float)(2 * Math.PI / 4.5);

        public static float InElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return -(float)Math.Pow(2, 10 * t - 10) * (float)Math.Sin((t * 10 - 10.75) * ElasticC4);
        }

        public static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return (float)Math.Pow(2, -10 * t) * (float)Math.Sin((t * 10 - 0.75) * ElasticC4) + 1f;
        }

        public static float InOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? -((float)Math.Pow(2, 20 * t - 10) * (float)Math.Sin((20 * t - 11.125) * ElasticC5)) / 2f
                : (float)Math.Pow(2, -20 * t + 10) * (float)Math.Sin((20 * t - 11.125) * ElasticC5) / 2f + 1f;
        }

        // ---- Bounce ----
        public static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
                return n1 * t * t;

            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        public static float InBounce(float t) => 1f - OutBounce(1f - t);

        public static float InOutBounce(float t) => t < 0.5f
            ? (1f - OutBounce(1f - 2f * t)) / 2f
            : (1f + OutBounce(2f * t - 1f)) / 2f;

        private static float Pow2(float v) => v * v;
        private static float Pow3(float v) => v * v * v;
        private static float Pow4(float v) { float square = v * v; return square * square; }
        private static float Pow5(float v) { float square = v * v; return square * square * v; }
    }
}
