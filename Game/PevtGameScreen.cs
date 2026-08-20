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
        /// 镜头目标：<c>player</c> 跟随玩家，<c>point</c> 使用显式坐标，
        /// 其余当作地图标签点，用 <see cref="Map2d.getPoint"/> 校验存在性——不存在时不产生副作用。
        /// </summary>
        public bool ResolveTarget(string targetId)
        {
            if (targetId == "player" || targetId == "point")
                return true;

            return PevtGameHost.Safe(
                () => M2DBase.Instance?.curMap?.getPoint(targetId, true) != null,
                false);
        }

        public PevtWait MoveTo(string targetId, float x, float y, float zoom, int frames, string easing)
        {
            PevtGameHost.Guard("CameraMoveTo", () =>
            {
                M2Camera camera = PevtGameHost.Camera;
                if (camera == null)
                    return;

                if (targetId == "player")
                {
                    // 跟随玩家时不覆写坐标，只把镜头交回给玩家的 mover。
                    M2Mover player = M2DBase.Instance?.curMap?.getKeyPr() as M2Mover;
                    if (player != null)
                        camera.assignBaseMover(player, -1);
                }
                else if (targetId == "point")
                {
                    camera.assignBaseMover(null, -1);
                    camera.moveTo(x, y, MoveSpeed(frames), default(Rect));
                }
                else
                {
                    camera.assignBaseMover(null, -1);
                    camera.moveToLabelPt(targetId);
                }

                camera.animateScaleTo(zoom, frames);
            });

            return TrackFrames(frames);
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
        }

        public PevtWait RestoreEventSnapshot(int frames)
        {
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
