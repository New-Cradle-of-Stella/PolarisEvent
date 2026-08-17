using System;
using System.Collections.Generic;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Runtime;

namespace Polaris.Pevt.Core.Tests.Runtime.Fakes
{
    /// <summary>
    /// 完整的内存替身。全部服务只记录调用顺序并返回可控的 <see cref="PevtWait"/>，
    /// 不引用任何游戏类型——功能阶段 C 的验收要求就是"不接游戏 API 也能跑完整流程"。
    /// </summary>
    public sealed class FakeClock : IPevtClock
    {
        public long Frame { get; private set; }

        public void Advance() => Frame++;

        public Func<int, PevtWait> FrameWaitFactory { get; set; }

        public PevtWait WaitFrames(int frames) =>
            FrameWaitFactory != null ? FrameWaitFactory(frames) : new PevtFrameWait(frames);

        public bool InputPressed { get; set; }

        public PevtWait<bool> WaitInput(int timeoutFrames) => new PevtInputWait(() => InputPressed, timeoutFrames);

        public bool MotionsFinished { get; set; } = true;

        public PevtWait<bool> WaitOwnedMotions(int timeoutFrames) => new PevtMotionWait(() => MotionsFinished, timeoutFrames);
    }

    /// <summary>把人物参数解析接到功能阶段 B 的目录上，不重新定义第二个人物数据模型。</summary>
    public sealed class FakeActorCatalogService : IPevtActorCatalogService
    {
        private readonly ActorDirectory _directory;

        public FakeActorCatalogService(ActorDirectory directory) => _directory = directory;

        public bool TryResolve(string actorId, out ActorRegistration registration) =>
            _directory.TryGetActor(actorId, out registration);

        public bool TryResolveAppearance(string actorId, string appearanceId, out ActorAppearance appearance)
        {
            appearance = null;
            return _directory.TryGetActor(actorId, out ActorRegistration registration)
                && registration.Actor.TryGetAppearance(appearanceId, out appearance);
        }

        public bool TryResolveAnchor(string actorId, string anchorId, out ActorAnchor anchor)
        {
            anchor = null;
            if (BuiltinActorAnchors.Contains(anchorId))
                return true;

            return _directory.TryGetActor(actorId, out ActorRegistration registration)
                && registration.Actor.TryGetAnchor(anchorId, out anchor);
        }
    }

    /// <summary>记录调用顺序的对话替身。<see cref="AdvanceSignal"/> 让测试精确控制何时推进。</summary>
    public sealed class FakeDialogue : IPevtDialogue
    {
        public List<string> Calls { get; } = new List<string>();

        public PevtSignalWait<int> AdvanceSignal { get; private set; }

        public void SelectSpeaker(string actorId) => Calls.Add($"SelectSpeaker({actorId})");

        public void ClearSpeaker() => Calls.Add("ClearSpeaker()");

        public void OpenText(string text) => Calls.Add($"OpenText({text})");

        public PevtWait WaitAdvance()
        {
            Calls.Add("WaitAdvance()");
            AdvanceSignal = new PevtSignalWait<int>("对话推进");
            return AdvanceSignal;
        }

        public void CommitLog() => Calls.Add("CommitLog()");

        public void OpenBoard(string text, string style) => Calls.Add($"OpenBoard({text},{style})");

        public PevtWait WaitBoardClose()
        {
            Calls.Add("WaitBoardClose()");
            AdvanceSignal = new PevtSignalWait<int>("文本板关闭");
            return AdvanceSignal;
        }

        public void CloseBoard() => Calls.Add("CloseBoard()");

        public void BindProfile(string actorId, string displayName, string voiceId) => Calls.Add($"BindProfile({actorId})");

        public void ResetProfile(string actorId) => Calls.Add($"ResetProfile({actorId})");

        public void SetVisible(bool visible, bool immediate) => Calls.Add($"SetVisible({visible})");

        public void SetHold(bool enabled) => Calls.Add($"SetHold({enabled})");

        public void SetAutoAdvance(bool enabled) => Calls.Add($"SetAutoAdvance({enabled})");

