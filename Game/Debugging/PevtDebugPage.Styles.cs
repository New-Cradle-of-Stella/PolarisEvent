using UnityEngine;

namespace Polaris.Event.Game.Debugging
{
    internal static partial class PevtDebugPage
    {
        /// <summary>
        /// 调试页自己的 IMGUI 样式。不改 <c>GUI.skin</c> 上的共享样式：那是进程级的，
        /// 改一次会波及所有同样用 IMGUI 的模组覆盖层。
        /// </summary>
        private static class Styles
        {
            internal static GUIStyle Window;
            internal static GUIStyle Panel;
            internal static GUIStyle Button;
            internal static GUIStyle Header;
            internal static GUIStyle Text;
            internal static GUIStyle Dim;
            internal static GUIStyle Warning;
            internal static GUIStyle Highlight;
            internal static GUIStyle Field;
            internal static GUIStyle Toggle;
        }

        private static bool _stylesReady;

        private static void EnsureStyles()
        {
            if (_stylesReady)
                return;

            _stylesReady = true;

            // Unity 内置的 IMGUI 字体没有汉字与假名，而事件 ID、@say 的文本和源码本身都可能是中日文，
            // 缺字形就只剩豆腐块。这里问操作系统要一份带 CJK 的字体，要不到就退回内置字体。
            Font font = null;
            try
            {
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Meiryo", "Yu Gothic UI", "Segoe UI" },
                    13);

                // 这份字体没有任何场景对象引用它，下一次 Resources.UnloadUnusedAssets 就会把它收走，
                // 而样式还攥着已销毁的引用；标上这个标志才能活过场景切换。
                font.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            catch (System.Exception)
            {
                font = null;
            }

            Styles.Window = new GUIStyle(GUI.skin.window) { padding = new RectOffset(8, 8, 22, 8) };
            Styles.Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 6, 6) };
            Styles.Button = new GUIStyle(GUI.skin.button);
            Styles.Field = new GUIStyle(GUI.skin.textField);
            Styles.Toggle = new GUIStyle(GUI.skin.toggle);

            Styles.Text = new GUIStyle(GUI.skin.label)
            {
                wordWrap = false,
                richText = false,
                padding = new RectOffset(2, 2, 1, 1),
            };

            Styles.Header = new GUIStyle(Styles.Text) { fontStyle = FontStyle.Bold };
            Styles.Dim = new GUIStyle(Styles.Text);
            Styles.Warning = new GUIStyle(Styles.Text);
            Styles.Highlight = new GUIStyle(Styles.Text) { fontStyle = FontStyle.Bold };

            Styles.Header.normal.textColor = new Color(0.62f, 0.82f, 1f);
            Styles.Dim.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
            Styles.Warning.normal.textColor = new Color(1f, 0.55f, 0.4f);
            Styles.Highlight.normal.textColor = new Color(1f, 0.85f, 0.35f);

            if (font == null)
                return;

            Styles.Window.font = font;
            Styles.Button.font = font;
            Styles.Field.font = font;
            Styles.Toggle.font = font;
            Styles.Text.font = font;
            Styles.Header.font = font;
            Styles.Dim.font = font;
            Styles.Warning.font = font;
            Styles.Highlight.font = font;
        }

        // ---- 布局小工具 ----

        /// <summary>一行"名字 : 值"。名字列固定宽，几十行叠起来才对得齐。</summary>
        private static void Row(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Styles.Dim, GUILayout.Width(150f));
            GUILayout.Label(value ?? string.Empty, Styles.Text);
            GUILayout.EndHorizontal();
        }

        private static void Header(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label(text, Styles.Header);
        }

        private static void Line(string text) => GUILayout.Label(text ?? string.Empty, Styles.Text);

        private static void Dim(string text) => GUILayout.Label(text ?? string.Empty, Styles.Dim);

        /// <summary>把一段可能很长的文本压成单行，避免一条带调用栈的诊断把整页撑开。</summary>
        private static string OneLine(string text, int max = 220)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string flat = text.Replace("\r", string.Empty).Replace('\n', ' ');
            return flat.Length <= max ? flat : flat.Substring(0, max) + " …";
        }
    }
}
