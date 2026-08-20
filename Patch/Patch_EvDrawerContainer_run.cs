using evt;
using HarmonyLib;

namespace Polaris.Event.Patch
{
    /// <summary>
    /// 原版没有 curEv/nexEv 时会以 deleting=true 推进事件图层，并删除仍在显示的 Drawer。
    /// PEVT 有自己的执行宿主，不建立 EvReader；它运行期间必须把这次推进视为正常事件绘制。
    /// </summary>
    [HarmonyPatch(typeof(EvDrawerContainer), nameof(EvDrawerContainer.run), new[] { typeof(float), typeof(bool) })]
    internal static class Patch_EvDrawerContainer_run
    {
        [HarmonyPrefix]
        private static void KeepPevtDrawersAlive(ref bool deleting)
        {
            if (deleting && PolarisEventComponent.Current != null)
                deleting = false;
        }
    }
}
