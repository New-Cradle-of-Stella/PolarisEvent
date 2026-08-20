using System;
using System.Collections.Generic;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 组合处理器共用的参数域与 ID 验证。
    /// </summary>
    public static class PevtArgumentDomains
    {
        /// <summary><c>opacity</c>、<c>volume</c>、<c>scale</c> 的常规范围。</summary>
        public const float UnitMinimum = 0f;

        public const float UnitMaximum = 1f;

        /// <summary>抛出 <see cref="PevtRoutineFailureException"/>，由调度边界转成运行诊断。</summary>
        private static PevtRoutineFailureException Invalid(string message) =>
            new PevtRoutineFailureException("PEVTR4001", message);

        /// <summary><c>frames</c>、<c>durationFrames</c>、<c>fadeFrames</c> 不能为负数。</summary>
        public static int RequireFrames(int frames, string parameterName)
        {
            if (frames < 0)
                throw Invalid($"`{parameterName}` 不能为负数，实际为 {frames}。");
            return frames;
        }

        /// <summary>只有显式写明为超时的参数才允许用 <c>0</c> 表示无超时。</summary>
        public static int RequireTimeoutFrames(int frames, string parameterName)
        {
            if (frames < 0)
                throw Invalid($"`{parameterName}` 不能为负数，实际为 {frames}。");
            return frames;
        }

        /// <summary><c>0.0</c>–<c>1.0</c> 的归一化值。越界不截断，直接失败。</summary>
        public static float RequireUnitRange(float value, string parameterName)
        {
            if (float.IsNaN(value) || value < UnitMinimum || value > UnitMaximum)
                throw Invalid($"`{parameterName}` 必须位于 {UnitMinimum}–{UnitMaximum} 之间，实际为 {value}。");
            return value;
        }

        /// <summary>坐标、缩放和角度必须是有限值。</summary>
        public static float RequireFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw Invalid($"`{parameterName}` 必须是有限值，实际为 {value}。");
            return value;
        }

        /// <summary><c>easing</c> 第一版只接受规范固定的四个取值。</summary>
        public static string RequireEasing(string easing)
        {
            IReadOnlyList<string> allowed = ParameterDomain.Easing.ClosedValues;
            foreach (string candidate in allowed)
            {
                if (string.Equals(candidate, easing, StringComparison.Ordinal))
                    return easing;
            }

            throw Invalid($"`easing` 只接受 {string.Join("、", allowed)}，实际为 `{easing}`。");
        }

        /// <summary>
        /// <c>color</c> 使用 <c>#RRGGBB</c> 或 <c>#RRGGBBAA</c>，也接受 PolarisEvent 登记的颜色名。
        /// 颜色名表由适配器提供；这里只在两者都不匹配时失败。
        /// </summary>
        public static string RequireColor(string color, Func<string, bool> isRegisteredName = null)
        {
            if (ActorColor.TryParse(color, out ActorColor _))
                return color;
            if (isRegisteredName != null && isRegisteredName(color))
                return color;

            throw Invalid($"`color` 必须是 `#RRGGBB`/`#RRGGBBAA` 或已登记的颜色名，实际为 `{color}`。");
        }

        /// <summary>非空字符串 ID。</summary>
        public static string RequireId(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                throw Invalid($"`{parameterName}` 不能为空。");
            return value;
        }

        /// <summary>
        /// 镜头目标（PEVT-E02）。这里只判"写法合不合法"——目标在当前地图上是否存在是运行期事实，
        /// 由 <c>Camera.ResolveTarget</c> 回答，报的是 PEVTR4601 而不是参数错。
        /// </summary>
        public static PevtCameraTarget RequireCameraTarget(string targetId)
        {
            if (PevtCameraTarget.TryParse(targetId, out PevtCameraTarget target))
                return target;

            throw Invalid(
                $"`targetId` 只接受 `{PevtCameraTarget.PlayerTarget}`、`{PevtCameraTarget.PointTarget}`、"
                + $"`{PevtCameraTarget.EntityPrefix}<key>`、`{PevtCameraTarget.AnchorPrefix}<id>` 或不带前缀的标签点 ID，"
                + $"实际为 `{targetId}`。");
        }

        // ---- 人物目录解析 ----

        /// <summary>解析人物；目录中不存在时 PEVTR4401。</summary>
        public static ActorRegistration RequireActor(PevtRoutineContext context, string actorId)
        {
            RequireId(actorId, "actorId");

            IPevtActorCatalogService catalog = context.Services.ActorCatalog
                ?? throw Invalid("当前宿主没有提供人物目录服务。");

            if (!catalog.TryResolve(actorId, out ActorRegistration registration))
                throw new PevtRoutineFailureException("PEVTR4401", $"全局人物目录中不存在人物 `{actorId}`。");

            return registration;
        }

        /// <summary>解析 appearance；人物存在但未登记该外观时 PEVTR4402。</summary>
        public static ActorAppearance RequireAppearance(PevtRoutineContext context, ActorRegistration actor, string appearanceId)
        {
            RequireId(appearanceId, "appearanceId");

            if (actor.Actor.TryGetAppearance(appearanceId, out ActorAppearance appearance))
                return appearance;

            // 没有登记语义 appearance 时允许直接用 portrait ID，这样纯立绘切换不必先造一个同名 appearance。
            if (actor.Actor.TryGetPortrait(appearanceId, out ActorVisual _))
                return null;

            throw new PevtRoutineFailureException("PEVTR4402",
                $"人物 `{actor.ActorId}` 没有登记 appearance 或 portrait `{appearanceId}`。");
        }

        /// <summary>解析站位：内置语义锚点或人物专用 anchor；都不是时 PEVTR4402。</summary>
        public static void RequireAnchor(PevtRoutineContext context, ActorRegistration actor, string anchorId)
        {
            RequireId(anchorId, "position");

            IPevtActorCatalogService catalog = context.Services.ActorCatalog
                ?? throw Invalid("当前宿主没有提供人物目录服务。");

            if (!catalog.TryResolveAnchor(actor.ActorId, anchorId, out ActorAnchor _))
                throw new PevtRoutineFailureException("PEVTR4402", $"人物 `{actor.ActorId}` 没有站位 `{anchorId}`。");
        }

        // ---- 服务可用性 ----

        public static T RequireService<T>(T service, string name) where T : class =>
            service ?? throw Invalid($"当前宿主没有提供 `{name}` 服务。");
    }
}
