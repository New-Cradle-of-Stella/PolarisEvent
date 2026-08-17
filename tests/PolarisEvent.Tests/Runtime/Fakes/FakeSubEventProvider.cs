using System;
using System.Collections.Generic;
using Polaris.Pevt.Runtime;

namespace Polaris.Pevt.Core.Tests.Runtime.Fakes
{
    /// <summary>
    /// <c>callevt</c> 的内存目标表。可以在事件已经开始执行之后再登记新目标——
    /// 这正是"晚注册"要验证的行为：目标存在性是运行期事实，不是编译期事实。
    /// </summary>
    public sealed class FakeSubEventProvider : IPevtSubEventProvider
    {
        private readonly Dictionary<string, PevtCompiledProgram> _events = new Dictionary<string, PevtCompiledProgram>(StringComparer.Ordinal);

        /// <summary>被标记为"有多个来源"的事件 ID，用来验证 PEVTR4302。</summary>
        public HashSet<string> Ambiguous { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>被标记为"解析到了但起不来"的事件 ID，用来验证 PEVTR4304。</summary>
        public HashSet<string> StartFailures { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void Add(string eventId, PevtCompiledProgram program) => _events[eventId] = program;

        public PevtSubEventStatus TryResolve(string eventId, out PevtCompiledProgram program, out bool declaresAsync)
        {
            program = null;
            declaresAsync = false;

            if (Ambiguous.Contains(eventId))
                return PevtSubEventStatus.Ambiguous;

            if (!_events.TryGetValue(eventId, out program))
                return PevtSubEventStatus.NotFound;

            if (StartFailures.Contains(eventId))
            {
                program = null;
                return PevtSubEventStatus.StartFailed;
            }

            declaresAsync = program.HasAsyncCapability;
            return PevtSubEventStatus.Found;
        }
    }
}
