using Polaris.Localization;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>调试页设置项文案的内置翻译；写在代码里是因为绑定配置文件时 `.plang` 还没注册。</summary>
    internal static class PevtDebugStrings
    {
        private const string P = "polarisevent.settings.";

        internal const string Group = "&" + P + "group";
        internal const string DebugPage = "&" + P + "debug_page";
        internal const string DebugPageDesc = "&" + P + "debug_page.desc";

        private static bool _registered;

        /// <summary>由组件 <c>Awake</c> 调一次，早于 Start 阶段的设置项扫描。</summary>
        internal static void Register()
        {
            if (_registered)
                return;

            _registered = true;

            LocalizationAPI loc = PolarisAPI.Localization;

            loc.Register(P + "group", new LocalizedText("Events (PEVT)")
            {
                ["zh"] = "事件（PEVT）",
                ["ja"] = "イベント（PEVT）",
            });

            loc.Register(P + "debug_page", new LocalizedText("PEVT debug page")
            {
                ["zh"] = "PEVT 调试页",
                ["ja"] = "PEVT デバッグページ",
            });

            loc.Register(P + "debug_page.desc", new LocalizedText(
                "Enable the in-game PEVT debug page: call stack, variables, coroutines, ownership "
                + "tree, source and the event registry of the running event.\n"
                + "For mod authors — leave it off while playing.")
            {
                ["zh"] = "启用游戏内的 PEVT 调试页：查看当前事件的调用栈、变量、协程、所有权树、源码与事件注册表。\n"
                       + "这是给模组作者用的，平时玩请保持关闭。",
                ["ja"] = "ゲーム内の PEVT デバッグページを有効にします：実行中イベントのコールスタック、変数、"
                       + "コルーチン、所有権ツリー、ソース、イベント登録表を確認できます。\n"
                       + "MOD制作者向けです。通常プレイではオフのままに。",
            });
        }
    }
}
