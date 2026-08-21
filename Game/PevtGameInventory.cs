using Polaris.API;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 物品、货币、技能与魔法的游戏侧适配器，全部走 <see cref="PolarisAPI.Game"/> 的受控入口。
    /// 商店查询与刷新通过 Core 的 <see cref="PolarisAPI.Game"/> 受控入口完成。
    /// </summary>
    internal sealed class PevtGameInventory : IPevtInventory
    {
        /// <summary>PEVT 的 <c>money</c> 指原版主货币；另外两种货币没有公开参数域，不在本层暴露。</summary>
        private const GameCurrency Money = GameCurrency.Gold;

        // ---- 物品 ----

        public bool ResolveItem(string itemId) => PolarisAPI.Game.Items.Resolve(itemId) != null;

        public int GetItemCount(string itemId)
        {
            GameItem item = PolarisAPI.Game.Items.Resolve(itemId);
            if (item == null)
                return 0;

            GameStorage storage = RequireStorage();
            return storage.Count(item);
        }

        /// <summary>
        /// <c>delta</c> 为正走 <see cref="GameStorage.Add"/>，为负走 <see cref="GameStorage.Reduce"/>，为零只回读数量。
        /// 减少时数量不足按原版语义一件不扣，返回值仍是回读到的真实数量，绝不返回调用方期望的结果。
        /// </summary>
        public int ChangeItem(string itemId, int delta, int grade)
        {
            GameItem item = PolarisAPI.Game.Items.Resolve(itemId);
            if (item == null)
                throw Failed($"物品 `{itemId}` 在本版本里不存在。");

            GameStorage storage = RequireStorage();

            if (delta > 0)
                storage.Add(item, delta, grade);
            else if (delta < 0)
                storage.Reduce(item, -delta, grade);

            return storage.Count(item);
        }

        // ---- 货币 ----

        public int GetMoney() => Clamp(PolarisAPI.Game.Economy.GetAmount(Money));

        /// <summary>
        /// 增减主货币并回读余额。<see cref="PolarisAPI.Game.Economy"/> 把"付不起"当正常分支，
        /// 扣款失败时一分不扣，因此这里同样只回读真实余额而不抛异常。
        /// </summary>
        public int ChangeMoney(int delta)
        {
            if (delta > 0)
                return Clamp(PolarisAPI.Game.Economy.Add(Money, delta));

            if (delta < 0)
                PolarisAPI.Game.Economy.Spend(Money, -delta);

            return GetMoney();
        }

        // ---- 技能 ----

        public bool ResolveSkill(string skillId) => PolarisAPI.Game.Skills.Resolve(skillId) != null;

        public bool HasSkill(string skillId) => PolarisAPI.Game.Skills.Resolve(skillId)?.IsObtained ?? false;

        /// <summary>授予或收回技能。原版把"授予"和"启用"分成两步，这里只改归属，启用状态交给 <see cref="SetSkillEnabled"/>。</summary>
        public void SetSkillOwned(string skillId, bool owned)
        {
            GameSkill skill = RequireSkill(skillId);

            bool changed = owned ? skill.Obtain() : skill.Revoke();
            if (!changed)
                throw Failed($"技能 `{skillId}` 的归属写入失败。");
        }

        public void SetSkillEnabled(string skillId, bool enabled)
        {
            GameSkill skill = RequireSkill(skillId);
            if (!skill.SetEnabled(enabled))
                throw Failed($"技能 `{skillId}` 的启用状态写入失败。");
        }

        // ---- 魔法 ----

        public bool ResolveMagic(string magicId) => IsMagic(PolarisAPI.Game.Skills.Resolve(magicId));

        public bool HasMagic(string magicId)
        {
            GameSkill skill = PolarisAPI.Game.Skills.Resolve(magicId);
            return IsMagic(skill) && skill.IsObtained;
        }

        /// <summary>魔法在原版里就是 <see cref="GameSkillCategory.Magic"/> 分类的技能，因此复用同一条授予通道。</summary>
        public void SetMagicOwned(string magicId, bool owned)
        {
            GameSkill skill = PolarisAPI.Game.Skills.Resolve(magicId);
            if (!IsMagic(skill))
                throw Failed($"`{magicId}` 不是本版本里的魔法技能。");

            bool changed = owned ? skill.Obtain() : skill.Revoke();
            if (!changed)
                throw Failed($"魔法 `{magicId}` 的归属写入失败。");
        }

        // ---- 商店 ----

        public bool ResolveStore(string storeId) => PolarisAPI.Game.Stores.Resolve(storeId) != null;

        public void RefreshStore(string storeId)
        {
            GameStore store = PolarisAPI.Game.Stores.Resolve(storeId);
            if (store == null)
                throw Failed($"商店 `{storeId}` 在本版本里不存在。");
            if (!store.Refresh())
                throw Failed($"商店 `{storeId}` 刷新失败。");
        }

        private static bool IsMagic(GameSkill skill) =>
            skill != null && (skill.Category & GameSkillCategory.Magic) != 0;

        private static GameSkill RequireSkill(string skillId)
        {
            GameSkill skill = PolarisAPI.Game.Skills.Resolve(skillId);
            if (skill == null)
                throw Failed($"技能 `{skillId}` 在本版本里不存在。");

            return skill;
        }

        /// <summary>主物品栏在标题画面或读档完成前为 null，那不是脚本能预期的情况，按宿主缺服务处理。</summary>
        private static GameStorage RequireStorage()
        {
            GameStorage storage = PolarisAPI.Game.Inventory.Main;
            if (storage == null)
                throw Failed("当前没有可用的主物品栏（尚未进入游戏世界或存档未读入）。");

            return storage;
        }

        /// <summary>原版货币是 <c>uint</c>，PEVT 只有 <c>int</c>；超出范围按 PEVTR2001 报，不静默截断。</summary>
        private static int Clamp(uint amount)
        {
            if (amount > int.MaxValue)
            {
                throw new PevtRoutineFailureException("PEVTR2001",
                    $"原版货币数量 {amount} 超出 PEVT `int` 范围。");
            }

            return (int)amount;
        }

        private static PevtRoutineFailureException Failed(string message) =>
            new PevtRoutineFailureException("PEVTR4001", message);
    }
}
