using System;
using System.Collections.Generic;
using evt;
using Polaris.Pevt.Runtime.Raw;

namespace Polaris.Event.Game.RawCmd
{
    /// <summary>
    /// <c>$raw cmd</c> 的游戏侧实现：唯一允许创建 <see cref="EvReader"/> 并进入
    /// <c>EV.readOneLine</c> 的地方。
    /// </summary>
    internal sealed class PevtGameRawCommandBridge : IPevtRawCommandBridge
    {
        /// <summary>会话 reader 名前缀。<c>%</c> 让原版跳过磁盘查找。</summary>
        public const string NamePrefix = "%PEVT_RAW@";

        /// <summary>
        /// PEVT 根事件期间常驻的原版 reader。raw 片段作为它的子事件执行；这样一段 raw 读完时
        /// <c>EV.evEnd</c> 只会切回本 reader，不会走“最后一个原版事件结束”的全局清场分支。
        /// </summary>
        private const string ScopeSource = "LABEL __PEVT_SCOPE_LOOP\nWAIT 3600\nGOTO __PEVT_SCOPE_LOOP";

        private int _sequence;
        private string _scopeName;
        private EvReader _scopeReader;

        /// <summary>
        /// 在 PEVT 第一条指令运行前建立原版解释器的局部宿主。它只维持 EV 生命周期，
        /// 不产生演出；真正的 raw reader 各自保留自己的游标、标签和临时变量。
        /// </summary>
        public bool OpenScope()
        {
            if (!PevtGameHost.Ready)
                return false;

            if (ScopeAlive())
                return true;

            _scopeName = NamePrefix + "SCOPE@" + (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                EV.setEventContent(_scopeName, ScopeSource);
                _scopeReader = CreateScopeReader();
                EV.stackReader(_scopeReader);
                return ScopeAlive();
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Game.RawCmd.OpenScope");
                CloseScope();
                return false;
            }
        }

