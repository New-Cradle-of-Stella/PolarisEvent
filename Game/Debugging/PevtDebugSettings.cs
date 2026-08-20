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

        /// <summary>
        /// 外部导入与热重载的总开关。关掉时既不扫目录，也不建热重载命名管道——
        /// 正常游玩的进程上没有任何可连的端点。
        /// </summary>
        [PolarisSetting(PevtDebugStrings.LiveImport, Desc = PevtDebugStrings.LiveImportDesc, Order = 10)]
        public static bool LiveImportEnabled = false;

        /// <summary>导入目录；留空用 <c>BepInEx/Polaris/pevt/</c>。支持 <c>%VAR%</c>。</summary>
        [PolarisSetting(PevtDebugStrings.LiveDirectory, Desc = PevtDebugStrings.LiveDirectoryDesc,
            MaxLength = 260, Order = 11)]
        public static string LiveDirectory = string.Empty;

        [PolarisSetting(PevtDebugStrings.LiveWatch, Desc = PevtDebugStrings.LiveWatchDesc, Order = 12)]
        public static bool LiveWatchEnabled = true;

        [PolarisSetting(PevtDebugStrings.LiveRestart, Desc = PevtDebugStrings.LiveRestartDesc, Order = 13)]
        public static bool LiveRestartEnabled = true;

        /// <summary>关掉开关时立刻收起页面，否则它会一直挂在屏幕上直到玩家再按一次热键。</summary>
        private static void OnDebugPageChanged()
        {
            if (!DebugPageEnabled)
                PevtDebugPage.Close();
        }
    }
}
