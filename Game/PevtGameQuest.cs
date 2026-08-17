using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 任务进度的游戏侧适配器，走 <see cref="PolarisAPI.Game.Quests"/>。
    /// 原版把"完成"和"放弃"都表示为从追踪列表移除，只差一个 <c>considerFinished</c>，因此这里分成两个方法而不是一个带布尔的入口。
    /// </summary>
    internal sealed class PevtGameQuest : IPevtQuest
    {
        /// <summary>不在追踪列表里，也不在已完成列表里。</summary>
        public const int StatusNotStarted = -1;

        /// <summary>已完成。其余非负返回值是当前阶段号。</summary>
        public const int StatusFinished = -2;

        public bool Resolve(string questId) => PolarisAPI.Game.Quests.Get(questId) != null;

        /// <summary>
        /// 规范化任务状态：<see cref="StatusNotStarted"/> 未接取，<see cref="StatusFinished"/> 已完成，其余为当前阶段号。
        /// 阶段号原样透出而不重新编号，脚本里的 <c>@quest_set</c> 与 <c>@quest_status</c> 因此用的是同一套数值。
        /// </summary>
        public int GetStatus(string questId)
        {
            GameQuestProgress progress = RequireQuest(questId).GetProgress();
            if (progress == null)
                return StatusNotStarted;

            return progress.Finished ? StatusFinished : progress.Phase;
        }

        /// <summary>设置任务阶段。走原版正常更新通道，因此该弹的任务变化提示照旧弹。</summary>
        public void SetStep(string questId, int step) =>
            RequireQuest(questId).Update(step);

        /// <summary>按"已完成"移除，进已完成列表。</summary>
        public void Finish(string questId) =>
            RequireQuest(questId).Remove(considerFinished: true);

        /// <summary>直接移除，不计入已完成——放弃或撤销误接的任务用这个。</summary>
        public void Remove(string questId) =>
            RequireQuest(questId).Remove(considerFinished: false);

        private static GameQuest RequireQuest(string questId)
        {
            GameQuest quest = PolarisAPI.Game.Quests.Get(questId);
            if (quest == null)
            {
                throw new PevtRoutineFailureException("PEVTR4001",
                    $"任务 `{questId}` 在本版本里不存在。");
            }

            return quest;
        }
    }
}
