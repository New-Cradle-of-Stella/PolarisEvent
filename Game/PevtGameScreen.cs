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
    /// 画面遮罩与聚光。
    ///
    /// 遮罩用一个专属的背景填充图层，而不是原版的画面转场（<see cref="EvTransFader"/>）——
    /// 转场是"从一张图切到另一张图"的过渡，而 <c>@screen_fade</c> 要的是一层可以停在任意
    /// 透明度上的纯色。遮罩层是事件临时占用，透明度归零或事件结束时释放。
    /// </summary>
    internal sealed class PevtGameScreen : IPevtScreen
    {
        /// <summary>遮罩层的原版键；<c>&amp;</c> 前缀把它放进前景层，盖住立绘与图层。</summary>
        private const string FadeKey = "&pevt_fade";

        private readonly PevtGameClock _clock;
        private string _color = "#000000";
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

                _fadeLayerOpen = true;
                ApplyColor(drawer);
            });
        }

        public void SetFadeColor(string color)
        {
            _color = string.IsNullOrEmpty(color) ? "#000000" : color;
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
            if (!drawer.setGrp(_color, "f"))
                return;

            drawer.initPosition("L", "B", "R", "T", 0f);
            drawer.fadein(0, 0, true);
            drawer.palp = drawer.palp <= 0f ? 0f : drawer.palp;
        }

        public PevtWait FadeTo(float opacity, int frames, string easing)
        {
            EvDrawer drawer = FadeLayer(true);
            if (drawer == null)
                return new PevtFrameWait(frames);

            _fadeLayerOpen = true;
            float from = drawer.palp;
            if (frames > 0)
            {
                long deadline = _clock.Frame + frames;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            return new PevtTweenWait(frames, easing, t =>
            {
                drawer.palp = from + (opacity - from) * t;
                drawer.draw_flag |= EvDrawer.DRAWF_CHANGED_ALPHA | EvDrawer.DRAWF_REDRAW;
            });
        }

        public void ReleaseFadeLayer()
        {
            if (!_fadeLayerOpen)
                return;

            _fadeLayerOpen = false;
            PevtGameHost.Guard("ReleaseFadeLayer", () => FadeLayer(false)?.release());
        }

        /// <summary>闪光走原版的 <c>setFlash</c>：出现、保持、淡出三段时间正好对应参数。</summary>
        public PevtWait Flash(string color, int delayFrames, int holdFrames, int fadeFrames)
        {
            PevtGameHost.Guard("ScreenFlash", () =>
            {
                EvDrawer drawer = PevtGameHost.Drawers?.Get(FadeKey + "_flash", false, false);
                if (drawer == null)
                    return;

                if (!drawer.setGrp(string.IsNullOrEmpty(color) ? "WHITE" : color, "f"))
                    return;

                drawer.initPosition("L", "B", "R", "T", 0f);
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
                () => PevtGameHost.Drawers?.Get(FadeKey + "_flash", false, true)?.release());
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
