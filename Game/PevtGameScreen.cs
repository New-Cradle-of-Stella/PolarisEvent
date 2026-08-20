using System;
using System.Globalization;
using evt;
using m2d;
using nel;
using Polaris.Pevt.Runtime;
using UnityEngine;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 画面遮罩与聚光。遮罩用一个专属的背景填充图层而不是原版画面转场（<see cref="EvTransFader"/>），
    /// 因为 <c>@screen_fade</c> 要的是一层能停在任意透明度上的纯色；遮罩层是事件临时占用，透明度归零或事件结束时释放。
    /// </summary>
    internal sealed class PevtGameScreen : IPevtScreen
    {
        /// <summary>
        /// 原版 <see cref="EvDrawerContainer.Get"/> 要求 <c>#</c>/<c>&amp;</c> 后必须是数字 ID。
        /// 使用较高的专用 ID，<c>&amp;</c> 前缀把遮罩放进前景层。
        /// </summary>
        private const string FadeKey = "&9000";
        private const string FlashKey = "&9001";

        private readonly PevtGameClock _clock;
        private string _color = "#000000";
        private float _colorAlpha = 1f;
        private float _opacity;
        private bool _fadeLayerOpen;
        private bool _spotlight;

        public PevtGameScreen(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private EvDrawer FadeLayer(bool create) =>
            PevtGameHost.Safe(() => PevtGameHost.Drawers?.Get(FadeKey, false, !create), null);

        public void EnsureFadeLayer()
        {
            PevtGameHost.Guard("EnsureFadeLayer", () =>
            {
                EvDrawer drawer = FadeLayer(true);
                if (drawer == null)
                    return;

                if (_fadeLayerOpen)
                    return;

                _opacity = 0f;
                _fadeLayerOpen = true;
                ApplyColor(drawer);
            });
        }

        public void SetFadeColor(string color)
        {
            string next = string.IsNullOrEmpty(color) ? "#000000" : color;
            bool changed = !string.Equals(_color, next, StringComparison.OrdinalIgnoreCase);
            _color = next;

            // screen_fade 每次都会调用 SetFadeColor。同色淡出时如果再调 setGrp，
            // 原版会清掉旧填充网格并在下一绘制帧重建，中间就会露出一帧底图。
            if (!changed && _fadeLayerOpen)
                return;

            PevtGameHost.Guard("SetFadeColor", () =>
            {
                EvDrawer drawer = FadeLayer(false);
                if (drawer != null)
                    ApplyColor(drawer);
            });
        }

        /// <summary>
        /// 用 <c>GRP_FILL</c> 视图把图层变成一块纯色，并铺满整屏。
        /// 原版填充色接受 <c>WHITE</c> 这类名字，也接受十六进制串。
        /// </summary>
        private void ApplyColor(EvDrawer drawer)
        {
            uint parsed = TX.str2color(ToVanillaColor(_color));
            _colorAlpha = ((parsed >> 24) & 0xFF) / 255f;

            // Alpha 先用 FE 创建，故意不触发原版 GRP_WHOLE_FILL 的“底图可省略”优化。
            // 否则半透明渐变时原版会停止绘制遮罩下方的画面，视觉上仍是瞬间黑屏。
            uint setupColor = 0xFE000000u | (parsed & 0x00FFFFFFu);
            string setup = "0x" + setupColor.ToString("x8", CultureInfo.InvariantCulture);
            if (!drawer.setGrp(setup, "f"))
                return;

            ApplyOpacity(drawer, _opacity);
        }

        /// <summary>
        /// 不使用 <see cref="EvDrawer.palp"/>：原版 <c>calc_sp_move</c> 在每个绘制帧都会把它重置为 1。
        /// 改写填充色 Alpha 才能让 PEVT 补间值稳定保留到实际绘制。
        /// </summary>
        private void ApplyOpacity(EvDrawer drawer, float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            uint alpha = (uint)Mathf.RoundToInt(_opacity * _colorAlpha * 255f);
            drawer.gcol = (drawer.gcol & 0x00FFFFFFu) | (alpha << 24);
            drawer.palp = 1f;
            drawer.draw_flag |= EvDrawer.DRAWF_CHANGED_ALPHA | EvDrawer.DRAWF_REDRAW;
        }

        /// <summary>
        /// PEVT 颜色是 CSS 式 <c>#RRGGBB</c>/<c>#RRGGBBAA</c>，原版填充解析器要求
        /// <c>0xAARRGGBB</c>。原版命名颜色保持不变。
        /// </summary>
        private static string ToVanillaColor(string color)
        {
            if (string.IsNullOrEmpty(color) || color[0] != '#')
                return color;

            string hex = color.Substring(1);
            if (hex.Length == 6)
                return "0xff" + hex;
            if (hex.Length == 8)
                return "0x" + hex.Substring(6, 2) + hex.Substring(0, 6);

            return color;
        }

        public PevtWait FadeTo(float opacity, int frames, string easing)
        {
            EvDrawer drawer = FadeLayer(true);
            if (drawer == null)
                return new PevtFrameWait(frames);

            if (!_fadeLayerOpen)
            {
                _opacity = 0f;
                _fadeLayerOpen = true;
                ApplyColor(drawer);
            }

            float from = _opacity;
            if (frames > 0)
            {
                long deadline = _clock.Frame + frames;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            return new PevtTweenWait(frames, easing, t =>
            {
                ApplyOpacity(drawer, from + (opacity - from) * t);
            });
        }

        public void ReleaseFadeLayer()
        {
            if (!_fadeLayerOpen)
                return;

            _fadeLayerOpen = false;
            _opacity = 0f;
            PevtGameHost.Guard("ReleaseFadeLayer", () => FadeLayer(false)?.release());
        }

        /// <summary>闪光走原版的 <c>setFlash</c>：出现、保持、淡出三段时间正好对应参数。</summary>
        public PevtWait Flash(string color, int delayFrames, int holdFrames, int fadeFrames)
        {
            PevtGameHost.Guard("ScreenFlash", () =>
            {
                EvDrawer drawer = PevtGameHost.Drawers?.Get(FlashKey, false, false);
                if (drawer == null)
                    return;

                if (!drawer.setGrp(ToVanillaColor(string.IsNullOrEmpty(color) ? "WHITE" : color), "f"))
                    return;

                drawer.setFlash(delayFrames, Math.Max(1, holdFrames), fadeFrames);
            });

            int total = delayFrames + holdFrames + fadeFrames;
            if (total > 0)
            {
                long deadline = _clock.Frame + total;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            return new PevtFrameWait(total);
        }

        /// <summary>聚光目标目前只支持玩家与画面中心；地图实体聚光属于功能阶段 F 的 World 适配。</summary>
        public bool ResolveTarget(string targetId) => targetId == "player" || targetId == "screen";

        public void SetSpotlight(string targetId, bool enabled, string style)
        {
            _spotlight = enabled;
            PevtGameHost.Guard("SetSpotlight", () =>
            {
                BlurScreen blur = PevtGameHost.Map?.BlurSc;
                if (blur == null)
                    return;

                if (enabled)
                {
                    blur.addFlag(PevtGameHost.Flag);
                    blur.need_recreate_img = true;
                }
                else
                {
                    blur.remFlag(PevtGameHost.Flag);
                }
            });
        }

        /// <summary>事件结束、替换或异常时收掉遮罩与聚光。</summary>
        public void ReleaseAll()
        {
            ReleaseFadeLayer();
            if (_spotlight)
                SetSpotlight(null, false, null);

            PevtGameHost.Guard("ReleaseFlashLayer",
                () => PevtGameHost.Drawers?.Get(FlashKey, false, true)?.release());
        }
    }

    /// <summary>
    /// 镜头适配器。事件开始时保存一份快照（位置、缩放与跟随对象），
    /// <c>@camera_reset</c> 和事件结束都从这份快照恢复，避免把偏移的镜头留给玩家。
    /// </summary>
    internal sealed class PevtGameCamera : IPevtCamera
    {
        private readonly PevtGameClock _clock;

        private bool _hasSnapshot;
        private float _snapshotX;
        private float _snapshotY;
        private float _snapshotScale;
        private M2Mover _snapshotMover;

        /// <summary>当前正在跟随的地图实体键；没有在跟随实体时为 null。</summary>
        private string _followEntityKey;

        /// <summary>当前跟随的地图实体键，供 F8 展示。</summary>
        internal string FollowEntityKey => _followEntityKey;

        /// <summary>最近一次"跟随目标消失、镜头已交回快照"的实体键，供 F8 展示。</summary>
        internal string LostFollowKey { get; private set; }

        public PevtGameCamera(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>由宿主在事件启动时调用一次。</summary>
        public void CaptureSnapshot()
        {
            PevtGameHost.Guard("CaptureCamera", () =>
            {
                M2Camera camera = PevtGameHost.Camera;
                if (camera == null)
                    return;

                _snapshotX = camera.PosMainTransform.x;
                _snapshotY = camera.PosMainTransform.y;
                _snapshotScale = camera.scale;
                _snapshotMover = camera.getBaseMover();
                _hasSnapshot = true;
            });
        }

        /// <summary>
        /// 镜头目标（PEVT-E02）。写法由 <see cref="PevtCameraTarget"/> 判定，这里只回答"在当前地图上存不存在"：
        /// <c>entity:</c> 查地图 mover 表，<c>anchor:</c> 与旧的裸写法查标签点。两条查询都不产生副作用。
        /// </summary>
        public bool ResolveTarget(string targetId)
        {
            if (!PevtCameraTarget.TryParse(targetId, out PevtCameraTarget target))
                return false;

            switch (target.Kind)
            {
                case PevtCameraTargetKind.Player:
                case PevtCameraTargetKind.Point:
                    return true;

                case PevtCameraTargetKind.Entity:
                    return PevtGameHost.Safe(() => FindMover(target.Key) != null, false);

                default:
                    return PevtGameHost.Safe(
                        () => M2DBase.Instance?.curMap?.getPoint(target.Key, true) != null,
                        false);
            }
        }

        /// <summary>
        /// 地图实体按键查找。<see cref="Map2d.getMoverByName"/> 自己会跳过 <c>destructed</c> 的对象，
        /// 因此"实体还在不在"和"实体叫什么"用的是同一条查询，跟随监视也不会拿到已销毁的引用。
        /// </summary>
        private static M2Mover FindMover(string key) =>
            string.IsNullOrEmpty(key) ? null : M2DBase.Instance?.curMap?.getMoverByName(key, true);

        /// <summary>
        /// <c>player</c> 与 <c>entity:</c> 是跟随：把镜头基准交给对应 mover，不覆写 <c>x</c>/<c>y</c>。
        /// <c>point</c> 用显式坐标，<c>anchor:</c>（含旧的裸写法）用标签点。
        /// </summary>
        public PevtWait<bool> MoveTo(string targetId, float x, float y, float zoom, int frames, string easing)
        {
            if (!PevtCameraTarget.TryParse(targetId, out PevtCameraTarget target))
                return new PevtGameCameraWait(frames, () => false);

            // 实体目标要在真正改动镜头之前拿到 mover：找不到就什么都不做，让等待以 false 结束，
            // 组合把它转成 PEVTR4602，而不是留下一个指向不存在实体的跟随。
            M2Mover follow = null;
            if (target.Kind == PevtCameraTargetKind.Entity)
            {
                follow = PevtGameHost.Safe(() => FindMover(target.Key), null);
                if (follow == null)
                    return new PevtGameCameraWait(0, () => false);
            }

            PevtGameHost.Guard("CameraMoveTo", () =>
            {
                M2Camera camera = PevtGameHost.Camera;
                if (camera == null)
                    return;

                switch (target.Kind)
                {
                    case PevtCameraTargetKind.Player:
                    {
                        // 跟随玩家时不覆写坐标，只把镜头交回给玩家的 mover。
                        M2Mover player = M2DBase.Instance?.curMap?.getKeyPr() as M2Mover;
                        if (player != null)
                            camera.assignBaseMover(player, -1);
                        _followEntityKey = null;
                        break;
                    }

                    case PevtCameraTargetKind.Entity:
                        camera.assignBaseMover(follow, -1);
                        _followEntityKey = target.Key;
                        break;

                    case PevtCameraTargetKind.Point:
                        camera.assignBaseMover(null, -1);
                        camera.moveTo(x, y, MoveSpeed(frames), default(Rect));
                        _followEntityKey = null;
                        break;

                    default:
                        camera.assignBaseMover(null, -1);
                        camera.moveToLabelPt(target.Key);
                        _followEntityKey = null;
                        break;
                }

                camera.animateScaleTo(zoom, frames);
            });

            if (frames > 0)
            {
                long deadline = _clock.Frame + frames;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            string followKey = target.Kind == PevtCameraTargetKind.Entity ? target.Key : null;
            return new PevtGameCameraWait(frames, followKey == null ? (Func<bool>)null : () => FindMover(followKey) != null);
        }

        public PevtWait Shake(float amplitude, int durationFrames, float frequency)
        {
            PevtGameHost.Guard("CameraShake",
                () => PevtGameHost.Camera?.setQuake(amplitude, durationFrames, frequency, 0));

            return TrackFrames(durationFrames);
        }

        /// <summary>
        /// PEVT 事件期间地图主循环暂停，M2Camera.run 不会推进 Quaker；
        /// Quaker.run 只计算 Qu.x/Qu.y，随后还必须调用 callImmediateMove，
        /// 原版才会把这份偏移写入摄像机 Transform。
        /// </summary>
        internal void Update()
        {
            PevtGameHost.Guard("UpdateCameraQuake", () =>
            {
                M2Camera camera = PevtGameHost.Camera;
                if (camera?.Qu == null)
                    return;

                camera.Qu.run(1f);
                camera.callImmediateMove();
            });

            WatchFollowTarget();
        }

        /// <summary>
        /// 跟随目标消失的看守。
        ///
        /// <c>@camera_move("entity:...")</c> 的等待在 <c>frames</c> 帧后就结束了，但跟随本身会一直持续到
        /// 事件结束，所以实体可能在动作完成之后才消失。那时不能把已经继续往下演的事件反向掀掉，
        /// 但也绝不能让 <see cref="M2Camera.MvCenter"/> 停在一个已销毁的对象上——于是把镜头交回事件开始时的快照基准。
        /// 动作进行中消失是另一回事：那由 <see cref="PevtGameCameraWait"/> 以 false 结束，组合报 PEVTR4602。
        /// </summary>
        private void WatchFollowTarget()
        {
            string key = _followEntityKey;
            if (key == null)
                return;

            PevtGameHost.Guard("WatchCameraFollow", () =>
            {
                if (FindMover(key) != null)
                    return;

                _followEntityKey = null;
                LostFollowKey = key;

                M2Camera camera = PevtGameHost.Camera;
                if (camera != null && _hasSnapshot)
                    camera.assignBaseMover(_snapshotMover, -1);
            });
        }

        public PevtWait RestoreEventSnapshot(int frames)
        {
            // 恢复快照就是"不再跟随任何本事件指定的实体"，看守随之停掉。
            _followEntityKey = null;

            PevtGameHost.Guard("RestoreCamera", () =>
            {
                M2Camera camera = PevtGameHost.Camera;
                if (camera == null || !_hasSnapshot)
                    return;

                camera.assignBaseMover(_snapshotMover, -1);
                if (frames <= 0)
                    camera.setTo(_snapshotX, _snapshotY);
                else
                    camera.moveTo(_snapshotX, _snapshotY, MoveSpeed(frames), default(Rect));

                camera.animateScaleTo(_snapshotScale, frames);
            });

            return TrackFrames(frames);
        }

        /// <summary>
        /// 原版镜头移动按"速度"而不是"帧数"参数化；这里按目标帧数折算，
        /// 0 帧表示立即到位（速度取一个足够大的值）。
        /// </summary>
        private static float MoveSpeed(int frames) => frames <= 0 ? 1000f : 60f / frames;

        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }
    }

    /// <summary>
    /// 一次镜头动作的等待。结果表示"动作结束时目标是否仍然有效"：
    /// 只有 <c>entity:</c> 目标会传进 <c>targetAlive</c>，其余目标没有可失效的东西，恒为 true。
    /// </summary>
    internal sealed class PevtGameCameraWait : PevtWait<bool>
    {
        private readonly int _frames;
        private readonly Func<bool> _targetAlive;
        private long _startFrame = -1;

        public PevtGameCameraWait(int frames, Func<bool> targetAlive)
        {
            _frames = frames < 0 ? 0 : frames;
            _targetAlive = targetAlive;
        }

        public override string ProgressSource => "镜头动作";

        protected override void OnTick(PevtWaitContext context)
        {
            if (_startFrame < 0)
                _startFrame = context.Frame;

            // 目标先检查再计时：0 帧动作也必须发现"目标已经不在了"。
            if (_targetAlive != null && !PevtGameHost.Safe(_targetAlive, false))
            {
                CompleteSucceeded(false);
                return;
            }

            if (context.Frame - _startFrame >= _frames)
                CompleteSucceeded(true);
        }
    }

    /// <summary>
    /// 后处理适配器。效果 ID 就是原版 <see cref="POSTM"/> 的名字，未知名字在
    /// <see cref="Resolve"/> 就被挡下，不会产生半开的后处理实例。
    /// </summary>
    internal sealed class PevtGameEffect : IPevtEffect
    {
        private readonly PevtGameClock _clock;

        public PevtGameEffect(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool Resolve(string effectId) => TryParse(effectId, out _);

        public PevtWait SetEnabled(string effectId, bool enabled, int frames)
        {
            PevtGameHost.Guard("EffectSetEnabled", () =>
            {
                PostEffect effects = PevtGameHost.Map?.PE;
                if (effects == null || !TryParse(effectId, out POSTM id))
                    return;

                if (enabled)
                    effects.setPE(id, frames <= 0 ? 0f : frames, 1f, 0);
                else
                    effects.setAlpha(id, 0f);
            });

            return TrackFrames(frames);
        }

        public PevtWait Pulse(string effectId, int fadeFrames, int holdFrames)
        {
            PevtGameHost.Guard("EffectPulse", () =>
            {
                PostEffect effects = PevtGameHost.Map?.PE;
                if (effects == null || !TryParse(effectId, out POSTM id))
                    return;

                effects.setPEfadeinout(id, fadeFrames, holdFrames, 1f, 0);
            });

            return TrackFrames(fadeFrames * 2 + holdFrames);
        }

        private static bool TryParse(string effectId, out POSTM id)
        {
            id = default(POSTM);
            if (string.IsNullOrEmpty(effectId))
                return false;

            string name = effectId.ToUpperInvariant();
            if (!Enum.IsDefined(typeof(POSTM), name))
                return false;

            id = (POSTM)Enum.Parse(typeof(POSTM), name);
            return true;
        }

        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }
    }
}
