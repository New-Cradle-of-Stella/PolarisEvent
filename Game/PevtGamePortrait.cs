using System;
using System.Collections.Generic;
using System.Globalization;
using evt;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 人物立绘适配器。站位坐标由 PolarisEvent 自己保存，不会被翻译回原版的 <c>L/R/C</c> 组合键；
    /// 唯一用到原版位置键的地方是 <see cref="EvDrawerContainer.activateTalker"/> 的创建参数，那里固定用中立的 <c>C</c> 引导，
    /// 紧接着就用 PEVT 自己的坐标覆盖。
    /// </summary>
    internal sealed class PevtGamePortrait : IPevtPortrait
    {
        /// <summary>只用于把 TalkDrawer 建出来的中立引导位置键。</summary>
        private const string BootstrapPositionKey = "C";

        /// <summary>
        /// 内置语义站位的屏幕坐标（相对画面中心的像素偏移）与入场坐标。
        /// 数值与原版立绘的构图刻度一致，但归 PolarisEvent 所有。
        /// </summary>
        private static readonly Dictionary<string, float[]> Anchors =
            new Dictionary<string, float[]>(StringComparer.Ordinal)
            {
                // { x, enterX }
                ["center"] = new[] { 0f, 0f },
                ["left"] = new[] { -360f, -560f },
                ["right"] = new[] { 360f, 560f },
                ["near-left"] = new[] { -120f, -420f },
                ["near-right"] = new[] { 120f, 420f },
                ["far-left"] = new[] { -490f, -640f },
                ["far-right"] = new[] { 490f, 640f },
                ["off-left"] = new[] { -640f, -640f },
                ["off-right"] = new[] { 640f, 640f },
            };

        private readonly PevtActorRegistry _actors;
        private readonly PevtGameClock _clock;

        /// <summary>本事件建出来的立绘，事件结束时逐个退场。</summary>
        private readonly Dictionary<string, TalkDrawer> _drawers = new Dictionary<string, TalkDrawer>(StringComparer.Ordinal);

        /// <summary>
        /// 已选中的外观。<c>@actor_enter</c> 会先选外观再创建 TalkDrawer，必须暂存到创建时再应用。
        /// </summary>
        private readonly Dictionary<string, string> _appearances = new Dictionary<string, string>(StringComparer.Ordinal);

        public PevtGamePortrait(
            PevtActorRegistry actors,
            PevtGameClock clock)
        {
            _actors = actors ?? throw new ArgumentNullException(nameof(actors));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ---- 解析 ----

        private bool TryActor(string actorId, out ActorDefinition actor)
        {
            actor = null;
            if (!_actors.Directory.TryGetActor(actorId, out ActorRegistration registration))
                return false;

            actor = registration.Actor;
            return true;
        }

        /// <summary>
        /// PEVT 人物 ID → 原版 <see cref="EvPerson"/> 键。与对话适配器同一套规则：
        /// 内置人物用目录声明的短键，模组人物用最终人物 ID 现建一个。
        /// </summary>
        private string PersonKey(string actorId)
        {
            if (!TryActor(actorId, out ActorDefinition actor))
                return null;

            string legacy = actor.LegacyPerson ?? actor.DefaultPortrait?.LegacyPerson;
            return string.IsNullOrEmpty(legacy) ? actorId : legacy;
        }

        /// <summary>把站位 ID 解析成 PEVT 自己的坐标；返回 false 表示这个站位不认识。</summary>
        private bool TryAnchor(string actorId, string anchorId, out float x, out float y, out float enterX, out float enterY)
        {
            x = y = enterX = enterY = 0f;

            if (TryActor(actorId, out ActorDefinition actor) && actor.TryGetAnchor(anchorId, out ActorAnchor anchor))
            {
                x = anchor.X;
                y = anchor.Y;
                enterX = anchor.EnterX ?? anchor.X;
                enterY = anchor.EnterY ?? anchor.Y;
                return true;
            }

            if (!Anchors.TryGetValue(anchorId ?? string.Empty, out float[] preset))
                return false;

            x = preset[0];
            enterX = preset[1];
            return true;
        }

        private static string Coord(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private TalkDrawer Drawer(string actorId) =>
            _drawers.TryGetValue(actorId ?? string.Empty, out TalkDrawer drawer) ? drawer : null;

        // ---- IPevtPortrait ----

        /// <summary>
        /// 外观切换。<c>pose__frame</c> 正是原版 <see cref="EvPerson.setGrp"/> 认的形状，
        /// 而 pose 与 frame 两段都来自 `.pactor` 的数据声明，不是脚本里能拼出来的任意字符串。
        /// </summary>
        public void SetAppearance(string actorId, string appearanceId)
        {
            PevtGameHost.Guard("SetAppearance", () =>
            {
                if (!TryActor(actorId, out ActorDefinition actor)
                    || !actor.TryGetAppearance(appearanceId, out ActorAppearance appearance))
                    return;

                string group = appearance.Pose + "__" + appearance.Frame;
                _appearances[actorId] = group;

                TalkDrawer drawer = Drawer(actorId);
                drawer?.setGrp(group, string.Empty);
            });
        }

        public PevtWait Place(string actorId, string anchorId, int frames)
        {
            PevtGameHost.Guard("PortraitPlace", () =>
            {
                EvDrawerContainer drawers = PevtGameHost.Drawers;
                string person = PersonKey(actorId);
                if (drawers == null || person == null)
                    return;

                if (!TryAnchor(actorId, anchorId, out float x, out float y, out float enterX, out float enterY))
                    return;

                TalkDrawer drawer = Drawer(actorId)
                    ?? drawers.activateTalker(BootstrapPositionKey, true, person, person, string.Empty);
                if (drawer == null)
                    return;

                _drawers[actorId] = drawer;

                if (_appearances.TryGetValue(actorId, out string appearance))
                    drawer.setGrp(appearance, string.Empty);

                // 从入场坐标滑到站位坐标；frames 为 0 时 initMove 会立刻到位。
                drawer.moveTo(Coord(enterX), Coord(enterY), Coord(x), Coord(y), frames, string.Empty);
                drawer.fadein(frames, 0, true);
            });

            return TrackFrames(frames);
        }

        public PevtWait Move(string actorId, string anchorId, int frames)
        {
            PevtGameHost.Guard("PortraitMove", () =>
            {
                TalkDrawer drawer = Drawer(actorId);
                if (drawer == null || !TryAnchor(actorId, anchorId, out float x, out float y, out _, out _))
                    return;

                drawer.moveTo(Coord(x), Coord(y), frames, string.Empty);
            });

            return TrackFrames(frames);
        }

        /// <summary>
        /// PEVT-E04：相对当前受管位置位移。原版 <see cref="TalkDrawer"/> 不对外暴露可写的 x/y，
        /// 位置只能通过 <c>moveTo</c>/<c>initPosition</c> 改变，所以曲线不能交给原版 movetype 反射
        /// （那本身与 PEVT 的 easing 取值集是两套独立命名）；改成和 <c>ScaleTo</c>/<c>RotateTo</c>
        /// 同样的做法——用 PEVT 自己的补间算出每帧的插值坐标，再用 <c>initPosition</c> 直接摆过去，
        /// 曲线选择完全在 C# 里，不依赖原版反射能认出哪个方法名。
        /// </summary>
        public PevtWait MoveBy(string actorId, float x, float y, int frames, string easing)
        {
            TalkDrawer drawer = null;
            float baseX = 0f, baseY = 0f;

            PevtGameHost.Guard("PortraitMoveBy", () =>
            {
                drawer = Drawer(actorId);
                if (drawer == null)
                    return;

                baseX = drawer.get_x();
                baseY = drawer.get_y();
            });

            if (drawer == null)
                return new PevtFrameWait(0);

            float targetX = baseX + x;
            float targetY = baseY + y;

            return Tween(frames, easing, t =>
                drawer.initPosition(
                    Coord(baseX + (targetX - baseX) * t),
                    Coord(baseY + (targetY - baseY) * t),
                    string.Empty, string.Empty, 0f));
        }

        public PevtWait Exit(string actorId, int frames)
        {
            PevtGameHost.Guard("PortraitExit", () =>
            {
                TalkDrawer drawer = Drawer(actorId);
                if (drawer == null)
                    return;

                // auto_deactivate=true 让原版在淡出结束后自己收掉显示实例，不留半透明残影。
                drawer.fadeout(frames, true);
                _drawers.Remove(actorId ?? string.Empty);
            });

            return TrackFrames(frames);
        }

        /// <summary>
        /// 收掉本次 PEVT 会话创建的全部人物立绘。对应原版事件中的 <c>PIC_HIDE_ALL</c>，
        /// 但严格限制在 PEVT 自己拥有的 TalkDrawer，不会误伤游戏或其他模组的绘制对象。
        /// </summary>
        public PevtWait HideAll(int frames)
        {
            PevtGameHost.Guard("PortraitHideAll", () =>
            {
                foreach (TalkDrawer drawer in _drawers.Values)
                    drawer.fadeout(frames, true);

                _drawers.Clear();
            });

            return TrackFrames(frames);
        }

        /// <summary>
        /// 表情用原版漫符（ManpuDrawer）；可用值来自 <see cref="ManpuDrawer.MP"/>。
        /// <see cref="EMOT"/> 是人物立绘的情绪/显示状态（JOY、SCARY 等），并不是
        /// <c>PIC_MP</c> 使用的漫符 ID（EXQ、SWT、BLS 等），不能拿来校验本参数。
        /// </summary>
        public bool ValidateEmote(string actorId, string emoteId)
        {
            if (string.IsNullOrWhiteSpace(emoteId))
                return false;

            string[] parts = emoteId.Split('|');
            foreach (string part in parts)
            {
                if (part.Length == 0 || !Enum.IsDefined(typeof(ManpuDrawer.MP), part.ToUpperInvariant()))
                    return false;
            }

            return true;
        }

        public void PlayEmote(string actorId, string emoteId) =>
            PevtGameHost.Guard("PlayEmote", () => Drawer(actorId)?.setManpu("_", emoteId, string.Empty, -1f, -1f));

        /// <summary>动作用原版特殊移动；<see cref="SpMove.Get"/> 查不到时返回 NONE。</summary>
        public bool ValidateMotion(string actorId, string motionId) =>
            motionId != null && PevtGameHost.Safe(() => SpMove.Get(motionId) != SPMOVE.NONE, false);

        public PevtWait PlayMotion(string actorId, string motionId, int frames)
        {
            PevtGameHost.Guard("PlayMotion", () => Drawer(actorId)?.moveA(motionId, frames));
            return TrackFrames(frames);
        }

        public void StopMotion(string actorId) =>
            PevtGameHost.Guard("StopMotion", () => Drawer(actorId)?.moveA(SPMOVE.NONE.ToString(), 0));

        public void SetFlip(string actorId, bool horizontal) =>
            PevtGameHost.Guard("PortraitFlip", () => Drawer(actorId)?.flip(horizontal, false, string.Empty));

        // ---- 清理 ----

        /// <summary>事件结束、替换或异常时收掉本事件建出来的全部立绘。</summary>
        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, TalkDrawer> entry in _drawers)
            {
                TalkDrawer drawer = entry.Value;
                PevtGameHost.Guard("PortraitRelease", () => drawer.release());
            }

            _drawers.Clear();
            _appearances.Clear();
        }

        /// <summary>
        /// 帧数动画的等待。原版立绘动画没有完成回调，只有一个由容器每帧推进的计时器，
        /// 所以把它登记为受管动作票据，让 <c>@wait_motion</c> 也能等到它。
        /// </summary>
        private PevtWait TrackFrames(int frames)
        {
            if (frames <= 0)
                return new PevtFrameWait(0);

            long deadline = _clock.Frame + frames;
            _clock.RegisterMotion(() => _clock.Frame >= deadline);
            return new PevtFrameWait(frames);
        }

        /// <summary>补间版的 <see cref="TrackFrames"/>：登记同样的截止帧动作票据，但返回值改成逐帧回调。</summary>
        private PevtWait Tween(int frames, string easing, Action<float> apply)
        {
            if (frames > 0)
            {
                long deadline = _clock.Frame + frames;
                _clock.RegisterMotion(() => _clock.Frame >= deadline);
            }

            return new PevtTweenWait(frames, easing, apply);
        }
    }
}
