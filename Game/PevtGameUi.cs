using System;
using System.Collections.Generic;
using evt;
using nel;
using Polaris.Pevt.Runtime;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 全局 UI、标题与教程。
    /// </summary>
    internal sealed class PevtGameUi : IPevtUi
    {
        private readonly PevtGameClock _clock;
        private readonly HashSet<string> _tutorials = new HashSet<string>(StringComparer.Ordinal);

        private bool _letterbox;
        private bool _letterboxAnimating;
        private long _letterboxDeadline;
        private float _letterboxStep;
        private bool _blur;
        private bool _statusHidden;
        private bool _portraitHidden;
        private bool _uiDisabled;
        private bool _titleShown;

        public PevtGameUi(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private static UIBase Ui => PevtGameHost.Map?.Ui;

        public void SetGlobalVisible(bool visible)
        {
            PevtGameHost.Guard("SetGlobalVisible", () =>
            {
                Ui?.setEnabled(visible);
                _uiDisabled = !visible;
            });
        }

        public void SetStatusVisible(bool visible)
        {
            PevtGameHost.Guard("SetStatusVisible", () =>
            {
                if (visible)
                    UIStatus.FlgStatusHide.Rem(PevtGameHost.Flag);
                else
                    UIStatus.FlgStatusHide.Add(PevtGameHost.Flag);

                _statusHidden = !visible;
            });
        }

        /// <summary>
        /// 游戏 HUD 自带的动态人物立绘（<see cref="UIBase.GobPictParent"/>），不是 PEVT 的 TalkDrawer。
        /// 对应原版事件命令 <c>UIPICT_GOB_DEACTIVE</c> 使用的同一 Flagger。
        /// </summary>
        public void SetPortraitVisible(bool visible)
        {
            PevtGameHost.Guard("SetPortraitVisible", () =>
            {
                UIBase ui = Ui;
                if (ui == null)
                    return;

                if (visible)
                    ui.FlgNoelAreaDisable.Rem(PevtGameHost.Flag);
                else
                    ui.FlgNoelAreaDisable.Add(PevtGameHost.Flag);

                _portraitHidden = !visible;
            });
        }

        /// <summary>
        /// 黑边。PEVT 会暂停游戏主循环，原版 <see cref="UIBase.run"/> 不再被地图侧推进，
        /// 因此不能只改 <c>draw_letter_box</c> 后空等；动画步长由这里登记，随后由
        /// <see cref="Update"/> 随 PEVT 时钟逐帧驱动原版 UI。
        /// </summary>
        public PevtWait SetLetterboxVisible(bool visible, int frames)
        {
            PevtGameHost.Guard("SetLetterboxVisible", () =>
            {
                UIBase ui = Ui;
                if (ui == null)
                    return;

                float current = Math.Abs(ui.letter_box_t);
                SetLetterboxOverride(visible);
                ui.draw_letter_box = visible;

                _letterboxAnimating = false;
                if (frames <= 0)
                {
                    // 与原版 START_LETTERBOX 1 的立即完成路径相同：让 UIBase 自己建立/清掉网格与状态标记。
                    ui.run(40f);
                }
                else
                {
                    float distance = visible ? Math.Max(0f, 40f - current) : current;
                    // UIBase 展开每 fcnt 前进 1，收起每 fcnt 前进 3；换算后恰好在 frames 帧完成。
                    _letterboxStep = distance / frames / (visible ? 1f : 3f);
                    _letterboxDeadline = _clock.Frame + frames;
                    _letterboxAnimating = true;
                }

                _letterbox = visible;
            });

            return TrackFrames(frames);
        }

        /// <summary>由 PEVT 会话更新点调用；只在黑框过渡期间手动补上被暂停的原版 UI 帧。</summary>
        internal void Update()
        {
            if (!_letterboxAnimating)
                return;

            PevtGameHost.Guard("UpdateLetterbox", () =>
            {
                UIBase ui = Ui;
                if (ui == null)
                    return;

                ui.run(_letterboxStep);
                if (_clock.Frame < _letterboxDeadline)
                    return;

                _letterboxAnimating = false;

                // 浮点步进可能留下极小余量；用原版立即路径完成最后一次网格刷新。
                float target = _letterbox ? 40f : 0f;
                if (Math.Abs(Math.Abs(ui.letter_box_t) - target) > 0.001f)
                    ui.run(40f);
            });
        }

        private static bool _letterboxOverrideActive;
        private static bool _letterboxOverrideVisible;

        private static void SetLetterboxOverride(bool visible)
        {
            _letterboxOverrideVisible = visible;
            _letterboxOverrideActive = true;
        }

        /// <summary>供 UIBase.run 的 Harmony 前缀调用，防止原版无 EvReader 的关闭路径覆盖 PEVT 状态。</summary>
        internal static void ApplyLetterboxOverride(UIBase ui)
        {
            if (_letterboxOverrideActive && ui != null)
                ui.draw_letter_box = _letterboxOverrideVisible;
        }

        private static void ClearLetterboxOverride() => _letterboxOverrideActive = false;

        public PevtWait SetBlurVisible(bool visible, int frames)
        {
            PevtGameHost.Guard("SetBlurVisible", () =>
            {
                BlurScreen blur = PevtGameHost.Map?.BlurSc;
                if (blur == null)
                    return;

                if (visible)
                {
                    blur.addFlag(PevtGameHost.Flag);
                    blur.need_recreate_img = true;
                }
                else
                {
                    blur.remFlag(PevtGameHost.Flag);
                }

                _blur = visible;
            });

            return TrackFrames(frames);
        }

        public void ShowAlert(string text, string style)
        {
            PevtGameHost.Guard("ShowAlert", () =>
            {
                UIBase ui = Ui;
                if (ui?.LogBox == null)
                    return;

                UILogRow.TYPE type = UILogRow.TYPE.ALERT;
                if (!string.IsNullOrEmpty(style) && Enum.IsDefined(typeof(UILogRow.TYPE), style.ToUpperInvariant()))
                    type = (UILogRow.TYPE)Enum.Parse(typeof(UILogRow.TYPE), style.ToUpperInvariant());

                ui.LogBox.AddAlertTX(text ?? string.Empty, type);
            });
        }

        /// <summary>提示条是非阻塞的浮动行，加进去就算完成；不假装它有一个关闭事件。</summary>
        public PevtWait WaitAlertClose() => new PevtFrameWait(0);

        public void ShowTitle(string text, float x, float y)
        {
            PevtGameHost.Guard("ShowTitle", () =>
            {
                M2AreaTitle title = PevtGameHost.Map?.AreaTitle;
                if (title == null)
                    return;

                title.init(text ?? string.Empty, false, 0f, true, false);
                title.finePosPixelShift(x, y);
                _titleShown = true;
            });
        }

        public PevtWait WaitTitleShown() =>
            new PevtPredicateWait(
                () => PevtGameHost.Safe(() => PevtGameHost.Map?.AreaTitle?.isActive() != true, true),
                "区域标题动画");

        public PevtWait HideTitle(int frames)
        {
            PevtGameHost.Guard("HideTitle", () =>
            {
                PevtGameHost.Map?.AreaTitle?.deactivate(frames <= 0);
                _titleShown = false;
            });

            return TrackFrames(frames);
        }

        /// <summary>教程条目由原版教程框自己保存内容；这里只校验它是不是一个可用的文本键。</summary>
        public bool ResolveTutorial(string tutorialId) =>
            PevtGameHost.Tutorials != null
            && !string.IsNullOrEmpty(tutorialId)
            && PevtGameHost.Safe(() => TX.getTX(tutorialId, true, false, null) != null, false);

        public void ShowTutorial(string tutorialId)
        {
            PevtGameHost.Guard("ShowTutorial", () =>
            {
                ITutorialBox box = PevtGameHost.Tutorials;
                if (box == null)
                    return;

                box.addActiveFlag(PevtGameHost.Flag);
                box.AddText(tutorialId, 0, null);
                _tutorials.Add(tutorialId);
            });
        }

        /// <summary>教程框是玩家自行关闭的浮层；等到本条被移出为止。</summary>
        public PevtWait WaitTutorialClose() =>
            new PevtPredicateWait(
                () => _tutorials.Count == 0 || PevtGameHost.AdvancePressed(),
                "教程框关闭输入",
                allowsIndefiniteWait: true);

        public void HideTutorial(string tutorialId)
        {
            PevtGameHost.Guard("HideTutorial", () =>
            {
                PevtGameHost.Tutorials?.RemText(tutorialId);
                _tutorials.Remove(tutorialId);
            });
        }

        public void ClearTutorials()
        {
            PevtGameHost.Guard("ClearTutorials", () =>
            {
                ITutorialBox box = PevtGameHost.Tutorials;
                if (box == null)
                    return;

                box.RemText(true, true);
                box.remActiveFlag(PevtGameHost.Flag);
                _tutorials.Clear();
            });
        }

        // ---- 持久状态变更提示 ----
        // 走的是 UI 日志框的提示行（<see cref="UILog.AddAlertTX"/>），和 @alert 同一条通道。刻意不用原版
        // <c>UILog.AddGetItem</c> / <c>AddMoney</c>，因为那两个入口要求传入原版 <c>NelItem</c> / <c>CoinEntry</c> 对象，
        // 而中立服务边界上只有字符串 ID 和数值。

        public void NotifyItemChange(string itemId, int delta, int count) =>
            Alert(delta >= 0
                ? $"{itemId} +{delta} ({count})"
                : $"{itemId} {delta} ({count})");

        public void NotifyMoneyChange(int delta, int amount) =>
            Alert(delta >= 0 ? $"+{delta} ({amount})" : $"{delta} ({amount})");

        public void NotifySkillChange(string skillId, bool owned) =>
            Alert(owned ? $"{skillId} +" : $"{skillId} -");

        private void Alert(string text) =>
            PevtGameHost.Guard("Notify", () => Ui?.LogBox?.AddAlertTX(text, UILogRow.TYPE.ALERT));

        /// <summary>
        /// 事件结束、替换或异常时把本事件改过的 UI 临时状态全部还原。
        /// 会话恢复表本来也会逐条还原，这里是最后一道兜底：会话恢复失败时不至于把黑边留在屏幕上。
        /// </summary>
        public void ReleaseAll()
        {
            _letterboxAnimating = false;
            if (_letterbox)
                SetLetterboxVisible(false, 0);
            if (_blur)
                SetBlurVisible(false, 0);
            if (_statusHidden)
                SetStatusVisible(true);
            if (_portraitHidden)
                SetPortraitVisible(true);
            if (_uiDisabled)
                SetGlobalVisible(true);
            if (_titleShown)
                HideTitle(0);
            if (_tutorials.Count > 0)
                ClearTutorials();
            ClearLetterboxOverride();
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

    /// <summary>
    /// 输入策略。能力是 PEVT 自己的一小组名字，逐个映射到原版对应的开关；按键直接对应 <see cref="KEY.SIMKEY"/>，
    /// 通过 <see cref="EV.addAllocEvHandleKey"/> 分配给事件，且只改事件期间的临时策略，不动任何持久设置。
    /// </summary>
    internal sealed class PevtGameInput : IPevtInput
    {
        private static readonly string[] Capabilities = { "skip", "log", "fast_travel", "menu" };

        public bool ValidateCapability(string capability)
        {
            foreach (string candidate in Capabilities)
            {
                if (candidate == capability)
                    return true;
            }

            return false;
        }

        public void SetCapability(string capability, bool enabled)
        {
            PevtGameHost.Guard("SetCapability", () =>
            {
                switch (capability)
                {
                    case "skip":
                        EV.deny_skip = !enabled;
                        break;
                    case "log":
                        if (PevtGameHost.Log != null)
                            PevtGameHost.Log.allow_msglog = enabled;
                        break;
                    case "fast_travel":
                        Flag(PevtGameHost.Map?.FlgFastTravelDeclined, !enabled);
                        break;
                    case "menu":
                        Flag(PevtGameHost.Map?.FlagDeclineOpenGm, !enabled);
                        break;
                }
            });
        }

        private static void Flag(Flagger flagger, bool set)
        {
            if (flagger == null)
                return;

            if (set)
                flagger.Add(PevtGameHost.Flag);
            else
                flagger.Rem(PevtGameHost.Flag);
        }

        public bool ValidateKey(string key) =>
            !string.IsNullOrEmpty(key) && Enum.IsDefined(typeof(KEY.SIMKEY), key.ToUpperInvariant());

        public void SetKeyEnabled(string key, bool enabled)
        {
            PevtGameHost.Guard("SetKeyEnabled", () =>
            {
                if (!ValidateKey(key))
                    return;

                var simKey = (KEY.SIMKEY)Enum.Parse(typeof(KEY.SIMKEY), key.ToUpperInvariant());
                EV.addAllocEvHandleKey(simKey, enabled);
            });
        }
    }
}
