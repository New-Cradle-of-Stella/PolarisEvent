using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 把全部 P0 组合处理器登记到指令注册表。
    /// </summary>
    public static class P0CommandRoutines
    {
        private static readonly PevtType Int = PevtType.Int;
        private static readonly PevtType Float = PevtType.Float;
        private static readonly PevtType Bool = PevtType.Bool;
        private static readonly PevtType Str = PevtType.String;

        /// <summary>建立一个登记了全部 P0 处理器的注册表。</summary>
        public static PevtCommandRegistry CreateRegistry(CommandDescriptorCatalog catalog = null)
        {
            var registry = new PevtCommandRegistry(catalog ?? CommandDescriptorCatalog.Builtin);
            RegisterAll(registry);
            return registry;
        }

        public static void RegisterAll(PevtCommandRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            RegisterWaits(registry);
            RegisterDialogue(registry);
            RegisterChoice(registry);
            RegisterActors(registry);
            RegisterImages(registry);
            RegisterScreen(registry);
            RegisterAudio(registry);
            RegisterUi(registry);
        }

        private static void Add(
            PevtCommandRegistry registry,
            string name,
            Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run,
            params PevtType[] parameterTypes) =>
            registry.Register(name, parameterTypes, new PevtDelegateRoutine(run));

        private static void RegisterWaits(PevtCommandRegistry registry)
        {
            Add(registry, "wait", UiRoutines.Wait, Int);
            Add(registry, "wait_input", UiRoutines.WaitInput, Int);
            Add(registry, "wait_motion", UiRoutines.WaitMotion, Int);
            Add(registry, "wait_resources", UiRoutines.WaitResources, Str, Int);
        }

        private static void RegisterDialogue(PevtCommandRegistry registry)
        {
            Add(registry, "say", DialogueRoutines.Say, Str, Str);
            Add(registry, "narrate", DialogueRoutines.Narrate, Str);
            Add(registry, "board", DialogueRoutines.Board, Str, Str);
            Add(registry, "talker_bind", DialogueRoutines.TalkerBind, Str, Str, Str);
            Add(registry, "talker_reset", DialogueRoutines.TalkerReset, Str);
            Add(registry, "dialogue_visible", DialogueRoutines.DialogueVisible, Bool, Bool);
            Add(registry, "dialogue_hold", DialogueRoutines.DialogueHold, Bool);
            Add(registry, "dialogue_auto", DialogueRoutines.DialogueAuto, Bool);
            Add(registry, "dialogue_log", DialogueRoutines.DialogueLog, Bool);
            Add(registry, "skip_enabled", DialogueRoutines.SkipEnabled, Bool);
        }

        private static void RegisterChoice(PevtCommandRegistry registry)
        {
            // 两项、三项、四项签名共用同一个组合，只有实参数量不同。
            Add(registry, "choose", DialogueRoutines.Choose, Str, Str, Str);
            Add(registry, "choose", DialogueRoutines.Choose, Str, Str, Str, Str);
            Add(registry, "choose", DialogueRoutines.Choose, Str, Str, Str, Str, Str);
            Add(registry, "choice_clear", DialogueRoutines.ChoiceClear);
            Add(registry, "choice_add", DialogueRoutines.ChoiceAdd, Str, Str, Bool, Bool);
            Add(registry, "choice_show", DialogueRoutines.ChoiceShow, Str, Str);
        }

        private static void RegisterActors(PevtCommandRegistry registry)
        {
            Add(registry, "actor_enter", ActorRoutines.ActorEnter, Str, Str, Str, Int);
            Add(registry, "actor_exit", ActorRoutines.ActorExit, Str, Int);
            Add(registry, "actor_hide_all", ActorRoutines.ActorHideAll, Int);
            Add(registry, "actor_move", ActorRoutines.ActorMove, Str, Str, Int);
            Add(registry, "actor_move_by", ActorRoutines.ActorMoveBy, Str, Float, Float, Int, Str);
            Add(registry, "actor_appearance", ActorRoutines.ActorAppearance, Str, Str);
            Add(registry, "actor_emote", ActorRoutines.ActorEmote, Str, Str);
            Add(registry, "actor_motion", ActorRoutines.ActorMotion, Str, Str, Int);
            Add(registry, "actor_motion_stop", ActorRoutines.ActorMotionStop, Str);
            Add(registry, "actor_flip", ActorRoutines.ActorFlip, Str, Bool);
        }

        private static void RegisterImages(PevtCommandRegistry registry)
        {
            Add(registry, "image_show", ImageRoutines.ImageShow, Str, Str);
            Add(registry, "image_show_at", ImageRoutines.ImageShowAt, Str, Str, Float, Float);
            Add(registry, "image_hide", ImageRoutines.ImageHide, Str, Int);
            Add(registry, "image_clear", ImageRoutines.ImageClear, Str, Int);
            Add(registry, "image_move", ImageRoutines.ImageMove, Str, Float, Float, Int, Str);
            Add(registry, "image_move_by", ImageRoutines.ImageMoveBy, Str, Float, Float, Int, Str);
            Add(registry, "image_fade", ImageRoutines.ImageFade, Str, Float, Int, Str);
            Add(registry, "image_scale", ImageRoutines.ImageScale, Str, Float, Float, Int, Str);
            Add(registry, "image_rotate", ImageRoutines.ImageRotate, Str, Float, Int, Str);
            Add(registry, "image_tint", ImageRoutines.ImageTint, Str, Str, Int);
            Add(registry, "image_flip", ImageRoutines.ImageFlip, Str, Bool, Bool);
            Add(registry, "image_order", ImageRoutines.ImageOrder, Str, Int);
            Add(registry, "image_fill", ImageRoutines.ImageFill, Str, Str);
            Add(registry, "cg_show", ImageRoutines.CgShow, Str, Str);
            Add(registry, "silhouette_show", ImageRoutines.SilhouetteShow, Str, Str, Str, Int);
        }

        private static void RegisterScreen(PevtCommandRegistry registry)
        {
            Add(registry, "screen_fade", ScreenRoutines.ScreenFade, Str, Float, Int, Str);
            Add(registry, "screen_flash", ScreenRoutines.ScreenFlash, Str, Int, Int, Int);
            Add(registry, "camera_shake", ScreenRoutines.CameraShake, Float, Int, Float);
            Add(registry, "camera_move", ScreenRoutines.CameraMove, Str, Float, Float, Float, Int, Str);
            Add(registry, "camera_reset", ScreenRoutines.CameraReset, Int);
            Add(registry, "effect_set", ScreenRoutines.EffectSet, Str, Bool, Int);
            Add(registry, "effect_pulse", ScreenRoutines.EffectPulse, Str, Int, Int);
            Add(registry, "spotlight", ScreenRoutines.Spotlight, Str, Bool, Str);
        }

        private static void RegisterAudio(PevtCommandRegistry registry)
        {
            Add(registry, "audio_preload", AudioRoutines.AudioPreload, Str, Str);
            Add(registry, "sound_play", AudioRoutines.SoundPlay, Str, Float, Float);
            Add(registry, "voice_play", AudioRoutines.VoicePlay, Str, Str, Float);
            Add(registry, "ambience_play", AudioRoutines.AmbiencePlay, Str, Float, Int);
            Add(registry, "ambience_stop", AudioRoutines.AmbienceStop, Int);
            Add(registry, "music_play", AudioRoutines.MusicPlay, Str, Int);
            Add(registry, "music_stop", AudioRoutines.MusicStop, Int);
            Add(registry, "music_pause", AudioRoutines.MusicPause, Int);
            Add(registry, "music_resume", AudioRoutines.MusicResume, Int);
            Add(registry, "music_volume", AudioRoutines.MusicVolume, Float, Int);
            Add(registry, "music_block", AudioRoutines.MusicBlock, Str);
        }

        private static void RegisterUi(PevtCommandRegistry registry)
        {
            Add(registry, "ui_visible", UiRoutines.UiVisible, Bool);
            Add(registry, "status_visible", UiRoutines.StatusVisible, Bool);
            Add(registry, "ui_portrait_visible", UiRoutines.UiPortraitVisible, Bool);
            Add(registry, "letterbox_visible", UiRoutines.LetterboxVisible, Bool, Int);
            Add(registry, "blur_visible", UiRoutines.BlurVisible, Bool, Int);
            Add(registry, "alert", UiRoutines.Alert, Str, Str);
            Add(registry, "title_show", UiRoutines.TitleShow, Str, Float, Float);
            Add(registry, "title_hide", UiRoutines.TitleHide, Int);
            Add(registry, "tutorial_show", UiRoutines.TutorialShow, Str);
            Add(registry, "tutorial_hide", UiRoutines.TutorialHide, Str);
            Add(registry, "tutorial_clear", UiRoutines.TutorialClear);
            Add(registry, "input_enabled", UiRoutines.InputEnabled, Str, Bool);
            Add(registry, "input_key_enabled", UiRoutines.InputKeyEnabled, Str, Bool);
        }
    }

    /// <summary>把一个委托当成组合处理器用。组合本体都是静态方法，不需要各写一个类。</summary>
    public sealed class PevtDelegateRoutine : IPevtCommandRoutine
    {
        private readonly Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> _run;

        public PevtDelegateRoutine(Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments arguments) => _run(context, arguments);
    }
}
