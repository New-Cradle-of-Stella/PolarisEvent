using System.Collections.Generic;
using Polaris.Pevt.Actors;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 对话与选择组合，逐条对应同步指令中间层规范第 6 节的原子方法顺序。
    ///
    /// 共同约定：人物 ID 一律先经人物目录解析（未知人物 PEVTR4401），任何校验失败都发生在第一个
    /// 副作用之前；选择类组合无论成功还是失败都要把构造器 Reset 掉，否则下一次选择会带上残留选项。
    /// </summary>
    internal static class DialogueRoutines
    {
        /// <summary>`ActorCatalog.Require` → `Dialogue.SelectSpeaker` → `OpenText` → `WaitAdvance` → `CommitLog`</summary>
        public static IEnumerator<PevtWait> Say(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));

            dialogue.SelectSpeaker(actor.ActorId);
            dialogue.OpenText(args.String(1));
            yield return dialogue.WaitAdvance();
            dialogue.CommitLog();
        }

        /// <summary>`Dialogue.ClearSpeaker` → `OpenText` → `WaitAdvance` → `CommitLog`</summary>
        public static IEnumerator<PevtWait> Narrate(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");

            dialogue.ClearSpeaker();
            dialogue.OpenText(args.String(0));
            yield return dialogue.WaitAdvance();
            dialogue.CommitLog();
        }

        /// <summary>`Dialogue.OpenBoard` → `WaitBoardClose` → `CloseBoard`；关闭动作登记为清理，异常路径也会执行。</summary>
        public static IEnumerator<PevtWait> Board(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");
            PevtArgumentDomains.RequireId(args.String(1), "style");

            dialogue.OpenBoard(args.String(0), args.String(1));
            context.Cleanup.Push("CloseBoard", dialogue.CloseBoard);

            yield return dialogue.WaitBoardClose();

            context.Cleanup.Pop();
            dialogue.CloseBoard();
        }

        public static IEnumerator<PevtWait> TalkerBind(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));

            dialogue.BindProfile(actor.ActorId, args.String(1), args.String(2));

            // 资料绑定是事件临时状态，事件结束时统一恢复。
            context.Services.Session.RegisterRestore($"ResetProfile({actor.ActorId})", () => dialogue.ResetProfile(actor.ActorId));
            yield break;
        }

        public static IEnumerator<PevtWait> TalkerReset(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");
            ActorRegistration actor = PevtArgumentDomains.RequireActor(context, args.String(0));

            dialogue.ResetProfile(actor.ActorId);
            yield break;
        }

        public static IEnumerator<PevtWait> DialogueVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");

            dialogue.SetVisible(args.Bool(0), args.Bool(1));
            context.Services.Session.RegisterRestore("Dialogue.SetVisible(true)", () => dialogue.SetVisible(true, true));
            yield break;
        }

        public static IEnumerator<PevtWait> DialogueHold(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");

            dialogue.SetHold(args.Bool(0));
            context.Services.Session.RegisterRestore("Dialogue.SetHold(false)", () => dialogue.SetHold(false));
            yield break;
        }

        public static IEnumerator<PevtWait> DialogueAuto(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");

            dialogue.SetAutoAdvance(args.Bool(0));
            yield break;
        }

        public static IEnumerator<PevtWait> DialogueLog(PevtRoutineContext context, PevtArguments args)
        {
            IPevtDialogue dialogue = PevtArgumentDomains.RequireService(context.Services.Dialogue, "Dialogue");

            dialogue.SetLogEnabled(args.Bool(0));
            context.Services.Session.RegisterRestore("Dialogue.SetLogEnabled(true)", () => dialogue.SetLogEnabled(true));
            yield break;
        }

        /// <summary>`@skip_enabled` 走的是输入能力，不是对话服务（规范第 6 节最后一行）。</summary>
        public static IEnumerator<PevtWait> SkipEnabled(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInput input = PevtArgumentDomains.RequireService(context.Services.Input, "Input");

            input.SetCapability("skip", args.Bool(0));
            context.Services.Session.RegisterRestore("Input.SetCapability(skip, true)", () => input.SetCapability("skip", true));
            yield break;
        }

        // ---- 选择 ----

        /// <summary>
        /// `Choice.Reset` → `Begin` → 按顺序 `AddIndex` → `PresentIndex` → `Reset`。
        /// 两项、三项、四项签名共用同一个组合，只有实参数量不同；序号从 1 开始。
        /// </summary>
        public static IEnumerator<PevtWait> Choose(PevtRoutineContext context, PevtArguments args)
        {
            IPevtChoice choice = PevtArgumentDomains.RequireService(context.Services.Choice, "Choice");

            choice.Reset();
            choice.Begin(args.String(0));
            for (int i = 1; i < args.Count; i++)
                choice.AddIndex(args.String(i));

            // 失败或取消时也要清空构造器，否则残留选项会泄漏到下一次选择。
            context.Cleanup.Push("Choice.Reset", choice.Reset);

            PevtWait<int> wait = choice.PresentIndex();
            yield return wait;

            context.Result.SetInt(wait.Result);
            context.Cleanup.Pop();
            choice.Reset();
        }

        public static IEnumerator<PevtWait> ChoiceClear(PevtRoutineContext context, PevtArguments args)
        {
            PevtArgumentDomains.RequireService(context.Services.Choice, "Choice").Reset();
            yield break;
        }

        public static IEnumerator<PevtWait> ChoiceAdd(PevtRoutineContext context, PevtArguments args)
        {
            IPevtChoice choice = PevtArgumentDomains.RequireService(context.Services.Choice, "Choice");
            PevtArgumentDomains.RequireId(args.String(0), "key");

            choice.Add(args.String(0), args.String(1), args.Bool(2), args.Bool(3));
            yield break;
        }

        /// <summary>`Choice.Begin` → `SetInitial` → `PresentKey` → `Reset`；返回稳定的选项键。</summary>
        public static IEnumerator<PevtWait> ChoiceShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtChoice choice = PevtArgumentDomains.RequireService(context.Services.Choice, "Choice");

            choice.Begin(args.String(0));
            choice.SetInitial(args.String(1));
            context.Cleanup.Push("Choice.Reset", choice.Reset);

            PevtWait<string> wait = choice.PresentKey();
            yield return wait;

            if (wait.Result == null)
                throw new PevtNullResultException();

            context.Result.SetString(wait.Result);
            context.Cleanup.Pop();
            choice.Reset();
        }
    }
}
