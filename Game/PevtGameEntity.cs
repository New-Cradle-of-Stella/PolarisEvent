using System;
using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 场景实体的游戏侧适配器，全部以字符串键经 <see cref="GameMap.FindCharacter"/> 查找，不向 PEVT 暴露对象引用。
    /// 位移直接调 <see cref="GameCharacter"/> 的受控方法，不生成也不解析 MoveScript 文本。
    /// </summary>
    internal sealed class PevtGameEntity : IPevtEntity
    {
        private const string Left = "left";
        private const string Right = "right";

        /// <summary>移动每帧的最小步长，避免 <c>speed</c> 过小时事件永远停在等待上。</summary>
        private const float MinStepPerFrame = 0.01f;

        public bool TryResolve(string entityId) => Find(entityId) != null;

        /// <summary>
        /// 原版 <c>M2Mover</c> 上没有任何可见性成员（运行时核对过声明成员），因此这条明确失败而不是假装成功。
        /// 想让实体消失只能用原版自己的登场/退场流程，那不是一个可见性开关。
        /// </summary>
        public void SetVisible(string entityId, bool visible) =>
            throw Failed("原版没有实体可见性入口，`@entity_visible` 暂不可用。");

        /// <summary>
        /// 原版没有"姿态名表"可查，只能问当前姿态是不是某个名字，所以这里只能确认实体存在。
        /// 不存在的姿态名会被精灵静默忽略，不会破坏状态。
        /// </summary>
        public bool TryResolvePose(string entityId, string poseId) =>
            !string.IsNullOrEmpty(poseId) && Find(entityId) != null;

        public void SetPose(string entityId, string poseId)
        {
            if (!Require(entityId).SetPose(poseId))
                throw Failed($"实体 `{entityId}` 的姿态 `{poseId}` 设置失败。");
        }

        public bool TryResolveDirection(string direction) =>
            string.Equals(direction, Left, StringComparison.Ordinal)
            || string.Equals(direction, Right, StringComparison.Ordinal);

        public void SetFacing(string entityId, string direction)
        {
            GameFacing facing = string.Equals(direction, Right, StringComparison.Ordinal)
                ? GameFacing.Right
                : GameFacing.Left;

            Require(entityId).SetFacing(facing);
        }

        /// <summary>移动目标可以是当前地图的锚点，也可以是另一个实体。</summary>
        public bool TryResolveTarget(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
                return false;

            GameMap map = PolarisAPI.Game.World.CurrentMap;
            if (map == null)
                return false;

            return map.HasAnchor(targetId) || map.FindCharacter(targetId) != null;
        }

        /// <summary>
        /// 走到锚点或另一个实体处。锚点是瞬时定位（原版 <c>setToLabelPt</c> 就是瞬时的），
        /// 追另一个实体则按 <paramref name="speed"/> 每帧逼近，因此目标是活动的也能跟上。
        /// </summary>
        public PevtWait<bool> MoveTo(string entityId, string targetId, float speed)
        {
            GameCharacter mover = Find(entityId);
            if (mover == null)
                return Done(false);

            GameMap map = PolarisAPI.Game.World.CurrentMap;
            if (map == null)
                return Done(false);

            if (map.HasAnchor(targetId))
                return Done(mover.MoveToAnchor(targetId));

            return Step(entityId, speed, () =>
            {
                GameCharacter target = PolarisAPI.Game.World.CurrentMap?.FindCharacter(targetId);
                if (target == null)
                    return (0f, 0f, false);

                return (target.X - mover.X, target.Y - mover.Y, true);
            });
        }

        /// <summary>按相对坐标移动。每帧逼近剩余位移，实体中途消失时返回 false。</summary>
        public PevtWait<bool> MoveBy(string entityId, float x, float y, float speed)
        {
            GameCharacter mover = Find(entityId);
            if (mover == null)
                return Done(false);

            float targetX = mover.X + x;
            float targetY = mover.Y + y;

            return Step(entityId, speed, () => (targetX - mover.X, targetY - mover.Y, true));
        }

        /// <summary>原版没有跟随系统（<c>IHkdsFollowable</c> 只是气泡定位接口），按签名返回 false 而不是抛异常。</summary>
        public bool StartFollow(string entityId, string targetId, float distance, float speed) => false;

        public bool StopFollow(string entityId) => false;

        /// <summary>
        /// 原版的"实体动作"就是 MoveScript 文本，而规范明确禁止把 MoveScript 交回原版解释器，
        /// 因此没有任何已登记动作，恒为 false，<c>@entity_action</c> 会干净地返回 false。
        /// </summary>
        public bool TryResolveAction(string entityId, string actionId) => false;

        public PevtWait<bool> RunAction(string entityId, string actionId) =>
            throw Failed("当前没有登记任何实体动作。");

        private static PevtWait<bool> Step(string entityId, float speed, Func<(float dx, float dy, bool alive)> remaining) =>
            new PevtGameApproachWait(entityId, speed, remaining);

        private static PevtWait<bool> Done(bool result) => new PevtGameSettledWait(result);

        private static GameCharacter Find(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
                return null;

            return PolarisAPI.Game.World.CurrentMap?.FindCharacter(entityId);
        }

        private static GameCharacter Require(string entityId)
        {
            GameCharacter mover = Find(entityId);
            if (mover == null)
                throw Failed($"当前地图上没有实体 `{entityId}`。");

            return mover;
        }

        private static PevtRoutineFailureException Failed(string message) =>
            new PevtRoutineFailureException("PEVTR4001", message);
    }

    /// <summary>结果已经确定的位移等待，用于瞬时定位这类不需要跨帧推进的情形。</summary>
    internal sealed class PevtGameSettledWait : PevtWait<bool>
    {
        private readonly bool _result;

        public PevtGameSettledWait(bool result) => _result = result;

        public override string ProgressSource => "实体位移";

        protected override void OnTick(PevtWaitContext context) => CompleteSucceeded(_result);
    }

    /// <summary>
    /// 由 PEVT 每帧推进的逼近移动：每帧按步长走一段剩余位移，进入步长内即算到达。
    /// 实体或目标中途消失时按契约以 false 结束，而不是抛异常——那是脚本可预期的情况。
    /// </summary>
    internal sealed class PevtGameApproachWait : PevtWait<bool>
    {
        private readonly string _entityId;
        private readonly float _step;
        private readonly Func<(float dx, float dy, bool alive)> _remaining;

        public PevtGameApproachWait(string entityId, float speed, Func<(float dx, float dy, bool alive)> remaining)
        {
            _entityId = entityId;
            _step = Math.Max(Math.Abs(speed), 0.01f);
            _remaining = remaining;
        }

        public override string ProgressSource => "实体逼近移动";

        protected override void OnTick(PevtWaitContext context)
        {
            GameCharacter mover = PolarisAPI.Game.World.CurrentMap?.FindCharacter(_entityId);
            if (mover == null)
            {
                CompleteSucceeded(false);
                return;
            }

            (float dx, float dy, bool alive) = _remaining();
            if (!alive)
            {
                CompleteSucceeded(false);
                return;
            }

            float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= _step)
            {
                mover.MoveBy(new GameVector2(dx, dy));
                CompleteSucceeded(true);
                return;
            }

            mover.MoveBy(new GameVector2(dx / distance * _step, dy / distance * _step));
        }
    }
}
