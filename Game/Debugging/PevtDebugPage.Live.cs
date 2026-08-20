using System.Collections.Generic;
using Polaris.Event.Game.Live;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Live;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Registration;
using UnityEngine;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>
    /// 调试页的 Live 分页：外部导入的目录、热重载通道状态与最近一次导入的逐文件结果。
    /// 这是作者在游戏里唯一需要看的"我改的那一版到了没有"的地方。
    /// </summary>
    internal static partial class PevtDebugPage
    {
        private static string _directoryDraft;

        private static void DrawLive()
        {
            PevtGameRuntime runtime = Runtime;
            if (runtime == null)
            {
                Dim("The PolarisEvent component has not initialised yet.");
                return;
            }

            if (!PevtLiveSettingsEnabled)
            {
                Header("External import is off");
                Dim("Turn on \"External .pevt import (live reload)\" in the Events (PEVT) settings group.");
                Dim("While it is off no folder is scanned and no hot reload pipe is created.");
                return;
            }

            DrawLiveFolder();
            DrawLiveChannel();
            DrawLiveLastImport();
            DrawLiveActive();
        }

        private static bool PevtLiveSettingsEnabled => PevtDebugSettings.LiveImportEnabled;

        // ---- 目录 ----

        private static void DrawLiveFolder()
        {
            Header("Import folder");
            Row("Resolved", string.IsNullOrEmpty(PevtLiveRuntime.Directory) ? "(not resolved yet)" : PevtLiveRuntime.Directory);
            Row("Watching", PevtDebugSettings.LiveWatchEnabled ? "yes — re-imports a moment after a file changes" : "no");
            Row("Restart on import", PevtDebugSettings.LiveRestartEnabled ? "yes" : "no");

            if (_directoryDraft == null)
                _directoryDraft = PevtDebugSettings.LiveDirectory ?? string.Empty;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Folder", Styles.Dim, GUILayout.Width(80f));
            _directoryDraft = GUILayout.TextField(_directoryDraft, Styles.Field, GUILayout.Width(520f));
            if (GUILayout.Button("Use", Styles.Button, GUILayout.Width(60f)))
            {
                // 只改这一次运行的值。设置项的持久化由设置界面负责，调试页不替玩家写配置文件。
                PevtDebugSettings.LiveDirectory = _directoryDraft ?? string.Empty;
                _notice = "Import folder set for this session only — the settings screen is what persists it.";
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload from folder", Styles.Button, GUILayout.Width(150f)))
                _notice = PevtLiveRuntime.Reload().Message;

            if (GUILayout.Button("Drop imported", Styles.Button, GUILayout.Width(120f)))
                _notice = PevtLiveImport.Clear().Message;

            GUILayout.EndHorizontal();

            IReadOnlyList<string> unreadable = PevtLiveImport.LastUnreadable;
            if (unreadable.Count == 0)
                return;

            Header(unreadable.Count + " file(s) could not be read");
            foreach (string entry in unreadable)
                GUILayout.Label(OneLine(entry), Styles.Warning);
        }

        // ---- 通道 ----

        private static void DrawLiveChannel()
        {
            Header("PolarisTools hot reload channel");
            Row("Pipe", PevtLiveProtocol.PipeName + "  (protocol v" + PevtLiveProtocol.Version + ")");
            Row("Server", PevtLiveServer.IsRunning ? "listening" : "stopped");
            Row("Pushes received", PevtLiveServer.ReceivedCount.ToString());

            if (!string.IsNullOrEmpty(PevtLiveServer.LastError))
                GUILayout.Label("Last channel error: " + OneLine(PevtLiveServer.LastError), Styles.Warning);
        }

        // ---- 最近一次导入 ----

        private static void DrawLiveLastImport()
        {
            Header("Last import");
            if (PevtLiveImport.Generation == 0)
            {
                Dim("Nothing has been imported yet.");
                return;
            }

            Row("Generation", PevtLiveImport.Generation.ToString());
            Row("At", PevtLiveImport.LastAppliedAt.ToString("HH:mm:ss"));
            Row("Origin", PevtLiveImport.LastOrigin);

            if (!string.IsNullOrEmpty(PevtLiveImport.LastRestartedEventId))
                Row("Restarted", PevtLiveImport.LastRestartedEventId);

            PevtExternalApplyReport report = PevtLiveImport.LastReport;
            if (report == null)
            {
                Dim(PevtLiveImport.LastSummary);
                return;
            }

            Row("Result", report.SucceededCount + " loaded, " + report.FailedCount + " failed");

            foreach (PevtExternalLoadResult result in report.Results)
            {
                GUILayout.BeginHorizontal();

                if (result.Success && GUILayout.Button("Start", Styles.Button, GUILayout.Width(60f)))
                    StartEvent(result.EventId);
                else if (!result.Success)
                    GUILayout.Label(string.Empty, Styles.Dim, GUILayout.Width(60f));

                GUILayout.Label(
                    result.Success ? result.EventId : "(no event)",
                    result.Success ? Styles.Text : Styles.Warning,
                    GUILayout.Width(240f));
                GUILayout.Label(result.Source.DisplayPath, Styles.Dim);
                GUILayout.EndHorizontal();

                // 警告也要显示：一份能跑的事件同样可能带着"这个等待没有推进源"之类的提示。
                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    GUILayout.Label(
                        "      " + diagnostic.Id + " " + OneLine(DescribeLocation(diagnostic) + diagnostic.Message),
                        diagnostic.Severity == DiagnosticSeverity.Error ? Styles.Warning : Styles.Text);
                }
            }

            if (report.Overrides.Count == 0)
                return;

            Header(report.Overrides.Count + " embedded event(s) overridden");
            foreach (PevtEventOverride entry in report.Overrides)
                GUILayout.Label(OneLine(entry.Describe()), Styles.Text);
        }

        // ---- 当前生效的外部事件 ----

        private static void DrawLiveActive()
        {
            IReadOnlyList<PevtEventCandidate> active = PevtLiveImport.ActiveExternal;
            Header(active.Count + " external event(s) currently in /event");

            foreach (PevtEventCandidate candidate in active)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start", Styles.Button, GUILayout.Width(60f)))
                    StartEvent(candidate.EventId);

                GUILayout.Label(candidate.EventId, Styles.Text, GUILayout.Width(240f));
                GUILayout.Label(candidate.SourcePath, Styles.Dim);
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>诊断位置的前缀，形如 <c>(12,5) </c>；没有位置时为空串。</summary>
        private static string DescribeLocation(Diagnostic diagnostic) =>
            diagnostic.Location == null
                ? string.Empty
                : "(" + diagnostic.Location.StartLine + "," + diagnostic.Location.StartColumn + ") ";
    }
}
