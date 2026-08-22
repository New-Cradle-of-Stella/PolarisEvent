namespace Polaris.Pevt.Runtime
{
    // 本文件补齐剩下的原子服务边界：World/Entity、State/Inventory/Quest、Player/Battle。
    // 规则与其余接口一致：只使用 PEVT 自己的类型和 PevtWait，方法刻意保持"先查再写"的粒度，
    // 好让组合处理器在第一个持久写入之前把该拒绝的全拒绝掉。

    /// <summary>地图、天气、地图元素与地图层。</summary>
    public interface IPevtWorld
    {
        // ---- 地图切换 ----

        /// <summary>当前已加载地图的稳定 ID；尚未加载地图时为 null。</summary>
        string CurrentMapId { get; }

        bool ResolveMap(string mapId);

        bool ResolveAnchor(string mapId, string anchorId);

        /// <summary>
        /// 进入地图转场。成功后组合必须立刻登记 <see cref="AbortTransition"/> 作为临时清理，
        /// 只有 <see cref="EndTransition"/> 成功时才撤销。
        /// </summary>
        void BeginTransition();

        void ChangeMap(string mapId);

        PevtWait WaitMapReady();

        void PlacePlayer(string anchorId);

        void EndTransition();

        /// <summary>转场未能走完时的回退：撤销 <see cref="BeginTransition"/> 建立的占用。</summary>
        void AbortTransition();

        // ---- 刷新与图层 ----

        bool ValidateRefreshMode(string mode);

        void Refresh(string mode);

        PevtWait WaitRefresh();

        bool TryResolveLayer(string layerId);

        void LoadLayer(string layerId);

        PevtWait<bool> WaitLayer(string layerId);

        // ---- 地图元素与环境 ----

        bool TryResolveElement(string kind, string elementId);

        void SetElementActive(string kind, string elementId, bool active);

        bool ResolveWeather(string weatherId);

        void SetWeather(string weatherId, int frames);

        /// <summary>危险度可以跨帧过渡；<c>immediate</c> 为 true 时立即到位。</summary>
        PevtWait SetDanger(int level, bool immediate);

        void SetDarkness(bool enabled);
    }

    /// <summary>场景实体。全部入口都以字符串 ID 查找目标，不向 PEVT 暴露对象引用，也不接受 MoveScript 文本。</summary>
    public interface IPevtEntity
    {
        /// <summary>实体是否存在。实体消失是脚本可预期情况，按签名返回 bool，不产生运行异常。</summary>
        bool TryResolve(string entityId);

        bool TryResolvePose(string entityId, string poseId);

        void SetPose(string entityId, string poseId);

        bool TryResolveDirection(string direction);

        void SetFacing(string entityId, string direction);

        /// <summary>移动或跟随的目标：地图锚点或另一个实体。</summary>
        bool TryResolveTarget(string targetId);

        /// <summary>结果表示是否到达；中途实体消失或被打断时返回 false，而不是失败。</summary>
        PevtWait<bool> MoveTo(string entityId, string targetId, float speed);

        PevtWait<bool> MoveBy(string entityId, float x, float y, float speed);

        // ---- 按像素与帧的位移（PEVT-E03）----
        // 与上面那两条按"速度"参数化的位移并存：演出要的是"这段位移正好走 30 帧"，
        // 而速度式位移的耗时取决于距离，两者不能互相表达。像素是作者在原版素材上量得到的单位，
        // 换算成地图单位是适配器的事，PEVT 侧不出现格子尺寸。

        /// <summary>
        /// 按像素相对位移，<paramref name="frames"/> 为目标持续帧数（<c>0</c> 表示立即到位）。
        /// 结果表示是否走完整段位移；实体中途消失或被碰撞挡住时为 false。
        /// </summary>
        PevtWait<bool> MoveByPixels(string entityId, float xPixels, float yPixels, int frames);

        /// <summary>
        /// 走到"另一个目标的位置 + 像素偏移"处。目标可以是锚点或另一个实体；
        /// 目标是活动实体时每帧重新计算剩余位移，因此目标自己在动也能跟上。
        /// </summary>
        PevtWait<bool> MoveToOffset(string entityId, string targetId, float xPixels, float yPixels, int frames);

        bool StartFollow(string entityId, string targetId, float distance, float speed);

        bool StopFollow(string entityId);

        bool TryResolveAction(string entityId, string actionId);

        /// <summary>结果表示已登记动作是否正常完成。</summary>
        PevtWait<bool> RunAction(string entityId, string actionId);
    }

    /// <summary>
    /// 持久布尔/整数状态、主进度与自动存档。
    /// 这里的写入是持久状态：事件失败不回滚，也不进入 <see cref="PevtEventSession"/> 的恢复表。
    /// </summary>
    public interface IPevtState
    {
        /// <summary><c>scope</c> 不是任意游戏 API 名称；未登记值必须报错（内置事件语句表「参数域」）。</summary>
        bool ValidateScope(string scope);

        /// <summary>只检查键是否已登记，不读取旗标当前值。</summary>
        bool HasFlag(string scope, string key);

        bool GetFlag(string scope, string key);

        void SetFlag(string scope, string key, bool value);

        int GetCounter(string scope, string key);

        void SetCounter(string scope, string key, int value);

        void SetMainProgress(int value);

        bool ValidateSaveMode(string mode);

        void RequestAutosave(string mode);

        PevtWait<bool> WaitAutosave();
    }

    /// <summary>物品、货币、技能、魔法与商店。全部为持久状态。</summary>
    public interface IPevtInventory
    {
        int GetItemCount(string itemId);

        bool ResolveItem(string itemId);

        /// <summary>返回变更后的数量。</summary>
        int ChangeItem(string itemId, int delta, int grade);

        int GetMoney();

        /// <summary>返回变更后的货币数量。</summary>
        int ChangeMoney(int delta);

        bool HasSkill(string skillId);

        bool ResolveSkill(string skillId);

        void SetSkillOwned(string skillId, bool owned);

        void SetSkillEnabled(string skillId, bool enabled);

        bool HasMagic(string magicId);

        bool ResolveMagic(string magicId);

        void SetMagicOwned(string magicId, bool owned);

        bool ResolveStore(string storeId);

        void RefreshStore(string storeId);
    }

    /// <summary>任务进度。<see cref="GetStatus"/> 返回的是规范化状态整数，不是原版内部枚举值。</summary>
    public interface IPevtQuest
    {
        bool Resolve(string questId);

        int GetStatus(string questId);

        void SetStep(string questId, int step);

        void Finish(string questId);

        void Remove(string questId);
    }

    /// <summary>
    /// <c>$raw cmd</c> 的进程级通道。
    /// </summary>
    public interface IPevtRawCommands
    {
        /// <summary>提交一段原版 DSL 文本。返回的等待在会话结束时完成，失败时以 PEVTR4101 结束。</summary>
        PevtWait Submit(string rawText);

        /// <summary>当前是否有原版会话在跑。</summary>
        bool IsBusy { get; }

        /// <summary>正在排队等待轮到自己的会话数（不含正在跑的那个）。</summary>
        int QueueLength { get; }
    }

    /// <summary>
    /// 受控原子服务集合里 P1 与两个逃生口的那一半。
    /// </summary>
    public sealed class PevtDomainServices
    {
        public IPevtWorld World { get; }

        public IPevtEntity Entity { get; }

        public IPevtState State { get; }

        public IPevtInventory Inventory { get; }

        public IPevtQuest Quest { get; }

        public PevtDomainServices(
            IPevtWorld world = null,
            IPevtEntity entity = null,
            IPevtState state = null,
            IPevtInventory inventory = null,
            IPevtQuest quest = null)
        {
            World = world;
            Entity = entity;
            State = state;
            Inventory = inventory;
            Quest = quest;
        }

        /// <summary>一个都没有接的空包，避免到处写 null 判断。</summary>
        public static PevtDomainServices Empty { get; } = new PevtDomainServices();
    }
}
