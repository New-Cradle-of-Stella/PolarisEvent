using System;
using System.Collections.Generic;
using Polaris.Pevt.Actors;
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
        private static bool _queryFailuresOnly;

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

            IReadOnlyCollection<string> declaredGroups = runtime.DeclaredResourceGroupIds;
            if (declaredGroups.Count > 0)
            {
                Row("Declared resource groups", declaredGroups.Count.ToString());
                foreach (string groupId in declaredGroups)
                {
                    Dim("    " + groupId);
                    foreach (string member in runtime.DescribeDeclaredResourceGroup(groupId))
                        Dim("        " + member);
                }
            }

            Row("Camera follow", runtime.CameraFollowKey ?? "(not following a map entity)");
            if (runtime.CameraLostFollowKey != null)
                GUILayout.Label("      follow target lost: " + runtime.CameraLostFollowKey
                                + " — camera handed back to the event snapshot", Styles.Warning);

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

            DrawSchedules(execution);
        }

        /// <summary>
        /// PEVT-E05：尚未触发的 <c>schedule</c> 项。这些还没有对应的协程——它们只是"排好队等着"，
        /// 所以单独列一段，而不是硬塞进上面的协程列表。
        /// </summary>
        private static void DrawSchedules(PevtExecution execution)
        {
            IReadOnlyList<PevtScheduledItem> schedules = execution.Schedules;
            if (schedules.Count == 0)
                return;

            long currentFrame = Runtime?.Frame ?? 0;

            Header(schedules.Count + " pending schedule(s)");
            foreach (PevtScheduledItem item in schedules)
            {
                long remaining = item.DueFrame - currentFrame;
                Line($"      #{item.TimelineId}  _{item.Block.Name}()  due in {(remaining > 0 ? remaining.ToString() : "0")} frame(s)");
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

            DrawActorExtensions();
        }

        /// <summary>
        /// PEVT-E06：人物目录扩展的来源与所有权。列的顺序就是应用顺序（`#order`），卸载按逆序进行，
        /// 所以这份列表同时也是"卸载会先撤掉哪一条"的答案。
        /// </summary>
        private static void DrawActorExtensions()
        {
            PevtActorRegistry actors = Runtime?.Registry?.Actors;
            if (actors == null)
                return;

            IReadOnlyList<ActorExtensionRecord> extensions = actors.Extensions;
            IReadOnlyList<Polaris.Pevt.Diagnostics.Diagnostic> rejected = actors.ExtensionDiagnostics;

            if (extensions.Count == 0 && rejected.Count == 0)
                return;

            Header("Actor catalog extensions (applied in this order; unloading reverses it)");
            foreach (ActorExtensionRecord record in extensions)
                Line("  " + record.Describe());

            if (rejected.Count == 0)
                return;

            Header("Rejected extension content");
            foreach (Polaris.Pevt.Diagnostics.Diagnostic diagnostic in rejected)
                GUILayout.Label("  " + diagnostic.Id + ": " + OneLine(diagnostic.Message), Styles.Warning);
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

            DrawExtendedActorSources();
        }

        /// <summary>
        /// PEVT-E06：每个 appearance 是来自基础 `.pactor` 还是某个扩展。
        /// 只列被扩展过的人物——没被扩展的人物这一栏永远是"基础目录"，铺开来只会淹没有用信息。
        /// </summary>
        private static void DrawExtendedActorSources()
        {
            PevtActorRegistry actors = Runtime?.Registry?.Actors;
            if (actors == null)
                return;

            var extended = new List<ActorRegistration>();
            foreach (ActorRegistration registration in actors.Directory.Actors)
            {
                if (registration.Extensions.Count > 0)
                    extended.Add(registration);
            }

            if (extended.Count == 0)
                return;

            Header("Extended actors — where each appearance comes from");
            foreach (ActorRegistration registration in extended)
            {
                Line("  " + registration.ActorId + "  (base: " + registration.Catalog.SourcePath + ")");
                foreach (ActorAppearance appearance in registration.Actor.Appearances)
                {
                    string origin = registration.TryGetAppearanceSource(appearance.Id, out ActorExtensionRecord record)
                        ? "extension #" + record.Order + " " + record.Owner + " " + record.SourcePath
                        : "base catalog";
                    Dim("      " + appearance.Id + "  <-  " + origin);
                }
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
                GUILayout.Label(
                    (candidate.Origin == PevtEventOrigin.External ? "[external] " : string.Empty)
                    + candidate.Owner + " : " + candidate.SourcePath,
                    Styles.Dim);
                GUILayout.EndHorizontal();
            }

            IReadOnlyList<PevtEventOverride> overrides = events.Overrides;
            if (overrides.Count > 0)
            {
                Header(overrides.Count + " event(s) overridden by an external import");
                foreach (PevtEventOverride entry in overrides)
                    GUILayout.Label(OneLine(entry.Describe()), Styles.Text);
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

        // ---- Queries ----

        /// <summary>
        /// PEVT-E01 的只读查询记录。展示键、参数、目标类型、原始结果与失败原因——
        /// "读到了什么"和"为什么用不了"是两件事，转换失败时两条记录都在，因此这里不合并它们。
        /// </summary>
        private static void DrawQueries()
        {
            PevtGameQueryLog log = PevtGameQueryLog.Shared;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear log", Styles.Button, GUILayout.Width(110f)))
                log.Clear();
            _queryFailuresOnly = GUILayout.Toggle(_queryFailuresOnly, " only show failures", Styles.Toggle);
            GUILayout.EndHorizontal();

            Header(log.TotalCount + " query(ies) since load, " + log.FailureCount + " failed"
                   + "  (keeping the last " + PevtGameQueryLog.Capacity + ")");

            IReadOnlyList<PevtGameQueryTrace> recent = log.Recent;
            if (recent.Count == 0)
            {
                Dim("No `@game_read_*` call has run yet.");
                return;
            }

            // 新的在上：排查刚出的问题时最想看的就是最后一次。
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                PevtGameQueryTrace trace = recent[i];
                if (_queryFailuresOnly && trace.IsSuccess)
                    continue;

                GUILayout.Label(
                    "frame " + trace.Frame + "  "
                    + (string.IsNullOrEmpty(trace.EventId) ? "(no event)" : trace.EventId) + "  " + trace.Call,
                    trace.IsSuccess ? Styles.Text : Styles.Warning);

                Dim("      key: " + trace.Key
                    + "   args: " + (trace.Arguments.Count == 0 ? "(none)" : string.Join(", ", trace.Arguments))
                    + "   as: " + trace.TargetType.DisplayName());

                Dim("      raw result: " + (trace.Value.HasValue ? trace.Value.Value.Describe() : "(none — key not resolved)"));

                if (!trace.IsSuccess)
                    GUILayout.Label("      " + trace.DiagnosticId + ": " + OneLine(trace.Failure), Styles.Warning);
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
