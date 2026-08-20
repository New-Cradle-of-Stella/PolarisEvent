using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 地图、天气与地图元素组合，对应同步指令中间层规范第 11 节的 World 部分。
    /// </summary>
    internal static class WorldRoutines
    {
        private static IPevtWorld World(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.World, "World");

        public static IEnumerator<PevtWait> RequireMap(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string required = PevtArgumentDomains.RequireId(args.String(0), "mapId");
            string current = world.CurrentMapId;

            if (!string.Equals(current, required, System.StringComparison.Ordinal))
                throw new PevtRoutineFailureException(
                    "PEVTR4001",
                    $"当前地图为 `{current ?? "<none>"}`，事件要求地图 `{required}`。");

            yield break;
        }

        public static IEnumerator<PevtWait> MapChange(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string mapId = PevtArgumentDomains.RequireId(args.String(0), "mapId");
            string anchorId = PevtArgumentDomains.RequireId(args.String(1), "anchorId");

            // 先把两个 ID 都验完再产生任何副作用（第 4 节执行顺序第 1 条）。
            if (!world.ResolveMap(mapId))
                throw new PevtRoutineFailureException("PEVTR4001", $"地图 `{mapId}` 无法解析。");
            if (!world.ResolveAnchor(mapId, anchorId))
                throw new PevtRoutineFailureException("PEVTR4001", $"地图 `{mapId}` 上没有锚点 `{anchorId}`。");

            world.BeginTransition();
            context.Cleanup.Push("World.AbortTransition", world.AbortTransition);

            world.ChangeMap(mapId);
            yield return world.WaitMapReady();

            world.PlacePlayer(anchorId);
            world.EndTransition();
            context.Cleanup.Pop();
        }

        public static IEnumerator<PevtWait> MapRefresh(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string mode = PevtArgumentDomains.RequireId(args.String(0), "mode");

            if (!world.ValidateRefreshMode(mode))
                throw new PevtRoutineFailureException("PEVTR4001", $"地图刷新模式 `{mode}` 未登记。");

            world.Refresh(mode);
            yield return world.WaitRefresh();
        }

        /// <summary>找不到图层是脚本可预期情况：提前返回 <c>false</c>，不产生运行异常。</summary>
        public static IEnumerator<PevtWait> MapLayerLoad(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");

            if (!world.TryResolveLayer(layerId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            world.LoadLayer(layerId);

            PevtWait<bool> loaded = world.WaitLayer(layerId);
            yield return loaded;
            context.Result.SetBool(loaded.Result);
        }

        public static IEnumerator<PevtWait> MapElementActive(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string kind = PevtArgumentDomains.RequireId(args.String(0), "kind");
            string elementId = PevtArgumentDomains.RequireId(args.String(1), "elementId");

            if (!world.TryResolveElement(kind, elementId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            world.SetElementActive(kind, elementId, args.Bool(2));
            context.Result.SetBool(true);
        }

        public static IEnumerator<PevtWait> WeatherSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            string weatherId = PevtArgumentDomains.RequireId(args.String(0), "weatherId");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            if (!world.ResolveWeather(weatherId))
                throw new PevtRoutineFailureException("PEVTR4001", $"天气 `{weatherId}` 未登记。");

            world.SetWeather(weatherId, frames);
            yield break;
        }

        /// <summary>
        /// 危险度是地图的持续状态，不是事件临时占用：原版 <c>DANGER</c> 同样会一直留着，
        /// 所以这里不登记会话恢复。
        /// </summary>
        public static IEnumerator<PevtWait> DangerSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtWorld world = World(context);
            int level = args.Int(0);

            if (level < 0)
                throw new PevtRoutineFailureException("PEVTR4001", $"`level` 不能为负数，实际为 {level}。");

            yield return world.SetDanger(level, args.Bool(1));
        }

        public static IEnumerator<PevtWait> DarknessSet(PevtRoutineContext context, PevtArguments args)
        {
            World(context).SetDarkness(args.Bool(0));
            yield break;
        }
    }

    /// <summary>
    /// 场景实体组合，对应同步指令中间层规范第 11 节的 Entity 部分。
    /// </summary>
    internal static class EntityRoutines
    {
        private static IPevtEntity Entity(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.Entity, "Entity");

        public static IEnumerator<PevtWait> EntityVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");

            if (!entity.TryResolve(entityId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            entity.SetVisible(entityId, args.Bool(1));
            context.Result.SetBool(true);
        }

        public static IEnumerator<PevtWait> EntityPose(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string poseId = PevtArgumentDomains.RequireId(args.String(1), "poseId");

            if (!entity.TryResolve(entityId) || !entity.TryResolvePose(entityId, poseId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            entity.SetPose(entityId, poseId);
            context.Result.SetBool(true);
        }

        public static IEnumerator<PevtWait> EntityFace(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string direction = PevtArgumentDomains.RequireId(args.String(1), "direction");

            if (!entity.TryResolve(entityId) || !entity.TryResolveDirection(direction))
            {
                context.Result.SetBool(false);
                yield break;
            }

            entity.SetFacing(entityId, direction);
            context.Result.SetBool(true);
        }

        public static IEnumerator<PevtWait> EntityMoveTo(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string targetId = PevtArgumentDomains.RequireId(args.String(1), "targetId");
            float speed = RequireSpeed(args.Float(2));

            if (!entity.TryResolve(entityId) || !entity.TryResolveTarget(targetId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> arrived = entity.MoveTo(entityId, targetId, speed);
            yield return arrived;
            context.Result.SetBool(arrived.Result);
        }

        public static IEnumerator<PevtWait> EntityMoveBy(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");
            float speed = RequireSpeed(args.Float(3));

            if (!entity.TryResolve(entityId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> arrived = entity.MoveBy(entityId, x, y, speed);
            yield return arrived;
            context.Result.SetBool(arrived.Result);
        }

        /// <summary>
        /// PEVT-E03：按像素相对位移。像素→地图单位的换算归适配器，这里只校验参数并把结果原样转出去。
        /// 位移登记为当前事件的受管动作，因此 <c>@wait_motion</c> 能等到它。
        /// </summary>
        public static IEnumerator<PevtWait> EntityMoveByPixels(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            float xPixels = PevtArgumentDomains.RequireFinite(args.Float(1), "xPixels");
            float yPixels = PevtArgumentDomains.RequireFinite(args.Float(2), "yPixels");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");

            if (!entity.TryResolve(entityId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> moved = entity.MoveByPixels(entityId, xPixels, yPixels, frames);
            yield return moved;
            context.Result.SetBool(moved.Result);
        }

        /// <summary>
        /// PEVT-E03：走到"参照目标的位置 + 像素偏移"处。参照目标可以是锚点或另一个实体，
        /// 与 <c>@entity_move_to</c> 用的是同一套目标解析。
        /// </summary>
        public static IEnumerator<PevtWait> EntityMoveToOffset(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string targetId = PevtArgumentDomains.RequireId(args.String(1), "targetId");
            float xPixels = PevtArgumentDomains.RequireFinite(args.Float(2), "xPixels");
            float yPixels = PevtArgumentDomains.RequireFinite(args.Float(3), "yPixels");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(4), "frames");

            if (!entity.TryResolve(entityId) || !entity.TryResolveTarget(targetId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> moved = entity.MoveToOffset(entityId, targetId, xPixels, yPixels, frames);
            yield return moved;
            context.Result.SetBool(moved.Result);
        }

        public static IEnumerator<PevtWait> EntityFollow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string targetId = PevtArgumentDomains.RequireId(args.String(1), "targetId");
            float distance = PevtArgumentDomains.RequireFinite(args.Float(2), "distance");
            float speed = RequireSpeed(args.Float(3));

            if (distance < 0f)
                throw new PevtRoutineFailureException("PEVTR4001", $"`distance` 不能为负数，实际为 {distance}。");

            if (!entity.TryResolve(entityId) || !entity.TryResolveTarget(targetId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            bool started = entity.StartFollow(entityId, targetId, distance, speed);

            // 跟随是持续行为，事件结束时必须停下——否则事件走完了 NPC 还在追着玩家跑。
            if (started)
                context.Services.Session.RegisterRestore($"Entity.StopFollow({entityId})", () => entity.StopFollow(entityId));

            context.Result.SetBool(started);
        }

        public static IEnumerator<PevtWait> EntityUnfollow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");

            if (!entity.TryResolve(entityId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            context.Result.SetBool(entity.StopFollow(entityId));
        }

        public static IEnumerator<PevtWait> EntityAction(PevtRoutineContext context, PevtArguments args)
        {
            IPevtEntity entity = Entity(context);
            string entityId = PevtArgumentDomains.RequireId(args.String(0), "entityId");
            string actionId = PevtArgumentDomains.RequireId(args.String(1), "actionId");

            if (!entity.TryResolve(entityId) || !entity.TryResolveAction(entityId, actionId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> finished = entity.RunAction(entityId, actionId);
            yield return finished;
            context.Result.SetBool(finished.Result);
        }

        private static float RequireSpeed(float speed)
        {
            PevtArgumentDomains.RequireFinite(speed, "speed");
            if (speed <= 0f)
                throw new PevtRoutineFailureException("PEVTR4001", $"`speed` 必须为正数，实际为 {speed}。");
            return speed;
        }
    }
}
