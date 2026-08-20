using HarmonyLib;
using nel;
using Polaris.Event.Game;

namespace Polaris.Event.Patch
{
    /// <summary>
    /// PEVT 不建立原版 EvReader，原版 UI 的关闭路径可能把 draw_letter_box 复位。
    /// 在统一 UI 更新入口重新应用本次 PEVT 会话拥有的黑框状态。
    /// </summary>
    [HarmonyPatch(typeof(UIBase), nameof(UIBase.run), new[] { typeof(float) })]
    internal static class Patch_UIBase_run
    {
        [HarmonyPrefix]
        private static void ApplyPevtLetterbox(UIBase __instance) =>
            PevtGameUi.ApplyLetterboxOverride(__instance);
    }
}
