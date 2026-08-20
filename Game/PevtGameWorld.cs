using System;
using System.Collections.Generic;
using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 地图、天气、危险度与地图元素的游戏侧适配器，全部走 <see cref="PolarisAPI.Game.World"/> 与 <see cref="GameMap"/>。
    /// 地图元素只登记 <c>lp</c>（标签点）：静态 chip 在原版里是网格数据，没有启用开关，因此 <c>chip</c> 解析为未登记。
    /// </summary>
    internal sealed class PevtGameWorld : IPevtWorld
    {
        /// <summary><c>@map_refresh</c> 的已登记模式。原版只有"整张重载"一种，所以只登记它。</summary>
        private const string RefreshWhole = "whole";

        /// <summary><c>@map_element_active</c> 已登记的元素种类。</summary>
        private const string ElementLabelPoint = "lp";

        private static readonly Dictionary<string, GameWeather> Weathers =
            new Dictionary<string, GameWeather>(StringComparer.Ordinal)
            {
                ["normal"] = GameWeather.Normal,
                ["wind"] = GameWeather.Wind,
                ["thunder"] = GameWeather.Thunder,
                ["mist"] = GameWeather.Mist,
                ["mist-dense"] = GameWeather.MistDense,
                ["drought"] = GameWeather.Drought,
                ["plague"] = GameWeather.Plague,
            };

        private string _pendingMapId;

        // ---- 地图切换 ----

        public string CurrentMapId => PolarisAPI.Game.World.CurrentMap?.Key;

        public bool ResolveMap(string mapId) => PolarisAPI.Game.World.ResolveMap(mapId);

        /// <summary>
        /// 目标地图还没成为当前地图时，它的标签点通常尚未加载，因此这里无法否证一个合法锚点。
        /// 能确认存在就返回 true；确认不了时也返回 true，把真正的判定交给 <see cref="PlacePlayer"/>——
        /// 那时转场的 <c>AbortTransition</c> 清理已经登记好，失败会完整回滚，不会留下"地图换了但人没放好"的半执行。
        /// </summary>
        public bool ResolveAnchor(string mapId, string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId))
                return false;

            GameMap current = PolarisAPI.Game.World.CurrentMap;
            if (current != null && string.Equals(current.Key, mapId, StringComparison.Ordinal))
                return current.HasAnchor(anchorId);

            return true;
        }

        /// <summary>转场没有需要在游戏侧占用的资源，标记由 <see cref="ChangeMap"/> 记录的目标地图承担。</summary>
        public void BeginTransition()
        {
        }

        public void ChangeMap(string mapId)
        {
            try
            {
                PolarisAPI.Game.World.ChangeMap(mapId);
            }
            catch (Exception ex)
            {
                throw new PevtRoutineFailureException("PEVTR4001", $"地图 `{mapId}` 切换失败：{ex.Message}", ex);
            }

            _pendingMapId = mapId;
        }

        /// <summary>等到目标地图成为当前地图为止。原版没有"转场完成"回调，只能轮询当前地图键。</summary>
        public PevtWait WaitMapReady()
        {
            string target = _pendingMapId;
            return new PevtPredicateWait(
                () => string.Equals(PolarisAPI.Game.World.CurrentMap?.Key, target, StringComparison.Ordinal),
                "地图转场");
        }

        public void PlacePlayer(string anchorId)
        {
            GamePlayer player = PolarisAPI.Game.World.CurrentPlayer;
            if (player == null)
                throw Failed("转场后取不到玩家实例，无法放置到锚点。");

            if (!player.MoveToAnchor(anchorId))
                throw Failed($"当前地图上没有锚点 `{anchorId}`。");
        }

        public void EndTransition() => _pendingMapId = null;

        /// <summary>
        /// 转场回退。地图切换本身在原版里不可撤销，所以这里只解除转场标记；
        /// 真正防止半执行的是"锚点失败发生在 <see cref="PlacePlayer"/>"这一点——事件会带着诊断终止，不会继续往下演。
        /// </summary>
        public void AbortTransition() => _pendingMapId = null;

        // ---- 刷新与图层 ----

        public bool ValidateRefreshMode(string mode) => string.Equals(mode, RefreshWhole, StringComparison.Ordinal);

        public void Refresh(string mode)
        {
            GameMap map = RequireMap();
            if (!map.Refresh())
                throw Failed("当前地图刷新失败。");
        }

        /// <summary><see cref="GameMap.Refresh"/> 是同步完成的，所以这里不需要真的等下一帧以外的东西。</summary>
        public PevtWait WaitRefresh() => new PevtNextFrameWait();

        public bool TryResolveLayer(string layerId) => RequireMap().HasLayer(layerId);

        public void LoadLayer(string layerId)
        {
            if (!RequireMap().LoadLayer(layerId))
                throw Failed($"地图图层 `{layerId}` 加载失败。");
        }

        /// <summary>图层重开是同步的，结果在 <see cref="LoadLayer"/> 就已经确定，这里直接回报成功。</summary>
        public PevtWait<bool> WaitLayer(string layerId) => new PevtMotionWait(() => true);

        // ---- 地图元素与环境 ----

        public bool TryResolveElement(string kind, string elementId)
        {
            if (!string.Equals(kind, ElementLabelPoint, StringComparison.Ordinal))
                return false;

            return RequireMap().HasAnchor(elementId);
        }

        public void SetElementActive(string kind, string elementId, bool active)
        {
            if (!RequireMap().SetAnchorActive(elementId, active))
                throw Failed($"地图元素 `{kind}:{elementId}` 开关失败。");
        }

        public bool ResolveWeather(string weatherId) => Weathers.ContainsKey(weatherId ?? string.Empty);

        /// <summary>
        /// 原版天气只有"切到某种天气"，没有过渡时长，因此 <paramref name="frames"/> 不参与调用。
        /// 走的是临时天气通道，不会被下一次日夜推进直接抹掉。
        /// </summary>
        public void SetWeather(string weatherId, int frames)
        {
            if (!Weathers.TryGetValue(weatherId ?? string.Empty, out GameWeather weather))
                throw Failed($"天气 `{weatherId}` 未登记。");

            if (weather == GameWeather.Normal)
            {
                PolarisAPI.Game.World.ClearWeather();
                return;
            }

            if (!PolarisAPI.Game.World.SetWeather(weather))
                throw Failed($"天气 `{weatherId}` 设置失败。");
        }

        /// <summary>
        /// 危险度只能改原版的"手动附加值"（0–45，游戏内部会截断），基础危险度不对外开放写入。
        /// 因此这里的 <paramref name="level"/> 是附加值而不是绝对等级，<paramref name="immediate"/> 无过渡可言，直接到位。
        /// </summary>
        public PevtWait SetDanger(int level, bool immediate)
        {
            if (level < 0)
                throw Failed($"危险度附加值不能为负数，实际为 {level}。");

            PolarisAPI.Game.World.DangerBonus = level;
            return new PevtNextFrameWait();
        }

        public void SetDarkness(bool enabled)
        {
            if (!RequireMap().SetDark(enabled))
                throw Failed("地图黑暗状态切换失败。");
        }

        private static GameMap RequireMap()
        {
            GameMap map = PolarisAPI.Game.World.CurrentMap;
            if (map == null)
                throw Failed("当前没有加载任何地图。");

            return map;
        }

        private static PevtRoutineFailureException Failed(string message) =>
            new PevtRoutineFailureException("PEVTR4001", message);
    }
}
