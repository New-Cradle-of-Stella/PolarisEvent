using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 持久状态、物品、能力、任务与存档组合，对应同步指令中间层规范第 12 节。
    /// </summary>
    internal static class StateRoutines
    {
        private static IPevtState State(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.State, "State");

        private static IPevtInventory Inventory(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.Inventory, "Inventory");

        private static IPevtQuest Quest(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.Quest, "Quest");

        /// <summary><c>scope</c> 未登记时报错，不静默把它当成默认作用域。</summary>
        private static void RequireScope(IPevtState state, string scope)
        {
            PevtArgumentDomains.RequireId(scope, "scope");
            if (!state.ValidateScope(scope))
                throw new PevtRoutineFailureException("PEVTR4001", $"状态作用域 `{scope}` 未登记。");
        }

        // ---- 布尔与整数状态 ----

        public static IEnumerator<PevtWait> FlagGet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");

            context.Result.SetBool(state.GetFlag(args.String(0), key));
            yield break;
        }

        public static IEnumerator<PevtWait> FlagSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");

            state.SetFlag(args.String(0), key, args.Bool(2));
            yield break;
        }

        public static IEnumerator<PevtWait> CounterGet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");

            context.Result.SetInt(state.GetCounter(args.String(0), key));
            yield break;
        }

        public static IEnumerator<PevtWait> CounterSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");

            state.SetCounter(args.String(0), key, args.Int(2));
            yield break;
        }

        /// <summary>
        /// <c>GetCounter</c> → 检查加法溢出 → <c>SetCounter</c>。
        /// </summary>
        public static IEnumerator<PevtWait> CounterAdd(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");
            int delta = args.Int(2);

            int current = state.GetCounter(args.String(0), key);

            int updated;
            try
            {
                updated = checked(current + delta);
            }
            catch (System.OverflowException ex)
            {
                throw new PevtRoutineFailureException("PEVTR2001",
                    $"计数器 `{args.String(0)}:{key}` 的 {current} + {delta} 超出 32 位有符号整数范围。", ex);
            }

            state.SetCounter(args.String(0), key, updated);
            context.Result.SetInt(updated);
            yield break;
        }

        public static IEnumerator<PevtWait> CounterRaise(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            RequireScope(state, args.String(0));
            string key = PevtArgumentDomains.RequireId(args.String(1), "key");
            int minimum = args.Int(2);

            int current = state.GetCounter(args.String(0), key);
            int updated = current >= minimum ? current : minimum;

            if (updated != current)
                state.SetCounter(args.String(0), key, updated);

            context.Result.SetInt(updated);
            yield break;
        }

        public static IEnumerator<PevtWait> ProgressSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            int value = args.Int(0);

            if (value < 0)
                throw new PevtRoutineFailureException("PEVTR4001", $"主进度不能为负数，实际为 {value}。");

            state.SetMainProgress(value);
            yield break;
        }

        // ---- 物品与货币 ----

        public static IEnumerator<PevtWait> ItemCount(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string itemId = PevtArgumentDomains.RequireId(args.String(0), "itemId");

            context.Result.SetInt(inventory.GetItemCount(itemId));
            yield break;
        }

        public static IEnumerator<PevtWait> ItemChange(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string itemId = PevtArgumentDomains.RequireId(args.String(0), "itemId");
            int delta = args.Int(1);
            int grade = args.Int(2);

            if (grade < 0)
                throw new PevtRoutineFailureException("PEVTR4001", $"`grade` 不能为负数，实际为 {grade}。");
            if (!inventory.ResolveItem(itemId))
                throw new PevtRoutineFailureException("PEVTR4001", $"物品 `{itemId}` 未登记。");

            int count = inventory.ChangeItem(itemId, delta, grade);
            context.Result.SetInt(count);

            if (args.Bool(3))
                Ui(context)?.NotifyItemChange(itemId, delta, count);

            yield break;
        }

        public static IEnumerator<PevtWait> MoneyGet(PevtRoutineContext context, PevtArguments args)
        {
            context.Result.SetInt(Inventory(context).GetMoney());
            yield break;
        }

        public static IEnumerator<PevtWait> MoneyChange(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            int delta = args.Int(0);

            int amount = inventory.ChangeMoney(delta);
            context.Result.SetInt(amount);

            if (args.Bool(1))
                Ui(context)?.NotifyMoneyChange(delta, amount);

            yield break;
        }

        // ---- 技能与魔法 ----

        public static IEnumerator<PevtWait> SkillHas(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string skillId = PevtArgumentDomains.RequireId(args.String(0), "skillId");

            context.Result.SetBool(inventory.HasSkill(skillId));
            yield break;
        }

        public static IEnumerator<PevtWait> SkillOwned(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string skillId = PevtArgumentDomains.RequireId(args.String(0), "skillId");
            bool owned = args.Bool(1);

            if (!inventory.ResolveSkill(skillId))
                throw new PevtRoutineFailureException("PEVTR4001", $"技能 `{skillId}` 未登记。");

            inventory.SetSkillOwned(skillId, owned);

            if (args.Bool(2))
                Ui(context)?.NotifySkillChange(skillId, owned);

            yield break;
        }

        public static IEnumerator<PevtWait> SkillEnabled(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string skillId = PevtArgumentDomains.RequireId(args.String(0), "skillId");

            if (!inventory.ResolveSkill(skillId))
                throw new PevtRoutineFailureException("PEVTR4001", $"技能 `{skillId}` 未登记。");

            inventory.SetSkillEnabled(skillId, args.Bool(1));
            yield break;
        }

        public static IEnumerator<PevtWait> MagicHas(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string magicId = PevtArgumentDomains.RequireId(args.String(0), "magicId");

            context.Result.SetBool(inventory.HasMagic(magicId));
            yield break;
        }

        public static IEnumerator<PevtWait> MagicOwned(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string magicId = PevtArgumentDomains.RequireId(args.String(0), "magicId");

            if (!inventory.ResolveMagic(magicId))
                throw new PevtRoutineFailureException("PEVTR4001", $"魔法 `{magicId}` 未登记。");

            inventory.SetMagicOwned(magicId, args.Bool(1));
            yield break;
        }

        public static IEnumerator<PevtWait> StoreRefresh(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInventory inventory = Inventory(context);
            string storeId = PevtArgumentDomains.RequireId(args.String(0), "storeId");

            if (!inventory.ResolveStore(storeId))
                throw new PevtRoutineFailureException("PEVTR4001", $"商店 `{storeId}` 未登记。");

            inventory.RefreshStore(storeId);
            yield break;
        }

        // ---- 任务 ----

        public static IEnumerator<PevtWait> QuestStatus(PevtRoutineContext context, PevtArguments args)
        {
            IPevtQuest quest = Quest(context);
            string questId = PevtArgumentDomains.RequireId(args.String(0), "questId");

            if (!quest.Resolve(questId))
                throw new PevtRoutineFailureException("PEVTR4001", $"任务 `{questId}` 未登记。");

            context.Result.SetInt(quest.GetStatus(questId));
            yield break;
        }

        public static IEnumerator<PevtWait> QuestSet(PevtRoutineContext context, PevtArguments args)
        {
            IPevtQuest quest = Quest(context);
            string questId = PevtArgumentDomains.RequireId(args.String(0), "questId");
            int step = args.Int(1);

            if (step < 0)
                throw new PevtRoutineFailureException("PEVTR4001", $"任务步骤不能为负数，实际为 {step}。");
            if (!quest.Resolve(questId))
                throw new PevtRoutineFailureException("PEVTR4001", $"任务 `{questId}` 未登记。");

            quest.SetStep(questId, step);
            yield break;
        }

        public static IEnumerator<PevtWait> QuestFinish(PevtRoutineContext context, PevtArguments args)
        {
            IPevtQuest quest = Quest(context);
            string questId = PevtArgumentDomains.RequireId(args.String(0), "questId");

            if (!quest.Resolve(questId))
                throw new PevtRoutineFailureException("PEVTR4001", $"任务 `{questId}` 未登记。");

            quest.Finish(questId);
            yield break;
        }

        public static IEnumerator<PevtWait> QuestRemove(PevtRoutineContext context, PevtArguments args)
        {
            IPevtQuest quest = Quest(context);
            string questId = PevtArgumentDomains.RequireId(args.String(0), "questId");

            if (!quest.Resolve(questId))
                throw new PevtRoutineFailureException("PEVTR4001", $"任务 `{questId}` 未登记。");

            quest.Remove(questId);
            yield break;
        }

        // ---- 自动存档 ----

        public static IEnumerator<PevtWait> Autosave(PevtRoutineContext context, PevtArguments args)
        {
            IPevtState state = State(context);
            string mode = PevtArgumentDomains.RequireId(args.String(0), "mode");

            if (!state.ValidateSaveMode(mode))
                throw new PevtRoutineFailureException("PEVTR4001", $"存档模式 `{mode}` 未登记。");

            state.RequestAutosave(mode);

            PevtWait<bool> saved = state.WaitAutosave();
            yield return saved;
            context.Result.SetBool(saved.Result);
        }

        /// <summary>
        /// 变更提示是可选的：宿主没有接 UI 服务时不该让 <c>@item_change(..., true)</c> 整条失败。
        /// 数量已经改完了，提示只是提示。
        /// </summary>
        private static IPevtUi Ui(PevtRoutineContext context) => context.Services.Ui;
    }
}
