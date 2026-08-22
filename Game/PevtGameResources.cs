using System;
using System.Collections.Generic;
using evt;
using PixelLiner;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Res.Pxls;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 一份中立视觉租约：把"原版借用"和"模组 PolarisRes 字段"两种来源统一成同一个东西交给演出层。
    /// </summary>
    public sealed class PevtVisualLease
    {
        private readonly GamePxlsLease _gameLease;
        private readonly PxlsCharacterHandle _modHandle;

        /// <summary>诊断用的来源描述。</summary>
        public string Source { get; }

        /// <summary>常驻资源在事件结束时不撤销借用（原版 <c>__ev_n</c> 之类一直留在内存里）。</summary>
        public bool IsStatic { get; }

        private PevtVisualLease(string source, GamePxlsLease gameLease, PxlsCharacterHandle modHandle, bool isStatic)
        {
            Source = source;
            _gameLease = gameLease;
            _modHandle = modHandle;
            IsStatic = isStatic;
        }

        internal static PevtVisualLease FromGame(GamePxlsLease lease, bool isStatic) =>
            new PevtVisualLease("game-pxls:" + lease.Id, lease, null, isStatic);

        internal static PevtVisualLease FromMod(string reference, PxlsCharacterHandle handle, bool isStatic) =>
            new PevtVisualLease("polaris-res:" + reference, null, handle, isStatic);

        /// <summary>资源当前的票据状态。</summary>
        public PevtResourceStatus Status
        {
            get
            {
                if (_gameLease != null)
                {
                    if (_gameLease.IsReleased)
                        return PevtResourceStatus.Failed;

                    // 借用之后原版才把 Bundle 拉进来是常态，所以每次都重查一遍。
                    // 只有 PxlCharacter 还不够：TalkDrawer 绘制还需要已经绑定的 MImage/纹理。
                    if (!_gameLease.IsReady || _gameLease.Image == null)
                        GamePxlsBridge.Refresh(_gameLease);

                    return _gameLease.IsReady && _gameLease.Image != null
                        ? PevtResourceStatus.Ready
                        : PevtResourceStatus.Loading;
                }

                if (_modHandle == null)
                    return PevtResourceStatus.Failed;
                if (_modHandle.IsFaulted)
                    return PevtResourceStatus.Failed;

                return _modHandle.IsReady ? PevtResourceStatus.Ready : PevtResourceStatus.Loading;
            }
        }

        /// <summary>原版 PXLS 角色；尚未就绪时为 null。立绘适配器用它挑 pose 和 frame。</summary>
        public PxlPose GetPose(string name) =>
            _gameLease != null ? _gameLease.GetPose(name) : _modHandle?.GetPose(name);

        public PxlFrame GetFrame(string frameName) =>
            _gameLease != null ? _gameLease.GetFrame(frameName) : _modHandle?.GetFrame(frameName);

        /// <summary>释放本次借用。原版来源只撤销 PolarisEvent 自己的引用；模组资源由其 owner 持有，这里什么都不做。</summary>
        public void Release() => _gameLease?.Release();

        public override string ToString() => $"{Source} ({Status})";
    }

    /// <summary>
    /// 游戏侧的资源服务：把人物目录里的视觉声明变成真实的借用或模组资源等待，
    /// 并为图层图像与音频提供同一套 <see cref="PevtResourceWait"/> 票据。
    /// </summary>
    public sealed class PevtGameResources : IPevtResources
    {
        private const string MusicKind = "music";
        private const string BgmLoadKey = "EV_BGM";

        /// <summary>
        /// 音频就绪等待的上限。<c>WaitAudio</c> 的返回值是"有没有备好"，组合层靠 false 才能报 PEVTR4403；
        /// 不设上限时一条永远备不好的 cue 会让事件停在那里，最后只得到一条定位不到原因的 PEVTR1002 停滞诊断。
        /// </summary>
        private const int AudioReadyTimeoutFrames = 300;

        private readonly PevtActorRegistry _actors;
        private readonly List<PevtVisualLease> _eventLeases = new List<PevtVisualLease>();

        /// <summary>本事件加载过的 BGM sheet；结束时统一卸载，避免把事件专用音轨留在内存里。</summary>
        private readonly List<string> _loadedSheets = new List<string>();

        /// <summary>
        /// 已经成功经过 <see cref="BgmPrepared"/> 的 BGM ID。原版把准备好的 OthPl 切换为实际播放后，
        /// <c>isPrepared()</c> 会重新变成 false；资源组票据若到这之后才第一次 Tick，不能把一首已经
        /// 开始播放的曲子重新判回 Loading，否则 <c>@wait_resources(..., 0)</c> 会永久卡住。
        /// </summary>
        private readonly HashSet<string> _preparedMusic = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已解析的立绘租约，键为 <c>&lt;actorId&gt;/&lt;visualKey&gt;</c>；同一事件内重复借用只借一次。</summary>
        private readonly Dictionary<string, PevtVisualLease> _portraits = new Dictionary<string, PevtVisualLease>(StringComparer.Ordinal);

        /// <summary>只用于 cue 存在性检查的探针播放器，不参与真正的播放。</summary>
        private SndPlayer _cueProbe;

        public PevtGameResources(PevtActorRegistry actors)
        {
            _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        }

        /// <summary>本次事件还没释放的租约数量，供所有权诊断查询。</summary>
        public int OutstandingLeaseCount => _eventLeases.Count;

        /// <summary>
        /// 为一个人物视觉建立租约。人物或视觉未登记时返回 null，由调用方报 PEVTR4401/4402——
        /// 这里不替它决定用哪个编号。
        /// </summary>
        public PevtVisualLease TryBorrowVisual(string actorId, string visualKey, ActorVisual visual)
        {
            if (visual == null)
                return null;

            string cacheKey = actorId + "/" + visualKey;
            if (_portraits.TryGetValue(cacheKey, out PevtVisualLease cached))
                return cached;

            bool isStatic = visual.Lifetime == ActorVisualLifetime.Static;
            PevtVisualLease lease = visual.Resource.Provider == ActorVisualProvider.GamePxls
                ? BorrowGame(visual.Resource, visual.LegacyPerson, isStatic)
                : BorrowMod(actorId, visualKey, visual.Resource, isStatic);

            if (lease == null)
                return null;

            _portraits[cacheKey] = lease;
            _eventLeases.Add(lease);
            return lease;
        }

        /// <summary>取一个已借用的立绘租约；没借过时返回 null。</summary>
        public PevtVisualLease FindPortrait(string actorId, string portraitId) =>
            _portraits.TryGetValue(actorId + "/" + ActorVisualKeys.Portrait(portraitId), out PevtVisualLease lease)
                ? lease
                : null;

        private static PevtVisualLease BorrowGame(ActorVisualResource resource, string legacyPerson, bool isStatic)
        {
            if (!GamePxlsId.TryParse(resource.Asset, out GamePxlsId id))
                return null;

            // 原版 EvtCacheRead 遇到人物 PIC 时正是走 cacheGraphics；PEVT 没有 EvReader，
            // 必须在这里显式触发同一条 EvPxlsLoader/LoadTicket 加载链。
            if (!string.IsNullOrEmpty(legacyPerson))
            {
                PevtGameHost.Guard("CachePortraitGraphics", () =>
                    EvPerson.getPerson(legacyPerson, null)?.cacheGraphics());
            }

            return PevtVisualLease.FromGame(GamePxlsBridge.Borrow(id), isStatic);
        }

        /// <summary>
        /// 模组视觉走生成代码登记的延迟访问器。访问器在这一刻才第一次被调用——注册扫描期不碰它，
        /// 正是为了让 PolarisRes 有机会在游戏就绪之后再完成 PXLS 加载。
        /// </summary>
        private PevtVisualLease BorrowMod(string actorId, string visualKey, ActorVisualResource resource, bool isStatic)
        {
            if (!_actors.TryGetVisualAccessor(actorId, visualKey, out Func<object> accessor))
                return null;

            object value;
            try
            {
                value = accessor();
            }
            catch (Exception)
            {
                return null;
            }

            return value is PxlsCharacterHandle handle
                ? PevtVisualLease.FromMod(resource.FieldReference, handle, isStatic)
                : null;
        }

        /// <summary>把租约的就绪判定包成统一等待；缺失或加载失败时以 PEVTR4403 结束。</summary>
        public PevtWait WaitFor(PevtVisualLease lease, string resourceId)
        {
            if (lease == null)
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);

            return new PevtResourceWait(resourceId, () => lease.Status);
        }

        // ---- IPevtResources ----

        /// <summary>
        /// 解析 <c>actorId</c> + <c>appearanceId</c> 到具体 portrait 视觉后再借用。
        /// appearance 只是"哪个 portrait 的哪个 pose/frame"的数据映射，资源边界仍在 portrait 上，所以同一 portrait 的多个 appearance 共用一份租约。
        /// </summary>
        public PevtWait RequirePortrait(string actorId, string appearanceId)
        {
            string resourceId = $"portrait:{actorId}:{appearanceId}";

            if (!_actors.Directory.TryGetActor(actorId, out ActorRegistration registration)
                || !registration.Actor.TryGetAppearance(appearanceId, out ActorAppearance appearance)
                || !registration.Actor.TryGetPortrait(appearance.PortraitId, out ActorVisual portrait))
            {
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);
            }

            PevtVisualLease lease = TryBorrowVisual(actorId, ActorVisualKeys.Portrait(portrait.Id), portrait);
            return WaitFor(lease, resourceId);
        }

        /// <summary>
        /// 演出图层用的原版事件图像。原版把它们放在已解包的 <see cref="EvImgContainer"/> 里，
        /// 纹理本身是懒加载的，所以先 <c>cacheReadFor</c> 触发一次装载，再轮询纹理是否到位。
        /// </summary>
        public PevtWait RequireImage(string assetId)
        {
            string resourceId = "image:" + assetId;
            EvImgContainer pictures = PevtGameHost.Pictures;
            if (pictures == null)
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);

            EvImg image = PevtGameHost.Safe(() => pictures.getPic(assetId, true, true), null);
            if (image == null || image.PF == null)
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);

            PevtGameHost.Guard("RequireImage", () => pictures.cacheReadFor(image));
            return new PevtResourceWait(resourceId,
                () => PevtGameHost.Safe(() => image.PF.TxUsing != null, false)
                    ? PevtResourceStatus.Ready
                    : PevtResourceStatus.Loading);
        }

        /// <summary>
        /// 音频票据。BGM 与其它音频的加载模型不同：BGM 要先把 sheet（原版叫 timing）装进来再
        /// prepare 一条 cue，其余音效只需要 cue 已经在某张已加载的 sheet 里。
        /// </summary>
        public PevtWait RequireAudio(string audioId, string kind)
        {
            if (kind == MusicKind)
                return PreloadAudio(audioId, kind);

            string resourceId = $"audio:{kind}:{audioId}";
            return new PevtResourceWait(resourceId,
                () => CueExists(audioId) ? PevtResourceStatus.Ready : PevtResourceStatus.Failed);
        }

        public PevtWait PreloadAudio(string audioId, string kind)
        {
            string resourceId = $"preload:{kind}:{audioId}";

            if (kind != MusicKind)
            {
                return new PevtResourceWait(resourceId,
                    () => CueExists(audioId) ? PevtResourceStatus.Ready : PevtResourceStatus.Failed);
            }

            SplitMusicId(audioId, out string sheet, out string cue);
            if (sheet == null)
                return new PevtResourceWait(resourceId, () => PevtResourceStatus.Failed);

            PevtGameHost.Guard("PreloadAudio", () =>
            {
                if (!_loadedSheets.Contains(sheet))
                {
                    SND.loadSheets(sheet, BgmLoadKey);
                    _loadedSheets.Add(sheet);
                }

                BGM.load(sheet, cue, true);
            });

            return new PevtResourceWait(resourceId,
                () => RememberMusicPrepared(audioId)
                    ? PevtResourceStatus.Ready
                    : PevtResourceStatus.Loading);
        }

        /// <summary>
        /// 有超时上限的就绪等待。超时不算失败——组合层需要区分"没准备好"和"资源不存在"，
        /// 前者由返回值 false 表达，后者才是 PEVTR4403。
        /// </summary>
        public PevtWait<bool> WaitAudio(string audioId, string kind)
        {
            if (kind != MusicKind)
                return new PevtMotionWait(() => CueExists(audioId), AudioReadyTimeoutFrames);

            return new PevtMotionWait(() => RememberMusicPrepared(audioId), AudioReadyTimeoutFrames);
        }

        /// <summary>
        /// 资源组解析。PEVT-E08 静态声明的组优先，既然加载阶段已经把它登记进来，它就一定"存在"。
        /// 没有被声明过的 groupId 落回第一版的隐式规则——"某个人物的全部立绘"或"某张事件图像"。
        /// </summary>
        public bool ResolveGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return false;

            if (_declaredGroups.ContainsKey(groupId))
                return true;

            if (_actors.Directory.TryGetActor(groupId, out _))
                return true;

            EvImgContainer pictures = PevtGameHost.Pictures;
            return pictures != null && PevtGameHost.Safe(() => pictures.getPic(groupId, true, true) != null, false);
        }

        public PevtWait<bool> WaitGroup(string groupId, int timeoutFrames) =>
            new PevtMotionWait(() => GroupReady(groupId), timeoutFrames);

        private bool GroupReady(string groupId)
        {
            if (_declaredGroupTickets.TryGetValue(groupId, out List<PevtWait> tickets))
                return DeclaredGroupReady(tickets);

            foreach (KeyValuePair<string, PevtVisualLease> entry in _portraits)
            {
                if (!entry.Key.StartsWith(groupId + "/", StringComparison.Ordinal))
                    continue;
                if (entry.Value.Status == PevtResourceStatus.Loading)
                    return false;
            }

            EvImgContainer pictures = PevtGameHost.Pictures;
            EvImg image = pictures == null ? null : PevtGameHost.Safe(() => pictures.getPic(groupId, true, true), null);
            return image?.PF == null || PevtGameHost.Safe(() => image.PF.TxUsing != null, false);
        }

        /// <summary>
        /// <see cref="PevtResourceWait"/> 只有反复 <c>Tick</c> 才会推进状态；它的轮询闭包不看
        /// <c>PevtWaitContext.Frame</c>，因此在这里随便传一个上下文重复调用是安全、幂等的。
        /// </summary>
        private static bool DeclaredGroupReady(List<PevtWait> tickets)
        {
            var context = new PevtWaitContext(0);
            foreach (PevtWait ticket in tickets)
            {
                if (!ticket.IsCompleted)
                    ticket.Tick(context);
                if (!ticket.IsCompleted)
                    return false;
            }

            return true;
        }

        // ---- PEVT-E08：静态声明的资源预载组 ----

        private readonly Dictionary<string, IReadOnlyList<PevtResourceMember>> _declaredGroups =
            new Dictionary<string, IReadOnlyList<PevtResourceMember>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<PevtWait>> _declaredGroupTickets =
            new Dictionary<string, List<PevtWait>>(StringComparer.Ordinal);

        /// <summary>
        /// 立即为每个成员发起对应的加载请求——这就是"预载"：不等 <c>@wait_resources</c> 被调用，
        /// 组一声明就开始借用/装载。按 groupId 幂等，重复声明（<c>async block</c>/<c>exec</c> 与父级
        /// 共用同一份编译产物）是安全的空操作。
        /// </summary>
        public void DeclareResourceGroup(string groupId, IReadOnlyList<PevtResourceMember> members)
        {
            if (string.IsNullOrEmpty(groupId) || _declaredGroups.ContainsKey(groupId))
                return;

            var tickets = new List<PevtWait>(members.Count);
            foreach (PevtResourceMember member in members)
                tickets.Add(RequireMember(member));

            _declaredGroups[groupId] = members;
            _declaredGroupTickets[groupId] = tickets;
        }

        /// <summary>PEVT-E08：已声明资源组的 ID，按声明顺序；供 F8 展示。</summary>
        public IReadOnlyCollection<string> DeclaredGroupIds => _declaredGroups.Keys;

        /// <summary>
        /// 某个已声明组里每个成员的当前状态，供 F8 展示"这个组还差哪个成员"。
        /// 未声明过的 groupId 返回空列表，不视为错误——F8 只是没什么可展示的。
        /// </summary>
        public IReadOnlyList<string> DescribeDeclaredGroup(string groupId)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(groupId)
                || !_declaredGroups.TryGetValue(groupId, out IReadOnlyList<PevtResourceMember> members)
                || !_declaredGroupTickets.TryGetValue(groupId, out List<PevtWait> tickets))
            {
                return lines;
            }

            var context = new PevtWaitContext(0);
            for (int i = 0; i < members.Count && i < tickets.Count; i++)
            {
                if (!tickets[i].IsCompleted)
                    tickets[i].Tick(context);

                lines.Add($"{members[i].RawText}  ({tickets[i].State})");
            }

            return lines;
        }

        private PevtWait RequireMember(PevtResourceMember member)
        {
            switch (member.Kind)
            {
                case "actor": return RequirePortrait(member.ActorId, member.AppearanceId);
                case "image": return RequireImage(member.AssetId);
                default: return PreloadAudio(member.AssetId, member.Kind);
            }
        }

        // ---- 清理 ----

        /// <summary>
        /// 释放本次事件持有的全部租约与事件专用 BGM sheet；事件结束、替换、异常和插件卸载都必须走这里，
        /// 否则借用计数会一直挂在原版资源上。标记为常驻的视觉按目录声明不撤销借用。
        /// </summary>
        public int ReleaseAll()
        {
            int count = 0;
            for (int i = _eventLeases.Count - 1; i >= 0; i--)
            {
                if (_eventLeases[i].IsStatic)
                    continue;

                _eventLeases[i].Release();
                count++;
            }

            _eventLeases.Clear();
            _portraits.Clear();

            ReleaseAudioSheets();
            return count;
        }

        /// <summary>
        /// 卸载本事件加载过的事件专用 BGM sheet。<c>@music_stop</c> 与事件收尾都会走到，
        /// 因此必须幂等——清单清空后再调是空操作。
        /// </summary>
        public void ReleaseAudioSheets()
        {
            if (_loadedSheets.Count == 0)
            {
                _preparedMusic.Clear();
                return;
            }

            var sheets = new List<string>(_loadedSheets);
            _loadedSheets.Clear();
            _preparedMusic.Clear();

            PevtGameHost.Guard("ReleaseAudioSheets", () =>
            {
                foreach (string sheet in sheets)
                    SND.unloadSheets(sheet, BgmLoadKey);
            });
        }

        // ---- 原版音频细节 ----

        /// <summary>
        /// cue 存在性检查。走 <see cref="SND.GetCue(string, SndPlayer)"/> 而不是取 <c>CueInfo</c> 的重载，
        /// 是为了不让 CRI 的类型出现在 PolarisEvent 的引用集合里；探针播放器是本服务私有的，不影响任何真正在响的声音。
        /// </summary>
        private bool CueExists(string cue)
        {
            if (string.IsNullOrEmpty(cue))
                return false;

            return PevtGameHost.Safe(() =>
            {
                _cueProbe = _cueProbe ?? new SndPlayer("polaris_pevt_probe");
                return SND.GetCue(cue, _cueProbe);
            }, false);
        }

        private static bool BgmPrepared() =>
            PevtGameHost.Safe(() => BGM.OthPl != null && BGM.OthPl.isPrepared(), false);

        /// <summary>
        /// BGM 的“准备完成”是一次性跃迁而不是稳定状态：开始播放后原版播放器不再报告 prepared。
        /// 这里按资源 ID 锁存成功结果，让预载组和显式 <c>@music_play</c> 共享同一事实。
        /// </summary>
        private bool RememberMusicPrepared(string audioId)
        {
            if (_preparedMusic.Contains(audioId))
                return true;

            if (!BgmPrepared())
                return false;

            _preparedMusic.Add(audioId);
            return true;
        }

        /// <summary>
        /// <c>@music_play</c> 的 ID 写成 <c>sheet/cue</c>；只给一段时把它同时当 sheet 和 cue，
        /// 对应原版 <c>LOAD_BGM</c> 省略第二参数的用法。
        /// </summary>
        private static void SplitMusicId(string audioId, out string sheet, out string cue)
        {
            sheet = null;
            cue = null;
            if (string.IsNullOrEmpty(audioId))
                return;

            int slash = audioId.IndexOf('/');
            if (slash < 0)
            {
                sheet = audioId;
                cue = null;
                return;
            }

            if (slash == 0 || slash == audioId.Length - 1)
                return;

            sheet = audioId.Substring(0, slash);
            cue = audioId.Substring(slash + 1);
        }
    }
}
