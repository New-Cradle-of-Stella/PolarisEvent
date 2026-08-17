using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// Alice In Cradle 领域组合（玩家与战斗），对应同步指令中间层规范第 13 节。
    /// </summary>
    internal static class PlayerRoutines
    {
        private static IPevtPlayer Player(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.Player, "Player");

        public static IEnumerator<PevtWait> PlayerOutfit(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPlayer player = Player(context);
            string outfitId = PevtArgumentDomains.RequireId(args.String(0), "outfitId");

            if (!player.TryResolveOutfit(outfitId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            player.SetOutfit(outfitId);
            context.Result.SetBool(true);
        }

        /// <summary>`Resources.RequireAudio(voiceId, "voice")` → `Player.PlayVoice(voiceId)`。</summary>
        public static IEnumerator<PevtWait> PlayerVoice(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPlayer player = Player(context);
            IPevtResources resources = PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");
            string voiceId = PevtArgumentDomains.RequireId(args.String(0), "voiceId");

            yield return resources.RequireAudio(voiceId, "voice");
            player.PlayVoice(voiceId);
        }

        public static IEnumerator<PevtWait> PlayerStatusApply(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPlayer player = Player(context);
            string statusId = PevtArgumentDomains.RequireId(args.String(0), "statusId");
            int strength = args.Int(1);
            int durationFrames = PevtArgumentDomains.RequireFrames(args.Int(2), "durationFrames");

            if (strength < 0)
                throw new PevtRoutineFailureException("PEVTR4001", $"`strength` 不能为负数，实际为 {strength}。");

            if (!player.TryResolveStatus(statusId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            player.ApplyStatus(statusId, strength, durationFrames);
            context.Result.SetBool(true);
        }

        public static IEnumerator<PevtWait> PlayerStatusCure(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPlayer player = Player(context);
            string mode = PevtArgumentDomains.RequireId(args.String(0), "mode");

            if (!player.ValidateCureMode(mode))
                throw new PevtRoutineFailureException("PEVTR4001", $"治疗模式 `{mode}` 未登记。");

            player.Cure(mode);
            yield break;
        }

        public static IEnumerator<PevtWait> PlayerMagicCancel(PevtRoutineContext context, PevtArguments args)
        {
            Player(context).CancelMagic(args.Bool(0));
            yield break;
        }
    }

    /// <summary>战斗领域组合。</summary>
    internal static class BattleRoutines
    {
        private static IPevtBattle Battle(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Domains.Battle, "Battle");

        public static IEnumerator<PevtWait> BattleSummonerActive(PevtRoutineContext context, PevtArguments args)
        {
            IPevtBattle battle = Battle(context);
            string summonerId = PevtArgumentDomains.RequireId(args.String(0), "summonerId");
            bool active = args.Bool(1);

            if (!battle.TryResolveSummoner(summonerId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            battle.SetSummonerActive(summonerId, active, args.Bool(2));

            PevtWait<bool> reached = battle.WaitSummonerState(summonerId, active);
            yield return reached;
            context.Result.SetBool(reached.Result);
        }

        public static IEnumerator<PevtWait> BattleEnemyAction(PevtRoutineContext context, PevtArguments args)
        {
            IPevtBattle battle = Battle(context);
            string enemyId = PevtArgumentDomains.RequireId(args.String(0), "enemyId");
            string actionId = PevtArgumentDomains.RequireId(args.String(1), "actionId");

            if (!battle.TryResolveEnemy(enemyId) || !battle.TryResolveEnemyAction(enemyId, actionId))
            {
                context.Result.SetBool(false);
                yield break;
            }

            PevtWait<bool> finished = battle.RunEnemyAction(enemyId, actionId);
            yield return finished;
            context.Result.SetBool(finished.Result);
        }

        public static IEnumerator<PevtWait> BattleMagicEffect(PevtRoutineContext context, PevtArguments args)
        {
            IPevtBattle battle = Battle(context);
            string effectId = PevtArgumentDomains.RequireId(args.String(0), "effectId");

            if (!battle.ResolveMagicEffect(effectId))
                throw new PevtRoutineFailureException("PEVTR4001", $"战斗魔法效果 `{effectId}` 未登记。");

            battle.PlayMagicEffect(effectId, args.Bool(1));
            yield break;
        }
    }
}
