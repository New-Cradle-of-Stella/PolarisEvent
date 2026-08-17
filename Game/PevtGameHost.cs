using System;
using evt;
using m2d;
using nel;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 游戏侧适配器共用的单例入口与"事件模式"开关。
    ///
    /// 关键前提：原版 <see cref="EV"/> 在游戏启动时就由 <c>NelM2DBase</c> 调用 <c>EV.initEvent</c>
    /// 建好了 <c>MsgCon</c>、<c>DC</c>、<c>Sel</c>、<c>Pics</c> 这些演出对象，而 <c>EV.Instance</c> 的
    /// <c>FixedUpdate</c> 每帧都会推进消息容器与图层容器——即使没有任何原版事件在跑。
    /// 所以 PolarisEvent 只需要直接操作这些演出对象，不需要（也绝不允许）压入 <c>EvReader</c>、
    /// 提交 CMD 文本或让 <c>EV.readOneLine</c> 参与调度。
    ///
    /// 由此带来的代价是 <c>EV.isActive()</c> 在 PEVT 事件期间是 false（它只看 EvReader 栈），
    /// 因此"停住游戏主循环"要由 <see cref="EnterEventMode"/> 用原版自己的公开开关显式完成。
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
        ///
        /// 用的全是原版 <c>STOP_GMAIN</c> / <c>STOP_GHANDLE</c> 走的同一组开关，
        /// 不改写 <c>EV.state</c>——那个字段属于原版解释器的游标，写它会让原版事件的恢复逻辑错乱。
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

        /// <summary>退出事件模式。必须在根事件结束、替换、异常和插件卸载的每一条路径上都走到。</summary>
        public static void ExitEventMode()
        {
            Guard("ExitEventMode", () =>
            {
                Messages?.hideMsg(true);
                Messages?.quitEvent();
                Selector?.evEnd();
                Drawers?.deactivateEvent();
                IN.FlgUiUse.Rem(Flag);
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
