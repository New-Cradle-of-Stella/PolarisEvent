using System.Collections.Generic;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 角色立绘与演出图层组合，对应同步指令中间层规范第 7 节。
    ///
    /// 规范第 9 节固定了执行顺序：解析人物 ID、解析 appearance/anchor、等待资源、验证 pose/frame、
    /// 创建或更新演出实例。任何一步失败都不得留下半创建的图层，因此全部校验都排在第一个副作用之前。
    /// 原版 <c>LegacyPerson</c> 只用于在内置目录里挑选视觉源，绝不把 TALKER/PIC 文本交回原版解释器。
    /// </summary>
    internal static class ActorRoutines
    {
        public static IEnumerator<PevtWait> ActorEnter(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            IPevtResources resources = PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");

            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            PevtArgumentDomains.RequireAnchor(context, actor, args.String(1));
            PevtArgumentDomains.RequireAppearance(context, actor, args.String(2));
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");

            yield return resources.RequirePortrait(actor.ActorId, args.String(2));

            portrait.SetAppearance(actor.ActorId, args.String(2));
            yield return portrait.Place(actor.ActorId, args.String(1), frames);
        }

        public static IEnumerator<PevtWait> ActorExit(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            yield return portrait.Exit(actor.ActorId, frames);
        }

        public static IEnumerator<PevtWait> ActorMove(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            PevtArgumentDomains.RequireAnchor(context, actor, args.String(1));
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");

            yield return portrait.Move(actor.ActorId, args.String(1), frames);
        }

        public static IEnumerator<PevtWait> ActorAppearance(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            IPevtResources resources = PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");

            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            PevtArgumentDomains.RequireAppearance(context, actor, args.String(1));

            yield return resources.RequirePortrait(actor.ActorId, args.String(1));
            portrait.SetAppearance(actor.ActorId, args.String(1));
        }

        public static IEnumerator<PevtWait> ActorEmote(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            string emoteId = PevtArgumentDomains.RequireId(args.String(1), "emoteId");

            if (!portrait.ValidateEmote(actor.ActorId, emoteId))
                throw new PevtRoutineFailureException("PEVTR4402", $"人物 `{actor.ActorId}` 没有登记表情 `{emoteId}`。");

            portrait.PlayEmote(actor.ActorId, emoteId);
            yield break;
        }

        public static IEnumerator<PevtWait> ActorMotion(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));
            string motionId = PevtArgumentDomains.RequireId(args.String(1), "motionId");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");

            if (!portrait.ValidateMotion(actor.ActorId, motionId))
                throw new PevtRoutineFailureException("PEVTR4402", $"人物 `{actor.ActorId}` 没有登记动作 `{motionId}`。");

            yield return portrait.PlayMotion(actor.ActorId, motionId, frames);
        }

        public static IEnumerator<PevtWait> ActorMotionStop(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));

            portrait.StopMotion(actor.ActorId);
            yield break;
        }

        public static IEnumerator<PevtWait> ActorFlip(PevtRoutineContext context, PevtArguments args)
        {
            IPevtPortrait portrait = PevtArgumentDomains.RequireService(context.Services.Portrait, "Portrait");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));

            portrait.SetFlip(actor.ActorId, args.Bool(1));
            yield break;
        }
    }
}
