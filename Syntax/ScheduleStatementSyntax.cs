using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// <c>schedule timelineId after frames call _block()</c>（PEVT-E05）。
    /// <c>timelineId</c> 只是一个供 F8 与 <see cref="DuplicateDiagnosticId"/> 使用的声明名，
    /// 不像 <c>handler</c> 那样可以被后续语句引用——<c>flush schedules</c>/<c>clear schedules</c>
    /// 一律作用于当前环境全部尚未触发的项，不按名字挑选。
    /// </summary>
    public sealed class ScheduleStatementSyntax : StatementSyntax
    {
        /// <summary>timelineId 重复声明时使用的诊断编号，绑定阶段引用。</summary>
        public const string DuplicateDiagnosticId = "PEVT7506";

        public SyntaxToken ScheduleKeyword { get; }

        public SyntaxToken TimelineId { get; }

        public SyntaxToken AfterKeyword { get; }

        public ExpressionSyntax Frames { get; }

        public SyntaxToken CallKeyword { get; }

        /// <summary>目标必须是 <c>_block()</c> 形状的自定义事件块调用；解析阶段就把非法形状拒了。</summary>
        public CustomBlockCallExpressionSyntax Target { get; }

        public ScheduleStatementSyntax(
            SyntaxToken scheduleKeyword,
            SyntaxToken timelineId,
            SyntaxToken afterKeyword,
            ExpressionSyntax frames,
            SyntaxToken callKeyword,
            CustomBlockCallExpressionSyntax target)
        {
            ScheduleKeyword = scheduleKeyword;
            TimelineId = timelineId;
            AfterKeyword = afterKeyword;
            Frames = frames;
            CallKeyword = callKeyword;
            Target = target;
        }

        public override TextSpan Span => TextSpan.FromBounds(
            ScheduleKeyword.Span.Start,
            Target != null ? Target.Span.End : CallKeyword.Span.End);

        public override string ToString() => $"Schedule({TimelineId.Text}, {Target})";
    }

    /// <summary><c>flush schedules</c>：立即启动当前环境全部尚未触发的调度项，已触发的不受影响。</summary>
    public sealed class FlushSchedulesStatementSyntax : StatementSyntax
    {
        public SyntaxToken FlushKeyword { get; }

        public SyntaxToken SchedulesKeyword { get; }

        public FlushSchedulesStatementSyntax(SyntaxToken flushKeyword, SyntaxToken schedulesKeyword)
        {
            FlushKeyword = flushKeyword;
            SchedulesKeyword = schedulesKeyword;
        }

        public override TextSpan Span => TextSpan.FromBounds(FlushKeyword.Span.Start, SchedulesKeyword.Span.End);

        public override string ToString() => "FlushSchedules";
    }

    /// <summary><c>clear schedules</c>：丢弃当前环境全部尚未触发的调度项，不停止已经启动的动作。</summary>
    public sealed class ClearSchedulesStatementSyntax : StatementSyntax
    {
        public SyntaxToken ClearKeyword { get; }

        public SyntaxToken SchedulesKeyword { get; }

        public ClearSchedulesStatementSyntax(SyntaxToken clearKeyword, SyntaxToken schedulesKeyword)
        {
            ClearKeyword = clearKeyword;
            SchedulesKeyword = schedulesKeyword;
        }

        public override TextSpan Span => TextSpan.FromBounds(ClearKeyword.Span.Start, SchedulesKeyword.Span.End);

        public override string ToString() => "ClearSchedules";
    }
}