        /// <summary>释放常驻 reader。必须在 PEVT 退出事件模式之前调用。</summary>
        public IReadOnlyList<Exception> CloseScope()
        {
            var failures = new List<Exception>();
            string scopeName = _scopeName;

            if (!string.IsNullOrEmpty(scopeName))
            {
                try
                {
                    // 正常状态只有一个当前 scope；强制停止时也可能还有一个排队中的接替 scope。
                    for (int guard = 0; guard < 4; guard++)
                    {
                        EvReader current = EV.getCurrentEvent();
                        if (current != null && string.Equals(current.name, scopeName, StringComparison.Ordinal))
                        {
                            EV.unstackReader(current);
                            continue;
                        }

                        EvReader stacked = EV.getStacked(scopeName);
                        if (stacked == null)
                            break;

                        EV.unstackReader(stacked);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }

                try
                {
                    EV.clearEventContent(scopeName);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _scopeReader = null;
            _scopeName = null;
            return failures.AsReadOnly();
        }

        public IPevtRawCommandSession Begin(string rawText)
        {
            if (!OpenScope())
                return null;

            string name = NamePrefix + (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var profiles = PevtVanillaTalkerSnapshot.Capture();

            EvReader reader = null;
            try
            {
                EV.setEventContent(name, rawText ?? string.Empty);
                reader = new EvReader(name);

                // changeEvent 只终止当前的空 scope，并把 raw 放到栈首；紧接着排一个新的 scope。
                // raw 结束后 EV 因而不会认为“全部事件结束”，也就不会清掉 PEVT 的对话框、图片和电影模式。
                if (!EV.changeEvent(reader))
                    reader = null;

                if (reader != null)
                {
                    _scopeReader = CreateScopeReader();
                    EV.stackReader(_scopeReader);
                }
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Game.RawCmd.Begin");
                reader = null;
            }

            if (reader == null)
            {
                // 压栈失败：立刻把内存内容和人物资料收回去，不留半个会话。
                PevtGameHost.Guard("RawCmd.ClearContent", () => EV.clearEventContent(name));
                profiles.Restore();
                return null;
            }

            return new PevtGameRawCommandSession(name, reader, profiles);
        }

        private EvReader CreateScopeReader()
        {
            var reader = new EvReader(_scopeName)
            {
                do_not_announce = true,
                no_init_load = true,
            };
            return reader;
        }

        private bool ScopeAlive()
        {
            if (string.IsNullOrEmpty(_scopeName))
                return false;

            return PevtGameHost.Safe(() =>
            {
                EvReader current = EV.getCurrentEvent();
                return (current != null && string.Equals(current.name, _scopeName, StringComparison.Ordinal))
                    || EV.getStacked(_scopeName) != null;
            }, false);
        }
    }

    /// <summary>
    /// 一次原版 EV 文本会话。
    /// </summary>
    internal sealed class PevtGameRawCommandSession : IPevtRawCommandSession
    {
        private readonly string _name;
        private readonly EvReader _reader;
        private readonly PevtVanillaTalkerSnapshot _profiles;
        private bool _cancelRequested;

        public PevtGameRawCommandSession(string name, EvReader reader, PevtVanillaTalkerSnapshot profiles)
        {
            _name = name;
            _reader = reader;
            _profiles = profiles;
        }

        public bool IsFinished => PevtGameHost.Safe(() =>
        {
            // getStacked() 不包含正在执行的 curEv。旧实现只查它，raw 一进入 WAIT/MESSAGE 就会被
            // 误判为完成并立刻 unstack，正是“图片闪现、对话中断、下一次启动卡死”的根因。
            if (ReferenceEquals(EV.getCurrentEvent(), _reader))
                return false;
            return EV.getStacked(_name) == null;
        }, true);

        /// <summary>
        /// 原版解释器不向外报告"这一行执行失败"——它自己打日志继续走。因此正常路径下没有失败消息；
        /// 只有会话对象自己丢了（游戏对象被销毁、栈被强制清空）才算异常结束。
        /// </summary>
        public string FailureMessage { get; private set; }

        public void RequestCancel()
        {
            if (_cancelRequested)
                return;

            _cancelRequested = true;
            PevtGameHost.Guard("RawCmd.Unstack", () => EV.unstackReader(_reader));
        }

        public void Release()
        {
            // 顺序固定：先确保 reader 不在栈上，再删内存内容，最后还原人物资料。
            // 反过来的话，一个还在栈上的 reader 会读到已经被删掉的内容。
            PevtGameHost.Guard("RawCmd.Release.Unstack", () =>
            {
                if (ReferenceEquals(EV.getCurrentEvent(), _reader) || EV.getStacked(_name) != null)
                    EV.unstackReader(_reader);
            });

            PevtGameHost.Guard("RawCmd.Release.ClearContent", () => EV.clearEventContent(_name));
            _profiles.Restore();
        }

        /// <summary>供只读诊断查询。</summary>
        public string ReaderName => _name;
    }

    /// <summary>
    /// 原版人物资料快照。<c>TALKER_REPLACE</c> 改写的 <see cref="EvPerson"/> 资料，以及为 <c>mb</c>、<c>x</c>、<c>a</c>
    /// 这类临时键新建的资料，都必须只活在当前 raw 会话里，一次都不会进入 <c>PevtActorRegistry</c>。
    /// </summary>
    internal sealed class PevtVanillaTalkerSnapshot
    {
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _knownKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly bool _captured;

        private PevtVanillaTalkerSnapshot(bool captured) => _captured = captured;

        private struct Entry
        {
            public EvPerson Person;
            public string TalkerName;
            public string TalkSound;
        }

        public static PevtVanillaTalkerSnapshot Capture()
        {
            var snapshot = new PevtVanillaTalkerSnapshot(true);

            PevtGameHost.Guard("RawCmd.CaptureTalkers", () =>
            {
                IDictionary<string, EvPerson> persons = EvPerson.getPersonDictionary();
                if (persons == null)
                    return;

                foreach (KeyValuePair<string, EvPerson> entry in persons)
                {
                    snapshot._knownKeys.Add(entry.Key);
                    if (entry.Value == null)
                        continue;

                    snapshot._entries.Add(new Entry
                    {
                        Person = entry.Value,
                        TalkerName = entry.Value.talker_name,
                        TalkSound = entry.Value.talk_snd,
                    });
                }
            });

            return snapshot;
        }

        public void Restore()
        {
            if (!_captured)
                return;

            foreach (Entry entry in _entries)
            {
                Entry local = entry;
                PevtGameHost.Guard("RawCmd.RestoreTalker", () =>
                {
                    local.Person.talker_name = local.TalkerName;
                    local.Person.talk_snd = local.TalkSound;
                });
            }

            PevtGameHost.Guard("RawCmd.RemoveTemporaryTalkers", () =>
            {
                IDictionary<string, EvPerson> persons = EvPerson.getPersonDictionary();
                if (persons == null)
                    return;

                var temporary = new List<string>();
                foreach (KeyValuePair<string, EvPerson> entry in persons)
                {
                    if (!_knownKeys.Contains(entry.Key))
                        temporary.Add(entry.Key);
                }

                foreach (string key in temporary)
                    persons.Remove(key);
            });
        }
    }
}