        public void SetLogEnabled(bool enabled) => Calls.Add($"SetLogEnabled({enabled})");
    }

    /// <summary>选择替身。<see cref="PresentSignal"/> 由测试触发，返回从 1 开始的序号。</summary>
    public sealed class FakeChoice : IPevtChoice
    {
        public List<string> Calls { get; } = new List<string>();

        public PevtSignalWait<int> PresentSignal { get; private set; }

        public PevtSignalWait<string> PresentKeySignal { get; private set; }

        public void Reset() => Calls.Add("Reset()");

        public void Begin(string prompt) => Calls.Add($"Begin({prompt})");

        public void AddIndex(string text) => Calls.Add($"AddIndex({text})");

        public void Add(string key, string text, bool enabled, bool cancel) => Calls.Add($"Add({key})");

        public void SetInitial(string initialKey) => Calls.Add($"SetInitial({initialKey})");

        public PevtWait<int> PresentIndex()
        {
            Calls.Add("PresentIndex()");
            PresentSignal = new PevtSignalWait<int>("选择结果");
            return PresentSignal;
        }

        public PevtWait<string> PresentKey()
        {
            Calls.Add("PresentKey()");
            PresentKeySignal = new PevtSignalWait<string>("选择键");
            return PresentKeySignal;
        }
    }

    /// <summary>资源替身：默认立即就绪；把 ID 加入 <see cref="Missing"/> 后以 PEVTR4403 失败。</summary>
    public sealed class FakeResources : IPevtResources
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Missing { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>需要显式放行的资源；留空表示全部立即就绪。</summary>
        public HashSet<string> Pending { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void Release(string id) => Pending.Remove(id);

        private PevtWait Ticket(string id)
        {
            Calls.Add($"Require({id})");
            return new PevtResourceWait(id, () =>
                Missing.Contains(id) ? PevtResourceStatus.Failed
                : Pending.Contains(id) ? PevtResourceStatus.Loading
                : PevtResourceStatus.Ready);
        }

        public PevtWait RequirePortrait(string actorId, string appearanceId) => Ticket($"portrait:{actorId}:{appearanceId}");

        public PevtWait RequireImage(string assetId) => Ticket($"image:{assetId}");

        public PevtWait RequireAudio(string audioId, string kind) => Ticket($"audio:{kind}:{audioId}");

        public PevtWait PreloadAudio(string audioId, string kind) => Ticket($"preload:{kind}:{audioId}");

        public PevtWait<bool> WaitAudio(string audioId, string kind)
        {
            Calls.Add($"WaitAudio({audioId})");
            return new PevtMotionWait(() => !Pending.Contains($"audio:{kind}:{audioId}"));
        }

        public bool ResolveGroup(string groupId)
        {
            Calls.Add($"ResolveGroup({groupId})");
            return !Missing.Contains(groupId);
        }

        public PevtWait<bool> WaitGroup(string groupId, int timeoutFrames)
        {
            Calls.Add($"WaitGroup({groupId})");
            return new PevtMotionWait(() => !Pending.Contains(groupId), timeoutFrames);
        }
    }

    /// <summary>立绘替身：所有跨帧操作返回可控帧数等待。</summary>
    public sealed class FakePortrait : IPevtPortrait
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> KnownEmotes { get; } = new HashSet<string>(StringComparer.Ordinal) { "smile", "angry" };

        public HashSet<string> KnownMotions { get; } = new HashSet<string>(StringComparer.Ordinal) { "nod", "shake" };

        public void SetAppearance(string actorId, string appearanceId) => Calls.Add($"SetAppearance({actorId},{appearanceId})");

        public PevtWait Place(string actorId, string anchorId, int frames)
        {
            Calls.Add($"Place({actorId},{anchorId},{frames})");
            return new PevtFrameWait(frames);
        }

        public PevtWait Move(string actorId, string anchorId, int frames)
        {
            Calls.Add($"Move({actorId},{anchorId},{frames})");
            return new PevtFrameWait(frames);
        }

        public PevtWait Exit(string actorId, int frames)
        {
            Calls.Add($"Exit({actorId},{frames})");
            return new PevtFrameWait(frames);
        }

        public bool ValidateEmote(string actorId, string emoteId) => KnownEmotes.Contains(emoteId);

        public void PlayEmote(string actorId, string emoteId) => Calls.Add($"PlayEmote({actorId},{emoteId})");

        public bool ValidateMotion(string actorId, string motionId) => KnownMotions.Contains(motionId);

        public PevtWait PlayMotion(string actorId, string motionId, int frames)
        {
            Calls.Add($"PlayMotion({actorId},{motionId})");
            return new PevtFrameWait(frames);
        }

        public void StopMotion(string actorId) => Calls.Add($"StopMotion({actorId})");

        public void SetFlip(string actorId, bool horizontal) => Calls.Add($"SetFlip({actorId},{horizontal})");
    }

