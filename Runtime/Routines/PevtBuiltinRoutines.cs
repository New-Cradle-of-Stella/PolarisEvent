using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// P1（地图、实体、持久状态与进度）与 P2（Alice In Cradle 领域）处理器的登记表。
    /// </summary>
    public static class P1CommandRoutines
    {
        private static readonly PevtType Int = PevtType.Int;
        private static readonly PevtType Float = PevtType.Float;
        private static readonly PevtType Bool = PevtType.Bool;
        private static readonly PevtType Str = PevtType.String;

        public static void RegisterAll(PevtCommandRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            RegisterWorld(registry);
            RegisterEntities(registry);
            RegisterState(registry);
            RegisterGameQueries(registry);
        }

        /// <summary>
        /// PEVT-E01：四条只读查询各有 <c>key + 0..MaxQueryArguments</c> 个重载，
        /// 全部共用同一个组合——重载之间只有实参数量不同。
        /// </summary>
        private static void RegisterGameQueries(PevtCommandRegistry registry)
        {
            for (int argumentCount = 0; argumentCount <= Commands.BuiltinCommandDescriptors.MaxQueryArguments; argumentCount++)
            {
                var types = new PevtType[argumentCount + 1];
                for (int i = 0; i < types.Length; i++)
                    types[i] = Str;

                Add(registry, "game_read_int", GameQueryRoutines.GameReadInt, types);
                Add(registry, "game_read_float", GameQueryRoutines.GameReadFloat, types);
                Add(registry, "game_read_bool", GameQueryRoutines.GameReadBool, types);
                Add(registry, "game_read_string", GameQueryRoutines.GameReadString, types);
            }
        }

        private static void Add(
            PevtCommandRegistry registry,
            string name,
            Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run,
            params PevtType[] parameterTypes) =>
            registry.Register(name, parameterTypes, new PevtDelegateRoutine(run));

        private static void RegisterWorld(PevtCommandRegistry registry)
        {
            Add(registry, "require_map", WorldRoutines.RequireMap, Str);
            Add(registry, "map_change", WorldRoutines.MapChange, Str, Str);
            Add(registry, "map_refresh", WorldRoutines.MapRefresh, Str);
            Add(registry, "map_layer_load", WorldRoutines.MapLayerLoad, Str);
            Add(registry, "map_element_active", WorldRoutines.MapElementActive, Str, Str, Bool);
            Add(registry, "weather_set", WorldRoutines.WeatherSet, Str, Int);
            Add(registry, "danger_set", WorldRoutines.DangerSet, Int, Bool);
            Add(registry, "darkness_set", WorldRoutines.DarknessSet, Bool);
        }

        private static void RegisterEntities(PevtCommandRegistry registry)
        {
            Add(registry, "entity_visible", EntityRoutines.EntityVisible, Str, Bool);
            Add(registry, "entity_pose", EntityRoutines.EntityPose, Str, Str);
            Add(registry, "entity_face", EntityRoutines.EntityFace, Str, Str);
            Add(registry, "entity_move_to", EntityRoutines.EntityMoveTo, Str, Str, Float);
            Add(registry, "entity_move_by", EntityRoutines.EntityMoveBy, Str, Float, Float, Float);
            Add(registry, "entity_move_by_pixels", EntityRoutines.EntityMoveByPixels, Str, Float, Float, Int);
            Add(registry, "entity_move_to_offset", EntityRoutines.EntityMoveToOffset, Str, Str, Float, Float, Int);
            Add(registry, "entity_follow", EntityRoutines.EntityFollow, Str, Str, Float, Float);
            Add(registry, "entity_unfollow", EntityRoutines.EntityUnfollow, Str);
            Add(registry, "entity_action", EntityRoutines.EntityAction, Str, Str);
        }

        private static void RegisterState(PevtCommandRegistry registry)
        {
            Add(registry, "flag_get", StateRoutines.FlagGet, Str, Str);
            Add(registry, "flag_set", StateRoutines.FlagSet, Str, Str, Bool);
            Add(registry, "counter_get", StateRoutines.CounterGet, Str, Str);
            Add(registry, "counter_set", StateRoutines.CounterSet, Str, Str, Int);
            Add(registry, "counter_add", StateRoutines.CounterAdd, Str, Str, Int);
            Add(registry, "counter_raise", StateRoutines.CounterRaise, Str, Str, Int);
            Add(registry, "progress_set", StateRoutines.ProgressSet, Int);
            Add(registry, "item_count", StateRoutines.ItemCount, Str);
            Add(registry, "item_change", StateRoutines.ItemChange, Str, Int, Int, Bool);
            Add(registry, "money_get", StateRoutines.MoneyGet);
            Add(registry, "money_change", StateRoutines.MoneyChange, Int, Bool);
            Add(registry, "skill_has", StateRoutines.SkillHas, Str);
            Add(registry, "skill_owned", StateRoutines.SkillOwned, Str, Bool, Bool);
            Add(registry, "skill_enabled", StateRoutines.SkillEnabled, Str, Bool);
            Add(registry, "magic_has", StateRoutines.MagicHas, Str);
            Add(registry, "magic_owned", StateRoutines.MagicOwned, Str, Bool);
            Add(registry, "quest_status", StateRoutines.QuestStatus, Str);
            Add(registry, "quest_set", StateRoutines.QuestSet, Str, Int);
            Add(registry, "quest_finish", StateRoutines.QuestFinish, Str);
            Add(registry, "quest_remove", StateRoutines.QuestRemove, Str);
            Add(registry, "autosave", StateRoutines.Autosave, Str);
            Add(registry, "store_refresh", StateRoutines.StoreRefresh, Str);
        }
    }

    /// <summary>P2：Alice In Cradle 领域扩展处理器。</summary>
    public static class P2CommandRoutines
    {
        private static readonly PevtType Int = PevtType.Int;
        private static readonly PevtType Bool = PevtType.Bool;
        private static readonly PevtType Str = PevtType.String;

        public static void RegisterAll(PevtCommandRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            Add(registry, "player_outfit", PlayerRoutines.PlayerOutfit, Str);
            Add(registry, "player_voice", PlayerRoutines.PlayerVoice, Str);
            Add(registry, "player_status_apply", PlayerRoutines.PlayerStatusApply, Str, Int, Int);
            Add(registry, "player_status_cure", PlayerRoutines.PlayerStatusCure, Str);
            Add(registry, "player_magic_cancel", PlayerRoutines.PlayerMagicCancel, Bool);
            Add(registry, "battle_summoner_active", BattleRoutines.BattleSummonerActive, Str, Bool, Bool);
            Add(registry, "battle_enemy_action", BattleRoutines.BattleEnemyAction, Str, Str);
            Add(registry, "battle_magic_effect", BattleRoutines.BattleMagicEffect, Str, Bool);
        }

        private static void Add(
            PevtCommandRegistry registry,
            string name,
            Func<PevtRoutineContext, PevtArguments, IEnumerator<PevtWait>> run,
            params PevtType[] parameterTypes) =>
            registry.Register(name, parameterTypes, new PevtDelegateRoutine(run));
    }

    /// <summary>
    /// 全部内置处理器的统一入口。
    /// </summary>
    public static class PevtBuiltinRoutines
    {
        public static PevtCommandRegistry CreateRegistry(CommandDescriptorCatalog catalog = null)
        {
            var registry = new PevtCommandRegistry(catalog ?? CommandDescriptorCatalog.Builtin);
            RegisterAll(registry);
            return registry;
        }

        public static void RegisterAll(PevtCommandRegistry registry)
        {
            P0CommandRoutines.RegisterAll(registry);
            P1CommandRoutines.RegisterAll(registry);
            P2CommandRoutines.RegisterAll(registry);
        }
    }
}
