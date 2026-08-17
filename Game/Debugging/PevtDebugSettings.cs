using Polaris.Settings;
using UnityEngine.InputSystem;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>
    /// 调试页热键的候选。不直接绑定 <see cref="Key"/>（整个键盘太大，设置界面选不动），只列功能键；
    /// F7 与 F9 刻意缺席，原版分别用它们开 <c>EvDebugger</c> 和触发重新加载。
    /// </summary>
    internal enum PevtDebugHotkey
    {
        F5,
        F6,
        F8,
        F10,
        F11,
        F12,
    }

    /// <summary>
    /// PolarisEvent 的全局设置；字段本身就是值的真身，<see cref="SettingsAttributeScanner"/> 在启动时写回，
    /// 玩家改动设置界面时也直接改这里。
    /// </summary>
    [PolarisSettingGroup("polarisevent", PevtDebugStrings.Group)]
    internal static class PevtDebugSettings
    {
        [PolarisSetting(PevtDebugStrings.DebugPage, Desc = PevtDebugStrings.DebugPageDesc,
            OnChanged = nameof(OnDebugPageChanged))]
        public static bool DebugPageEnabled = false;

        [PolarisSetting(PevtDebugStrings.Hotkey, Desc = PevtDebugStrings.HotkeyDesc)]
        public static PevtDebugHotkey DebugPageHotkey = PevtDebugHotkey.F8;

        /// <summary>当前热键对应的输入系统按键。</summary>
        internal static Key HotkeyCode
        {
            get
            {
                switch (DebugPageHotkey)
                {
                    case PevtDebugHotkey.F5: return Key.F5;
                    case PevtDebugHotkey.F6: return Key.F6;
                    case PevtDebugHotkey.F10: return Key.F10;
                    case PevtDebugHotkey.F11: return Key.F11;
                    case PevtDebugHotkey.F12: return Key.F12;
                    default: return Key.F8;
                }
            }
        }

        /// <summary>关掉开关时立刻收起页面，否则它会一直挂在屏幕上直到玩家再按一次热键。</summary>
        private static void OnDebugPageChanged()
        {
            if (!DebugPageEnabled)
                PevtDebugPage.Close();
        }
    }
}
