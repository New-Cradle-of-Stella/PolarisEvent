using System;
using System.Collections.Generic;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Raw;

namespace Polaris.Pevt.Core.Tests.Runtime.Fakes
{
    /// <summary>
    /// P1/P2 领域服务的内存替身。
    ///
    /// 它们记录调用顺序（<see cref="Calls"/>），同时把状态真的存下来——持久状态的验收点是
    /// "组合失败不回滚已经写入的部分"，只记调用名无法证明这件事。
    /// </summary>
    public sealed class FakeWorld : IPevtWorld
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Maps { get; } = new HashSet<string>(StringComparer.Ordinal) { "town", "cave" };

        public HashSet<string> Anchors { get; } = new HashSet<string>(StringComparer.Ordinal) { "town/gate", "cave/entrance" };

        public HashSet<string> Layers { get; } = new HashSet<string>(StringComparer.Ordinal) { "fog" };

        public HashSet<string> Elements { get; } = new HashSet<string>(StringComparer.Ordinal) { "lp/torch", "chip/door" };

        public HashSet<string> Weathers { get; } = new HashSet<string>(StringComparer.Ordinal) { "clear", "rain" };

        public HashSet<string> RefreshModes { get; } = new HashSet<string>(StringComparer.Ordinal) { "flush", "remake" };

        /// <summary>为 true 时 <see cref="WaitMapReady"/> 永远不完成，用来验证取消路径。</summary>
        public bool MapNeverReady { get; set; }

        /// <summary><see cref="WaitLayer"/> 的结果。</summary>
        public bool LayerLoadSucceeds { get; set; } = true;

        public int TransitionDepth { get; private set; }

        public string CurrentMap { get; private set; } = "town";

        public string CurrentMapId => CurrentMap;

        public bool ResolveMap(string mapId)
        {
            Calls.Add($"ResolveMap({mapId})");
            return Maps.Contains(mapId);
        }

        public bool ResolveAnchor(string mapId, string anchorId)
        {
            Calls.Add($"ResolveAnchor({mapId},{anchorId})");
            return Anchors.Contains(mapId + "/" + anchorId);
        }

        public void BeginTransition()
        {
            Calls.Add("BeginTransition()");
            TransitionDepth++;
        }

        public void ChangeMap(string mapId)
        {
            Calls.Add($"ChangeMap({mapId})");
            CurrentMap = mapId;
        }

        public PevtWait WaitMapReady()
        {
            Calls.Add("WaitMapReady()");
            return new PevtPredicateWait(() => !MapNeverReady, "地图就绪");
        }

        public void PlacePlayer(string anchorId) => Calls.Add($"PlacePlayer({anchorId})");

        public void EndTransition()
        {
            Calls.Add("EndTransition()");
            TransitionDepth--;
        }

        public void AbortTransition()
        {
            Calls.Add("AbortTransition()");
            TransitionDepth--;
        }

        public bool ValidateRefreshMode(string mode) => RefreshModes.Contains(mode);

        public void Refresh(string mode) => Calls.Add($"Refresh({mode})");

        public PevtWait WaitRefresh() => new PevtFrameWait(0);

        public bool TryResolveLayer(string layerId)
        {
            Calls.Add($"TryResolveLayer({layerId})");
            return Layers.Contains(layerId);
        }

        public void LoadLayer(string layerId) => Calls.Add($"LoadLayer({layerId})");

        public PevtWait<bool> WaitLayer(string layerId) => new FakeBoolWait(LayerLoadSucceeds);

        public bool TryResolveElement(string kind, string elementId)
        {
            Calls.Add($"TryResolveElement({kind},{elementId})");
            return Elements.Contains(kind + "/" + elementId);
        }

        public void SetElementActive(string kind, string elementId, bool active) =>
            Calls.Add($"SetElementActive({kind},{elementId},{active})");

        public bool ResolveWeather(string weatherId) => Weathers.Contains(weatherId);

        public void SetWeather(string weatherId, int frames) => Calls.Add($"SetWeather({weatherId},{frames})");

        public PevtWait SetDanger(int level, bool immediate)
        {
            Calls.Add($"SetDanger({level},{immediate})");
            return new PevtFrameWait(0);
        }

