using System;
using System.Collections.Generic;
using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event
{
    /// <summary>一个按字符串 ID 注册的安全实体动作。动作不得把原版 MoveScript 文本交给游戏解释器。</summary>
    public delegate PevtWait<bool> PevtGameEntityAction(GameCharacter entity);

    public static partial class EventAPI
    {
        /// <summary>注册或替换一个实体动作；动作 ID 在当前进程内全局唯一。</summary>
        public static void RegisterEntityAction(string actionId, PevtGameEntityAction action) =>
            PevtGameEntityActionRegistry.Register(actionId, action);

        /// <summary>移除一个已注册的实体动作。</summary>
        public static bool UnregisterEntityAction(string actionId) =>
            PevtGameEntityActionRegistry.Unregister(actionId);

        /// <summary>判断实体动作 ID 是否已注册。</summary>
        public static bool HasEntityAction(string actionId) =>
            PevtGameEntityActionRegistry.Contains(actionId);
    }

    internal static class PevtGameEntityActionRegistry
    {
        static readonly Dictionary<string, PevtGameEntityAction> Actions =
            new Dictionary<string, PevtGameEntityAction>(StringComparer.Ordinal);

        internal static void Register(string actionId, PevtGameEntityAction action)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Entity action id is required.", nameof(actionId));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            Actions[actionId] = action;
        }

        internal static bool Unregister(string actionId) =>
            !string.IsNullOrEmpty(actionId) && Actions.Remove(actionId);

        internal static bool Contains(string actionId) =>
            !string.IsNullOrEmpty(actionId) && Actions.ContainsKey(actionId);

        internal static bool TryGet(string actionId, out PevtGameEntityAction action) =>
            Actions.TryGetValue(actionId ?? string.Empty, out action);
    }
}
