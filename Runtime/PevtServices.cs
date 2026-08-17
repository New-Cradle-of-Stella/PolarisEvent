using System;
using System.Collections.Generic;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Runtime
{
    // 本文件定义同步指令中间层规范第 5 节的中立原子服务边界。

    /// <summary>帧等待、输入等待和受管动作聚合等待。</summary>
    public interface IPevtClock
    {
        /// <summary>当前 PolarisEvent 更新帧号，单调递增。</summary>
        long Frame { get; }

        PevtWait WaitFrames(int frames);

        PevtWait<bool> WaitInput(int timeoutFrames);

        /// <summary>等待当前事件拥有的全部受管动作结束。</summary>
        PevtWait<bool> WaitOwnedMotions(int timeoutFrames);
    }

    /// <summary>
    /// 把可读人物 ID 解析为对话资料、视觉、appearance 与 anchor，并隔离原版短键。
    /// 这是运行时查询接口，不重新定义第二个人物目录数据模型——它查的就是功能阶段 B 的
    /// <see cref="ActorDirectory"/>。
    /// </summary>
    public interface IPevtActorCatalogService
    {
        bool TryResolve(string actorId, out ActorRegistration registration);

        bool TryResolveAppearance(string actorId, string appearanceId, out ActorAppearance appearance);

        /// <summary>解析语义锚点或人物专用 anchor。内置语义锚点没有坐标时返回 null anchor 但仍为 true。</summary>
        bool TryResolveAnchor(string actorId, string anchorId, out ActorAnchor anchor);
    }

    /// <summary>解析、预载并等待图像、音频与角色资源。</summary>
    public interface IPevtResources
    {
        PevtWait RequirePortrait(string actorId, string appearanceId);

        PevtWait RequireImage(string assetId);

        PevtWait RequireAudio(string audioId, string kind);

        PevtWait PreloadAudio(string audioId, string kind);

        PevtWait<bool> WaitAudio(string audioId, string kind);

        bool ResolveGroup(string groupId);

        PevtWait<bool> WaitGroup(string groupId, int timeoutFrames);
    }

    /// <summary>对话、旁白、文本板与回看。</summary>
    public interface IPevtDialogue
    {
        void SelectSpeaker(string actorId);

        void ClearSpeaker();

        void OpenText(string text);

        PevtWait WaitAdvance();

        void CommitLog();

        void OpenBoard(string text, string style);

        PevtWait WaitBoardClose();

        void CloseBoard();

        void BindProfile(string actorId, string displayName, string voiceId);

        void ResetProfile(string actorId);

        void SetVisible(bool visible, bool immediate);

        void SetHold(bool enabled);

        void SetAutoAdvance(bool enabled);

        void SetLogEnabled(bool enabled);
    }

    /// <summary>选择 UI。</summary>
    public interface IPevtChoice
    {
        void Reset();

        void Begin(string prompt);

        void AddIndex(string text);

        void Add(string key, string text, bool enabled, bool cancel);

        void SetInitial(string initialKey);

        /// <summary>返回从 1 开始的序号。</summary>
        PevtWait<int> PresentIndex();

        /// <summary>返回稳定的选项键。</summary>
        PevtWait<string> PresentKey();
    }

    /// <summary>角色立绘。</summary>
    public interface IPevtPortrait
    {
        void SetAppearance(string actorId, string appearanceId);

        PevtWait Place(string actorId, string anchorId, int frames);

        PevtWait Move(string actorId, string anchorId, int frames);

        PevtWait Exit(string actorId, int frames);

        bool ValidateEmote(string actorId, string emoteId);

        void PlayEmote(string actorId, string emoteId);

        bool ValidateMotion(string actorId, string motionId);

        PevtWait PlayMotion(string actorId, string motionId, int frames);

        void StopMotion(string actorId);

        void SetFlip(string actorId, bool horizontal);
    }

    /// <summary>通用演出图层。</summary>
    public interface IPevtImage
    {
        void SetContent(string layerId, string assetId);

        void SetVisible(string layerId, bool visible);

        void SetPosition(string layerId, float x, float y);

        PevtWait MoveTo(string layerId, float x, float y, int frames, string easing);

        PevtWait MoveBy(string layerId, float x, float y, int frames, string easing);

        PevtWait FadeTo(string layerId, float opacity, int frames, string easing);

        PevtWait ScaleTo(string layerId, float x, float y, int frames, string easing);

        PevtWait RotateTo(string layerId, float degrees, int frames, string easing);

        PevtWait TintTo(string layerId, string color, int frames);

        void SetFlip(string layerId, bool horizontal, bool vertical);

        void SetOrder(string layerId, int order);

        void Fill(string layerId, string color);

        PevtWait ClearGroup(string groupId, int frames);

        void OpenSingle(string assetId, string caption);

        PevtWait WaitSingleClose();

        void CloseSingle();

        PevtWait ShowSilhouette(string layerId, string assetId, string anchorId, int frames);
    }

    /// <summary>遮罩与闪光。</summary>
    public interface IPevtScreen
    {
        void EnsureFadeLayer();

        void SetFadeColor(string color);

        PevtWait FadeTo(float opacity, int frames, string easing);

        void ReleaseFadeLayer();

        PevtWait Flash(string color, int delayFrames, int holdFrames, int fadeFrames);

        bool ResolveTarget(string targetId);

        void SetSpotlight(string targetId, bool enabled, string style);
    }

    /// <summary>镜头。</summary>
    public interface IPevtCamera
    {
        bool ResolveTarget(string targetId);

        PevtWait MoveTo(string targetId, float x, float y, float zoom, int frames, string easing);

        PevtWait Shake(float amplitude, int durationFrames, float frequency);

        /// <summary>恢复事件开始前保存的镜头快照。</summary>
        PevtWait RestoreEventSnapshot(int frames);
    }

    /// <summary>后处理。</summary>
    public interface IPevtEffect
    {
        bool Resolve(string effectId);

        PevtWait SetEnabled(string effectId, bool enabled, int frames);

        PevtWait Pulse(string effectId, int fadeFrames, int holdFrames);
    }

    /// <summary>音效、语音与环境声。</summary>
    public interface IPevtAudio
    {
        void PlaySound(string soundId, float volume, float pitch);

        void PlayVoice(string actorId, string voiceId, float volume);

        PevtWait PlayAmbience(string trackId, float volume, int fadeFrames);

        PevtWait StopAmbience(int fadeFrames);
    }

    /// <summary>BGM。</summary>
    public interface IPevtMusic
    {
        PevtWait Replace(string musicId, int fadeFrames);

        PevtWait Stop(int fadeFrames);

        void ReleaseCurrent();

        PevtWait Pause(int fadeFrames);

        PevtWait Resume(int fadeFrames);

        PevtWait FadeVolume(float scale, int frames);

        void GoToBlock(string blockId);
    }

    /// <summary>全局 UI、标题与教程。</summary>
    public interface IPevtUi
    {
        void SetGlobalVisible(bool visible);

        void SetStatusVisible(bool visible);

        PevtWait SetLetterboxVisible(bool visible, int frames);

        PevtWait SetBlurVisible(bool visible, int frames);

        void ShowAlert(string text, string style);

        PevtWait WaitAlertClose();

        void ShowTitle(string text, float x, float y);

        PevtWait WaitTitleShown();

        PevtWait HideTitle(int frames);

        bool ResolveTutorial(string tutorialId);

        void ShowTutorial(string tutorialId);

        PevtWait WaitTutorialClose();

        void HideTutorial(string tutorialId);

        void ClearTutorials();

        // ---- 持久状态变更提示（同步指令中间层规范第 12 节的 notify 分支）----
        // 提示归 UI、改数量归 Inventory：放在 UI 服务而不是 Inventory 上，"静默改数量"才不必绕过一个硬编码的通知。

        void NotifyItemChange(string itemId, int delta, int count);

        void NotifyMoneyChange(int delta, int amount);

        void NotifySkillChange(string skillId, bool owned);
    }

    /// <summary>输入策略。</summary>
    public interface IPevtInput
    {
        bool ValidateCapability(string capability);

        void SetCapability(string capability, bool enabled);

        bool ValidateKey(string key);

        void SetKeyEnabled(string key, bool enabled);
    }

    /// <summary>
    /// 一次事件会话里被标记为"事件临时状态"的修改。事件结束、替换或异常时由会话统一恢复；
    /// 物品、货币、任务、技能和进度属于持久状态，不进这里。
    /// </summary>
    public sealed class PevtEventSession
    {
        private readonly List<KeyValuePair<string, Action>> _restores = new List<KeyValuePair<string, Action>>();

        public string EventId { get; }

        public PevtEventSession(string eventId) => EventId = eventId ?? string.Empty;

        public int PendingRestoreCount => _restores.Count;

        public IReadOnlyList<string> PendingRestores
        {
            get
            {
                var result = new List<string>();
                foreach (KeyValuePair<string, Action> entry in _restores)
                    result.Add(entry.Key);
                return result;
            }
        }

        public void RegisterRestore(string description, Action restore)
        {
            if (restore == null)
                throw new ArgumentNullException(nameof(restore));
            _restores.Add(new KeyValuePair<string, Action>(description ?? string.Empty, restore));
        }

        /// <summary>逆序恢复并清空。单个恢复动作失败不阻止其余动作执行。</summary>
        public IReadOnlyList<Exception> RestoreAll()
        {
            var failures = new List<Exception>();
            for (int i = _restores.Count - 1; i >= 0; i--)
            {
                try
                {
                    _restores[i].Value();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _restores.Clear();
            return failures;
        }
    }

    /// <summary>受控原子服务的集合。全部成员都是中立接口，Core 不知道它们背后是不是 Unity。</summary>
    public sealed class PevtServices
    {
        public IPevtClock Clock { get; }

        public IPevtActorCatalogService ActorCatalog { get; }

        public IPevtResources Resources { get; }

        public IPevtDialogue Dialogue { get; }

        public IPevtChoice Choice { get; }

        public IPevtPortrait Portrait { get; }

        public IPevtImage Image { get; }

        public IPevtScreen Screen { get; }

        public IPevtCamera Camera { get; }

        public IPevtEffect Effect { get; }

        public IPevtAudio Audio { get; }

        public IPevtMusic Music { get; }

        public IPevtUi Ui { get; }

        public IPevtInput Input { get; }

        /// <summary>P1/P2 领域服务包。宿主没有接时是 <see cref="PevtDomainServices.Empty"/>，不为 null。</summary>
        public PevtDomainServices Domains { get; }

        /// <summary>
        /// <c>$raw cmd</c> 的进程级通道；宿主没有接时为 null，此时任何 <c>$raw cmd</c> 都以
        /// PEVTR9001 结束（编译出了指令却没有执行器，属于宿主接线不变量被破坏）。
        /// </summary>
        public IPevtRawCommands RawCommands { get; }

        /// <summary>
        /// <c>$raw cs</c> 的执行器；宿主没有接时为 null。它和 <see cref="RawCommands"/> 一样是
        /// 进程级共享对象——编译缓存必须跨事件复用，否则每次启动事件都要重编一遍同一段 C#。
        /// </summary>
        public Raw.PevtRawCsExecutor RawCs { get; }

        /// <summary>当前事件会话的临时状态恢复表。</summary>
        public PevtEventSession Session { get; }

        public PevtServices(
            IPevtClock clock,
            PevtEventSession session,
            IPevtActorCatalogService actorCatalog = null,
            IPevtResources resources = null,
            IPevtDialogue dialogue = null,
            IPevtChoice choice = null,
            IPevtPortrait portrait = null,
            IPevtImage image = null,
            IPevtScreen screen = null,
            IPevtCamera camera = null,
            IPevtEffect effect = null,
            IPevtAudio audio = null,
            IPevtMusic music = null,
            IPevtUi ui = null,
            IPevtInput input = null,
            PevtDomainServices domains = null,
            IPevtRawCommands rawCommands = null,
            Raw.PevtRawCsExecutor rawCs = null)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            ActorCatalog = actorCatalog;
            Resources = resources;
            Dialogue = dialogue;
            Choice = choice;
            Portrait = portrait;
            Image = image;
            Screen = screen;
            Camera = camera;
            Effect = effect;
            Audio = audio;
            Music = music;
            Ui = ui;
            Input = input;
            Domains = domains ?? PevtDomainServices.Empty;
            RawCommands = rawCommands;
            RawCs = rawCs;
        }
    }
}
