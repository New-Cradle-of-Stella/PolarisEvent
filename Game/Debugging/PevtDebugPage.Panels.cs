using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Text;
using UnityEngine;

namespace Polaris.Event.Game.Debugging
{
    /// <summary>调试页的各个分页。全部只读取运行时的公开视图，没有一处写回解释器。</summary>
    internal static partial class PevtDebugPage
    {
        private static string _startId = string.Empty;
        private static bool _showFinished;

        private static PevtGameRuntime Runtime => PolarisEventComponent.Runtime;

        /// <summary>当前根事件；组件没初始化或没有事件在跑时为 null。</summary>
        private static PevtEventInstance Current => Runtime?.Current;

        /// <summary>当前根事件的执行实例。变量、协程与指令指针都只能从它上面读。</summary>
        private static PevtExecution CurrentExecution => Current?.Routine.Execution;

        // ---- Overview ----

        private static void DrawOverview()
        {
            PevtGameRuntime runtime = Runtime;
            if (runtime == null)
            {
                Dim("The PolarisEvent component has not initialised yet.");
                return;
            }

            Header("Runtime");
            Row("Clock frame", runtime.Frame.ToString());
            Row("Event mode", PevtGameHost.Ready ? "vanilla EV ready" : "vanilla EV not ready");
            Row("Outstanding leases", runtime.OutstandingLeaseCount.ToString());

            IReadOnlyList<string> restores = runtime.PendingRestores;
            Row("Pending restores", restores.Count.ToString());
            foreach (string restore in restores)
                Dim("    " + restore);

            Header("Raw channel");
            Row("$raw cmd busy", runtime.RawCommands.IsBusy ? "yes" : "no");
            Row("$raw cmd queued", runtime.RawCommands.QueueLength.ToString());
            Row("$raw cmd sessions", runtime.RawCommands.StartedSessionCount.ToString());

            Header("Root event");
            PevtEventInstance instance = Current;
            if (instance == null)
            {
                Dim("No event is running. Start one from the Events tab.");
                return;
            }

            PevtExecution execution = instance.Routine.Execution;
            Row("Event", instance.EventId + "#" + instance.Id);
            Row("Owner", string.IsNullOrEmpty(instance.Owner) ? "(started from source)" : instance.Owner);
            Row("Status", instance.Status.ToString());
            Row("Current @", instance.CurrentCommand ?? "(none)");
            Row("Waiting on", instance.CurrentWaitSource ?? "(not waiting)");
            Row("Frame depth", execution.TotalDepth + " / " + execution.Budget.Limits.MaxCallDepth);
            Row("Dynamic depth", execution.DynamicDepth.ToString());
            Row("Steps", instance.TotalSteps + " total, " + execution.Budget.StepsThisFrame + " this frame");
            Row("Stalled frames", execution.Budget.StallFrames + " / " + execution.Budget.Limits.StallFrames);
            Row("Async routines", execution.AsyncRoutines.RunningCount + " running of "
                                 + execution.AsyncRoutines.Routines.Count);
            Row("Live ownership", instance.Ownership.LiveCount.ToString());

            if (instance.Diagnostic != null)
            {
                Header("Diagnostic");
                GUILayout.Label(instance.Diagnostic.Describe(), Styles.Warning);
            }

            IReadOnlyList<PevtRuntimeDiagnostic> warnings = execution.Warnings;
            if (warnings.Count == 0)
                return;

            Header("Warnings");
            foreach (PevtRuntimeDiagnostic warning in warnings)
                GUILayout.Label(OneLine(warning.Describe()), Styles.Warning);
        }

        // ---- Stack ----

        private static void DrawStack()
        {
            PevtEventInstance instance = Current;
            if (instance == null)
            {
                Dim("No event is running.");
                return;
            }

            Header("Call stack (innermost first)");
            foreach (PevtCallFrame frame in instance.CallStack)
            {
                Line("  at " + frame.Kind + " " + frame.Name
                     + (frame.Location != null ? "  (" + frame.Location + ")" : string.Empty));
            }

            PevtExecution execution = instance.Routine.Execution;
            IReadOnlyList<PevtFrame> frames = execution.Frames;

            Header("Frames and environments (outermost first)");
            for (int i = 0; i < frames.Count; i++)
            {
                PevtFrame frame = frames[i];
                Line($"[{i}] {frame.Kind} {frame.Name}  ip={frame.Ip}  eval={frame.EvalStackDepth}"
                     + (frame.ReturnIp >= 0 ? "  return->" + frame.ReturnIp : string.Empty));
                DrawEnvironment(frame.Environment);
            }
        }

