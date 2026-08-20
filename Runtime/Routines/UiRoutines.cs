using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// UI、输入与等待组合，对应同步指令中间层规范第 10 节和第 6 节的调度部分。
    ///
    /// UI 可见性、黑边、模糊和输入能力都是事件临时状态，全部登记会话恢复；持久设置不在这一层改动。
    /// </summary>
    internal static class UiRoutines
    {
        private static IPevtUi Ui(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Ui, "Ui");

        private static IPevtInput Input(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Input, "Input");

        private static IPevtClock Clock(PevtRoutineContext context) => context.Services.Clock;

        // ---- 等待 ----

        public static IEnumerator<PevtWait> Wait(PevtRoutineContext context, PevtArguments args)
        {
            int frames = PevtArgumentDomains.RequireFrames(args.Int(0), "frames");
            yield return Clock(context).WaitFrames(frames);
        }

        public static IEnumerator<PevtWait> WaitInput(PevtRoutineContext context, PevtArguments args)
        {
            int timeout = PevtArgumentDomains.RequireTimeoutFrames(args.Int(0), "timeoutFrames");

            PevtWait<bool> wait = Clock(context).WaitInput(timeout);
            yield return wait;
            context.Result.SetBool(wait.Result);
        }

        public static IEnumerator<PevtWait> WaitMotion(PevtRoutineContext context, PevtArguments args)
        {
            int timeout = PevtArgumentDomains.RequireTimeoutFrames(args.Int(0), "timeoutFrames");

            PevtWait<bool> wait = Clock(context).WaitOwnedMotions(timeout);
            yield return wait;
            context.Result.SetBool(wait.Result);
        }

        public static IEnumerator<PevtWait> WaitResources(PevtRoutineContext context, PevtArguments args)
        {
            IPevtResources resources = PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");
            string groupId = PevtArgumentDomains.RequireId(args.String(0), "groupId");
            int timeout = PevtArgumentDomains.RequireTimeoutFrames(args.Int(1), "timeoutFrames");

            if (!resources.ResolveGroup(groupId))
                throw new PevtRoutineFailureException("PEVTR4403", $"资源组 `{groupId}` 无法解析。");

            PevtWait<bool> wait = resources.WaitGroup(groupId, timeout);
            yield return wait;
            context.Result.SetBool(wait.Result);
        }

        // ---- UI ----

        public static IEnumerator<PevtWait> UiVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            ui.SetGlobalVisible(args.Bool(0));
            context.Services.Session.RegisterRestore("Ui.SetGlobalVisible(true)", () => ui.SetGlobalVisible(true));
            yield break;
        }

        public static IEnumerator<PevtWait> StatusVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            ui.SetStatusVisible(args.Bool(0));
            context.Services.Session.RegisterRestore("Ui.SetStatusVisible(true)", () => ui.SetStatusVisible(true));
            yield break;
        }

        public static IEnumerator<PevtWait> UiPortraitVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            bool visible = args.Bool(0);

            ui.SetPortraitVisible(visible);
            if (!visible)
                context.Services.Session.RegisterRestore("Ui.SetPortraitVisible(true)", () => ui.SetPortraitVisible(true));

            yield break;
        }

        public static IEnumerator<PevtWait> LetterboxVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            bool visible = args.Bool(0);
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            if (visible)
                context.Services.Session.RegisterRestore("Ui.SetLetterboxVisible(false)", () => ui.SetLetterboxVisible(false, 0));

            yield return ui.SetLetterboxVisible(visible, frames);
        }

        public static IEnumerator<PevtWait> BlurVisible(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            bool visible = args.Bool(0);
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            if (visible)
                context.Services.Session.RegisterRestore("Ui.SetBlurVisible(false)", () => ui.SetBlurVisible(false, 0));

            yield return ui.SetBlurVisible(visible, frames);
        }

        public static IEnumerator<PevtWait> Alert(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            PevtArgumentDomains.RequireId(args.String(1), "style");

            ui.ShowAlert(args.String(0), args.String(1));
            yield return ui.WaitAlertClose();
        }

        public static IEnumerator<PevtWait> TitleShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");

            ui.ShowTitle(args.String(0), x, y);
            context.Services.Session.RegisterRestore("Ui.HideTitle", () => ui.HideTitle(0));
            yield return ui.WaitTitleShown();
        }

        public static IEnumerator<PevtWait> TitleHide(PevtRoutineContext context, PevtArguments args)
        {
            int frames = PevtArgumentDomains.RequireFrames(args.Int(0), "frames");
            yield return Ui(context).HideTitle(frames);
        }

        public static IEnumerator<PevtWait> TutorialShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtUi ui = Ui(context);
            string tutorialId = PevtArgumentDomains.RequireId(args.String(0), "tutorialId");

            if (!ui.ResolveTutorial(tutorialId))
                throw new PevtRoutineFailureException("PEVTR4001", $"教程 `{tutorialId}` 未登记。");

            ui.ShowTutorial(tutorialId);
            context.Cleanup.Push("HideTutorial", () => ui.HideTutorial(tutorialId));

            yield return ui.WaitTutorialClose();

            context.Cleanup.Pop();
        }

        public static IEnumerator<PevtWait> TutorialHide(PevtRoutineContext context, PevtArguments args)
        {
            Ui(context).HideTutorial(PevtArgumentDomains.RequireId(args.String(0), "tutorialId"));
            yield break;
        }

        public static IEnumerator<PevtWait> TutorialClear(PevtRoutineContext context, PevtArguments args)
        {
            Ui(context).ClearTutorials();
            yield break;
        }

        // ---- 输入策略 ----

        public static IEnumerator<PevtWait> InputEnabled(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInput input = Input(context);
            string capability = PevtArgumentDomains.RequireId(args.String(0), "capability");

            if (!input.ValidateCapability(capability))
                throw new PevtRoutineFailureException("PEVTR4001", $"输入能力 `{capability}` 未登记。");

            input.SetCapability(capability, args.Bool(1));
            context.Services.Session.RegisterRestore($"Input.SetCapability({capability}, true)", () => input.SetCapability(capability, true));
            yield break;
        }

        public static IEnumerator<PevtWait> InputKeyEnabled(PevtRoutineContext context, PevtArguments args)
        {
            IPevtInput input = Input(context);
            string key = PevtArgumentDomains.RequireId(args.String(0), "key");

            if (!input.ValidateKey(key))
                throw new PevtRoutineFailureException("PEVTR4001", $"事件按键 `{key}` 未登记。");

            input.SetKeyEnabled(key, args.Bool(1));
            context.Services.Session.RegisterRestore($"Input.SetKeyEnabled({key}, true)", () => input.SetKeyEnabled(key, true));
            yield break;
        }
    }
}
