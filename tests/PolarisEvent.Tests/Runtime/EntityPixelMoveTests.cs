using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// PEVT-E03：按像素和帧移动场景实体。
    /// 这些用例盯住"帧数是目标持续时间而不是速度"、"失败按既有实体契约回报 bool"、
    /// 以及 <c>_start</c> 变体由描述目录自动派生这三件事。
    /// </summary>
    public class EntityPixelMoveTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost();
            host.WithBuiltinRoutines();
            return host;
        }

        private static PevtExecutionResult Run(PevtTestHost host, string body, int maxFrames = 64) =>
            host.RunToCompletion(host.Start("id \"T\"\n" + body + "end\n"), maxFrames);

        private static bool ResultOf(PevtTestHost host, string call)
        {
            PevtExecution execution = host.Start("id \"T\"\nvar v : bool = " + call + "\nend\n");
            Assert.Equal(PevtExecutionStatus.Completed, host.RunToCompletion(execution).Status);
            Assert.True(execution.RootEnvironment.TryGetSlot("v", out PevtSlot slot));
            return slot.Value.AsBool;
        }

        // ---- 描述目录 ----

        [Fact]
        public void BothCommandsAreParallelAndGetADerivedStartVariant()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            Assert.True(catalog.TryResolve(
                "entity_move_by_pixels",
                new[] { PevtType.String, PevtType.Float, PevtType.Float, PevtType.Int },
                out CommandDescriptor byPixels));

            Assert.True(catalog.TryResolve(
                "entity_move_to_offset",
                new[] { PevtType.String, PevtType.String, PevtType.Float, PevtType.Float, PevtType.Int },
                out CommandDescriptor toOffset));

            foreach (CommandDescriptor descriptor in new[] { byPixels, toOffset })
            {
                Assert.Equal(CommandWaitKind.WaitParallel, descriptor.WaitKind);
                Assert.Equal(PevtType.Bool, descriptor.ReturnType);
                Assert.Equal(CommandPriority.P1, descriptor.Priority);
                Assert.Equal("entity.move", descriptor.Capability);

                // `_start` 别名由可并行条目自动派生，不是手写登记的第二条 API。
                Assert.Equal(descriptor.Name + "_start", descriptor.StartName);
                Assert.Single(catalog.Find(descriptor.StartName));
                Assert.True(catalog.Find(descriptor.StartName)[0].IsAsync);
                Assert.Same(descriptor, catalog.Find(descriptor.StartName)[0].ParallelSource);
            }
        }

        [Fact]
        public void ThereIsStillNoMoveScriptShapedEntityCommand()
        {
            CommandDescriptorCatalog catalog = CommandDescriptorCatalog.Builtin;

            // 计划明确禁止再加一条接受 MoveScript 文本的入口。
            foreach (string name in new[] { "entity_action_raw", "entity_move_raw", "entity_script" })
                Assert.Empty(catalog.Find(name));
        }

        // ---- 参数转发 ----

        [Fact]
        public void PixelsAndFramesReachTheAdapterUnchanged()
        {
            PevtTestHost host = Host();
            Run(host, "@entity_move_by_pixels(\"npc_a\", -120.0, 48.5, 30)\n");

            Assert.Contains("MoveByPixels(npc_a,-120,48.5,30)", host.Entity.Calls);
        }

        [Fact]
        public void TheReferenceTargetIsResolvedWithTheSameTargetRulesAsEntityMoveTo()
        {
            PevtTestHost host = Host();
            Run(host, "@entity_move_to_offset(\"npc_a\", \"gate\", 8.0, 0.0, 10)\n");

            Assert.Contains("MoveToOffset(npc_a,gate,8,0,10)", host.Entity.Calls);
        }

        // ---- frames 是持续时间 ----

        [Fact]
        public void ZeroFramesLandsWithinTheSameFrame()
        {
            PevtTestHost host = Host();
            host.Entity.PixelMoveTakesFrames = true;

            PevtExecution execution = host.Start("id \"T\"\n@entity_move_by_pixels(\"npc_a\", 10.0, 0.0, 0)\nend\n");

            host.Step(execution);
            Assert.True(execution.IsFinished);
        }

        [Fact]
        public void FramesIsADurationNotASpeed()
        {
            PevtTestHost host = Host();
            host.Entity.PixelMoveTakesFrames = true;

            PevtExecution execution = host.Start("id \"T\"\n@entity_move_by_pixels(\"npc_a\", 10.0, 0.0, 5)\nend\n");

            // 同一段位移写 5 帧就必须占满 5 帧：位移长度不参与计时。
            for (int i = 0; i < 5; i++)
            {
                host.Step(execution);
                Assert.False(execution.IsFinished, $"第 {i} 帧就结束了。");
            }

            host.Step(execution);
            Assert.True(execution.IsFinished);
        }

        [Fact]
        public void TheStartVariantReturnsAHandlerImmediatelyAndIsAwaitable()
        {
            PevtTestHost host = Host();
            host.Entity.PixelMoveTakesFrames = true;

            PevtExecution execution = host.Start(
                "id \"T\"\nenable async\n"
                + "handler h = @entity_move_by_pixels_start(\"npc_a\", 10.0, 0.0, 3)\n"
                + "var done : bool = await h\n"
                + "end\n");

            PevtExecutionResult result = host.RunToCompletion(execution);
            Assert.Equal(PevtExecutionStatus.Completed, result.Status);

            Assert.True(execution.RootEnvironment.TryGetSlot("done", out PevtSlot slot));
            Assert.True(slot.Value.AsBool);
        }

        [Fact]
        public void OwnedMotionsIncludeThePixelMoveSoWaitMotionCanWaitForIt()
        {
            PevtTestHost host = Host();
            host.Entity.PixelMoveTakesFrames = true;

            // FakeClock 的受管动作聚合等待由测试直接控制；这里验证的是"并行位移仍在跑时
            // @wait_motion 会挂住"，而不是替身内部怎么记票据。
            host.Clock.MotionsFinished = false;

            PevtExecution execution = host.Start(
                "id \"T\"\nenable async\n"
                + "handler h = @entity_move_by_pixels_start(\"npc_a\", 10.0, 0.0, 2)\n"
                + "var ok : bool = @wait_motion(0)\n"
                + "end\n");

            for (int i = 0; i < 5; i++)
                host.Step(execution);

            Assert.False(execution.IsFinished);

            host.Clock.MotionsFinished = true;
            Assert.Equal(PevtExecutionStatus.Completed, host.RunToCompletion(execution).Status);
        }

        // ---- 失败：按既有实体契约回报 bool ----

        [Fact]
        public void AMissingEntityReturnsFalseWithoutTouchingTheAdapter()
        {
            PevtTestHost host = Host();

            Assert.False(ResultOf(host, "@entity_move_by_pixels(\"ghost\", 1.0, 0.0, 1)"));
            Assert.DoesNotContain(host.Entity.Calls, c => c.StartsWith("MoveByPixels"));
        }

        [Fact]
        public void AMissingReferenceTargetReturnsFalseWithoutTouchingTheAdapter()
        {
            PevtTestHost host = Host();

            Assert.False(ResultOf(host, "@entity_move_to_offset(\"npc_a\", \"nowhere\", 1.0, 0.0, 1)"));
            Assert.DoesNotContain(host.Entity.Calls, c => c.StartsWith("MoveToOffset"));
        }

        [Fact]
        public void BeingBlockedOrLosingTheEntityMidMoveReturnsFalseRatherThanFaulting()
        {
            PevtTestHost host = Host();
            host.Entity.PixelMoveCompletes = false;

            // 碰撞挡住、落脚失败和实体中途消失在契约上是同一件事：位移没走完，返回 false。
            Assert.False(ResultOf(host, "@entity_move_by_pixels(\"npc_a\", 200.0, 0.0, 4)"));
            Assert.False(ResultOf(host, "@entity_move_to_offset(\"npc_a\", \"npc_b\", 200.0, 0.0, 4)"));
        }

        [Fact]
        public void ACompletedMoveReturnsTrue()
        {
            PevtTestHost host = Host();

            Assert.True(ResultOf(host, "@entity_move_by_pixels(\"npc_a\", 12.0, -4.0, 2)"));
            Assert.True(ResultOf(host, "@entity_move_to_offset(\"npc_a\", \"npc_b\", 12.0, -4.0, 2)"));
        }

        // ---- 参数域 ----

        [Theory]
        [InlineData("@entity_move_by_pixels(\"npc_a\", 1.0, 0.0, -1)")]
        [InlineData("@entity_move_to_offset(\"npc_a\", \"gate\", 1.0, 0.0, -1)")]
        [InlineData("@entity_move_by_pixels(\"\", 1.0, 0.0, 1)")]
        public void ArgumentDomainViolationsAreRejectedWithoutSideEffects(string call)
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = Run(host, call + "\n");

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.DoesNotContain(host.Entity.Calls, c => c.StartsWith("MoveByPixels") || c.StartsWith("MoveToOffset"));
        }
    }
}