        /// <summary>把一层环境里的槽位与句柄摊开。<c>exec</c> 片段的环境有父链，一并沿着它往上走。</summary>
        private static void DrawEnvironment(PevtEnvironment environment)
        {
            for (PevtEnvironment scope = environment; scope != null; scope = scope.Parent)
            {
                var names = new List<string>(scope.SlotNames);
                names.Sort(StringComparer.Ordinal);

                var handlers = new List<string>(scope.HandlerNames);
                handlers.Sort(StringComparer.Ordinal);

                if (names.Count == 0 && handlers.Count == 0)
                {
                    Dim("      " + scope.ScopeName + ": (empty)");
                    continue;
                }

                Dim("      " + scope.ScopeName + ":");

                foreach (string name in names)
                {
                    // 未初始化的槽位不能读值，PevtSlot.ToString 已经替我们区分了这两种写法。
                    if (scope.TryGetSlot(name, out PevtSlot slot))
                        Line("        " + slot);
                }

                foreach (string name in handlers)
                {
                    if (!scope.TryGetHandler(name, out PevtHandlerValue handler))
                        continue;

                    string type = handler.ExpectedResultType.HasValue
                        ? handler.ExpectedResultType.Value.DisplayName()
                        : "void";
                    Line($"        {name} : handler<{type}> -> routine#{handler.RoutineId}");
                }
            }
        }

        // ---- Async ----

        private static void DrawAsync()
        {
            PevtExecution execution = CurrentExecution;
            if (execution == null)
            {
                Dim("No event is running.");
                return;
            }

            IReadOnlyList<PevtAsyncRoutine> routines = execution.AsyncRoutines.Routines;
            if (routines.Count == 0)
            {
                Dim("This event owns no coroutines.");
                return;
            }

            Header(routines.Count + " coroutine(s), " + execution.AsyncRoutines.RunningCount + " still running");

            foreach (PevtAsyncRoutine routine in routines)
            {
                GUIStyle style = routine.State == PevtAsyncState.Faulted ? Styles.Warning : Styles.Text;
                GUILayout.Label($"async#{routine.Id}  {routine.State}  status={routine.StatusCode}  {routine.Description}", style);

                if (!routine.IsFinished)
                {
                    Dim("      waiting on: " + (routine.CurrentWaitSource ?? "(nothing)")
                        + (routine.HasProgressSource ? string.Empty : "   <- no progress source"));
                }

                if (routine.HasResult)
                    Dim("      result: " + routine.Result);

                if (routine.Error != null)
                    GUILayout.Label("      " + OneLine(routine.Error.Describe()), Styles.Warning);

                if (routine.IsFinished && !routine.Observed)
                    Dim("      result never observed");
            }
        }

        // ---- Ownership ----

        private static void DrawOwnership()
        {
            PevtGameRuntime runtime = Runtime;
            if (runtime == null)
            {
                Dim("The PolarisEvent component has not initialised yet.");
                return;
            }

            IReadOnlyList<PevtOwnershipNode> roots = runtime.Host.OwnershipRoots;
            if (roots.Count == 0)
            {
                Dim("The ownership tree is empty — nothing is holding a vanilla resource.");
                return;
            }

            Header("Ownership tree (a live node is something still to be released)");
            foreach (PevtOwnershipNode root in roots)
                DrawOwnershipNode(root, 0);
        }

        private static void DrawOwnershipNode(PevtOwnershipNode node, int depth)
        {
            string indent = new string(' ', depth * 4);
            GUILayout.Label(
                $"{indent}{node.Kind}#{node.Id}  {node.Description}"
                + (node.IsReleased ? "  (released)" : "  live=" + node.LiveCount),
                node.IsReleased ? Styles.Dim : Styles.Text);

            foreach (PevtOwnershipNode child in node.Children)
                DrawOwnershipNode(child, depth + 1);
        }

        // ---- Source ----

        private static void DrawSource()
        {
            PevtEventInstance instance = Current;
            if (instance == null)
            {
                Dim("No event is running.");
                return;
            }

            // 编译产物本身不外露，但调用栈的每一层都带着它的 TextLocation，源文本从那里取。
            SourceText source = null;
            int currentLine = -1;
            foreach (PevtCallFrame frame in instance.CallStack)
            {
                if (frame.Location == null)
                    continue;

                source = frame.Location.Source;
                currentLine = frame.Location.StartLine;
                break;
            }

            if (source == null)
            {
                Dim("This event carries no source text (it was started from a definition without one).");
                return;
            }

            Header(source.FilePath + "  —  " + source.LineCount + " lines, currently at line " + currentLine);

            string[] lines = source.Content.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int number = i + 1;
                bool here = number == currentLine;
                GUILayout.Label(
                    (here ? "> " : "  ") + number.ToString().PadLeft(4) + " | " + lines[i],
                    here ? Styles.Highlight : Styles.Text);
            }
        }

