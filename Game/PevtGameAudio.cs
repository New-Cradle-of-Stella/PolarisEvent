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
        /// 音量与音高。原版 cue 的音量由 CRI 的类别音量统一决定，单条音效没有公开的
        /// 逐次覆写入口，因此这两个参数只在音量为 0 时被尊重（等于不播放），
        /// 其余情况按 cue 自身的设定播放。
        /// </summary>
        public void PlaySound(string soundId, float volume, float pitch)
        {
            if (volume <= 0f)
                return;

            PevtGameHost.Guard("PlaySound", () => PolarisAPI.Game.Audio.Play(soundId, false));
        }

        /// <summary>
        /// 语音。人物 ID 只用于诊断与将来的口型同步；cue 名由 <c>voiceId</c> 直接给出，
        /// 不从原版人物短键推导——那会把短键带回公开参数里。
        /// </summary>
        public void PlayVoice(string actorId, string voiceId, float volume)
        {
            if (volume <= 0f)
                return;

            PevtGameHost.Guard("PlayVoice", () => PolarisAPI.Game.Audio.Play(voiceId, false));
        }

        public PevtWait PlayAmbience(string trackId, float volume, int fadeFrames)
        {
            PevtGameHost.Guard("PlayAmbience", () =>
            {
                StopAmbienceNow();
                _ambience = PolarisAPI.Game.Audio.Play(trackId, true);
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

        /// <summary>卸载本事件加载过的 BGM sheet；资源服务持有这份清单。</summary>
        public void ReleaseCurrent() =>
            PevtGameHost.Guard("MusicRelease", BGM.flushEventLoadedTiming);

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
