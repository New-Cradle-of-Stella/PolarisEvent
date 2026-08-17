using System;
using System.Collections.Generic;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Res.Pxls;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 一份中立视觉租约：把"原版借用"和"模组 PolarisRes 字段"两种来源统一成同一个东西交给演出层。
    ///
    /// 之所以要这层，是因为 Core 的 <see cref="IPevtResources"/> 只认字符串 ID 和
    /// <see cref="PevtWait"/>，而两种来源的就绪判定完全不同：原版靠轮询全局 title 字典，
    /// 模组靠 <see cref="PxlsCharacterHandle"/> 的 Ready/Faulted 事件。统一之后，演出层不需要
    /// 知道一个立绘到底来自哪边。
    /// </summary>
    public sealed class PevtVisualLease
    {
        private readonly GamePxlsLease _gameLease;
        private readonly PxlsCharacterHandle _modHandle;

        /// <summary>诊断用的来源描述。</summary>
        public string Source { get; }

        private PevtVisualLease(string source, GamePxlsLease gameLease, PxlsCharacterHandle modHandle)
        {
            Source = source;
            _gameLease = gameLease;
            _modHandle = modHandle;
        }

        internal static PevtVisualLease FromGame(GamePxlsLease lease) =>
            new PevtVisualLease("game-pxls:" + lease.Id, lease, null);

        internal static PevtVisualLease FromMod(string reference, PxlsCharacterHandle handle) =>
            new PevtVisualLease("polaris-res:" + reference, null, handle);

        /// <summary>资源当前的票据状态。</summary>
        public PevtResourceStatus Status
        {
            get
            {
                if (_gameLease != null)
                {
                    if (_gameLease.IsReleased)
                        return PevtResourceStatus.Failed;

                    // 借用之后原版才把 Bundle 拉进来是常态，所以每次都重查一遍。
                    if (!_gameLease.IsReady)
                        GamePxlsBridge.Refresh(_gameLease);

                    return _gameLease.IsReady ? PevtResourceStatus.Ready : PevtResourceStatus.Loading;
                }

                if (_modHandle == null)
                    return PevtResourceStatus.Failed;
                if (_modHandle.IsFaulted)
                    return PevtResourceStatus.Failed;

                return _modHandle.IsReady ? PevtResourceStatus.Ready : PevtResourceStatus.Loading;
            }
        }

        /// <summary>释放本次借用。原版来源只撤销 PolarisEvent 自己的引用；模组资源由其 owner 持有，这里什么都不做。</summary>
        public void Release() => _gameLease?.Release();

        public override string ToString() => $"{Source} ({Status})";
    }

    /// <summary>
    /// 游戏侧的资源服务：把人物目录里的视觉声明变成真实的借用或模组资源等待。
    ///
    /// 原版 <c>game-pxls</c> 是借用——事件退场和结束只清 PolarisEvent 自己的显示实例与租约；
    /// 自定义 <c>polaris-res</c> 资源由注册来源的 PolarisRes owner 持有，事件只持临时租约。
    /// 两者缺失都以 <c>PEVTR4403</c> 结束，走的是同一个 <see cref="PevtResourceWait"/>。
    /// </summary>
    public sealed class PevtGameResources
    {
        private readonly PevtActorRegistry _actors;
        private readonly List<PevtVisualLease> _eventLeases = new List<PevtVisualLease>();

        public PevtGameResources(PevtActorRegistry actors)
        {
            _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        }

        /// <summary>本次事件还没释放的租约数量，供所有权诊断查询。</summary>
        public int OutstandingLeaseCount => _eventLeases.Count;

        /// <summary>
        /// 为一个人物视觉建立租约。人物或视觉未登记时返回 null，由调用方报 PEVTR4401/4402——
        /// 这里不替它决定用哪个编号。
        /// </summary>
        public PevtVisualLease TryBorrowVisual(string actorId, string visualKey, ActorVisual visual)
        {
            if (visual == null)
                return null;

            PevtVisualLease lease = visual.Resource.Provider == ActorVisualProvider.GamePxls
                ? BorrowGame(visual.Resource)
                : BorrowMod(actorId, visualKey, visual.Resource);

            if (lease != null)
                _eventLeases.Add(lease);

            return lease;
        }

        private static PevtVisualLease BorrowGame(ActorVisualResource resource)
        {
            if (!GamePxlsId.TryParse(resource.Asset, out GamePxlsId id))
                return null;

            return PevtVisualLease.FromGame(GamePxlsBridge.Borrow(id));
        }

        /// <summary>
        /// 模组视觉走生成代码登记的延迟访问器。访问器在这一刻才第一次被调用——注册扫描期不碰它，
        /// 正是为了让 PolarisRes 有机会在游戏就绪之后再完成 PXLS 加载。
        /// </summary>
        private PevtVisualLease BorrowMod(string actorId, string visualKey, ActorVisualResource resource)
        {
            if (!_actors.TryGetVisualAccessor(actorId, visualKey, out Func<object> accessor))
                return null;

            object value;
            try
            {
                value = accessor();
            }
            catch (Exception)
            {
                return null;
            }

            return value is PxlsCharacterHandle handle
                ? PevtVisualLease.FromMod(resource.FieldReference, handle)
                : null;
        }

        /// <summary>把租约的就绪判定包成统一等待；缺失或加载失败时以 PEVTR4403 结束。</summary>
        public PevtWait WaitFor(PevtVisualLease lease, string resourceId)
        {
            if (lease == null)
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);

            return new PevtResourceWait(resourceId, () => lease.Status);
        }

        /// <summary>
        /// 释放本次事件持有的全部租约。事件结束、替换、异常和插件卸载都必须走这里，
        /// 否则借用计数会一直挂在原版资源上。
        /// </summary>
        public int ReleaseAll()
        {
            int count = _eventLeases.Count;
            for (int i = _eventLeases.Count - 1; i >= 0; i--)
                _eventLeases[i].Release();

            _eventLeases.Clear();
            return count;
        }
    }
}
