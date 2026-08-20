using System.Collections.Generic;
using System.Reflection;
using evt;
using HarmonyLib;
using Polaris.Event.Game.Debugging;

namespace Polaris.Event.Patch
{
    /// <summary>
    /// 让调试页 Autoplay 也能推进 <c>$raw cmd</c> 内由原版 EV 创建的 MESSAGE 等待。
    /// 这里只覆盖原版“确认”查询的返回值，不写 Unity 输入状态，也不会影响 PEVT 之外的事件。
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_EV_AutoplayInput
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EV), "INisKettei", System.Type.EmptyTypes);
            yield return AccessTools.Method(typeof(EV), "INisKetteiPD", new[] { typeof(int) });
            yield return AccessTools.Method(typeof(EV), "INisKetteiOn", System.Type.EmptyTypes);
            yield return AccessTools.Method(typeof(EV), "INisKetteiM3", System.Type.EmptyTypes);
        }

        [HarmonyPostfix]
        private static void SupplyAutoplayInput(ref bool __result)
        {
            if (!__result && PevtDebugPage.PeekAutoplayAdvance())
                __result = true;
        }
    }
}
