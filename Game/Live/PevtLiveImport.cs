using System;
using System.Collections.Generic;
using System.Text;
using Polaris.Event.Game.Debugging;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game.Live
{
    /// <summary>一次外部导入的结果，回执给调试页页脚或 PolarisTools 的状态栏。</summary>
    internal sealed class PevtLiveApplyOutcome
    {
        internal PevtLiveApplyOutcome(bool ok, string message)
        {
            Ok = ok;
            Message = message ?? string.Empty;
        }

        internal bool Ok { get; }

        internal string Message { get; }
    }

    /// <summary>
    /// 外部导入的唯一落地点：磁盘扫描与 PolarisTools 热重载推送最终都走到这里，两条入口共用同一段校验、登记、覆盖判定与重启策略。
    /// 只能在 Unity 主线程调用：它会改注册表并可能重启当前事件，管道线程要经 <see cref="PevtLivePump"/> 排队。
    /// </summary>
    internal static class PevtLiveImport
    {
        private static readonly string[] NoStrings = new string[0];

        /// <summary>导入代数，从 0 开始；每应用一次 +1。调试页与回执用它判断"推送到了没有"。</summary>
        internal static int Generation { get; private set; }

        internal static DateTime LastAppliedAt { get; private set; }

        /// <summary>本次导入的来源描述，例如 <c>disk: D:\mod\Events</c> 或 <c>PolarisTools push</c>。</summary>
        internal static string LastOrigin { get; private set; } = string.Empty;

        internal static string LastSummary { get; private set; } = string.Empty;

        /// <summary>最近一次导入的逐文件结果；从未导入过时为 null。</summary>
        internal static PevtExternalApplyReport LastReport { get; private set; }

        /// <summary>最近一次磁盘扫描里读不出来的文件。</summary>
        internal static IReadOnlyList<string> LastUnreadable { get; private set; } = NoStrings;

        /// <summary>最近一次导入之后被重启的事件 ID；没有重启过时为空串。</summary>
        internal static string LastRestartedEventId { get; private set; } = string.Empty;

        /// <summary>当前生效的外部候选，按事件 ID 的序数序。</summary>
        internal static IReadOnlyList<PevtEventCandidate> ActiveExternal
        {
            get
            {
                PevtGameRuntime runtime = PolarisEventComponent.Runtime;
                if (runtime == null)
                    return new PevtEventCandidate[0];

                var result = new List<PevtEventCandidate>();
                foreach (PevtEventCandidate candidate in runtime.Registry.Events.ActiveEvents)
                {
                    if (candidate.Origin == PevtEventOrigin.External)
                        result.Add(candidate);
                }

                result.Sort((left, right) => string.CompareOrdinal(left.EventId, right.EventId));
                return result;
            }
        }

        /// <summary>扫描并导入一个目录。目录不存在时按"导入了 0 个"处理——作者可能只用推送通道。</summary>
        internal static PevtLiveApplyOutcome ApplyFromDisk(string configuredDirectory)
        {
            string directory = PevtLiveDirectory.Resolve(configuredDirectory);
            PevtLiveDirectoryScan scan = PevtLiveDirectory.Scan(directory);

            if (!scan.Exists)
                return Apply(scan.Sources, "disk: " + directory + "（目录不存在）", null);

            PevtLiveApplyOutcome outcome = Apply(scan.Sources, "disk: " + directory, null);
            LastUnreadable = scan.Unreadable;

            if (scan.Unreadable.Count == 0 && !scan.Truncated)
                return outcome;

            var message = new StringBuilder(outcome.Message);
            if (scan.Truncated)
            {
                message.Append(Environment.NewLine).Append("目录里的 `.pevt` 超过上限，只导入了前 ")
                    .Append(scan.Sources.Count).Append(" 个。");
            }

            foreach (string entry in scan.Unreadable)
                message.Append(Environment.NewLine).Append("读不出来：").Append(entry);

            LastSummary = message.ToString();
            return new PevtLiveApplyOutcome(outcome.Ok, LastSummary);
        }

        /// <summary>
        /// 整批应用一组外部源。这是"替换"而不是"追加"：上一次导入里有、这一次没有的事件
        /// 会从 `/event` 消失，作者删掉一个 `.pevt` 之后它就不该还能被启动。
        /// </summary>
        /// <param name="focusPath">
        /// 触发本次导入的文件（推送通道会带上作者刚保存的那一个）。只进回执文案；
        /// 重启判定看的是"正在跑的事件有没有被重新导入"，与焦点无关。
        /// </param>
        internal static PevtLiveApplyOutcome Apply(
            IReadOnlyList<PevtExternalSource> sources,
            string origin,
            string focusPath)
        {
            PevtGameRuntime runtime = PolarisEventComponent.Runtime;
            if (runtime == null)
                return new PevtLiveApplyOutcome(false, "PolarisEvent 组件尚未初始化，无法导入外部事件。");

            PevtExternalApplyReport report;
            try
            {
                report = runtime.Registry.ApplyExternal(sources ?? new PevtExternalSource[0]);
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Live.Apply");
                return new PevtLiveApplyOutcome(false, "导入外部事件时出错：" + ex.Message);
            }

            // 编译缓存按"事件 ID + 内容哈希"索引，旧版本不会被误命中；这里清掉只是不让它们越积越多。
            foreach (PevtEventCandidate candidate in report.Registered)
                runtime.Host.DropCompiled(candidate.EventId);

            Generation++;
            LastAppliedAt = DateTime.Now;
            LastOrigin = origin ?? string.Empty;
            LastReport = report;
            LastUnreadable = NoStrings;

            var summary = new StringBuilder(report.Describe());
            string restarted = RestartIfRunning(report);
            LastRestartedEventId = restarted ?? string.Empty;

            if (!string.IsNullOrEmpty(restarted))
                summary.Append(Environment.NewLine).Append("已重启正在运行的 `").Append(restarted).Append("`。");
            else if (!string.IsNullOrEmpty(focusPath))
                summary.Append(Environment.NewLine).Append("触发文件：").Append(focusPath);

            LastSummary = summary.ToString();
            return new PevtLiveApplyOutcome(report.FailedCount == 0, LastSummary);
        }

        /// <summary>撤销全部外部候选，`/event` 回到只有嵌入源的状态。</summary>
        internal static PevtLiveApplyOutcome Clear()
        {
            PevtGameRuntime runtime = PolarisEventComponent.Runtime;
            if (runtime == null)
                return new PevtLiveApplyOutcome(false, "PolarisEvent 组件尚未初始化。");

            int removed = runtime.Registry.ClearExternal();
            Generation++;
            LastAppliedAt = DateTime.Now;
            LastOrigin = "cleared";
            LastReport = null;
            LastUnreadable = NoStrings;
            LastRestartedEventId = string.Empty;
            LastSummary = removed + " 个外部候选已撤销。";
            return new PevtLiveApplyOutcome(true, LastSummary);
        }

        /// <summary>
        /// 当前根事件刚被重新导入时把它从头重启，返回重启的事件 ID；没有重启则返回 null。
        /// 不做"就地换掉指令流"：正在跑的事件已经建立了等待、协程和所有权树，接到新指令流上无法保证语义，从头重启才是唯一能说清楚的语义。
        /// </summary>
        private static string RestartIfRunning(PevtExternalApplyReport report)
        {
            if (!PevtDebugSettings.LiveRestartEnabled)
                return null;

            PevtEventInstance current = PolarisEventComponent.Current;
            if (current == null)
                return null;

            foreach (PevtEventCandidate candidate in report.Registered)
            {
                if (!string.Equals(candidate.EventId, current.EventId, StringComparison.Ordinal))
                    continue;

                try
                {
                    PolarisEventComponent.Change(candidate.EventId);
                    return candidate.EventId;
                }
                catch (Exception ex)
                {
                    // 新版本可能有运行期起不来的问题（比如用到了尚未支持的构造）。这不该让整次导入失败：
                    // 事件已经登记好了，作者改完再存一次即可。
                    PolarisAPI.Errors.Report(ex, "PolarisEvent.Live.Restart");
                    return null;
                }
            }

            return null;
        }
    }
}
