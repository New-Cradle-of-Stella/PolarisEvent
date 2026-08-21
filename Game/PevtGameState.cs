using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 持久布尔／整数状态与主进度的游戏侧适配器。
    /// </summary>
    internal sealed class PevtGameState : IPevtState
    {
        /// <summary>原版具名 GFB／GFC 表。</summary>
        public const string GlobalScope = "global";

        /// <summary>原版剧情标记（SF）。</summary>
        public const string StoryScope = "story";

        private static readonly string[] Scopes = { GlobalScope, StoryScope };

        public bool ValidateScope(string scope)
        {
            foreach (string candidate in Scopes)
            {
                if (candidate == scope)
                    return true;
            }

            return false;
        }

        public bool GetFlag(string scope, string key)
        {
            if (scope == StoryScope)
                return PolarisAPI.Game.Progress.HasStoryFlag(key);

            RequireFlagKey(key);
            return PolarisAPI.Game.Progress.GetFlag(key);
        }

        public bool HasFlag(string scope, string key)
        {
            if (scope == StoryScope)
                return false;
            return scope == GlobalScope && PolarisAPI.Game.Progress.HasFlag(key);
        }

        public void SetFlag(string scope, string key, bool value)
        {
            if (scope == StoryScope)
            {
                if (!PolarisAPI.Game.Progress.SetStoryFlag(key, value ? 1 : 0))
                    throw Failed($"剧情标记 `{key}` 写入失败。");
                return;
            }

            RequireFlagKey(key);
            if (!PolarisAPI.Game.Progress.SetFlag(key, value))
                throw Failed($"全局旗标 `{key}` 写入失败。");
        }

        public int GetCounter(string scope, string key)
        {
            if (scope == StoryScope)
                return PolarisAPI.Game.Progress.GetStoryFlag(key);

            RequireCounterKey(key);

            // 原版计数器是 uint，PEVT 只有有符号 int。越界不静默回绕。
            uint raw = PolarisAPI.Game.Progress.GetCounter(key);
            if (raw > int.MaxValue)
            {
                throw new PevtRoutineFailureException("PEVTR2001",
                    $"原版计数器 `{key}` 的值 {raw} 超出 PEVT `int` 范围。");
            }

            return (int)raw;
        }

        public void SetCounter(string scope, string key, int value)
        {
            if (scope == StoryScope)
            {
                if (!PolarisAPI.Game.Progress.SetStoryFlag(key, value))
                    throw Failed($"剧情标记 `{key}` 写入失败。");
                return;
            }

            if (value < 0)
                throw Failed($"原版具名计数器不接受负值，实际为 {value}。");

            RequireCounterKey(key);
            if (!PolarisAPI.Game.Progress.SetCounter(key, (uint)value))
                throw Failed($"全局计数器 `{key}` 写入失败。");
        }

        public void SetMainProgress(int value)
        {
            if (!PolarisAPI.Game.Progress.SetMainProgress(value))
                throw Failed($"主进度写入失败，实际值 {value}。");
        }

        private bool _lastAutosaveSucceeded;

        /// <summary>验证 PEVT 自动存档模式。</summary>
        public bool ValidateSaveMode(string mode) => TryParseSaveMode(mode, out _);

        public void RequestAutosave(string mode)
        {
            if (!TryParseSaveMode(mode, out GameAutosaveMode parsed))
                throw Failed($"存档模式 `{mode}` 未登记。");

            _lastAutosaveSucceeded = PolarisAPI.Game.Save.RequestAutosave(parsed);
        }

        /// <summary>返回已完成的存档等待。</summary>
        public PevtWait<bool> WaitAutosave() => new PevtGameSettledWait(_lastAutosaveSucceeded);

        private static bool TryParseSaveMode(string mode, out GameAutosaveMode parsed)
        {
            switch (mode)
            {
                case "normal":
                    parsed = GameAutosaveMode.Normal;
                    return true;
                case "bench":
                    parsed = GameAutosaveMode.Bench;
                    return true;
                case "force":
                    parsed = GameAutosaveMode.Force;
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        private static PevtRoutineFailureException Failed(string message) =>
            new PevtRoutineFailureException("PEVTR4001", message);

        private static void RequireFlagKey(string key)
        {
            if (!PolarisAPI.Game.Progress.HasFlag(key))
                throw Failed($"原版没有定义名为 `{key}` 的全局旗标。");
        }

        private static void RequireCounterKey(string key)
        {
            if (!PolarisAPI.Game.Progress.HasCounter(key))
                throw Failed($"原版没有定义名为 `{key}` 的全局计数器。");
        }
    }
}