        // ---- Events ----

        private static void DrawEvents()
        {
            PevtGameRuntime runtime = Runtime;
            if (runtime == null)
            {
                Dim("The PolarisEvent component has not initialised yet.");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Start by id", Styles.Dim, GUILayout.Width(80f));
            _startId = GUILayout.TextField(_startId ?? string.Empty, Styles.Field, GUILayout.Width(320f));
            if (GUILayout.Button("Start", Styles.Button, GUILayout.Width(70f)))
                StartEvent(_startId);
            GUILayout.EndHorizontal();

            PevtEventRegistry events = runtime.Registry.Events;

            var active = new List<PevtEventCandidate>(events.ActiveEvents);
            active.Sort((left, right) => string.CompareOrdinal(left.EventId, right.EventId));

            Header(active.Count + " registered event(s)");
            foreach (PevtEventCandidate candidate in active)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start", Styles.Button, GUILayout.Width(60f)))
                    StartEvent(candidate.EventId);

                GUILayout.Label(candidate.EventId, Styles.Text, GUILayout.Width(260f));
                GUILayout.Label(candidate.Owner + " : " + candidate.SourcePath, Styles.Dim);
                GUILayout.EndHorizontal();
            }

            IReadOnlyList<PevtEventConflict> conflicts = events.Conflicts;
            if (conflicts.Count > 0)
            {
                Header(conflicts.Count + " id conflict(s)");
                foreach (PevtEventConflict conflict in conflicts)
                {
                    GUILayout.Label(
                        (conflict.IsSameOwner ? "[warning] " : "[fatal] ") + OneLine(conflict.Describe()),
                        conflict.IsSameOwner ? Styles.Text : Styles.Warning);
                }
            }

            IReadOnlyList<PevtEventLoadFailure> failures = events.Failures;
            if (failures.Count == 0)
                return;

            Header(failures.Count + " event(s) failed to load");
            foreach (PevtEventLoadFailure failure in failures)
                GUILayout.Label(OneLine(failure.ToString()), Styles.Warning);
        }

        // ---- History ----

        private static void DrawHistory()
        {
            PevtGameRuntime runtime = Runtime;
            if (runtime == null)
            {
                Dim("The PolarisEvent component has not initialised yet.");
                return;
            }

            _showFinished = GUILayout.Toggle(_showFinished, " only show finished instances", Styles.Toggle);

            IReadOnlyList<PevtEventInstance> instances = runtime.Host.Instances;
            Header("Retained instances (the host keeps the last "
                   + PevtEventHost.FinishedHistoryLimit + " finished ones)");

            // 新的在上：排查刚出的问题时最想看的就是最后一次。
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                PevtEventInstance instance = instances[i];
                if (_showFinished && !instance.IsFinished)
                    continue;

                GUILayout.Label(
                    $"{instance.EventId}#{instance.Id}  {instance.Status}  steps={instance.TotalSteps}"
                    + (instance.CompletedFrame >= 0 ? "  finished at frame " + instance.CompletedFrame : "  (running)"),
                    instance.Status == PevtExecutionStatus.Faulted ? Styles.Warning : Styles.Text);

                if (instance.Diagnostic != null)
                    GUILayout.Label("      " + OneLine(instance.Diagnostic.Describe()), Styles.Warning);
            }
        }

        // ---- 操作 ----

        private static void StartEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                _notice = "Type an event id first.";
                return;
            }

            try
            {
                PevtEventInstance instance = PolarisEventComponent.Start(eventId);
                _notice = "Started " + instance;
                _startId = eventId;
            }
            catch (Exception ex)
            {
                // 启动失败在调试页里是家常便饭（打错 ID、事件没注册），只报给作者看，不上报成错误。
                _notice = "Could not start `" + eventId + "`: " + ex.Message;
            }
        }

        private static void StopCurrent()
        {
            IReadOnlyList<Exception> failures = PolarisEventComponent.Stop();
            _notice = failures.Count == 0
                ? "Stopped."
                : "Stopped with " + failures.Count + " cleanup failure(s): " + failures[0].Message;
        }
    }
}
