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
    /// P0 组合处理器：原子方法顺序、参数域验证、失败路径不留半成品、事件临时状态进会话清理。
    /// 全程使用内存替身，验证"不接游戏 API 也能跑完整演出流程"。
    /// </summary>
    public class P0RoutineTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            P0CommandRoutines.RegisterAll(host.Commands);
            return host;
        }

        private static PevtExecutionResult Run(PevtTestHost host, string body, int maxFrames = 256) =>
            host.RunToCompletion(host.Start("id \"T\"\n" + body + "end\n"), maxFrames);

        private static void AssertFails(string body, string expectedId, Action<PevtTestHost> arrange = null)
        {
            PevtTestHost host = Host();
            arrange?.Invoke(host);

            PevtExecutionResult result = Run(host, body);

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal(expectedId, result.Diagnostic.Id);
        }

        // ---- 登记完整性 ----

        [Fact]
        public void EveryP0DescriptorHasAHandler()
        {
            PevtCommandRegistry registry = P0CommandRoutines.CreateRegistry();

            List<CommandDescriptor> p0 = CommandDescriptorCatalog.Builtin.DeclaredDescriptors
                .Where(descriptor => descriptor.Priority == CommandPriority.P0)
                .ToList();

            Assert.NotEmpty(p0);
            foreach (CommandDescriptor descriptor in p0)
                Assert.True(registry.TryGetRoutine(descriptor, out _), $"`@{descriptor}` 没有登记处理器。");
        }

        /// <summary>
        /// P0 那张表只挂 P0。功能阶段 F 之后 P1/P2 也有处理器了，但它们属于各自的表——
        /// 混进 P0 会让"只想要 P0 的宿主"悄悄拿到地图与存档能力。
        /// </summary>
        [Fact]
        public void P0RegistryContainsOnlyP0Descriptors()
        {
            PevtCommandRegistry registry = P0CommandRoutines.CreateRegistry();

            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.DeclaredDescriptors)
            {
                if (descriptor.Priority == CommandPriority.P0)
                    continue;

                Assert.False(registry.TryGetRoutine(descriptor, out _), $"`@{descriptor.Name}` 属于 {descriptor.Priority}，本阶段不应登记。");
            }
        }

        // ---- 对话 ----

        [Fact]
        public void SayResolvesTheActorThroughTheCatalogBeforeAnySideEffect()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@say(\"aic:noel\", \"早上好\")\nend\n");

            host.Step(execution);
            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            Assert.Equal(
                new[] { "SelectSpeaker(aic:noel)", "OpenText(早上好)", "WaitAdvance()", "CommitLog()" },
                host.Dialogue.Calls);
        }

        [Fact]
        public void Pevtr4401_SayWithAnUnknownActorProducesNoSideEffect()
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = Run(host, "@say(\"other.mod:ghost\", \"hi\")\n");

            Assert.Equal("PEVTR4401", result.Diagnostic.Id);
            Assert.Empty(host.Dialogue.Calls);
        }

        [Fact]
        public void NarrateClearsTheSpeaker()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@narrate(\"风穿过走廊。\")\nend\n");

            host.Step(execution);
            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            Assert.Equal(new[] { "ClearSpeaker()", "OpenText(风穿过走廊。)", "WaitAdvance()", "CommitLog()" }, host.Dialogue.Calls);
        }

        [Fact]
        public void BoardClosesEvenWhenTheEventIsCancelledWhileWaiting()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@board(\"告示\", \"plain\")\nend\n");

            host.Step(execution);
            Assert.Contains("OpenBoard(告示,plain)", host.Dialogue.Calls);
            Assert.DoesNotContain("CloseBoard()", host.Dialogue.Calls);

            execution.Cancel();

            Assert.Contains("CloseBoard()", host.Dialogue.Calls);
        }

        [Fact]
        public void TalkerBindIsRestoredWhenTheEventEnds()
        {
            PevtTestHost host = Host();
            Run(host, "@talker_bind(\"aic:noel\", \"？？？\", \"talk_x\")\n");

            Assert.Equal(new[] { "BindProfile(aic:noel)", "ResetProfile(aic:noel)" }, host.Dialogue.Calls);
        }

        [Fact]
        public void DialogueTemporaryStateIsRestoredOnCompletion()
        {
            PevtTestHost host = Host();
            Run(host, "@dialogue_visible(false, true)\n@dialogue_hold(true)\n@dialogue_log(false)\n");

            // 逆序恢复：最后设置的最先还原。
            Assert.Equal(
                new[] { "SetVisible(False)", "SetHold(True)", "SetLogEnabled(False)", "SetLogEnabled(True)", "SetHold(False)", "SetVisible(True)" },
                host.Dialogue.Calls);
        }

        [Fact]
        public void SkipEnabledGoesThroughTheInputCapabilityNotTheDialogueService()
        {
            PevtTestHost host = Host();
            Run(host, "@skip_enabled(false)\n");

            Assert.Equal(new[] { "SetCapability(skip,False)", "SetCapability(skip,True)" }, host.Stage.Calls);
            Assert.Empty(host.Dialogue.Calls);
        }

        // ---- 选择 ----

        [Theory]
        [InlineData("@choose(\"p\", \"a\", \"b\")", 2)]
        [InlineData("@choose(\"p\", \"a\", \"b\", \"c\")", 3)]
        [InlineData("@choose(\"p\", \"a\", \"b\", \"c\", \"d\")", 4)]
        public void ChooseOverloadsShareOneRoutineAndReturnAOneBasedIndex(string call, int optionCount)
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start($"id \"T\"\nvar n : int = {call}\nend\n");

            host.Step(execution);
            host.Choice.PresentSignal.Signal(optionCount);
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("n", out PevtSlot slot));
            Assert.Equal(optionCount, slot.Value.AsInt);
            Assert.Equal(optionCount, host.Choice.Calls.Count(call2 => call2.StartsWith("AddIndex", StringComparison.Ordinal)));
            Assert.Equal("Reset()", host.Choice.Calls.First());
            Assert.Equal("Reset()", host.Choice.Calls.Last());
        }

        [Fact]
        public void ChooseResetsTheBuilderEvenWhenTheEventIsCancelledMidSelection()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nvar n : int = @choose(\"p\", \"a\", \"b\")\nend\n");

            host.Step(execution);
            Assert.Equal(1, host.Choice.Calls.Count(c => c == "Reset()"));

            execution.Cancel();

            // 取消路径也必须 Reset，否则残留选项会泄漏到下一次选择。
            Assert.Equal(2, host.Choice.Calls.Count(c => c == "Reset()"));
        }

        [Fact]
        public void ChoiceShowReturnsAStableKey()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start(
                "id \"T\"\n@choice_add(\"stay\", \"留下\", true, false)\nvar k : string = @choice_show(\"p\", \"stay\")\nend\n");

            host.Step(execution);
            host.Choice.PresentKeySignal.Signal("stay");
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("k", out PevtSlot slot));
            Assert.Equal("stay", slot.Value.AsString);
            Assert.Contains("Add(stay)", host.Choice.Calls);
        }

        [Fact]
        public void Pevtr3003_ChoiceShowWithANullKey()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nvar k : string = @choice_show(\"p\", \"i\")\nend\n");

            host.Step(execution);
            host.Choice.PresentKeySignal.Signal(null);
            PevtExecutionResult result = host.RunToCompletion(execution);

            Assert.Equal("PEVTR3003", result.Diagnostic.Id);
        }

        // ---- 人物演出 ----

        [Fact]
        public void ActorEnterValidatesEverythingBeforeTouchingThePortraitService()
        {
            PevtTestHost host = Host();
            Run(host, "@actor_enter(\"aic:noel\", \"left\", \"default\", 0)\n");

            Assert.Equal(new[] { "SetAppearance(aic:noel,default)", "Place(aic:noel,left,0)" }, host.Portrait.Calls);
            Assert.Contains("Require(portrait:aic:noel:default)", host.Resources.Calls);
        }

        [Fact]
        public void Pevtr4402_UnknownAnchorLeavesNoHalfCreatedLayer() =>
            AssertFails("@actor_enter(\"aic:noel\", \"nowhere\", \"default\", 1)\n", "PEVTR4402");

        [Fact]
        public void Pevtr4402_UnknownAppearance() =>
            AssertFails("@actor_enter(\"aic:noel\", \"left\", \"nonexistent\", 1)\n", "PEVTR4402");

        [Fact]
        public void Pevtr4403_MissingPortraitResource() =>
            AssertFails("@actor_enter(\"aic:noel\", \"left\", \"default\", 1)\n", "PEVTR4403",
                host => host.Resources.Missing.Add("portrait:aic:noel:default"));

        [Fact]
        public void ActorMoveAcceptsEveryBuiltinSemanticAnchor()
        {
            foreach (string anchor in BuiltinActorAnchors.All)
            {
                PevtTestHost host = Host();
                PevtExecutionResult result = Run(host, $"@actor_move(\"aic:noel\", \"{anchor}\", 0)\n");

                Assert.Equal(PevtExecutionStatus.Completed, result.Status);
                Assert.Contains($"Move(aic:noel,{anchor},0)", host.Portrait.Calls);
            }
        }

        [Fact]
        public void Pevtr4402_UnregisteredEmoteAndMotion()
        {
            AssertFails("@actor_emote(\"aic:noel\", \"unknown\")\n", "PEVTR4402");
            AssertFails("@actor_motion(\"aic:noel\", \"unknown\", 1)\n", "PEVTR4402");
        }

        [Fact]
        public void LegacyPersonKeysAreNotAcceptedAsPublicActorIds() =>
            AssertFails("@say(\"n\", \"hi\")\n", "PEVTR4401");

        // ---- 图层与画面 ----

        [Fact]
        public void ImageHideFadesToZeroThenHides()
        {
            PevtTestHost host = Host();
            Run(host, "@image_hide(\"layer\", 0)\n");

            Assert.Equal(new[] { "FadeTo(layer,0)", "SetVisible(layer,False)" }, host.Stage.Calls);
        }

        [Theory]
        [InlineData("@image_move(\"l\", 1.0, 2.0, -1, \"linear\")")]
        [InlineData("@image_move(\"l\", 1.0, 2.0, 1, \"bounce\")")]
        [InlineData("@image_fade(\"l\", 1.5, 1, \"linear\")")]
        [InlineData("@image_fade(\"l\", -0.1, 1, \"linear\")")]
        [InlineData("@image_tint(\"l\", \"not-a-color\", 1)")]
        [InlineData("@image_fill(\"l\", \"#GG0000\")")]
        [InlineData("@screen_fade(\"#000000\", 2.0, 1, \"linear\")")]
        [InlineData("@camera_move(\"t\", 0.0, 0.0, 0.0, 1, \"linear\")")]
        [InlineData("@sound_play(\"s\", 1.5, 1.0)")]
        [InlineData("@music_volume(2.0, 1)")]
        public void Pevtr4001_ArgumentDomainViolationsAreRejectedWithoutSideEffects(string call)
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = Run(host, call + "\n");

            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.Empty(host.Stage.Calls);
        }

        [Theory]
        [InlineData("linear")]
        [InlineData("ease_in")]
        [InlineData("ease_out")]
        [InlineData("ease_in_out")]
        public void EveryEasingFixedBySpecIsAccepted(string easing)
        {
            PevtTestHost host = Host();
            Assert.Equal(PevtExecutionStatus.Completed, Run(host, $"@image_move(\"l\", 1.0, 2.0, 0, \"{easing}\")\n").Status);
        }

        [Theory]
        [InlineData("#000000")]
        [InlineData("#FFFFFFFF")]
        public void ColorsAcceptBothSpecFormats(string color)
        {
            PevtTestHost host = Host();
            Assert.Equal(PevtExecutionStatus.Completed, Run(host, $"@image_fill(\"l\", \"{color}\")\n").Status);
        }

        [Fact]
        public void ScreenFadeReleasesTheMaskWhenItReachesZeroOpacity()
        {
            PevtTestHost host = Host();
            Run(host, "@screen_fade(\"#000000\", 0.0, 0, \"linear\")\n");

            Assert.Equal(
                new[] { "EnsureFadeLayer()", "SetFadeColor(#000000)", "ScreenFadeTo(0)", "ReleaseFadeLayer()", "ReleaseFadeLayer()" },
                host.Stage.Calls);
        }

        [Fact]
        public void ScreenFadeToOpaqueLeavesTheMaskForTheSessionToRestore()
        {
            // 后面跟一条等待，好在"渐变已完成、事件还没结束"这个中间态上观察遮罩。
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@screen_fade(\"#000000\", 1.0, 0, \"linear\")\n@wait(5)\nend\n");

            host.Step(execution);
            Assert.Contains("ScreenFadeTo(1)", host.Stage.Calls);
            Assert.DoesNotContain("ReleaseFadeLayer()", host.Stage.Calls);

            host.RunToCompletion(execution);
            Assert.Contains("ReleaseFadeLayer()", host.Stage.Calls);
        }

        [Fact]
        public void CameraSnapshotIsRestoredOnceEvenAfterSeveralCameraCommands()
        {
            PevtTestHost host = Host();
            Run(host, "@camera_shake(1.0, 0, 2.0)\n@camera_move(\"t\", 1.0, 1.0, 1.0, 0, \"linear\")\n");

            Assert.Equal(1, host.Stage.Calls.Count(c => c == "RestoreEventSnapshot()"));
        }

        [Fact]
        public void CameraSnapshotIsAlsoRestoredWhenTheEventIsCancelled()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@camera_shake(1.0, 100, 2.0)\nend\n");

            host.Step(execution);
            Assert.DoesNotContain("RestoreEventSnapshot()", host.Stage.Calls);

            execution.Cancel();
            Assert.Contains("RestoreEventSnapshot()", host.Stage.Calls);
        }

        [Fact]
        public void CgShowClosesTheSingleImageOnCancellation()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\n@cg_show(\"cg01\", \"说明\")\nend\n");

            host.Step(execution);
            Assert.Contains("OpenSingle(cg01)", host.Stage.Calls);

            execution.Cancel();
            Assert.Contains("CloseSingle()", host.Stage.Calls);
        }

        // ---- 音频、UI 与输入 ----

        [Fact]
        public void MusicPlayPreloadsWaitsThenReplaces()
        {
            PevtTestHost host = Host();
            Run(host, "@music_play(\"bgm_school\", 0)\n");

            Assert.Contains("Require(preload:music:bgm_school)", host.Resources.Calls);
            Assert.Contains("WaitAudio(bgm_school)", host.Resources.Calls);
            Assert.Contains("MusicReplace(bgm_school)", host.Stage.Calls);
        }

        [Fact]
        public void Pevtr4403_MusicThatNeverBecomesReady()
        {
            PevtTestHost host = Host();
            host.Resources.Missing.Add("preload:music:bgm_missing");

            PevtExecutionResult result = Run(host, "@music_play(\"bgm_missing\", 0)\n");

            Assert.Equal("PEVTR4403", result.Diagnostic.Id);
            Assert.DoesNotContain("MusicReplace(bgm_missing)", host.Stage.Calls);
        }

        [Fact]
        public void AmbienceAndUiTemporaryStateAreRestoredInReverseOrder()
        {
            PevtTestHost host = Host();
            Run(host, "@ambience_play(\"rain\", 1.0, 0)\n@ui_visible(false)\n@letterbox_visible(true, 0)\n");

            List<string> restores = host.Stage.Calls
                .SkipWhile(c => c != "SetLetterboxVisible(True)")
                .Skip(1)
                .ToList();

            Assert.Equal(new[] { "SetLetterboxVisible(False)", "SetGlobalVisible(True)", "StopAmbience()" }, restores);
        }

        [Fact]
        public void InputCapabilitiesAreValidatedAndRestored()
        {
            PevtTestHost host = Host();
            Run(host, "@input_enabled(\"skip\", false)\n");

            Assert.Equal(new[] { "SetCapability(skip,False)", "SetCapability(skip,True)" }, host.Stage.Calls);
        }

        [Fact]
        public void Pevtr4001_UnregisteredInputCapability() =>
            AssertFails("@input_enabled(\"teleport\", false)\n", "PEVTR4001");

        [Fact]
        public void Pevtr4001_UnknownAudioKind() =>
            AssertFails("var ok : bool = @audio_preload(\"a\", \"video\")\n", "PEVTR4001");

        [Fact]
        public void WaitResourcesReportsWhetherTheGroupBecameReady()
        {
            PevtTestHost host = Host();
            PevtExecution execution = host.Start("id \"T\"\nvar ok : bool = @wait_resources(\"grp\", 0)\nend\n");
            host.RunToCompletion(execution);

            Assert.True(execution.RootEnvironment.TryGetSlot("ok", out PevtSlot slot));
            Assert.True(slot.Value.AsBool);
        }

        [Fact]
        public void Pevtr4403_UnresolvableResourceGroup()
        {
            PevtTestHost host = Host();
            host.Resources.Missing.Add("grp");

            PevtExecutionResult result = Run(host, "var ok : bool = @wait_resources(\"grp\", 0)\n");

            Assert.Equal("PEVTR4403", result.Diagnostic.Id);
        }
    }
}