    /// <summary>其余服务的最小替身，只记录调用。</summary>
    public sealed class FakeStage : IPevtImage, IPevtScreen, IPevtCamera, IPevtEffect, IPevtAudio, IPevtMusic, IPevtUi, IPevtInput
    {
        public List<string> Calls { get; } = new List<string>();

        private PevtWait Record(string call, int frames = 0)
        {
            Calls.Add(call);
            return new PevtFrameWait(frames);
        }

        // IPevtImage
        public void SetContent(string layerId, string assetId) => Calls.Add($"SetContent({layerId},{assetId})");
        public void SetVisible(string layerId, bool visible) => Calls.Add($"SetVisible({layerId},{visible})");
        public void SetPosition(string layerId, float x, float y) => Calls.Add($"SetPosition({layerId})");
        public PevtWait MoveTo(string layerId, float x, float y, int frames, string easing) => Record($"MoveTo({layerId})", frames);
        public PevtWait MoveBy(string layerId, float x, float y, int frames, string easing) => Record($"MoveBy({layerId})", frames);
        public PevtWait FadeTo(string layerId, float opacity, int frames, string easing) => Record($"FadeTo({layerId},{opacity})", frames);
        public PevtWait ScaleTo(string layerId, float x, float y, int frames, string easing) => Record($"ScaleTo({layerId})", frames);
        public PevtWait RotateTo(string layerId, float degrees, int frames, string easing) => Record($"RotateTo({layerId})", frames);
        public PevtWait TintTo(string layerId, string color, int frames) => Record($"TintTo({layerId})", frames);
        public void SetFlip(string layerId, bool horizontal, bool vertical) => Calls.Add($"SetFlip({layerId})");
        public void SetOrder(string layerId, int order) => Calls.Add($"SetOrder({layerId},{order})");
        public void Fill(string layerId, string color) => Calls.Add($"Fill({layerId},{color})");
        public PevtWait ClearGroup(string groupId, int frames) => Record($"ClearGroup({groupId})", frames);
        public void OpenSingle(string assetId, string caption) => Calls.Add($"OpenSingle({assetId})");
        public PevtWait WaitSingleClose() => Record("WaitSingleClose()");
        public void CloseSingle() => Calls.Add("CloseSingle()");
        public PevtWait ShowSilhouette(string layerId, string assetId, string anchorId, int frames) => Record($"ShowSilhouette({layerId})", frames);

        // IPevtScreen
        public void EnsureFadeLayer() => Calls.Add("EnsureFadeLayer()");
        public void SetFadeColor(string color) => Calls.Add($"SetFadeColor({color})");
        public PevtWait FadeTo(float opacity, int frames, string easing) => Record($"ScreenFadeTo({opacity})", frames);
        public void ReleaseFadeLayer() => Calls.Add("ReleaseFadeLayer()");
        public PevtWait Flash(string color, int delayFrames, int holdFrames, int fadeFrames) => Record($"Flash({color})", fadeFrames);
        bool IPevtScreen.ResolveTarget(string targetId) => true;
        public void SetSpotlight(string targetId, bool enabled, string style) => Calls.Add($"SetSpotlight({targetId})");

