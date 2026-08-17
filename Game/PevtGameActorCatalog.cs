using System;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 运行时人物解析。它查的就是功能阶段 B 建立的 <see cref="ActorDirectory"/>，
    /// 不在游戏侧再造第二个人物模型，也不接受原版 CMD 短键作为公开 ID：
    /// <c>n</c> / <c>so</c> 这类短键只出现在 <see cref="ActorVisual.LegacyPerson"/> 里，
    /// 供立绘适配器挑选原版视觉源使用（PEVT-人物目录与原版别名规范.md 第 3 节）。
    /// </summary>
    internal sealed class PevtGameActorCatalog : IPevtActorCatalogService
    {
        private readonly PevtActorRegistry _registry;

        public PevtGameActorCatalog(PevtActorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        private ActorDirectory Directory => _registry.Directory;

        public bool TryResolve(string actorId, out ActorRegistration registration) =>
            Directory.TryGetActor(actorId, out registration);

        public bool TryResolveAppearance(string actorId, string appearanceId, out ActorAppearance appearance)
        {
            appearance = null;
            return Directory.TryGetActor(actorId, out ActorRegistration registration)
                && registration.Actor.TryGetAppearance(appearanceId, out appearance);
        }

        /// <summary>
        /// 内置语义站位没有人物专用坐标，返回 <c>true</c> 且 <paramref name="anchor"/> 为 null；
        /// 由立绘适配器把它翻译成自己保存的屏幕坐标，而不是交回原版的 <c>L/R/C</c> 组合键。
        /// </summary>
        public bool TryResolveAnchor(string actorId, string anchorId, out ActorAnchor anchor)
        {
            anchor = null;
            if (BuiltinActorAnchors.Contains(anchorId))
                return true;

            return Directory.TryGetActor(actorId, out ActorRegistration registration)
                && registration.Actor.TryGetAnchor(anchorId, out anchor);
        }
    }
}
