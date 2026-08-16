using System;

namespace Polaris.Pevt.Runtime
{
    /// <summary>预算配置。全部上限都是显式的，没有"跑到栈溢出为止"的路径。</summary>
    public sealed class PevtBudgetLimits
    {
        /// <summary>单个更新帧内允许执行的指令数。用完时内部让出一帧，不报错。</summary>
        public int StepsPerFrame { get; }

        /// <summary>一次事件执行允许的指令总数。超出报 PEVTR1001。</summary>
        public int TotalSteps { get; }

        /// <summary>显式帧栈的最大深度。超出报 PEVTR1003。</summary>
        public int MaxCallDepth { get; }

        /// <summary>连续多少帧没有任何进展就判定停滞。超出报 PEVTR1002。</summary>
        public int StallFrames { get; }

        public PevtBudgetLimits(int stepsPerFrame = 10_000, int totalSteps = 5_000_000, int maxCallDepth = 64, int stallFrames = 600)
        {
            if (stepsPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepsPerFrame), stepsPerFrame, "每帧步数上限必须为正数。");
            if (totalSteps <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalSteps), totalSteps, "总步数上限必须为正数。");
            if (maxCallDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCallDepth), maxCallDepth, "调用深度上限必须为正数。");
            if (stallFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(stallFrames), stallFrames, "停滞帧数上限必须为正数。");

            StepsPerFrame = stepsPerFrame;
            TotalSteps = totalSteps;
            MaxCallDepth = maxCallDepth;
            StallFrames = stallFrames;
        }

        public static PevtBudgetLimits Default { get; } = new PevtBudgetLimits();
    }

    /// <summary>
    /// 一次事件执行的预算账本。
    ///
    /// 三个上限各自映射到明确的诊断：总步数 → PEVTR1001，调用深度 → PEVTR1003，
    /// 无进展 → PEVTR1002。每帧步数不是错误条件，用完只是让出一帧。
    /// </summary>
    public sealed class PevtExecutionBudget
    {
        private int _stepsThisFrame;
        private int _stallFrames;

        public PevtBudgetLimits Limits { get; }

        public long TotalSteps { get; private set; }

        public int StepsThisFrame => _stepsThisFrame;

        /// <summary>连续没有进展的帧数。任何一次指令推进都会清零。</summary>
        public int StallFrames => _stallFrames;

        public PevtExecutionBudget(PevtBudgetLimits limits = null)
        {
            Limits = limits ?? PevtBudgetLimits.Default;
        }

        /// <summary>每个更新帧开始时调用，重置每帧步数。</summary>
        public void BeginFrame() => _stepsThisFrame = 0;

        /// <summary>本帧是否还能继续执行指令。</summary>
        public bool HasFrameBudget => _stepsThisFrame < Limits.StepsPerFrame;

        /// <summary>记一步。返回 false 表示总步数已经超限。</summary>
        public bool TryConsumeStep()
        {
            _stepsThisFrame++;
            TotalSteps++;
            _stallFrames = 0;
            return TotalSteps <= Limits.TotalSteps;
        }

        /// <summary>本帧一步也没走时调用，累计停滞帧数。返回 false 表示已判定停滞。</summary>
        public bool RecordNoProgress()
        {
            _stallFrames++;
            return _stallFrames < Limits.StallFrames;
        }

        /// <summary>等待正常推进时调用，清掉停滞计数。</summary>
        public void RecordWaitProgress() => _stallFrames = 0;

        public bool IsWithinCallDepth(int depth) => depth <= Limits.MaxCallDepth;
    }
}
