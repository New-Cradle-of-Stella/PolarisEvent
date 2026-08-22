using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 画面、镜头与后处理组合。
    /// </summary>
    internal static class ScreenRoutines
    {
        private static IPevtScreen Screen(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Screen, "Screen");

        private static IPevtCamera Camera(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Camera, "Camera");

        private static IPevtEffect Effect(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Effect, "Effect");

        /// <summary>`EnsureFadeLayer` → `SetFadeColor` → `FadeTo` → 透明度为零时 `ReleaseFadeLayer`。</summary>
        public static IEnumerator<PevtWait> ScreenFade(PevtRoutineContext context, PevtArguments args)
        {
            IPevtScreen screen = Screen(context);
            string color = PevtArgumentDomains.RequireColor(args.String(0));
            float opacity = PevtArgumentDomains.RequireUnitRange(args.Float(1), "opacity");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(3));

            screen.EnsureFadeLayer();
            context.Services.Session.RegisterRestore("ReleaseFadeLayer", screen.ReleaseFadeLayer);
            screen.SetFadeColor(color);

            yield return screen.FadeTo(opacity, frames, easing);

            if (opacity == 0f)
                screen.ReleaseFadeLayer();
        }

        public static IEnumerator<PevtWait> ScreenFlash(PevtRoutineContext context, PevtArguments args)
        {
            IPevtScreen screen = Screen(context);
            string color = PevtArgumentDomains.RequireColor(args.String(0));
            int delayFrames = PevtArgumentDomains.RequireFrames(args.Int(1), "delayFrames");
            int holdFrames = PevtArgumentDomains.RequireFrames(args.Int(2), "holdFrames");
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(3), "fadeFrames");

            yield return screen.Flash(color, delayFrames, holdFrames, fadeFrames);
        }

        public static IEnumerator<PevtWait> CameraShake(PevtRoutineContext context, PevtArguments args)
        {
            IPevtCamera camera = Camera(context);
            float amplitude = PevtArgumentDomains.RequireFinite(args.Float(0), "amplitude");
            int durationFrames = PevtArgumentDomains.RequireFrames(args.Int(1), "durationFrames");
            float frequency = PevtArgumentDomains.RequireFinite(args.Float(2), "frequency");

            RegisterCameraRestore(context, camera);
            yield return camera.Shake(amplitude, durationFrames, frequency);
        }

        /// <summary>
        /// <c>ResolveTarget</c> → 登记一次镜头快照恢复 → <c>MoveTo</c>。
        /// 目标写法由 <see cref="PevtCameraTarget"/> 判定，目标是否存在由适配器回答；
        /// <c>entity:</c> 目标在动作进行中消失时 <c>MoveTo</c> 以 false 结束，这里转成 PEVTR4602。
        /// </summary>
        public static IEnumerator<PevtWait> CameraMove(PevtRoutineContext context, PevtArguments args)
        {
            IPevtCamera camera = Camera(context);
            string targetId = args.String(0);
            PevtCameraTarget target = PevtArgumentDomains.RequireCameraTarget(targetId);
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");
            float zoom = PevtArgumentDomains.RequireFinite(args.Float(3), "zoom");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(4), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(5));

            if (zoom <= 0f)
                throw new PevtRoutineFailureException("PEVTR4001", $"`zoom` 必须为正数，实际为 {zoom}。");

            if (!camera.ResolveTarget(targetId))
                throw new PevtRoutineFailureException("PEVTR4601", $"镜头目标 `{target}` 在当前地图上不存在。");

            // 快照必须在第一次真正改动镜头之前登记好，否则中途失败就没有可恢复的原状。
            RegisterCameraRestore(context, camera);

            PevtWait<bool> move = camera.MoveTo(targetId, x, y, zoom, frames, easing);
            yield return move;

            if (!move.Result)
            {
                throw new PevtRoutineFailureException("PEVTR4602",
                    $"镜头跟随的 `{target}` 在动作进行中消失。");
            }
        }

        public static IEnumerator<PevtWait> CameraReset(PevtRoutineContext context, PevtArguments args)
        {
            IPevtCamera camera = Camera(context);
            int frames = PevtArgumentDomains.RequireFrames(args.Int(0), "frames");

            yield return camera.RestoreEventSnapshot(frames);
        }

        public static IEnumerator<PevtWait> EffectSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEffect effect = Effect(context);
            string effectId = PevtArgumentDomains.RequireId(args.String(0), "effectId");
            bool enabled = args.Bool(1);
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");

            if (!effect.Resolve(effectId))
                throw new PevtRoutineFailureException("PEVTR4001", $"后处理 `{effectId}` 未登记。");

            if (enabled)
                context.Services.Session.RegisterRestore($"Effect.SetEnabled({effectId}, false)", () => effect.SetEnabled(effectId, false, 0));

            yield return effect.SetEnabled(effectId, enabled, frames);
        }

        public static IEnumerator<PevtWait> EffectPulse(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEffect effect = Effect(context);
            string effectId = PevtArgumentDomains.RequireId(args.String(0), "effectId");
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(1), "fadeFrames");
            int holdFrames = PevtArgumentDomains.RequireFrames(args.Int(2), "holdFrames");

            if (!effect.Resolve(effectId))
                throw new PevtRoutineFailureException("PEVTR4001", $"后处理 `{effectId}` 未登记。");

            yield return effect.Pulse(effectId, fadeFrames, holdFrames);
        }

        public static IEnumerator<PevtWait> Spotlight(PevtRoutineContext context, PevtArguments args)
        {
            IPevtScreen screen = Screen(context);
            string targetId = PevtArgumentDomains.RequireId(args.String(0), "targetId");
            bool enabled = args.Bool(1);

            if (!screen.ResolveTarget(targetId))
                throw new PevtRoutineFailureException("PEVTR4001", $"聚光目标 `{targetId}` 无法解析。");

            if (enabled)
                context.Services.Session.RegisterRestore($"Spotlight({targetId}, false)", () => screen.SetSpotlight(targetId, false, args.String(2)));

            screen.SetSpotlight(targetId, enabled, args.String(2));
            yield break;
        }

        /// <summary>镜头快照只需要恢复一次；重复登记会在事件结束时白跑几趟。</summary>
        private static void RegisterCameraRestore(PevtRoutineContext context, IPevtCamera camera)
        {
            const string key = "Camera.RestoreEventSnapshot";
            foreach (string pending in context.Services.Session.PendingRestores)
            {
                if (pending == key)
                    return;
            }

            context.Services.Session.RegisterRestore(key, () => camera.RestoreEventSnapshot(0));
        }
    }
}
