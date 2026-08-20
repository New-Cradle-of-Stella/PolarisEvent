using System;
using System.Collections.Generic;
using evt;
using nel;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Utils;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 对话适配器。文本直接交一组字符串给 <see cref="NelMSG.makeMessage(List{string}, EvPerson, bool, bool)"/>，
    /// 不走原版"从 .cmd 回读内容"的路径；推进逻辑复刻 <c>EV.progressMsg</c> 的判定顺序，但由 PEVT 自己的等待驱动。
    /// </summary>
    internal sealed class PevtGameDialogue : IPevtDialogue
    {
        /// <summary>
        /// 原版独白仍走 Noel 的消息通道（<c>HKDS n C @CB MONOLOGUE</c>），
        /// 只是 MONOLOGUE 边界隐藏说话人标题与气泡尾巴。
        /// </summary>
        private const string NarratorPerson = "n";

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
        private bool _monologue;

        public PevtGameDialogue(PevtActorRegistry actors, string eventId)
        {
            _actors = actors ?? throw new ArgumentNullException(nameof(actors));
            _eventId = eventId ?? string.Empty;
        }

        // ---- 说话人 ----

        public void SelectSpeaker(string actorId)
        {
            _monologue = false;
            _speakerActorId = actorId;
            _personKey = ResolvePersonKey(actorId);
        }

        public void ClearSpeaker()
        {
            _monologue = true;
            _speakerActorId = null;
            _personKey = NarratorPerson;
        }

        /// <summary>
        /// 把 PEVT 人物 ID 映射到原版 <see cref="EvPerson"/> 的键。
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

                // 原版 MSG 会在气泡出现前就把 T/THINK 之类的类型交给 getByDrawer。
                // PEVT 的样式写在文本首标签中，若等文本渲染器逐字解析，会先闪过一帧普通气泡。
                // 所以在创建/复用气泡时先预取首个样式标签，标签本身仍交给原版渲染器处理。
                NelMSG.HKDSTYPE initialType = InitialBubbleType(text);
                NelMSG drawer = container.getByDrawer(_personKey, false, initialType, false, -1);
                if (drawer == null)
                    return;

                if (!drawer.isSame(_personKey, person))
                    drawer.initPerson(_personKey, person);

                if (_monologue)
                {
                    // 对齐原版 s13_4b.cmd：HKDS n C @CB MONOLOGUE。
                    // makeMessage 会在 reserveText 前消费这份信息并切换到无标题、无尾巴的居中旁白框。
                    drawer.readInfo(new NelMSGContainer.HkdsInfo("C", "@CB", "MONOLOGUE", "="));
                }

                // 中文原版默认缩字而不换行。PEVT 在提交前自行按可见字符宽度插入真实换行，
                // 不依赖 TextRenderer.auto_wrap 的语言与气泡初始化时序。
                float textWidth = _monologue ? 665f : drawer.Tx.max_swidth_px;
                string displayText = TextLayoutUtils.WrapRichText(text ?? string.Empty, textWidth);
                ConfigureManualWrap(drawer);

                var lines = new List<string> { displayText };
                drawer.makeMessage(lines, person, initialType == NelMSG.HKDSTYPE._MAX, false);
                ConfigureManualWrap(drawer);

                drawer.activateFront();

                _current = drawer;
                RecordLog(text);
            });
        }

        private static void ConfigureManualWrap(NelMSG drawer)
        {
            if (drawer == null)
                return;

            drawer.Tx.auto_condense = false;
            drawer.Tx.auto_condense_line = false;
            drawer.Tx.auto_wrap = false;
        }

        /// <summary>
        /// 预取原版文本渲染器支持的段首气泡标签。只识别紧贴文本开头的完整标签，
        /// 避免把正文中的 <c>&lt;think&gt;</c> 误当成初始样式。
        /// </summary>
        private static NelMSG.HKDSTYPE InitialBubbleType(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '<')
                return NelMSG.HKDSTYPE._MAX;

            int close = text.IndexOf('>');
            if (close <= 1)
                return NelMSG.HKDSTYPE._MAX;

            switch (text.Substring(1, close - 1).ToLowerInvariant())
            {
                case "normal": return NelMSG.HKDSTYPE.NORMAL;
                case "circ": return NelMSG.HKDSTYPE.CIRC;
                case "think": return NelMSG.HKDSTYPE.THINK;
                case "angry": return NelMSG.HKDSTYPE.ANGRY;
                case "device": return NelMSG.HKDSTYPE.DEVICE;
                case "evil": return NelMSG.HKDSTYPE.EVIL;
                case "book": return NelMSG.HKDSTYPE.BOOK;
                case "oneline": return NelMSG.HKDSTYPE.ONELINE;
                default: return NelMSG.HKDSTYPE._MAX;
            }
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