        // IPevtCamera
        bool IPevtCamera.ResolveTarget(string targetId) => true;
        public PevtWait MoveTo(string targetId, float x, float y, float zoom, int frames, string easing) => Record($"CameraMoveTo({targetId})", frames);
        public PevtWait Shake(float amplitude, int durationFrames, float frequency) => Record("Shake()", durationFrames);
        public PevtWait RestoreEventSnapshot(int frames) => Record("RestoreEventSnapshot()", frames);

        // IPevtEffect
        public bool Resolve(string effectId) => true;
        public PevtWait SetEnabled(string effectId, bool enabled, int frames) => Record($"EffectSetEnabled({effectId})", frames);
        public PevtWait Pulse(string effectId, int fadeFrames, int holdFrames) => Record($"EffectPulse({effectId})", fadeFrames);

        // IPevtAudio
        public void PlaySound(string soundId, float volume, float pitch) => Calls.Add($"PlaySound({soundId})");
        public void PlayVoice(string actorId, string voiceId, float volume) => Calls.Add($"PlayVoice({voiceId})");
        public PevtWait PlayAmbience(string trackId, float volume, int fadeFrames) => Record($"PlayAmbience({trackId})", fadeFrames);
        public PevtWait StopAmbience(int fadeFrames) => Record("StopAmbience()", fadeFrames);

        // IPevtMusic
        public PevtWait Replace(string musicId, int fadeFrames) => Record($"MusicReplace({musicId})", fadeFrames);
        public PevtWait Stop(int fadeFrames) => Record("MusicStop()", fadeFrames);
        public void ReleaseCurrent() => Calls.Add("ReleaseCurrent()");
        public PevtWait Pause(int fadeFrames) => Record("MusicPause()", fadeFrames);
        public PevtWait Resume(int fadeFrames) => Record("MusicResume()", fadeFrames);
        public PevtWait FadeVolume(float scale, int frames) => Record($"FadeVolume({scale})", frames);
        public void GoToBlock(string blockId) => Calls.Add($"GoToBlock({blockId})");

        // IPevtUi
        public void SetGlobalVisible(bool visible) => Calls.Add($"SetGlobalVisible({visible})");
        public void SetStatusVisible(bool visible) => Calls.Add($"SetStatusVisible({visible})");
        public PevtWait SetLetterboxVisible(bool visible, int frames) => Record($"SetLetterboxVisible({visible})", frames);
        public PevtWait SetBlurVisible(bool visible, int frames) => Record($"SetBlurVisible({visible})", frames);
        public void ShowAlert(string text, string style) => Calls.Add($"ShowAlert({text})");
        public PevtWait WaitAlertClose() => Record("WaitAlertClose()");
        public void ShowTitle(string text, float x, float y) => Calls.Add($"ShowTitle({text})");
        public PevtWait WaitTitleShown() => Record("WaitTitleShown()");
        public PevtWait HideTitle(int frames) => Record("HideTitle()", frames);
        public bool ResolveTutorial(string tutorialId) => true;
        public void ShowTutorial(string tutorialId) => Calls.Add($"ShowTutorial({tutorialId})");
        public PevtWait WaitTutorialClose() => Record("WaitTutorialClose()");
        public void HideTutorial(string tutorialId) => Calls.Add($"HideTutorial({tutorialId})");
        public void ClearTutorials() => Calls.Add("ClearTutorials()");

        public void NotifyItemChange(string itemId, int delta, int count) => Calls.Add($"NotifyItemChange({itemId},{delta},{count})");
        public void NotifyMoneyChange(int delta, int amount) => Calls.Add($"NotifyMoneyChange({delta},{amount})");
        public void NotifySkillChange(string skillId, bool owned) => Calls.Add($"NotifySkillChange({skillId},{owned})");

        // IPevtInput
        public bool ValidateCapability(string capability) => capability == "skip" || capability == "log" || capability == "fast_travel";
        public void SetCapability(string capability, bool enabled) => Calls.Add($"SetCapability({capability},{enabled})");
        public bool ValidateKey(string key) => key.Length > 0;
        public void SetKeyEnabled(string key, bool enabled) => Calls.Add($"SetKeyEnabled({key},{enabled})");
    }
}
