using System;
using Polaris.Pevt.Runtime;
using UnityEngine;
using XX;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>
    /// PEVT 调试页。作用与原版 F7 的 <c>evt.EvDebugger</c> 相同——一个把解释器当下的状态摊开给作者看的窗口，
    /// 但不复用原版 <c>Designer</c>：那套控件绑死在原版事件的数据结构上，而 PEVT 有自己的执行栈、协程与所有权树。
    /// 这里只用 IMGUI，界面全部由运行时的只读视图渲染，任何一格都不写回解释器。
    /// </summary>
    internal static partial class PevtDebugPage
    {
        /// <summary>调试页占住原版 UI 使用标记时用的名字；与事件模式的 <c>__PEVT</c> 分开，两者可以同时成立。</summary>
        private const string InputFlag = "__PEVT_DBG";

        /// <summary>IMGUI 窗口 ID，取 'PEVT' 四个字节，避开别的模组随手写的小整数。</summary>
        private const int WindowId = 0x50455654;

        private static GameObject _host;
        private static bool _open;
        private static bool _inputHeld;
        private static bool _cursorHeld;
        private static bool _previousCursorVisible;
        private static CursorLockMode _previousCursorLockState;

        private static Rect _window = new Rect(40f, 40f, 980f, 640f);
        private static int _tab;
        private static string _notice = string.Empty;
        private static long _lastObservedFinishedInstanceId;

        private static readonly string[] TabTitles =
            { "Overview", "Stack", "Async", "Ownership", "Source", "Events", "Live", "Queries", "History" };

        private static readonly Vector2[] Scroll = new Vector2[TabTitles.Length];

        /// <summary>页面是否正开着。</summary>
        internal static bool IsOpen => _open;

        // ---- 生命周期 ----

        /// <summary>组件更新点每帧调一次：只负责热键与开关联动，绘制在 <see cref="PevtDebugOverlay"/> 的 OnGUI 里。</summary>
        internal static void Update()
        {
            if (!PevtDebugSettings.DebugPageEnabled)
            {
                Close();
                return;
            }

            // 走原版 IN.getKD 而不是 Input.GetKeyDown：它顺带尊重"有输入框正在获得焦点"，
            // 玩家在原版文本框里打字时不会被热键打断。
            if (PevtGameHost.Safe(() => IN.getKD(PevtDebugSettings.HotkeyCode), false))
                Toggle();

            if (_open)
                ObserveFinishedInstances();
        }

        /// <summary>
        /// 调试按钮拿到实例时它通常还只是 Created；真正的运行错误要到后续 Update 才出现。
        /// 主动观察宿主保留的终态，避免页脚永远停在 "Started ..."、让 PEVTR 看起来像被吞掉。
        /// </summary>
        private static void ObserveFinishedInstances()
        {
            PevtGameRuntime runtime = PolarisEventComponent.Runtime;
            if (runtime == null)
                return;

            foreach (PevtEventInstance instance in runtime.Host.Instances)
            {
                if (!instance.IsFinished || instance.Id <= _lastObservedFinishedInstanceId)
                    continue;

                _lastObservedFinishedInstanceId = instance.Id;
                if (instance.Status != PevtExecutionStatus.Faulted || instance.Diagnostic == null)
                    continue;

                _notice = "Runtime error: " + OneLine(instance.Diagnostic.Describe());
                _tab = TabTitles.Length - 1;
            }
        }

        internal static void Toggle()
        {
            if (_open)
                Close();
            else
                Open();
        }

        internal static void Open()
        {
            if (_open || !PevtDebugSettings.DebugPageEnabled)
                return;

            _open = true;
            _notice = string.Empty;
            EnsureHost();
            ClampWindow();
            HoldInput(true);
            HoldCursor(true);
        }

        internal static void Close()
        {
            if (!_open)
                return;

            _open = false;
            HoldInput(false);
            HoldCursor(false);
        }

        /// <summary>插件卸载：收起页面、放开输入标记并销毁宿主对象。</summary>
        internal static void Shutdown()
        {
            Close();

            if (_host == null)
                return;

            UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        /// <summary>
        /// 页面开着期间占住原版的 UI 使用标记，玩家的移动与攻击键就不会穿过窗口打到角色身上。
        /// 与事件模式用的是两个不同的名字，因此调试页开关不会把正在跑的事件从事件模式里踢出去。
        /// </summary>
        private static void HoldInput(bool hold)
        {
            if (hold == _inputHeld)
                return;

            _inputHeld = hold;
            PevtGameHost.Guard("DebugPage.HoldInput", () =>
            {
                if (hold)
                    IN.FlgUiUse.Add(InputFlag);
                else
                    IN.FlgUiUse.Rem(InputFlag);
            });
        }

        /// <summary>
        /// IMGUI 需要系统光标才能点击。打开时记住游戏原有状态并临时显示、解锁，
        /// 关闭或卸载时原样恢复，避免调试页改变正常游玩时的鼠标行为。
        /// </summary>
        private static void HoldCursor(bool hold)
        {
            if (hold == _cursorHeld)
                return;

            PevtGameHost.Guard("DebugPage.HoldCursor", () =>
            {
                if (hold)
                {
                    _previousCursorVisible = Cursor.visible;
                    _previousCursorLockState = Cursor.lockState;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _cursorHeld = true;
                    return;
                }

                Cursor.lockState = _previousCursorLockState;
                Cursor.visible = _previousCursorVisible;
                _cursorHeld = false;
            });
        }

        /// <summary>
        /// IMGUI 只能从 <c>MonoBehaviour.OnGUI</c> 里画，而组件是普通类库，所以第一次打开时才建这个宿主对象；
        /// 关掉开关不销毁它，重新打开的代价比重建一个 <c>DontDestroyOnLoad</c> 对象低。
        /// </summary>
        private static void EnsureHost()
        {
            if (_host != null)
                return;

            PevtGameHost.Guard("DebugPage.EnsureHost", () =>
            {
                _host = new GameObject("PolarisEvent Debug Page");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.AddComponent<PevtDebugOverlay>();
            });
        }

        private static void ClampWindow()
        {
            float width = Mathf.Min(_window.width, Screen.width - 20f);
            float height = Mathf.Min(_window.height, Screen.height - 20f);
            _window = new Rect(
                Mathf.Clamp(_window.x, 0f, Math.Max(0f, Screen.width - width)),
                Mathf.Clamp(_window.y, 0f, Math.Max(0f, Screen.height - height)),
                width,
                height);
        }

        // ---- 绘制 ----

        /// <summary>由宿主的 OnGUI 调用。</summary>
        internal static void Draw()
        {
            if (!_open)
                return;

            EnsureStyles();

            // 负 depth 让窗口画在别的 IMGUI 之上；游戏本体不用 IMGUI，这里争的只是别的模组的覆盖层。
            GUI.depth = -1000;

            // 用 GUI.Window 而不是 GUILayout.Window：后者会把窗口缩放到内容大小，
            // 而这里每一帧的内容长度都在变，窗口会跟着抖。GUILayout 在窗口内部照常可用。
            _window = GUI.Window(WindowId, _window, DrawWindow, "PEVT Debug", Styles.Window);
        }

        private static void DrawWindow(int id)
        {
            GUILayout.Space(2f);
            _tab = GUILayout.Toolbar(_tab, TabTitles, Styles.Button);
            GUILayout.Space(4f);

            // 滚动区高度写死成"窗口减去工具条与页脚"，否则它会一路撑到内容的高度、把页脚挤出窗口。
            Scroll[_tab] = GUILayout.BeginScrollView(
                Scroll[_tab], Styles.Panel, GUILayout.Height(Mathf.Max(80f, _window.height - 92f)));
            try
            {
                DrawTab(_tab);
            }
            catch (Exception ex)
            {
                // 调试页自己抛异常时不能把宿主的 OnGUI 掀掉，否则这一帧后面的覆盖层全不画。
                GUILayout.Label("The debug page failed to render: " + ex.Message, Styles.Warning);
            }
            finally
            {
                GUILayout.EndScrollView();
            }

            DrawFooter();
            GUI.DragWindow(new Rect(0f, 0f, _window.width, 22f));
        }

        private static void DrawTab(int tab)
        {
            switch (tab)
            {
                case 0: DrawOverview(); break;
                case 1: DrawStack(); break;
                case 2: DrawAsync(); break;
                case 3: DrawOwnership(); break;
                case 4: DrawSource(); break;
                case 5: DrawEvents(); break;
                case 6: DrawLive(); break;
                case 7: DrawQueries(); break;
                default: DrawHistory(); break;
            }
        }

        private static void DrawFooter()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Stop event", Styles.Button, GUILayout.Width(110f)))
                StopCurrent();

            if (GUILayout.Button("Clear notice", Styles.Button, GUILayout.Width(110f)))
                _notice = string.Empty;

            GUILayout.Label(
                string.IsNullOrEmpty(_notice)
                    ? "F8 closes this page"
                    : _notice,
                string.IsNullOrEmpty(_notice) ? Styles.Dim : Styles.Warning);

            GUILayout.EndHorizontal();
        }
    }
}
