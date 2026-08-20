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

        private readonly PevtGameClock _clock;

        /// <param name="clock">
        /// 按帧推进的位移要登记成受管动作票据，<c>@wait_motion</c> 才能等到它们；
        /// 速度式位移不需要时钟，所以这个参数允许为 null，只是那时按像素位移拿不到票据。
        /// </param>
        public PevtGameEntity(PevtGameClock clock = null) => _clock = clock;

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

        // ---- 按像素与帧的位移（PEVT-E03）----

        /// <summary>
        /// 按像素相对位移。
        ///
        /// 原版实体坐标以"格"为单位（<see cref="Map2d.CLEN"/> 像素一格，见 <c>canStandArea</c> 里
        /// <c>drawx / CLEN</c> 的用法），而作者在素材上量到的是像素，因此这里按当前地图的 CLEN 换算。
        /// 换算发生在适配层：PEVT 侧永远看不到格子尺寸。
        /// </summary>
        public PevtWait<bool> MoveByPixels(string entityId, float xPixels, float yPixels, int frames)
        {
            float cell = RequireCellSize();
            GameCharacter mover = Find(entityId);
            if (mover == null)
                return Done(false);

            // 目标点在动作开始时就定下来：相对位移的终点不该随实体自己被推来推去而漂移。
            float targetX = mover.X + (xPixels / cell);
            float targetY = mover.Y + (yPixels / cell);

            return Track(new PevtGameEntityPixelWait(entityId, frames, _ => new GameVector2(targetX, targetY)));
        }

        /// <summary>
        /// 走到"参照目标的位置 + 像素偏移"处。参照目标是锚点时终点固定；是另一个实体时每帧重新取它的位置，
        /// 所以目标自己在动也能跟上，目标中途消失则按契约以 false 结束。
        /// </summary>
        public PevtWait<bool> MoveToOffset(string entityId, string targetId, float xPixels, float yPixels, int frames)
        {
            float cell = RequireCellSize();
            if (Find(entityId) == null)
                return Done(false);

            float offsetX = xPixels / cell;
            float offsetY = yPixels / cell;

            GameMap map = PolarisAPI.Game.World.CurrentMap;
            if (map == null)
                return Done(false);

            if (map.HasAnchor(targetId))
            {
                // 锚点坐标只能通过"把某个东西放上去"读到，原版没有公开的取点入口；
                // 因此锚点形式先瞬时定位到锚点，再按帧走完偏移这一段。
                GameCharacter mover = Find(entityId);
                if (mover == null || !mover.MoveToAnchor(targetId))
                    return Done(false);

                float anchorTargetX = mover.X + offsetX;
                float anchorTargetY = mover.Y + offsetY;
                return Track(new PevtGameEntityPixelWait(entityId, frames, _ => new GameVector2(anchorTargetX, anchorTargetY)));
            }

            return Track(new PevtGameEntityPixelWait(entityId, frames, _ =>
            {
                GameCharacter target = PolarisAPI.Game.World.CurrentMap?.FindCharacter(targetId);
                if (target == null)
                    return null;

                return new GameVector2(target.X + offsetX, target.Y + offsetY);
            }));
        }

        /// <summary>
        /// 当前地图的格子像素尺寸。没有地图就没有换算依据，这一条明确失败而不是猜一个默认值——
        /// 猜出来的位移在别的地图上是错的，而且错得不明显。
        /// </summary>
        private static float RequireCellSize()
        {
            float cell = PevtGameHost.Safe(() => m2d.M2DBase.Instance?.curMap?.CLEN ?? 0f, 0f);
            if (cell <= 0f)
                throw Failed("当前没有加载地图，无法把像素换算成实体坐标。");

            return cell;
        }

        /// <summary>把按帧推进的位移登记成受管动作票据，让 <c>@wait_motion</c> 也能等到它。</summary>
        private PevtWait<bool> Track(PevtWait<bool> wait)
        {
            _clock?.RegisterMotion(() => wait.IsCompleted);
            return wait;
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

    /// <summary>
    /// PEVT-E03 的按帧位移：把剩余位移平均分到剩下的帧里，每帧走一段。
    ///
    /// 由 PEVT 时钟推进，不依赖地图主循环——事件期间主循环是停着的，靠它推进的位移会一动不动。
    /// 每一步都走带落脚判定的位移（<see cref="GameCharacter.MoveBy"/> 的 <c>checkFoot</c>），
    /// 因为让 NPC 穿墙比走不到位难查得多；被挡住时立刻以 false 结束，实体停在已经走到的位置。
    /// </summary>
    internal sealed class PevtGameEntityPixelWait : PevtWait<bool>
    {
        private readonly string _entityId;
        private readonly int _frames;

        /// <summary>每帧回答"终点在哪"；返回 null 表示参照目标已经消失。</summary>
        private readonly Func<GameCharacter, GameVector2?> _destination;

        private long _startFrame = -1;

        public PevtGameEntityPixelWait(string entityId, int frames, Func<GameCharacter, GameVector2?> destination)
        {
            _entityId = entityId;
            _frames = frames < 0 ? 0 : frames;
            _destination = destination;
        }

        public override string ProgressSource => "实体按帧位移";

        protected override void OnTick(PevtWaitContext context)
        {
            GameCharacter mover = PolarisAPI.Game.World.CurrentMap?.FindCharacter(_entityId);
            if (mover == null)
            {
                CompleteSucceeded(false);
                return;
            }

            if (_startFrame < 0)
                _startFrame = context.Frame;

            GameVector2? destination = _destination(mover);
            if (destination == null)
            {
                CompleteSucceeded(false);
                return;
            }

            float dx = destination.Value.X - mover.X;
            float dy = destination.Value.Y - mover.Y;

            // 与 PevtFrameWait 同一套帧计数：frames 为 0 时第一次推进就到位。
            long remaining = _frames - (context.Frame - _startFrame);
            if (remaining <= 0)
            {
                CompleteSucceeded(Step(mover, dx, dy));
                return;
            }

            float step = 1f / remaining;
            if (!Step(mover, dx * step, dy * step))
                CompleteSucceeded(false);
        }

        private static bool Step(GameCharacter mover, float dx, float dy)
        {
            if (dx == 0f && dy == 0f)
                return true;

            return PevtGameHost.Safe(() => mover.MoveBy(new GameVector2(dx, dy)), false);
        }
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
