using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 一次只读查询的可观察记录：键、参数、原始结果、目标类型与失败原因。
    /// 失败时原始结果可能根本不存在（连键都没找到），所以 <see cref="Value"/> 是可空的。
    /// </summary>
    public sealed class PevtGameQueryTrace
    {
        private PevtGameQueryTrace(
            long frame,
            string eventId,
            string key,
            IReadOnlyList<string> arguments,
            PevtType targetType,
            PevtQueryValue? value,
            string diagnosticId,
            string failure)
        {
            Frame = frame;
            EventId = eventId ?? string.Empty;
            Key = key ?? string.Empty;
            Arguments = arguments ?? Array.Empty<string>();
            TargetType = targetType;
            Value = value;
            DiagnosticId = diagnosticId;
            Failure = failure;
        }

        /// <summary>发生这次查询的 PolarisEvent 更新帧号。</summary>
        public long Frame { get; }

        public string EventId { get; }

        public string Key { get; }

        public IReadOnlyList<string> Arguments { get; }

        /// <summary>调用点要求的 PEVT 普通类型。</summary>
        public PevtType TargetType { get; }

        /// <summary>查询表回报的原始结果；键都没找到时为 null。</summary>
        public PevtQueryValue? Value { get; }

        /// <summary>失败时的运行诊断编号；成功时为 null。</summary>
        public string DiagnosticId { get; }

        /// <summary>失败原因；成功时为 null。</summary>
        public string Failure { get; }

        public bool IsSuccess => DiagnosticId == null;

        /// <summary>形如 <c>@game_read_int("DANGER", "3")</c> 的调用还原。</summary>
        public string Call
        {
            get
            {
                var text = new System.Text.StringBuilder("@game_read_")
                    .Append(TargetType.DisplayName())
                    .Append("(\"")
                    .Append(Key)
                    .Append('"');

                foreach (string argument in Arguments)
                    text.Append(", \"").Append(argument).Append('"');

                return text.Append(')').ToString();
            }
        }

        internal static PevtGameQueryTrace Succeeded(
            long frame, string eventId, string key, IReadOnlyList<string> arguments,
            PevtType targetType, PevtQueryValue value) =>
            new PevtGameQueryTrace(frame, eventId, key, Copy(arguments), targetType, value, null, null);

        internal static PevtGameQueryTrace Failed(
            long frame, string eventId, string key, IReadOnlyList<string> arguments, PevtType targetType,
            PevtQueryValue? value, string diagnosticId, string failure) =>
            new PevtGameQueryTrace(frame, eventId, key, Copy(arguments), targetType, value, diagnosticId, failure);

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
                return Array.Empty<string>();

            var copy = new string[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
                copy[i] = arguments[i];
            return copy;
        }

        public override string ToString() =>
            IsSuccess
                ? Call + " -> " + (Value.HasValue ? Value.Value.Describe() : "?")
                : Call + " !! " + DiagnosticId + " " + Failure;
    }

    /// <summary>
    /// 最近若干次只读查询的环形日志。
    ///
    /// 它**只**服务于观测（F8 的 Queries 分页与测试断言）：解释器从不读它，因此丢掉最老的记录
    /// 不会改变任何执行语义。日志是进程级的，和 <c>$raw</c> 通道一样——作者在 F8 里想看的是
    /// "刚才那几次查询"，而不是"当前这个事件实例的那几次"。
    /// </summary>
    public sealed class PevtGameQueryLog
    {
        /// <summary>环形缓冲的容量。够覆盖一屏 F8 列表，也不至于把失败记录顶掉太快。</summary>
        public const int Capacity = 48;

        private readonly List<PevtGameQueryTrace> _entries = new List<PevtGameQueryTrace>(Capacity);

        /// <summary>进程级共享日志。</summary>
        public static PevtGameQueryLog Shared { get; } = new PevtGameQueryLog();

        /// <summary>最近的记录，最新的在最后。</summary>
        public IReadOnlyList<PevtGameQueryTrace> Recent => _entries;

        /// <summary>本进程一共记过多少次查询（含已经被顶掉的）。</summary>
        public long TotalCount { get; private set; }

        /// <summary>其中失败了多少次。</summary>
        public long FailureCount { get; private set; }

        /// <summary>最近一次记录；一次都没有时为 null。</summary>
        public PevtGameQueryTrace Last => _entries.Count == 0 ? null : _entries[_entries.Count - 1];

        internal void Add(PevtGameQueryTrace trace)
        {
            if (trace == null)
                return;

            TotalCount++;
            if (!trace.IsSuccess)
                FailureCount++;

            if (_entries.Count == Capacity)
                _entries.RemoveAt(0);
            _entries.Add(trace);
        }

        public void Clear()
        {
            _entries.Clear();
            TotalCount = 0;
            FailureCount = 0;
        }
    }
}
