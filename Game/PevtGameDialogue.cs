using System;
using System.Collections.Generic;
using evt;
using nel;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 对话适配器。
    ///
    /// 文本走的是 <see cref="NelMSG.makeMessage(List{string}, EvPerson, bool, bool)"/>——直接交一组
    /// 字符串，而不是原版 <c>MSG</c> 命令那条"事件名 + 标签 → 从 .cmd 里回读内容"的路径。
    /// PEVT 的真相是它自己的源文本，任何回到 <see cref="EvReader"/> 的做法都会把内容所有权
    /// 交还给原版解释器。
    ///
    /// 推进逻辑复刻原版 <c>EV.progressMsg</c> 的判定顺序（先把字全部显示出来，再等下一次确认键
    /// 才翻页），但由 PEVT 自己的等待驱动，因为 <c>EV.state</c> 不会进入 MESSAGE。
    /// </summary>
    internal sealed class PevtGameDialogue : IPevtDialogue
    {
        /// <summary>没有说话人时使用的原版旁白短键。</summary>
        private const string NarratorPerson = "_";

        private readonly PevtActorRegistry _actors;
        private readonly string _eventId;

        /// <summary>被 <c>@talker_bind</c> 改过资料的人物，事件结束时逐个还原。</summary>
        private readonly Dictionary<string, string> _boundNames = new Dictionary<string, string>(StringComparer.Ordinal);

        private string _speakerActorId;
        private string _personKey = NarratorPerson;
        private NelMSG _current;
        private int _logIndex;
        private bool _logEnabled = true;
        private bool _autoAdvance;

        public PevtGameDialogue(PevtActorRegistry actors, string eventId)
        {
            _actors = actors ?? throw new ArgumentNullException(nameof(actors));
            _eventId = eventId ?? string.Empty;
        }

        // ---- 说话人 ----

        public void SelectSpeaker(string actorId)
        {
            _speakerActorId = actorId;
            _personKey = ResolvePersonKey(actorId);
        }

        public void ClearSpeaker()
        {
            _speakerActorId = null;
            _personKey = NarratorPerson;
        }

        /// <summary>
        /// 把 PEVT 人物 ID 映射到原版 <see cref="EvPerson"/> 的键。
        ///
        /// 内置人物用目录里声明的原版短键（<c>aic:noel</c> → <c>n</c>），这样原版立绘、语音和
        /// 名字配色都能直接复用；模组人物没有短键，就以最终人物 ID 本身建一个新的 EvPerson。
        /// 短键只在这里出现一次——它不会成为公开参数，也不会写回人物注册表。
        /// </summary>
        private string ResolvePersonKey(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || !_actors.Directory.TryGetActor(actorId, out ActorRegistration registration))
                return NarratorPerson;

            ActorDefinition actor = registration.Actor;
            string legacy = actor.LegacyPerson ?? actor.DefaultPortrait?.LegacyPerson;
            if (!string.IsNullOrEmpty(legacy))
                return legacy;

            string displayName = actor.DisplayKey != null
                ? PevtGameHost.Safe(() => TX.Get(actor.DisplayKey, actor.DisplayName ?? actor.LocalId), actor.LocalId)
                : actor.DisplayName ?? actor.LocalId;

            PevtGameHost.Guard("ResolvePerson", () => EvPerson.getPerson(actorId, displayName));
            return actorId;
        }

        private EvPerson CurrentPerson() =>
            PevtGameHost.Safe(() => EvPerson.getPerson(_personKey, null), null);

        // ---- 文本 ----

        public void OpenText(string text)
        {
            PevtGameHost.Guard("OpenText", () =>
            {
                if (!(PevtGameHost.Messages is NelMSGContainer container))
                    return;

                EvPerson person = CurrentPerson();

                // getByDrawer 按 hkds id 复用同一个气泡；同一个人连说几句不会每句都新建一个框。
                NelMSG drawer = container.getByDrawer(_personKey, false, NelMSG.HKDSTYPE._MAX, false, -1);
                if (drawer == null)
                    return;

                if (!drawer.isSame(_personKey, person))
                    drawer.initPerson(_personKey, person);

                var lines = new List<string> { text ?? string.Empty };
                drawer.makeMessage(lines, person, true, false);
                drawer.activateFront();

                _current = drawer;
                RecordLog(text);
            });
        }

        /// <summary>
        /// 回看记录。原版日志保存的是"事件名 + 标签"并在展开时回读 .cmd，PEVT 没有 .cmd，
        /// 因此用 <see cref="EvTextLog.AddBufferInjection"/> 把真实文本直接挂到刚加的那条记录上。
        /// </summary>
        private void RecordLog(string text)
        {
            if (!_logEnabled)
                return;

            EvTextLog log = PevtGameHost.Log;
            if (log == null || !log.allow_msglog)
                return;

            string label = "pevt#" + _logIndex++;
            log.AddBuffer(_personKey, _eventId, label);
            log.AddBufferInjection(new[] { text ?? string.Empty });
        }

        public PevtWait WaitAdvance() => new PevtDialogueAdvanceWait(this);

        public void CommitLog()
        {
            PevtGameHost.Guard("CommitLog", () => PevtGameHost.Log?.ExplodeBuffer());
            _current = null;
        }

        // ---- 推进状态，供 PevtDialogueAdvanceWait 使用 ----

        private bool AllCharsShown() =>
            PevtGameHost.Safe(() => PevtGameHost.Messages?.isAllCharsShown() ?? true, true);

        private void ShowImmediate() =>
            PevtGameHost.Guard("ShowImmediate", () => PevtGameHost.Messages?.showImmediate(false, false));

        /// <summary>翻到下一段；没有下一段时收起消息框并返回 true 表示这次 <c>@say</c> 结束。</summary>
        private bool ProgressOrFinish()
        {
            bool finished = true;
            PevtGameHost.Guard("ProgressMessage", () =>
            {
                IMessageContainer messages = PevtGameHost.Messages;
                if (messages == null)
                    return;

                SND.Ui.play("talk_progress", false);
                if (messages.progressNextParagraph())
                {
                    finished = false;
                    return;
                }

                messages.hideMsg(false);
                IN.clearSubmitPushDown(false);
            });

            return finished;
        }

        private bool AutoAdvance => _autoAdvance;

        // ---- 文本板 ----

        public void OpenBoard(string text, string style)
        {
            PevtGameHost.Guard("OpenBoard", () =>
            {
                NelItemManager items = PevtGameHost.Map?.IMNG;
                if (items == null)
                    return;

                NelItemManager.POPUP popup = NelItemManager.POPUP.FRAMED_BOARD | NelItemManager.POPUP._FOCUS_MODE;
                if (style == "paper")
                    popup = NelItemManager.POPUP.FRAMED_PAPER | NelItemManager.POPUP._FOCUS_MODE;
                if (style == "center")
                    popup |= NelItemManager.POPUP._TX_CENTER;

                items.showPopUpAbs(popup, text ?? string.Empty, 0f, 50f, true);
            });
        }

        public PevtWait WaitBoardClose() =>
            new PevtPredicateWait(() => !BoardActive(), "文本板关闭");

        /// <summary>
        /// <c>EvtWait(false)</c> 在原版里的含义是"还得继续等"——它为 true 表示描述框上还有一个
        /// 处于 focus 的任务。所以这里判定"文本板仍然打开"就是它返回 true。
        /// </summary>
        private static bool BoardActive() =>
            PevtGameHost.Safe(() => PevtGameHost.Map?.IMNG?.get_DescBox()?.EvtWait(false) == true, false);

        public void CloseBoard() =>
            PevtGameHost.Guard("CloseBoard", () => PevtGameHost.Map?.IMNG?.hideWindows());

        // ---- 资料与显示开关 ----

        public void BindProfile(string actorId, string displayName, string voiceId)
        {
            PevtGameHost.Guard("BindProfile", () =>
            {
                string key = ResolvePersonKey(actorId);
                EvPerson person = EvPerson.getPerson(key, displayName);
                if (person == null)
                    return;

                if (!_boundNames.ContainsKey(key))
                    _boundNames[key] = person.talker_name;

                if (!string.IsNullOrEmpty(displayName))
                    person.talker_name = displayName;
                if (!string.IsNullOrEmpty(voiceId))
                    person.talk_snd = voiceId;
            });
        }

        public void ResetProfile(string actorId)
        {
            PevtGameHost.Guard("ResetProfile", () =>
            {
                string key = ResolvePersonKey(actorId);
                if (!_boundNames.TryGetValue(key, out string original))
                    return;

                EvPerson person = EvPerson.getPerson(key, null);
                if (person != null)
                    person.talker_name = original;

                _boundNames.Remove(key);
            });
        }

        public void SetVisible(bool visible, bool immediate)
        {
            PevtGameHost.Guard("SetDialogueVisible", () =>
            {
                if (visible)
                    _current?.activateFront();
                else
                    PevtGameHost.Messages?.hideMsg(immediate);
            });
        }

        /// <summary>
        /// 保持当前消息不被下一条自动收起。
        ///
        /// 走的不是 <see cref="IMessageContainer.hold"/>——那个方法在 ver029 的
        /// <see cref="NelMSGContainer"/> 里是个空实现，调它等于什么都没做。真正控制这件事的是
        /// <c>auto_msg_hide</c>：它决定新消息出现时是否顺手隐藏上一条，所以"保持"就是把它关掉。
        /// 这个开关是双向的，<c>@dialogue_hold(false)</c> 与会话恢复都能真正还原。
        /// </summary>
        public void SetHold(bool enabled) =>
            PevtGameHost.Guard("SetHold", () =>
            {
                if (PevtGameHost.Messages is NelMSGContainer container)
                    container.auto_msg_hide = !enabled;
            });

        public void SetAutoAdvance(bool enabled) => _autoAdvance = enabled;

        public void SetLogEnabled(bool enabled)
        {
            _logEnabled = enabled;
            PevtGameHost.Guard("SetLogEnabled", () =>
            {
                EvTextLog log = PevtGameHost.Log;
                if (log != null)
                    log.allow_msglog = enabled;
            });
        }

        /// <summary>事件结束时把改过的人物资料全部还原。</summary>
        public void RestoreProfiles()
        {
            var keys = new List<string>(_boundNames.Keys);
            foreach (string key in keys)
            {
                string original = _boundNames[key];
                PevtGameHost.Guard("RestoreProfile", () =>
                {
                    EvPerson person = EvPerson.getPerson(key, null);
                    if (person != null)
                        person.talker_name = original;
                });
            }

            _boundNames.Clear();
        }

        /// <summary>
        /// 一次 <c>@say</c> 的推进等待：字没显示完时确认键先把字补全，显示完后再按一次才结束。
        /// 判定顺序照抄原版 <c>EV.progressMsg</c>，否则玩家会觉得 PEVT 事件"手感不一样"。
        /// </summary>
        private sealed class PevtDialogueAdvanceWait : PevtWait
        {
            private readonly PevtGameDialogue _dialogue;
            private long _startFrame = -1;

            public PevtDialogueAdvanceWait(PevtGameDialogue dialogue) => _dialogue = dialogue;

            public override string ProgressSource => "对话推进输入";

            protected override void OnTick(PevtWaitContext context)
            {
                if (_startFrame < 0)
                    _startFrame = context.Frame;

                bool shown = _dialogue.AllCharsShown();
                bool pressed = PevtGameHost.AdvancePressed();

                if (!shown)
                {
                    // 刚开口的一两帧内不接受输入，避免上一条消息的按键被吃进这一条。
                    if (pressed && context.Frame - _startFrame > 1)
                        _dialogue.ShowImmediate();
                    return;
                }

                if (!pressed && !_dialogue.AutoAdvance)
                    return;

                if (_dialogue.ProgressOrFinish())
                    CompleteSucceeded();
                else
                    _startFrame = context.Frame;
            }
        }
    }
}
