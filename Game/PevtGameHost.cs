using System;
using evt;
using m2d;
using nel;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 游戏侧适配器共用的单例入口与"事件模式"开关。
    /// </summary>
    internal static class PevtGameHost
    {
        /// <summary>PolarisEvent 在原版各种 <c>Flagger</c> 上使用的统一标记。</summary>
        public const string Flag = "__PEVT";

        public static bool Ready => EV.Instance != null && EV.getMessageContainer() != null;

        public static IMessageContainer Messages => EV.getMessageContainer();

        public static EvSelector Selector => EV.Sel;

        public static EvDrawerContainer Drawers => EV.DC;

        public static EvImgContainer Pictures => EV.Pics;

        public static ITutorialBox Tutorials => EV.TutoBox;

        public static EvTextLog Log => EV.Log;

        public static NelM2DBase Map => M2DBase.Instance as NelM2DBase;

        public static M2Camera Camera => M2DBase.Instance?.Cam;

        /// <summary>
        /// 进入事件模式：停掉游戏主循环与玩家操作，并占住 UI 使用标记。
        /// </summary>
        public static void EnterEventMode()
        {
            Guard("EnterEventMode", () =>
            {
                EV.stopGMain(true);
                EV.setGHandleFlag(false);
                EV.addAllocEvHandleKey(KEY.SIMKEY._EVHANDLE, true);
                IN.FlgUiUse.Add(Flag);
                Messages?.setHandle(true);
            });
        }

        /// <summary>
        /// 退出事件模式。必须在根事件结束、替换、异常和插件卸载的每一条路径上都走到。
        /// </summary>
        public static void ExitEventMode()
        {
            Guard("ExitEventMode", () =>
            {
                bool vanillaEventRunning = Safe(() => EV.isActive(false), false);

                Messages?.hideMsg(true);

                if (!vanillaEventRunning)
                {
                    Messages?.quitEvent();
                    Selector?.evEnd();
                    Drawers?.deactivateEvent();
                }
                else
                {
                    Selector?.deactivate();
                }

                IN.FlgUiUse.Rem(Flag);

                if (vanillaEventRunning)
                    return;

                EV.setGHandleFlag(true);
                EV.stopGMain(false);
                EV.StopGMainDrawFlag(false);
            });
        }

        /// <summary>玩家是否按下了"推进"键。与原版 <c>INisKettei</c> 同源，尊重事件按键分配。</summary>
        public static bool AdvancePressed() => Safe(EV.INisKettei, false);

        public static bool CancelPressed() => Safe(EV.INisCancel, false);

        /// <summary>
        /// 适配器统一的异常边界。游戏对象在场景切换时会突然变成已销毁的 Unity 空引用，
        /// 让这种异常穿到解释器里只会得到一条无法定位的 PEVTR4001，所以在这里就地上报。
        /// </summary>
        public static void Guard(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Game." + operation);
            }
        }

        public static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
