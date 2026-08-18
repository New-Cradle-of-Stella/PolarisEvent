using Polaris.Settings;
using UnityEngine.InputSystem;

namespace Polaris.Event.Game.Debugging
{
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

        /// <summary>调试页开关键，硬编码：F7 与 F9 已被原版分别用于 <c>EvDebugger</c> 和重新加载。</summary>
        internal const Key HotkeyCode = Key.F8;

        /// <summary>关掉开关时立刻收起页面，否则它会一直挂在屏幕上直到玩家再按一次热键。</summary>
        private static void OnDebugPageChanged()
        {
            if (!DebugPageEnabled)
                PevtDebugPage.Close();
        }
    }
}
