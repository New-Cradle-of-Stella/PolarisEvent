using Xunit;

namespace Polaris.Pevt.IntegrationTests
{
    /// <summary>
    /// 证明本项目能同时引用 PolarisCore（游戏侧宿主）与 PolarisEvent（PEVT 语言核心）并
    /// 正常构建、运行。真正的游戏接口替身与运行集成测试留给后续阶段。
    /// </summary>
    public class HostAssemblySmokeTests
    {
        [Fact]
        public void PolarisAssembly_IsReferencable()
        {
            var polarisType = typeof(global::Polaris.PolarisAPI);
            Assert.Equal("PolarisCore", polarisType.Assembly.GetName().Name);
        }

        [Fact]
        public void PevtCoreAssembly_IsReferencable()
        {
            var coreType = typeof(Polaris.Pevt.Text.SourceText);
            Assert.Equal("PolarisEvent", coreType.Assembly.GetName().Name);
        }

        [Fact]
        public void DiagnosticsAssembly_IsSeparateDll()
        {
            var diagnosticsType = typeof(global::Polaris.Diagnostics.PolarisDiagnosticsComponent);
            Assert.Equal("PolarisDiagnostics", diagnosticsType.Assembly.GetName().Name);
        }

        [Fact]
        public void DiagnosticsContract_IsOwnedByCore()
        {
            var contractType = typeof(global::Polaris.Diagnostics.FatalError);
            Assert.Equal("PolarisCore", contractType.Assembly.GetName().Name);
        }

        [Fact]
        public void BasicErrorCapture_IsOwnedByCore()
        {
            var coreAssembly = typeof(global::Polaris.PolarisAPI).Assembly;
            var captureType = coreAssembly.GetType("Polaris.Diagnostics.CoreErrorCapture", throwOnError: true);
            Assert.Equal("PolarisCore", captureType.Assembly.GetName().Name);
        }
    }
}
