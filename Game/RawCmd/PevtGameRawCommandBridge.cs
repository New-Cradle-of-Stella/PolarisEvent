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

        private int _sequence;

        public IPevtRawCommandSession Begin(string rawText)
        {
            if (!PevtGameHost.Ready)
                return null;

            string name = NamePrefix + (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var profiles = PevtVanillaTalkerSnapshot.Capture();

            EvReader reader = null;
            try
            {
                EV.setEventContent(name, rawText ?? string.Empty);
                reader = EV.stack(name, 0, -1, null, null);
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

        public bool IsFinished =>
            PevtGameHost.Safe(() => EV.getStacked(_name) == null, true);

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
                if (EV.getStacked(_name) != null)
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
