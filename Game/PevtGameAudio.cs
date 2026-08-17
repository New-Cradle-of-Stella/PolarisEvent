using System;
using Polaris.API;
using Polaris.Pevt.Runtime;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 音效与语音。播放走 PolarisCore 的 <see cref="PolarisAPI.Game.Audio"/>，
    /// 它已经管好了并发上限、播放实例回收和异常归因——组件不再自己 new CRI 播放器。
    ///
    /// 环境声是事件临时状态：一个循环播放实例由本适配器持有，事件结束、替换或异常时停掉。
    /// </summary>
    internal sealed class PevtGameAudio : IPevtAudio
    {
        private readonly PevtGameClock _clock;
        private GameAudioPlayback _ambience;

        public PevtGameAudio(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// 音量为 0 直接不播；其余情况把 volume/pitch 覆写到这一次播放的 CRI 播放器上。
        /// 覆写只影响本次播放实例，不动全局类别音量。
        /// </summary>
        public void PlaySound(string soundId, float volume, float pitch)
        {
            if (volume <= 0f)
                return;

            PevtGameHost.Guard("PlaySound", () => Apply(PolarisAPI.Game.Audio.Play(soundId, false), volume, pitch));
        }

        /// <summary>1 是"按 cue 自己的设定"，因此只有偏离 1 时才下发覆写。</summary>
        private static void Apply(GameAudioPlayback playback, float volume, float pitch)
        {
            if (playback == null)
                return;

            if (volume != 1f)
                playback.SetVolume(volume);
            if (pitch != 1f)
                playback.SetPitch(pitch);
        }

        /// <summary>
        /// 语音。人物 ID 只用于诊断与将来的口型同步；cue 名由 <c>voiceId</c> 直接给出，
        /// 不从原版人物短键推导——那会把短键带回公开参数里。
        /// </summary>
        public void PlayVoice(string actorId, string voiceId, float volume)
        {
            if (volume <= 0f)
                return;

            PevtGameHost.Guard("PlayVoice", () => Apply(PolarisAPI.Game.Audio.Play(voiceId, false), volume, 1f));
        }

        public PevtWait PlayAmbience(string trackId, float volume, int fadeFrames)
        {
            PevtGameHost.Guard("PlayAmbience", () =>
            {
                StopAmbienceNow();
                _ambience = PolarisAPI.Game.Audio.Play(trackId, true);
                Apply(_ambience, volume, 1f);
            });

            return TrackFrames(fadeFrames);
        }

        public PevtWait StopAmbience(int fadeFrames)
        {
            PevtGameHost.Guard("StopAmbience", StopAmbienceNow);
            return TrackFrames(fadeFrames);
        }

        private void StopAmbienceNow()
        {
            _ambience?.Stop();
            _ambience = null;
        }

        /// <summary>事件结束、替换或异常时停掉环境声。</summary>
        public void ReleaseAll() => PevtGameHost.Guard("ReleaseAmbience", StopAmbienceNow);

        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }
    }

    /// <summary>
    /// BGM。全部操作映射到原版 <see cref="BGM"/> 自己的淡入淡出模型：
    /// <c>fadeout(100)</c> 是"淡回满音量"，<c>fadeout(0)</c> 是"淡到静音"，
    /// 音量缩放就是淡到某个百分比。这样与原版事件里的 BGM 行为完全一致。
    /// </summary>
    internal sealed class PevtGameMusic : IPevtMusic
    {
        private readonly PevtGameClock _clock;
        private readonly PevtGameResources _resources;

        private bool _touched;

        public PevtGameMusic(PevtGameClock clock, PevtGameResources resources)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        /// <summary>切到已经由 <c>PreloadAudio</c> 备好的下一条 BGM。</summary>
        public PevtWait Replace(string musicId, int fadeFrames)
        {
            PevtGameHost.Guard("MusicReplace", () =>
            {
                _touched = true;
                BGM.replace(fadeFrames, fadeFrames, true, false);
            });

            return TrackFrames(fadeFrames);
        }

        public PevtWait Stop(int fadeFrames)
        {
            PevtGameHost.Guard("MusicStop", () =>
            {
                _touched = true;
                BGM.fadeout(0f, fadeFrames, true);
            });

            return TrackFrames(fadeFrames);
        }

        /// <summary>
        /// 卸载本事件加载过的 BGM sheet。
        ///
        /// 不能只调 <see cref="BGM.flushEventLoadedTiming"/>：它卸的是 <c>BGM.Aevt_loaded_timing</c>，
        /// 而那份清单只有原版 <c>LOAD_SND_TIMING</c> 命令会填。PEVT 自己加载的 sheet 记在资源服务里，
        /// 所以两边都要走一遍，否则 <c>@music_stop</c> 到事件结束前都不会真正释放。
        /// </summary>
        public void ReleaseCurrent() =>
            PevtGameHost.Guard("MusicRelease", () =>
            {
                _resources.ReleaseAudioSheets();
                BGM.flushEventLoadedTiming();
            });

        public PevtWait Pause(int fadeFrames)
        {
            PevtGameHost.Guard("MusicPause", () =>
            {
                _touched = true;

                // auto_unload=false：暂停不卸载 sheet，否则 Resume 时无声可恢复。
                BGM.fadeout(0f, fadeFrames, false);
            });

            return TrackFrames(fadeFrames);
        }

        public PevtWait Resume(int fadeFrames)
        {
            PevtGameHost.Guard("MusicResume", () => BGM.fadeout(100f, fadeFrames, false));
            return TrackFrames(fadeFrames);
        }

        public PevtWait FadeVolume(float scale, int frames)
        {
            PevtGameHost.Guard("MusicFadeVolume", () =>
            {
                _touched = true;
                BGM.fadeout(scale * 100f, frames, false);
            });

            return TrackFrames(frames);
        }

        public void GoToBlock(string blockId) =>
            PevtGameHost.Guard("MusicGoToBlock", () => BGM.GotoBlock(blockId, true));

        /// <summary>事件结束时把音量恢复到满值；具体播哪条曲子由地图自己的 BGM 逻辑决定。</summary>
        public void ReleaseAll()
        {
            if (!_touched)
                return;

            _touched = false;
            PevtGameHost.Guard("MusicRestore", () => BGM.fadeout(100f, 0f, false));
        }

        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }
    }
}