        public void SetDarkness(bool enabled) => Calls.Add($"SetDarkness({enabled})");
    }

    /// <summary>一个立即完成、结果可控的 <c>bool</c> 等待。</summary>
    public sealed class FakeBoolWait : PevtWait<bool>
    {
        private readonly bool _result;

        public FakeBoolWait(bool result) => _result = result;

        public override string ProgressSource => "测试替身";

        protected override void OnTick(PevtWaitContext context) => CompleteSucceeded(_result);
    }

    public sealed class FakeEntity : IPevtEntity
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Entities { get; } = new HashSet<string>(StringComparer.Ordinal) { "npc_a", "npc_b" };

        public HashSet<string> Poses { get; } = new HashSet<string>(StringComparer.Ordinal) { "npc_a/sit" };

        public HashSet<string> Targets { get; } = new HashSet<string>(StringComparer.Ordinal) { "gate", "npc_b" };

        public HashSet<string> Actions { get; } = new HashSet<string>(StringComparer.Ordinal) { "npc_a/wave" };

        public HashSet<string> Directions { get; } = new HashSet<string>(StringComparer.Ordinal) { "left", "right" };

        public bool MoveArrives { get; set; } = true;

        public bool ActionCompletes { get; set; } = true;

        public bool FollowStarts { get; set; } = true;

        public bool TryResolve(string entityId)
        {
            Calls.Add($"TryResolve({entityId})");
            return Entities.Contains(entityId);
        }

        public void SetVisible(string entityId, bool visible) => Calls.Add($"SetVisible({entityId},{visible})");

        public bool TryResolvePose(string entityId, string poseId) => Poses.Contains(entityId + "/" + poseId);

        public void SetPose(string entityId, string poseId) => Calls.Add($"SetPose({entityId},{poseId})");

        public bool TryResolveDirection(string direction) => Directions.Contains(direction);

        public void SetFacing(string entityId, string direction) => Calls.Add($"SetFacing({entityId},{direction})");

        public bool TryResolveTarget(string targetId) => Targets.Contains(targetId);

        public PevtWait<bool> MoveTo(string entityId, string targetId, float speed)
        {
            Calls.Add($"MoveTo({entityId},{targetId},{speed})");
            return new FakeBoolWait(MoveArrives);
        }

        public PevtWait<bool> MoveBy(string entityId, float x, float y, float speed)
        {
            Calls.Add($"MoveBy({entityId},{x},{y},{speed})");
            return new FakeBoolWait(MoveArrives);
        }

        public bool StartFollow(string entityId, string targetId, float distance, float speed)
        {
            Calls.Add($"StartFollow({entityId},{targetId})");
            return FollowStarts;
        }

        public bool StopFollow(string entityId)
        {
            Calls.Add($"StopFollow({entityId})");
            return true;
        }

        public bool TryResolveAction(string entityId, string actionId) => Actions.Contains(entityId + "/" + actionId);

        public PevtWait<bool> RunAction(string entityId, string actionId)
        {
            Calls.Add($"RunAction({entityId},{actionId})");
            return new FakeBoolWait(ActionCompletes);
        }
    }

    public sealed class FakeState : IPevtState
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Scopes { get; } = new HashSet<string>(StringComparer.Ordinal) { "global", "map" };

        public HashSet<string> SaveModes { get; } = new HashSet<string>(StringComparer.Ordinal) { "checkpoint", "bench" };

        public Dictionary<string, bool> Flags { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);

        public Dictionary<string, int> Counters { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public int MainProgress { get; private set; }

        public bool AutosaveSucceeds { get; set; } = true;

        public bool ValidateScope(string scope) => Scopes.Contains(scope);

        public bool GetFlag(string scope, string key) => Flags.TryGetValue(scope + ":" + key, out bool value) && value;

        public void SetFlag(string scope, string key, bool value)
        {
            Calls.Add($"SetFlag({scope}:{key},{value})");
            Flags[scope + ":" + key] = value;
        }

        public int GetCounter(string scope, string key) => Counters.TryGetValue(scope + ":" + key, out int value) ? value : 0;

        public void SetCounter(string scope, string key, int value)
        {
            Calls.Add($"SetCounter({scope}:{key},{value})");
            Counters[scope + ":" + key] = value;
        }

        public void SetMainProgress(int value)
        {
            Calls.Add($"SetMainProgress({value})");
            MainProgress = value;
        }

        public bool ValidateSaveMode(string mode) => SaveModes.Contains(mode);

        public void RequestAutosave(string mode) => Calls.Add($"RequestAutosave({mode})");

        public PevtWait<bool> WaitAutosave() => new FakeBoolWait(AutosaveSucceeds);
    }

    public sealed class FakeInventory : IPevtInventory
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Items { get; } = new HashSet<string>(StringComparer.Ordinal) { "herb", "potion" };

        public HashSet<string> Skills { get; } = new HashSet<string>(StringComparer.Ordinal) { "dash" };

        public HashSet<string> Magics { get; } = new HashSet<string>(StringComparer.Ordinal) { "spark" };

        public HashSet<string> Stores { get; } = new HashSet<string>(StringComparer.Ordinal) { "general" };

        public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public HashSet<string> OwnedSkills { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> OwnedMagics { get; } = new HashSet<string>(StringComparer.Ordinal);

        public int Money { get; private set; }

        public int GetItemCount(string itemId) => Counts.TryGetValue(itemId, out int value) ? value : 0;

        public bool ResolveItem(string itemId) => Items.Contains(itemId);

        public int ChangeItem(string itemId, int delta, int grade)
        {
            Calls.Add($"ChangeItem({itemId},{delta},{grade})");
            int updated = GetItemCount(itemId) + delta;
            if (updated < 0)
                updated = 0;
            Counts[itemId] = updated;
            return updated;
        }

        public int GetMoney() => Money;

        public int ChangeMoney(int delta)
        {
            Calls.Add($"ChangeMoney({delta})");
            Money += delta;
            if (Money < 0)
                Money = 0;
            return Money;
        }

        public bool HasSkill(string skillId) => OwnedSkills.Contains(skillId);

        public bool ResolveSkill(string skillId) => Skills.Contains(skillId);

        public void SetSkillOwned(string skillId, bool owned)
        {
            Calls.Add($"SetSkillOwned({skillId},{owned})");
            if (owned)
                OwnedSkills.Add(skillId);
            else
                OwnedSkills.Remove(skillId);
        }

        public void SetSkillEnabled(string skillId, bool enabled) => Calls.Add($"SetSkillEnabled({skillId},{enabled})");

        public bool HasMagic(string magicId) => OwnedMagics.Contains(magicId);

        public bool ResolveMagic(string magicId) => Magics.Contains(magicId);

        public void SetMagicOwned(string magicId, bool owned)
        {
            Calls.Add($"SetMagicOwned({magicId},{owned})");
            if (owned)
                OwnedMagics.Add(magicId);
            else
                OwnedMagics.Remove(magicId);
        }

        public bool ResolveStore(string storeId) => Stores.Contains(storeId);

        public void RefreshStore(string storeId) => Calls.Add($"RefreshStore({storeId})");
    }

    public sealed class FakeQuest : IPevtQuest
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Quests { get; } = new HashSet<string>(StringComparer.Ordinal) { "q_main" };

        public Dictionary<string, int> Steps { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>未接取。与游戏侧 <c>PevtGameQuest.StatusNotStarted</c> 保持同一套规范化值。</summary>
        public const int NotStarted = -1;

        /// <summary>已完成。与游戏侧 <c>PevtGameQuest.StatusFinished</c> 保持同一套规范化值。</summary>
        public const int Finished = -2;

        public bool Resolve(string questId) => Quests.Contains(questId);

        public int GetStatus(string questId) => Steps.TryGetValue(questId, out int value) ? value : NotStarted;

        public void SetStep(string questId, int step)
        {
            Calls.Add($"SetStep({questId},{step})");
            Steps[questId] = step;
        }

        public void Finish(string questId)
        {
            Calls.Add($"Finish({questId})");
            Steps[questId] = Finished;
        }

        public void Remove(string questId)
        {
            Calls.Add($"Remove({questId})");
            Steps.Remove(questId);
        }
    }

    public sealed class FakePlayer : IPevtPlayer
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Outfits { get; } = new HashSet<string>(StringComparer.Ordinal) { "casual" };

        public HashSet<string> Statuses { get; } = new HashSet<string>(StringComparer.Ordinal) { "wet" };

        public HashSet<string> CureModes { get; } = new HashSet<string>(StringComparer.Ordinal) { "all", "status" };

        public bool TryResolveOutfit(string outfitId) => Outfits.Contains(outfitId);

        public void SetOutfit(string outfitId) => Calls.Add($"SetOutfit({outfitId})");

        public void PlayVoice(string voiceId) => Calls.Add($"PlayVoice({voiceId})");

        public bool TryResolveStatus(string statusId) => Statuses.Contains(statusId);

        public void ApplyStatus(string statusId, int strength, int durationFrames) =>
            Calls.Add($"ApplyStatus({statusId},{strength},{durationFrames})");

        public bool ValidateCureMode(string mode) => CureModes.Contains(mode);

        public void Cure(string mode) => Calls.Add($"Cure({mode})");

        public void CancelMagic(bool force) => Calls.Add($"CancelMagic({force})");
    }

    public sealed class FakeBattle : IPevtBattle
    {
        public List<string> Calls { get; } = new List<string>();

        public HashSet<string> Summoners { get; } = new HashSet<string>(StringComparer.Ordinal) { "s1" };

        public HashSet<string> Enemies { get; } = new HashSet<string>(StringComparer.Ordinal) { "e1" };

        public HashSet<string> EnemyActions { get; } = new HashSet<string>(StringComparer.Ordinal) { "e1/charge" };

        public HashSet<string> MagicEffects { get; } = new HashSet<string>(StringComparer.Ordinal) { "flash" };

        public bool SummonerReaches { get; set; } = true;

        public bool EnemyActionCompletes { get; set; } = true;

        public bool TryResolveSummoner(string summonerId) => Summoners.Contains(summonerId);

        public void SetSummonerActive(string summonerId, bool active, bool effect) =>
            Calls.Add($"SetSummonerActive({summonerId},{active},{effect})");

        public PevtWait<bool> WaitSummonerState(string summonerId, bool active) => new FakeBoolWait(SummonerReaches);

        public bool TryResolveEnemy(string enemyId) => Enemies.Contains(enemyId);

        public bool TryResolveEnemyAction(string enemyId, string actionId) => EnemyActions.Contains(enemyId + "/" + actionId);

        public PevtWait<bool> RunEnemyAction(string enemyId, string actionId)
        {
            Calls.Add($"RunEnemyAction({enemyId},{actionId})");
            return new FakeBoolWait(EnemyActionCompletes);
        }

        public bool ResolveMagicEffect(string effectId) => MagicEffects.Contains(effectId);

        public void PlayMagicEffect(string effectId, bool produceMana) =>
            Calls.Add($"PlayMagicEffect({effectId},{produceMana})");
    }

    /// <summary>
    /// <c>$raw cmd</c> 桥替身。
    ///
    /// 会话不会自己结束：测试用 <see cref="FinishCurrent"/> 决定它什么时候读完，这样"同一时间只有
    /// 一个会话在跑"和排队顺序都能被精确断言。
    /// </summary>
    public sealed class FakeRawCommandBridge : IPevtRawCommandBridge
    {
        public List<string> Started { get; } = new List<string>();

        /// <summary>加进来的文本会被拒绝（<see cref="Begin"/> 返回 null）。</summary>
        public HashSet<string> Rejected { get; } = new HashSet<string>(StringComparer.Ordinal);

        public FakeRawCommandSession Current { get; private set; }

        public int ReleaseCount { get; private set; }

        public IPevtRawCommandSession Begin(string rawText)
        {
            Started.Add(rawText);
            if (Rejected.Contains(rawText))
                return null;

            Current = new FakeRawCommandSession(this, rawText);
            return Current;
        }

        public void FinishCurrent(string failureMessage = null)
        {
            if (Current == null)
                throw new InvalidOperationException("没有正在进行的原版会话。");
            Current.Finish(failureMessage);
        }

        internal void Released(FakeRawCommandSession session)
        {
            ReleaseCount++;
            if (ReferenceEquals(Current, session))
                Current = null;
        }
    }

    public sealed class FakeRawCommandSession : IPevtRawCommandSession
    {
        private readonly FakeRawCommandBridge _bridge;

        internal FakeRawCommandSession(FakeRawCommandBridge bridge, string rawText)
        {
            _bridge = bridge;
            CommandText = rawText;
        }

        public string CommandText { get; }

        public bool IsFinished { get; private set; }

        public string FailureMessage { get; private set; }

        public bool CancelRequested { get; private set; }

        public bool Released { get; private set; }

        public void Finish(string failureMessage)
        {
            IsFinished = true;
            FailureMessage = failureMessage;
        }

        public void RequestCancel() => CancelRequested = true;

        public void Release()
        {
            Released = true;
            _bridge.Released(this);
        }
    }
}
