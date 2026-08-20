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
        internal const string LiveImport = "&" + P + "live_import";
        internal const string LiveImportDesc = "&" + P + "live_import.desc";
        internal const string LiveDirectory = "&" + P + "live_dir";
        internal const string LiveDirectoryDesc = "&" + P + "live_dir.desc";
        internal const string LiveWatch = "&" + P + "live_watch";
        internal const string LiveWatchDesc = "&" + P + "live_watch.desc";
        internal const string LiveRestart = "&" + P + "live_restart";
        internal const string LiveRestartDesc = "&" + P + "live_restart.desc";

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

            loc.Register(P + "live_import", new LocalizedText("External .pevt import (live reload)")
            {
                ["zh"] = "外部导入 .pevt（热重载）",
                ["ja"] = "外部 .pevt の読み込み（ホットリロード）",
            });

            loc.Register(P + "live_import.desc", new LocalizedText(
                "Load .pevt files straight from a folder instead of an embedded package, and open the "
                + "PolarisTools hot reload channel so saving a .pevt in Visual Studio re-imports it here.\n"
                + "Externally imported events override the embedded ones with the same id.\n"
                + "For mod authors — leave it off while playing.")
            {
                ["zh"] = "直接从文件夹读取 .pevt，不必先打进程序集；同时开启 PolarisTools 热重载通道，"
                       + "在 Visual Studio 里保存 .pevt 就会重新导入到这里。\n"
                       + "外部导入的事件会盖住同 ID 的嵌入版本。\n"
                       + "这是给模组作者用的，平时玩请保持关闭。",
                ["ja"] = "アセンブリに埋め込まずにフォルダから直接 .pevt を読み込み、PolarisTools の"
                       + "ホットリロード通信も有効にします（Visual Studio で保存すると再読み込みされます）。\n"
                       + "外部読み込みしたイベントは同じ ID の埋め込み版を上書きします。\n"
                       + "MOD制作者向けです。通常プレイではオフのままに。",
            });

            loc.Register(P + "live_dir", new LocalizedText("Import folder")
            {
                ["zh"] = "导入目录",
                ["ja"] = "読み込みフォルダ",
            });

            loc.Register(P + "live_dir.desc", new LocalizedText(
                "Folder scanned for .pevt files, subfolders included; bin and obj are skipped.\n"
                + "Leave it empty to use BepInEx/Polaris/pevt/. %ENVVAR% is expanded, so you can point it "
                + "straight at your PolarisTools project folder.")
            {
                ["zh"] = "扫描 .pevt 的目录，含子目录；bin 与 obj 会跳过。\n"
                       + "留空则使用 BepInEx/Polaris/pevt/。支持 %环境变量%，可以直接指向 PolarisTools 的工程目录。",
                ["ja"] = ".pevt を探すフォルダ（サブフォルダを含む。bin と obj は除外）。\n"
                       + "空欄なら BepInEx/Polaris/pevt/ を使います。%環境変数% を展開するので、"
                       + "PolarisTools のプロジェクトフォルダを直接指定できます。",
            });

            loc.Register(P + "live_watch", new LocalizedText("Re-import on file change")
            {
                ["zh"] = "文件改动时自动重新导入",
                ["ja"] = "ファイル変更時に自動で再読み込み",
            });

            loc.Register(P + "live_watch.desc", new LocalizedText(
                "Watch the import folder and re-import a moment after a .pevt changes on disk.\n"
                + "Turn it off to re-import only from the debug page button or a PolarisTools push.")
            {
                ["zh"] = "监视导入目录，磁盘上的 .pevt 变动后稍等一下自动重新导入。\n"
                       + "关掉之后只能用调试页的按钮或 PolarisTools 的推送来重新导入。",
                ["ja"] = "読み込みフォルダを監視し、.pevt が変わった少し後に自動で再読み込みします。\n"
                       + "オフにすると、デバッグページのボタンか PolarisTools からの送信のみで再読み込みします。",
            });

            loc.Register(P + "live_restart", new LocalizedText("Restart the running event after import")
            {
                ["zh"] = "导入后重启正在运行的事件",
                ["ja"] = "読み込み後に実行中イベントを再起動",
            });

            loc.Register(P + "live_restart.desc", new LocalizedText(
                "When the event currently running is re-imported, restart it from the top so the change "
                + "is visible right away. A running event cannot be patched in place — its waits, "
                + "coroutines and ownership tree belong to the old instruction stream.")
            {
                ["zh"] = "当前正在跑的事件被重新导入时，从头重启它，改动立刻可见。\n"
                       + "正在运行的事件无法就地替换：它的等待、协程与所有权树都属于旧的那份指令流。",
                ["ja"] = "実行中のイベントが再読み込みされたとき、先頭から再起動して変更をすぐ確認できるようにします。\n"
                       + "実行中のイベントは差し替えできません（待機・コルーチン・所有権ツリーが旧命令列に属するため）。",
            });
        }
    }
}
