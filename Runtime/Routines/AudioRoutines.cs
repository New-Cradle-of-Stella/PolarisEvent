using System.Collections.Generic;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 音频组合。
    /// </summary>
    internal static class AudioRoutines
    {
        private const string SoundKind = "sound";
        private const string VoiceKind = "voice";
        private const string AmbienceKind = "ambience";
        private const string MusicKind = "music";

        private static readonly string[] AudioKinds = { SoundKind, VoiceKind, AmbienceKind, MusicKind };

        private static IPevtResources Resources(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");

        private static IPevtAudio Audio(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Audio, "Audio");

        private static IPevtMusic Music(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Music, "Music");

        private static string RequireKind(string kind)
        {
            foreach (string candidate in AudioKinds)
            {
                if (candidate == kind)
                    return kind;
            }

            throw new PevtRoutineFailureException("PEVTR4001",
                $"`kind` 只接受 {string.Join("、", AudioKinds)}，实际为 `{kind}`。");
        }

        public static IEnumerator<PevtWait> AudioPreload(PevtRoutineContext context, PevtArguments args)
        {
            IPevtResources resources = Resources(context);
            string audioId = PevtArgumentDomains.RequireId(args.String(0), "audioId");
            string kind = RequireKind(args.String(1));

            yield return resources.PreloadAudio(audioId, kind);

            PevtWait<bool> ready = resources.WaitAudio(audioId, kind);
            yield return ready;
            context.Result.SetBool(ready.Result);
        }

        public static IEnumerator<PevtWait> SoundPlay(PevtRoutineContext context, PevtArguments args)
        {
            IPevtAudio audio = Audio(context);
            string soundId = PevtArgumentDomains.RequireId(args.String(0), "soundId");
            float volume = PevtArgumentDomains.RequireUnitRange(args.Float(1), "volume");
            float pitch = PevtArgumentDomains.RequireFinite(args.Float(2), "pitch");

            yield return Resources(context).RequireAudio(soundId, SoundKind);
            audio.PlaySound(soundId, volume, pitch);
        }

        public static IEnumerator<PevtWait> VoicePlay(PevtRoutineContext context, PevtArguments args)
        {
            IPevtAudio audio = Audio(context);
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            string voiceId = PevtArgumentDomains.RequireId(args.String(1), "voiceId");
            float volume = PevtArgumentDomains.RequireUnitRange(args.Float(2), "volume");

            yield return Resources(context).RequireAudio(voiceId, VoiceKind);
            audio.PlayVoice(actor.ActorId, voiceId, volume);
        }

        public static IEnumerator<PevtWait> AmbiencePlay(PevtRoutineContext context, PevtArguments args)
        {
            IPevtAudio audio = Audio(context);
            string trackId = PevtArgumentDomains.RequireId(args.String(0), "trackId");
            float volume = PevtArgumentDomains.RequireUnitRange(args.Float(1), "volume");
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(2), "fadeFrames");

            yield return Resources(context).RequireAudio(trackId, AmbienceKind);

            context.Services.Session.RegisterRestore("Audio.StopAmbience", () => audio.StopAmbience(0));
            yield return audio.PlayAmbience(trackId, volume, fadeFrames);
        }

        public static IEnumerator<PevtWait> AmbienceStop(PevtRoutineContext context, PevtArguments args)
        {
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(0), "fadeFrames");
            yield return Audio(context).StopAmbience(fadeFrames);
        }

        /// <summary>`PreloadAudio` → `WaitAudio` → `Music.Replace`。</summary>
        public static IEnumerator<PevtWait> MusicPlay(PevtRoutineContext context, PevtArguments args)
        {
            IPevtMusic music = Music(context);
            IPevtResources resources = Resources(context);
            string musicId = PevtArgumentDomains.RequireId(args.String(0), "musicId");
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(1), "fadeFrames");

            yield return resources.PreloadAudio(musicId, MusicKind);

            PevtWait<bool> ready = resources.WaitAudio(musicId, MusicKind);
            yield return ready;
            if (!ready.Result)
                throw new PevtRoutineFailureException("PEVTR4403", $"BGM `{musicId}` 未能在等待上限内就绪。");

            yield return music.Replace(musicId, fadeFrames);
        }

        public static IEnumerator<PevtWait> MusicStop(PevtRoutineContext context, PevtArguments args)
        {
            IPevtMusic music = Music(context);
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(0), "fadeFrames");

            yield return music.Stop(fadeFrames);
            music.ReleaseCurrent();
        }

        public static IEnumerator<PevtWait> MusicPause(PevtRoutineContext context, PevtArguments args)
        {
            IPevtMusic music = Music(context);
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(0), "fadeFrames");

            context.Services.Session.RegisterRestore("Music.Resume", () => music.Resume(0));
            yield return music.Pause(fadeFrames);
        }

        public static IEnumerator<PevtWait> MusicResume(PevtRoutineContext context, PevtArguments args)
        {
            int fadeFrames = PevtArgumentDomains.RequireFrames(args.Int(0), "fadeFrames");
            yield return Music(context).Resume(fadeFrames);
        }

        public static IEnumerator<PevtWait> MusicVolume(PevtRoutineContext context, PevtArguments args)
        {
            IPevtMusic music = Music(context);
            float scale = PevtArgumentDomains.RequireUnitRange(args.Float(0), "scale");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            context.Services.Session.RegisterRestore("Music.FadeVolume(1.0)", () => music.FadeVolume(1f, 0));
            yield return music.FadeVolume(scale, frames);
        }

        public static IEnumerator<PevtWait> MusicBlock(PevtRoutineContext context, PevtArguments args)
        {
            Music(context).GoToBlock(PevtArgumentDomains.RequireId(args.String(0), "blockId"));
            yield break;
        }
    }
}
