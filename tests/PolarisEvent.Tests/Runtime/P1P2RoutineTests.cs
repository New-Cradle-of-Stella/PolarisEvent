using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Routines;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// P1（地图、实体、持久状态与进度）与 P2（Alice In Cradle 领域）组合处理器。
    /// </summary>
    public class P1P2RoutineTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            host.WithBuiltinRoutines();
            return host;
        }

        private static PevtExecutionResult Run(PevtTestHost host, string body, int maxFrames = 256) =>
            host.RunToCompletion(host.Start("id \"T\"\n" + body + "end\n"), maxFrames);

        private static PevtExecutionResult RunExpectingFault(PevtTestHost host, string body, string expectedId)
        {
            PevtExecutionResult result = Run(host, body);
            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal(expectedId, result.Diagnostic.Id);
            return result;
        }

        // ---- 登记完整性 ----

        [Fact]
        public void EveryDescriptorInTheCatalogHasAHandler()
        {
            PevtCommandRegistry registry = PevtBuiltinRoutines.CreateRegistry();

            List<CommandDescriptor> all = CommandDescriptorCatalog.Builtin.DeclaredDescriptors.ToList();

            Assert.NotEmpty(all);
            foreach (CommandDescriptor descriptor in all)
                Assert.True(registry.TryGetRoutine(descriptor, out _), $"`@{descriptor}` 没有登记处理器。");
        }

        [Fact]
        public void P1AndP2TablesCoverExactlyTheirOwnPriorities()
        {
            var p1 = new PevtCommandRegistry(CommandDescriptorCatalog.Builtin);
            P1CommandRoutines.RegisterAll(p1);

            var p2 = new PevtCommandRegistry(CommandDescriptorCatalog.Builtin);
            P2CommandRoutines.RegisterAll(p2);

            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.DeclaredDescriptors)
            {
                Assert.Equal(descriptor.Priority == CommandPriority.P1, p1.TryGetRoutine(descriptor, out _));
                Assert.Equal(descriptor.Priority == CommandPriority.P2, p2.TryGetRoutine(descriptor, out _));
            }
        }

        // ---- 地图 ----

        [Fact]
        public void RequireMapContinuesOnlyOnTheRequiredMap()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host,
                "@require_map(\"town\")\n" +
                "@flag_set(\"global\", \"after_require\", true)\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.True(host.State.GetFlag("global", "after_require"));
        }

        [Fact]
        public void RequireMapFaultsAndStopsLaterCommandsOnMismatch()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = RunExpectingFault(host,
                "@require_map(\"cave\")\n" +
                "@flag_set(\"global\", \"after_require\", true)\n",
                "PEVTR4001");

            Assert.Contains("当前地图为 `town`", result.Diagnostic.Message);
            Assert.False(host.State.GetFlag("global", "after_require"));
        }

        [Fact]
        public void MapChangeRunsTheTransitionInOrderAndDropsTheAbortCleanup()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host, "@map_change(\"cave\", \"entrance\")\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Equal(
                new[]
                {
                    "ResolveMap(cave)", "ResolveAnchor(cave,entrance)", "BeginTransition()",
                    "ChangeMap(cave)", "WaitMapReady()", "PlacePlayer(entrance)", "EndTransition()",
                },
                host.World.Calls);
            Assert.Equal(0, host.World.TransitionDepth);
        }

        [Fact]
        public void MapChangeValidatesBothIdsBeforeTouchingTheWorld()
        {
            PevtTestHost host = Host();

            RunExpectingFault(host, "@map_change(\"cave\", \"nowhere\")\n", "PEVTR4001");

            Assert.DoesNotContain("BeginTransition()", host.World.Calls);
            Assert.DoesNotContain("ChangeMap(cave)", host.World.Calls);
        }

        /// <summary>
        /// 转场中途事件被取消：<c>AbortTransition</c> 必须跑到，否则地图会停在半切状态。
        /// </summary>
        [Fact]
        public void CancellingAnEventDuringAMapTransitionAbortsIt()
        {
            PevtTestHost host = Host();
            host.World.MapNeverReady = true;

            PevtExecution execution = host.Start("id \"T\"\n@map_change(\"cave\", \"entrance\")\nend\n");
            host.Step(execution);

            Assert.Equal(1, host.World.TransitionDepth);

            execution.Cancel();

            Assert.Contains("AbortTransition()", host.World.Calls);
            Assert.Equal(0, host.World.TransitionDepth);
        }

        [Fact]
        public void MapLayerLoadReturnsFalseForAnUnknownLayerWithoutLoadingAnything()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host, "var ok : bool = @map_layer_load(\"nope\")\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.DoesNotContain("LoadLayer(nope)", host.World.Calls);
        }

        [Fact]
        public void WeatherAndRefreshModesMustBeRegistered()
        {
            RunExpectingFault(Host(), "@weather_set(\"blizzard\", 0)\n", "PEVTR4001");
            RunExpectingFault(Host(), "@map_refresh(\"whatever\")\n", "PEVTR4001");
        }

        // ---- 实体 ----

        [Fact]
        public void EntityOperationsReturnFalseWhenTheEntityIsGone()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host,
                "var a : bool = @entity_visible(\"ghost\", true)\n" +
                "var b : bool = @entity_pose(\"ghost\", \"sit\")\n" +
                "var c : bool = @entity_move_to(\"ghost\", \"gate\", 1.0)\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.DoesNotContain("SetVisible(ghost,True)", host.Entity.Calls);
            Assert.DoesNotContain("MoveTo(ghost,gate,1)", host.Entity.Calls);
        }

        [Fact]
        public void EntityMoveToReportsWhetherItArrived()
        {
            PevtTestHost host = Host();
            host.Entity.MoveArrives = false;

            PevtExecution execution = host.Start("id \"T\"\nvar ok : bool = @entity_move_to(\"npc_a\", \"gate\", 2.0)\nend\n");
            host.RunToCompletion(execution);

            Assert.False(execution.RootEnvironment.TryGetSlot("ok", out PevtSlot slot) ? slot.Value.AsBool : true);
        }

        [Fact]
        public void FollowIsStoppedWhenTheEventEnds()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start("id \"T\"\nvar ok : bool = @entity_follow(\"npc_a\", \"npc_b\", 1.0, 1.0)\nend\n");
            host.RunToCompletion(execution);

            Assert.Contains("StartFollow(npc_a,npc_b)", host.Entity.Calls);
            Assert.Contains("StopFollow(npc_a)", host.Entity.Calls);
        }

        [Fact]
        public void SpeedMustBePositive()
        {
            RunExpectingFault(Host(), "var ok : bool = @entity_move_to(\"npc_a\", \"gate\", 0.0)\n", "PEVTR4001");
        }

        // ---- 持久状态 ----

        [Fact]
        public void FlagsAndCountersRoundTripThroughTheStateService()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start(
                "id \"T\"\n" +
                "@flag_set(\"global\", \"met\", true)\n" +
                "var met : bool = @flag_get(\"global\", \"met\")\n" +
                "@counter_set(\"global\", \"n\", 3)\n" +
                "var n : int = @counter_add(\"global\", \"n\", 4)\n" +
                "var m : int = @counter_raise(\"global\", \"n\", 5)\n" +
                "end\n");
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("met", out PevtSlot met) && met.Value.AsBool);
            Assert.Equal(7, Slot(execution, "n").AsInt);
            Assert.Equal(7, Slot(execution, "m").AsInt);
            Assert.Equal(7, host.State.GetCounter("global", "n"));
        }

        [Fact]
        public void UnregisteredScopeIsRejectedBeforeAnyWrite()
        {
            PevtTestHost host = Host();

            RunExpectingFault(host, "@flag_set(\"secret\", \"k\", true)\n", "PEVTR4001");

            Assert.Empty(host.State.Flags);
        }

        /// <summary><c>counter_add</c> 使用 checked：溢出报 PEVTR2001 且不写入。</summary>
        [Fact]
        public void CounterAddUsesCheckedArithmetic()
        {
            PevtTestHost host = Host();
            host.State.Counters["global:n"] = int.MaxValue;

            RunExpectingFault(host, "var n : int = @counter_add(\"global\", \"n\", 1)\n", "PEVTR2001");

            Assert.Equal(int.MaxValue, host.State.GetCounter("global", "n"));
            Assert.DoesNotContain(host.State.Calls, call => call.StartsWith("SetCounter", StringComparison.Ordinal));
        }

        /// <summary>
        /// 持久状态不随事件失败回滚：物品已经加进去了，后面一条指令失败也不该把它变回去。
        /// </summary>
        [Fact]
        public void PersistentWritesSurviveALaterFailureInTheSameEvent()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host,
                "var c : int = @item_change(\"herb\", 2, 0, false)\n" +
                "@flag_set(\"secret\", \"k\", true)\n");

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal(2, host.Inventory.GetItemCount("herb"));
        }

        [Fact]
        public void ItemAndMoneyNotificationsOnlyFireWhenAsked()
        {
            PevtTestHost host = Host();

            Run(host,
                "var a : int = @item_change(\"herb\", 1, 0, true)\n" +
                "var b : int = @item_change(\"herb\", 1, 0, false)\n" +
                "var c : int = @money_change(10, true)\n");

            Assert.Equal(new[] { "NotifyItemChange(herb,1,1)", "NotifyMoneyChange(10,10)" },
                host.Stage.Calls.Where(call => call.StartsWith("Notify", StringComparison.Ordinal)).ToArray());
        }

        [Fact]
        public void UnknownItemsSkillsMagicsQuestsAndStoresAreRejected()
        {
            RunExpectingFault(Host(), "var c : int = @item_change(\"rock\", 1, 0, false)\n", "PEVTR4001");
            RunExpectingFault(Host(), "@skill_owned(\"fly\", true, false)\n", "PEVTR4001");
            RunExpectingFault(Host(), "@magic_owned(\"meteor\", true)\n", "PEVTR4001");
            RunExpectingFault(Host(), "@quest_set(\"q_none\", 1)\n", "PEVTR4001");
            RunExpectingFault(Host(), "@store_refresh(\"black_market\")\n", "PEVTR4001");
        }

        [Fact]
        public void AutosaveValidatesTheModeAndReportsTheResult()
        {
            PevtTestHost host = Host();
            host.State.AutosaveSucceeds = false;

            PevtExecution execution = host.Start("id \"T\"\nvar ok : bool = @autosave(\"bench\")\nend\n");
            host.RunToCompletion(execution);

            Assert.False(Slot(execution, "ok").AsBool);
            Assert.Contains("RequestAutosave(bench)", host.State.Calls);

            RunExpectingFault(Host(), "var ok : bool = @autosave(\"nope\")\n", "PEVTR4001");
        }

        [Fact]
        public void QuestFinishAndRemoveGoThroughResolveFirst()
        {
            PevtTestHost host = Host();

            Run(host, "@quest_set(\"q_main\", 2)\n@quest_finish(\"q_main\")\n@quest_remove(\"q_main\")\n");

            Assert.Equal(new[] { "SetStep(q_main,2)", "Finish(q_main)", "Remove(q_main)" }, host.Quest.Calls);
        }

        /// <summary>
        /// <c>@quest_status</c> 的三段规范化取值：未接取、阶段号原样透出、已完成。
        /// 阶段号不重新编号，所以 <c>@quest_set</c> 写进去的数就是 <c>@quest_status</c> 读回来的数。
        /// </summary>
        [Fact]
        public void QuestStatusNormalizesNotStartedPhaseAndFinished()
        {
            PevtTestHost host = Host();

            PevtExecution execution = host.Start(
                "id \"T\"\n" +
                "var before : int = @quest_status(\"q_main\")\n" +
                "@quest_set(\"q_main\", 3)\n" +
                "var during : int = @quest_status(\"q_main\")\n" +
                "@quest_finish(\"q_main\")\n" +
                "var after : int = @quest_status(\"q_main\")\n" +
                "end\n");
            host.RunToCompletion(execution);

            Assert.Equal(FakeQuest.NotStarted, Slot(execution, "before").AsInt);
            Assert.Equal(3, Slot(execution, "during").AsInt);
            Assert.Equal(FakeQuest.Finished, Slot(execution, "after").AsInt);
        }

        // ---- P2 ----

        [Fact]
        public void PlayerOutfitAndStatusReturnFalseForUnknownIds()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host,
                "var a : bool = @player_outfit(\"armor\")\n" +
                "var b : bool = @player_status_apply(\"burning\", 1, 30)\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Empty(host.Player.Calls);
        }

        [Fact]
        public void PlayerVoiceRequiresTheAudioResourceFirst()
        {
            PevtTestHost host = Host();

            Run(host, "@player_voice(\"pr_hey\")\n");

            Assert.Contains("Require(audio:voice:pr_hey)", host.Resources.Calls);
            Assert.Equal(new[] { "PlayVoice(pr_hey)" }, host.Player.Calls);
        }

        [Fact]
        public void CureModeMustBeRegistered()
        {
            RunExpectingFault(Host(), "@player_status_cure(\"magic\")\n", "PEVTR4001");
        }

        [Fact]
        public void BattleSummonerWaitsForTheTargetState()
        {
            PevtTestHost host = Host();
            host.Battle.SummonerReaches = false;

            PevtExecution execution = host.Start("id \"T\"\nvar ok : bool = @battle_summoner_active(\"s1\", true, false)\nend\n");
            host.RunToCompletion(execution);

            Assert.False(Slot(execution, "ok").AsBool);
            Assert.Equal(new[] { "SetSummonerActive(s1,True,False)" }, host.Battle.Calls);
        }

        [Fact]
        public void BattleEnemyActionReturnsFalseWhenTheEnemyIsGone()
        {
            PevtTestHost host = Host();

            PevtExecutionResult result = Run(host, "var ok : bool = @battle_enemy_action(\"e_ghost\", \"charge\")\n");

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);
            Assert.Empty(host.Battle.Calls);
        }

        [Fact]
        public void BattleMagicEffectMustBeRegistered()
        {
            RunExpectingFault(Host(), "@battle_magic_effect(\"nova\", true)\n", "PEVTR4001");
        }

        /// <summary>宿主没接领域服务时，P1/P2 调用报 PEVTR4001 而不是空引用崩溃。</summary>
        [Fact]
        public void MissingDomainServicesFailWithADiagnosticNotAnException()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            host.WithBuiltinRoutines();

            // 只有时钟与会话，一个领域服务都没接。
            var bare = new PevtServices(host.Clock, new PevtEventSession("T"));
            var execution = new PevtExecution(host.Compile("id \"T\"\n@darkness_set(true)\nend\n"), bare, host.Commands);

            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
        }

        private static PevtValue Slot(PevtExecution execution, string name)
        {
            Assert.True(execution.RootEnvironment.TryGetSlot(name, out PevtSlot slot), $"环境里没有 `{name}`。");
            return slot.Value;
        }
    }
}
