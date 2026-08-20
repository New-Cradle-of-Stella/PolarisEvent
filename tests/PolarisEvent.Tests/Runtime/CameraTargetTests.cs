using System.Linq;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// PEVT-E02：<c>@camera_move</c> 的 <c>entity:</c> / <c>anchor:</c> 目标。
    /// 重点是三件事——旧的裸标签点写法继续可用、前缀拼错要报参数错而不是静默当标签点、
    /// 以及"目标不存在"与"目标中途消失"是两条可分辨的诊断。
    /// </summary>
    public class CameraTargetTests
    {
        private static PevtTestHost Host()
        {
            var host = new PevtTestHost();
            host.WithBuiltinRoutines();
            return host;
        }

        private static PevtExecutionResult Run(PevtTestHost host, string body, int maxFrames = 64) =>
            host.RunToCompletion(host.Start("id \"T\"\n" + body + "end\n"), maxFrames);

        private static string Move(string target, int frames = 0) =>
            $"@camera_move(\"{target}\", 0.0, 0.0, 1.0, {frames}, \"linear\")\n";

        // ---- 目标写法 ----

        [Theory]
        [InlineData("player", PevtCameraTargetKind.Player, "")]
        [InlineData("point", PevtCameraTargetKind.Point, "")]
        [InlineData("entity:cam_left", PevtCameraTargetKind.Entity, "cam_left")]
        [InlineData("anchor:forest_center", PevtCameraTargetKind.Anchor, "forest_center")]
        [InlineData("forest_center", PevtCameraTargetKind.Anchor, "forest_center")]
        public void TargetGrammarParsesIntoKindAndKey(string targetId, PevtCameraTargetKind kind, string key)
        {
            Assert.True(PevtCameraTarget.TryParse(targetId, out PevtCameraTarget target));
            Assert.Equal(kind, target.Kind);
            Assert.Equal(key, target.Key);
        }

        [Fact]
        public void BareLabelPointIsMarkedAsTheLegacyFormAndRoundTripsToTheExplicitOne()
        {
            Assert.True(PevtCameraTarget.TryParse("forest_center", out PevtCameraTarget bare));
            Assert.True(bare.IsLegacyAnchor);
            Assert.Equal("anchor:forest_center", bare.ToString());

            Assert.True(PevtCameraTarget.TryParse("anchor:forest_center", out PevtCameraTarget explicitForm));
            Assert.False(explicitForm.IsLegacyAnchor);
            Assert.Equal(bare.Key, explicitForm.Key);
            Assert.Equal(bare.Kind, explicitForm.Kind);
        }

        [Theory]
        [InlineData("")]
        [InlineData("entity:")]
        [InlineData("anchor:")]
        [InlineData("entities:foo")]
        [InlineData("Entity:foo")]
        [InlineData("foo:bar")]
        public void MalformedTargetsAreRejectedRatherThanTreatedAsLabelPoints(string targetId)
        {
            Assert.False(PevtCameraTarget.TryParse(targetId, out _));
        }

        [Theory]
        [InlineData("entities:foo")]
        [InlineData("entity:")]
        [InlineData("foo:bar")]
        public void AMistypedPrefixIsAnArgumentError(string targetId)
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = Run(host, Move(targetId));

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4001", result.Diagnostic.Id);
            Assert.Empty(host.Stage.Calls);
        }

        // ---- 兼容与转发 ----

        [Theory]
        [InlineData("player")]
        [InlineData("point")]
        [InlineData("forest_center")]
        [InlineData("anchor:forest_center")]
        [InlineData("entity:cam_left")]
        public void EveryWellFormedTargetReachesTheAdapterVerbatim(string targetId)
        {
            PevtTestHost host = Host();
            PevtExecutionResult result = Run(host, Move(targetId));

            Assert.Equal(PevtExecutionStatus.Completed, result.Status);

            // 适配器拿到的是原样的 targetId：前缀解析在 Core 与适配器里是同一份判定，
            // 组合不会先把它改写成别的形状再传下去。
            Assert.Contains($"CameraMoveTo({targetId})", host.Stage.Calls);
        }

        // ---- 失败路径 ----

        [Fact]
        public void ATargetThatDoesNotExistReportsCameraTargetNotFound()
        {
            PevtTestHost host = Host();
            host.Stage.UnresolvableCameraTargets.Add("entity:ghost");

            PevtExecutionResult result = Run(host, Move("entity:ghost"));

            Assert.Equal("PEVTR4601", result.Diagnostic.Id);

            // 解析失败必须发生在任何镜头改动之前。
            Assert.Empty(host.Stage.Calls);
        }

        [Fact]
        public void AnAnchorThatDoesNotExistReportsTheSameDiagnostic()
        {
            PevtTestHost host = Host();
            host.Stage.UnresolvableCameraTargets.Add("anchor:nowhere");

            Assert.Equal("PEVTR4601", Run(host, Move("anchor:nowhere")).Diagnostic.Id);
        }

        [Fact]
        public void AnEntityThatVanishesDuringTheMoveReportsCameraTargetLost()
        {
            PevtTestHost host = Host();
            host.Stage.VanishingCameraTargets.Add("entity:cam_left");

            PevtExecutionResult result = Run(host, Move("entity:cam_left", 4));

            Assert.Equal(PevtExecutionStatus.Faulted, result.Status);
            Assert.Equal("PEVTR4602", result.Diagnostic.Id);

            // 动作已经开始过：这不是"目标不存在"，而是"跟着的东西没了"。
            Assert.Contains("CameraMoveTo(entity:cam_left)", host.Stage.Calls);
        }

        // ---- 快照 ----

        [Fact]
        public void TheSnapshotIsRegisteredBeforeTheMoveAndRestoredEvenWhenTheTargetIsLost()
        {
            PevtTestHost host = Host();
            host.Stage.VanishingCameraTargets.Add("entity:cam_left");

            PevtExecution execution = host.Start("id \"T\"\n" + Move("entity:cam_left", 4) + "end\n");
            PevtExecutionResult result = host.RunToCompletion(execution);
            Assert.Equal("PEVTR4602", result.Diagnostic.Id);

            // 失败也要把镜头交回事件开始前的状态，而且只交一次。
            Assert.Equal(1, host.Stage.Calls.Count(c => c == "RestoreEventSnapshot()"));
        }

        [Fact]
        public void SeveralEntityTargetsStillShareTheSingleSnapshotRestore()
        {
            PevtTestHost host = Host();
            Run(host, Move("entity:a") + Move("entity:b") + Move("anchor:c"));

            Assert.Equal(1, host.Stage.Calls.Count(c => c == "RestoreEventSnapshot()"));
        }

        // ---- 描述目录 ----

        [Fact]
        public void TheTargetParameterCarriesTheCameraTargetDomain()
        {
            Assert.True(CommandDescriptorCatalog.Builtin.TryResolve(
                "camera_move",
                new[] { PevtType.String, PevtType.Float, PevtType.Float, PevtType.Float, PevtType.Int, PevtType.String },
                out CommandDescriptor descriptor));

            Assert.Same(ParameterDomain.CameraTarget, descriptor.Parameters[0].Domain);

            // 前缀是封闭集，但具体键是地图运行期事实，所以这个域不产生静态错误。
            Assert.False(ParameterDomain.CameraTarget.RejectsUnknownValues);
        }
    }
}
