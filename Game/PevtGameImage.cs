using System;
using System.Collections.Generic;
using System.Globalization;
using evt;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 通用演出图层适配器，建立在原版 <see cref="EvDrawer"/> 之上。
    /// </summary>
    internal sealed class PevtGameImage : IPevtImage
    {
        private const string BackPrefix = "#";
        private const string FrontPrefix = "&";
        private const int FirstLayerNumber = 9100;

        /// <summary>单张 CG 用固定的前景图层，不占用脚本可见的图层名字空间。</summary>
        private const string SingleLayerId = "__single";

        private readonly PevtGameClock _clock;

        /// <summary>本事件建出来的图层：PEVT 图层 ID → 当前使用的原版键。</summary>
        private readonly Dictionary<string, string> _layers = new Dictionary<string, string>(StringComparer.Ordinal);

        private int _nextLayerNumber = FirstLayerNumber;

        private bool _singleOpen;

        public PevtGameImage(PevtGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private string VanillaKey(string layerId, bool create)
        {
            string id = layerId ?? string.Empty;
            if (_layers.TryGetValue(id, out string key))
                return key;
            if (!create)
                return null;

            key = BackPrefix + _nextLayerNumber++.ToString(CultureInfo.InvariantCulture);
            _layers[id] = key;
            return key;
        }

        /// <summary>取图层；<paramref name="create"/> 为 true 时不存在就建一个。</summary>
        private EvDrawer Layer(string layerId, bool create)
        {
            EvDrawerContainer drawers = PevtGameHost.Drawers;
            if (drawers == null)
                return null;

            string key = VanillaKey(layerId, create);
            if (key == null)
                return null;

            EvDrawer drawer = PevtGameHost.Safe(() => drawers.Get(key, false, !create), null);
            return drawer;
        }

        // ---- 内容与可见性 ----

        public void SetContent(string layerId, string assetId) =>
            PevtGameHost.Guard("ImageSetContent", () => Layer(layerId, true)?.setGrp(assetId, string.Empty));

        public void SetVisible(string layerId, bool visible) =>
            PevtGameHost.Guard("ImageSetVisible", () =>
            {
                EvDrawer drawer = Layer(layerId, visible);
                if (drawer == null)
                    return;

                if (visible)
                    drawer.fadein(0, 0, true);
                else
                    drawer.fadeout(0, true);
            });

        public void SetPosition(string layerId, float x, float y) =>
            PevtGameHost.Guard("ImageSetPosition", () => Layer(layerId, true)?.initPosition(Num(x), Num(y), string.Empty, string.Empty, 0f));

        public PevtWait MoveTo(string layerId, float x, float y, int frames, string easing)
        {
            PevtGameHost.Guard("ImageMoveTo", () => Layer(layerId, true)?.moveTo(Num(x), Num(y), frames, easing ?? string.Empty));
            return TrackFrames(frames);
        }

        public PevtWait MoveBy(string layerId, float x, float y, int frames, string easing)
        {
            PevtGameHost.Guard("ImageMoveBy", () =>
            {
                EvDrawer drawer = Layer(layerId, true);
                if (drawer == null)
                    return;

                // get_x/get_y 已经是解算过 base 的实际像素，所以相对移动直接加就行。
                drawer.moveTo(Num(drawer.get_x() + x), Num(drawer.get_y() + y), frames, easing ?? string.Empty);
            });

            return TrackFrames(frames);
        }

        /// <summary>
        /// 透明度。完全显示与完全隐藏交给原版的淡入淡出（它同时管激活状态），
        /// 中间值则补间 <c>palp</c>——原版没有"淡到某个透明度"的命令。
        /// </summary>
        public PevtWait FadeTo(string layerId, float opacity, int frames, string easing)
        {
            EvDrawer drawer = null;
            PevtGameHost.Guard("ImageFadeTo", () => drawer = Layer(layerId, opacity > 0f));
            if (drawer == null)
                return new PevtFrameWait(0);

            if (opacity <= 0f)
            {
                PevtGameHost.Guard("ImageFadeOut", () => drawer.fadeout(frames, true));
                return TrackFrames(frames);
            }

            float from = drawer.palp;
            return Tween(frames, easing, t =>
            {
                drawer.palp = from + (opacity - from) * t;
                drawer.draw_flag |= EvDrawer.DRAWF_CHANGED_ALPHA | EvDrawer.DRAWF_REDRAW;
                if (t >= 1f && opacity >= 1f)
                    drawer.fadein(0, 0, true);
            });
        }

        public PevtWait ScaleTo(string layerId, float x, float y, int frames, string easing)
        {
            EvDrawer drawer = Layer(layerId, true);
            if (drawer == null)
                return new PevtFrameWait(0);

            // 原版图层只有一个各向同性的缩放系数；两轴不同时取 x，并按规范不静默拉伸另一轴。
            float target = x;
            float from = drawer.pz;
            return Tween(frames, easing, t =>
            {
                drawer.pz = from + (target - from) * t;
                drawer.draw_flag |= EvDrawer.DRAWF_POS | EvDrawer.DRAWF_REDRAW;
            });
        }

        public PevtWait RotateTo(string layerId, float degrees, int frames, string easing)
        {
            EvDrawer drawer = Layer(layerId, true);
            if (drawer == null)
                return new PevtFrameWait(0);

            float target = degrees * (float)Math.PI / 180f;
            float from = drawer.prot;
            return Tween(frames, easing, t =>
            {
                drawer.prot = from + (target - from) * t;
                drawer.draw_flag |= EvDrawer.DRAWF_POS | EvDrawer.DRAWF_REDRAW;
            });
        }

        public PevtWait TintTo(string layerId, string color, int frames)
        {
            EvDrawer drawer = Layer(layerId, true);
            if (drawer == null || !TryParseColor(color, out uint target))
                return new PevtFrameWait(0);

            uint from = drawer.get_gcol();
            return Tween(frames, "linear", t =>
            {
                drawer.gcol = Lerp(from, target, t);
                drawer.draw_flag |= EvDrawer.DRAWF_FINEMATERIAL | EvDrawer.DRAWF_REDRAW;
            });
        }

        public void SetFlip(string layerId, bool horizontal, bool vertical) =>
            PevtGameHost.Guard("ImageFlip", () => Layer(layerId, true)?.flip(horizontal, vertical, string.Empty));

        /// <summary>
        /// 层次。原版只有"背景层"和"前景层"两档可用键名，所以非负 order 归前景、负数归背景，
        /// 用 <see cref="EvDrawer.rewriteKeyAndLayer"/> 原地改写而不是重建图层。
        /// </summary>
        public void SetOrder(string layerId, int order) =>
            PevtGameHost.Guard("ImageSetOrder", () =>
            {
                EvDrawer drawer = Layer(layerId, true);
                if (drawer == null)
                    return;

                string id = layerId ?? string.Empty;
                string oldKey = VanillaKey(id, true);
                string key = (order >= 0 ? FrontPrefix : BackPrefix) + oldKey.Substring(1);
                if (key == oldKey)
                    return;

                drawer.rewriteKeyAndLayer(key);
                _layers[id] = key;

                // 容器只在 need_sort 置位的那一帧重排图层；不置位的话改完键名层序也不会立刻生效。
                EvDrawerContainer drawers = PevtGameHost.Drawers;
                if (drawers != null)
                    drawers.need_sort = true;
            });

        public void Fill(string layerId, string color) =>
            PevtGameHost.Guard("ImageFill", () =>
            {
                EvDrawer drawer = Layer(layerId, true);
                if (drawer == null)
                    return;

                if (drawer.setGrp(string.IsNullOrEmpty(color) ? "WHITE" : color, "f"))
                    drawer.fadein(0, 0, true);
            });

        /// <summary>
        /// 清一组图层。组 ID 就是原版的 layerset 记号，<c>picHideWhole</c> 负责把该组里
        /// 本事件建立的显示实例全部收掉。
        /// </summary>
        public PevtWait ClearGroup(string groupId, int frames)
        {
            PevtGameHost.Guard("ImageClearGroup", () => PevtGameHost.Drawers?.picHideWhole(groupId, frames <= 0));
            return TrackFrames(frames);
        }

        // ---- 单张 CG ----

        public void OpenSingle(string assetId, string caption)
        {
            PevtGameHost.Guard("OpenSingle", () =>
            {
                EvDrawer drawer = Layer(SingleLayerId, true);
                if (drawer == null)
                    return;

                SetOrder(SingleLayerId, 0);
                drawer.setGrp(assetId, string.Empty);
                drawer.initPosition("0", "0", string.Empty, string.Empty, 0f);
                drawer.fadein(0, 0, true);
                _singleOpen = true;
            });
        }

        /// <summary>单张 CG 由玩家的确认或取消键关闭，与原版看图界面一致。</summary>
        public PevtWait WaitSingleClose() =>
            new PevtPredicateWait(() => !_singleOpen || PevtGameHost.AdvancePressed() || PevtGameHost.CancelPressed(), "单张 CG 关闭输入");

        public void CloseSingle()
        {
            if (!_singleOpen)
                return;

            _singleOpen = false;
            PevtGameHost.Guard("CloseSingle", () => Layer(SingleLayerId, false)?.fadeout(0, true));
        }

        /// <summary>剪影：原版把它当带 <c>H_</c> 视图类型的图层，位置取自立绘站位表。</summary>
        public PevtWait ShowSilhouette(string layerId, string assetId, string anchorId, int frames)
        {
            PevtGameHost.Guard("ShowSilhouette", () =>
            {
                EvDrawer drawer = Layer(layerId, true);
                if (drawer == null || !drawer.setGrp(assetId, "H_"))
                    return;

                if (TalkDrawer.getDefinedPositionRaw(anchorId, out TalkDrawer.TkDep dep))
                    drawer.initSilhouettePic(dep, true, false, frames <= 0);

                drawer.fadein(frames, 0, true);
            });

            return TrackFrames(frames);
        }

        // ---- 清理 ----

        /// <summary>事件结束、替换或异常时收掉本事件建立的全部图层。</summary>
        public void ReleaseAll()
        {
            EvDrawerContainer drawers = PevtGameHost.Drawers;
            if (drawers != null)
            {
                foreach (KeyValuePair<string, string> entry in _layers)
                {
                    string key = entry.Value;
                    PevtGameHost.Guard("ImageRelease", () => drawers.Get(key, false, true)?.release());
                }
            }

            _layers.Clear();
            _singleOpen = false;
        }

        // ---- 辅助 ----

        private PevtWait Tween(int frames, string easing, Action<float> apply)
        {
            if (frames > 0)
            {
                long deadline = _clock.Frame + frames;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            return new PevtTweenWait(frames, easing, apply);
        }

        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }

        /// <summary>解析 <c>#RRGGBB</c> / <c>#AARRGGBB</c>。组合层已经做过格式校验。</summary>
        private static bool TryParseColor(string color, out uint value)
        {
            value = 0xFFFFFFFF;
            if (string.IsNullOrEmpty(color))
                return false;

            string hex = color[0] == '#' ? color.Substring(1) : color;
            if (hex.Length == 6)
                hex = "FF" + hex;
            if (hex.Length != 8)
                return false;

            return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static uint Lerp(uint from, uint to, float t)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                float a = (from >> shift) & 0xFF;
                float b = (to >> shift) & 0xFF;
                var channel = (uint)(a + (b - a) * t);
                result |= (channel & 0xFF) << shift;
            }

            return result;
        }
    }
}
