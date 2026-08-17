using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Diagnostics;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// `.pactor` 严格读取器的逐编号测试：每个 PEVT91xx 至少有一个直接断言，
    /// 并且都配一个不会误报同一编号的合法邻近样例，避免读取器靠"一律报错"通过。
    /// </summary>
    public class ActorCatalogReaderTests
    {
        private const string ValidExternal = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""example.mod"">
  <Actor Id=""iris"" DisplayName=""Iris"" DisplayKey=""Talker_Iris"" Voice=""talk_iris"" Color=""#DCCAE7""
         Icon=""MyMod.Resources.IrisIcon"" DefaultPortrait=""default"">
    <WorldSprite Provider=""polaris-res"" Resource=""MyMod.Resources.IrisWorldPxls"" />
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""MyMod.Resources.IrisPortraitPxls"" />
    <UiPortrait Id=""default"" Provider=""polaris-res"" Resource=""MyMod.Resources.IrisUiImage"" />
    <Appearance Id=""neutral"" Portrait=""default"" Pose=""stand"" Frame=""neutral"" />
    <Anchor Id=""stage-left"" X=""-3.5"" Y=""0"" EnterX=""-8"" EnterY=""0"" />
  </Actor>
</ActorCatalog>";

        private static ActorCatalogReadResult ReadExternal(string xml) =>
            ActorCatalogReader.ReadText(xml, "Actors/Test.pactor", ActorCatalogSourceKind.External);

        private static ActorCatalogReadResult ReadBuiltIn(string xml) =>
            ActorCatalogReader.ReadText(xml, "Actors/Test.pactor", ActorCatalogSourceKind.BuiltIn);

        private static void AssertDiagnostic(ActorCatalogReadResult result, string expectedId)
        {
            Assert.Null(result.Catalog);
            Assert.Contains(result.Diagnostics, d => d.Id == expectedId);
        }

        private static void AssertNoDiagnostic(ActorCatalogReadResult result, string unexpectedId) =>
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == unexpectedId);

        [Fact]
        public void ValidExternalCatalog_ReadsWithoutDiagnostics()
        {
            ActorCatalogReadResult result = ReadExternal(ValidExternal);

            Assert.Empty(result.Diagnostics);
            Assert.True(result.Success);

            ActorCatalog catalog = result.Catalog;
            Assert.Equal("example.mod", catalog.Namespace);
            Assert.Equal(1, catalog.Version);
            Assert.False(catalog.IsBuiltIn);

            ActorDefinition iris = Assert.Single(catalog.Actors);
            Assert.Equal("iris", iris.LocalId);
            Assert.Equal("example.mod:iris", catalog.GetActorId(iris));
            Assert.Equal("Talker_Iris", iris.DisplayKey);
            Assert.Equal("Iris", iris.DisplayName);
            Assert.Equal("talk_iris", iris.Voice);
            Assert.Equal(new ActorColor(0xDC, 0xCA, 0xE7, 0xFF), iris.Color);
            Assert.Equal("MyMod.Resources.IrisIcon", iris.Icon.FieldReference);
            Assert.Equal(ActorVisualProvider.PolarisRes, iris.WorldSprite.Resource.Provider);
            Assert.Equal("default", iris.DefaultPortraitId);
            Assert.Equal("default", iris.DefaultPortrait.Id);
            Assert.Null(iris.LegacyPerson);

            ActorAppearance appearance = Assert.Single(iris.Appearances);
            Assert.Equal("neutral", appearance.Id);
            Assert.Equal("stand", appearance.Pose);

            ActorAnchor anchor = Assert.Single(iris.Anchors);
            Assert.Equal(-3.5f, anchor.X);
            Assert.Equal(-8f, anchor.EnterX);
        }

        [Fact]
        public void ValidCatalog_RecordsSourceLocationsForTooling()
        {
            ActorCatalogReadResult result = ReadExternal(ValidExternal);
            ActorDefinition iris = result.Catalog.Actors[0];

            Assert.Equal(3, iris.Location.StartLine);
            Assert.Equal(6, iris.Portraits[0].Location.StartLine);
            Assert.Equal("Actors/Test.pactor", iris.Location.FilePath);
        }

        [Theory]
        [InlineData("<ActorCatalog xmlns=\"urn:polaris:pevt:actors:v1\" Version=\"1\" Namespace=\"a\">")] // 未闭合
        [InlineData("<Actors xmlns=\"urn:polaris:pevt:actors:v1\" Version=\"1\" Namespace=\"a\" />")] // 根元素错误
        [InlineData("<ActorCatalog xmlns=\"urn:polaris:pevt:actors:v2\" Version=\"1\" Namespace=\"a\" />")] // XML 命名空间错误
        [InlineData("<ActorCatalog Version=\"1\" Namespace=\"a\" />")] // 无命名空间
        public void Pevt9101_RejectsInvalidXmlRootOrNamespace(string xml) =>
            AssertDiagnostic(ReadExternal(xml), "PEVT9101");

        [Fact]
        public void Pevt9101_ProhibitsDtdAndExternalEntities()
        {
            const string xml = @"<?xml version=""1.0""?>
<!DOCTYPE ActorCatalog [ <!ENTITY steal SYSTEM ""file:///c:/windows/win.ini""> ]>
<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""evil.mod"">
  <Actor Id=""x"" DisplayName=""&steal;"" />
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(xml), "PEVT9101");
        }

        [Fact]
        public void Pevt9101_RejectsUnknownElementsAndAttributes()
        {
            const string unknownElement = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X""><Script Type=""System.Diagnostics.Process"" Method=""Start"" /></Actor>
</ActorCatalog>";
            const string unknownAttribute = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" OnEnter=""MyMod.Hooks.Run"" />
</ActorCatalog>";
            const string bodyText = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">TALKER n</Actor>
</ActorCatalog>";
            const string cdata = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X""><![CDATA[PIC a_3]]></Actor>
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(unknownElement), "PEVT9101");
            AssertDiagnostic(ReadExternal(unknownAttribute), "PEVT9101");
            AssertDiagnostic(ReadExternal(bodyText), "PEVT9101");
            AssertDiagnostic(ReadExternal(cdata), "PEVT9101");
        }

        [Fact]
        public void Pevt9101_AcceptsCommentsAndWhitespaceInValidCatalog()
        {
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <!-- 说明性注释不是执行性内容 -->
  <Actor Id=""x"" DisplayName=""X"" />
</ActorCatalog>";

            ActorCatalogReadResult result = ReadExternal(xml);
            Assert.Empty(result.Diagnostics);
            Assert.True(result.Success);
        }

        [Theory]
        [InlineData("Version=\"2\"")]
        [InlineData("Version=\"0\"")]
        [InlineData("Version=\"1.0\"")]
        [InlineData("Version=\"\"")]
        [InlineData("")]
        public void Pevt9102_RejectsUnsupportedVersion(string versionAttribute)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" {versionAttribute} Namespace=""a"" />";
            AssertDiagnostic(ReadExternal(xml), "PEVT9102");
        }

        [Theory]
        [InlineData("Example.Mod")] // 大写
        [InlineData("1mod")] // 数字开头
        [InlineData("mod..sub")] // 空段
        [InlineData("mod-name")] // 连字符
        [InlineData("")]
        public void Pevt9103_RejectsInvalidNamespace(string ns)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""{ns}"" />";
            AssertDiagnostic(ReadExternal(xml), "PEVT9103");
        }

        [Theory]
        [InlineData("a")]
        [InlineData("example.mod")]
        [InlineData("a.b.c_1")]
        public void Pevt9103_AcceptsValidNamespace(string ns)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""{ns}"" />";
            ActorCatalogReadResult result = ReadExternal(xml);

            Assert.True(result.Success);
            Assert.Equal(ns, result.Catalog.Namespace);
        }

        [Fact]
        public void Pevt9104_RejectsExternalCatalogClaimingBuiltInOrAicNamespace()
        {
            const string forgedBuiltIn = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""evil.mod"" BuiltIn=""true"" />";
            const string forgedNamespace = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""aic"" />";

            AssertDiagnostic(ReadExternal(forgedBuiltIn), "PEVT9104");
            AssertDiagnostic(ReadExternal(forgedNamespace), "PEVT9104");

            // 同样两份内容由可信来源读取时不得报 PEVT9104。
            Assert.True(ReadBuiltIn(forgedBuiltIn).Success);
            Assert.True(ReadBuiltIn(forgedNamespace).Success);
        }

        [Theory]
        [InlineData("Noel")] // 大写开头
        [InlineData("1st")] // 数字开头
        [InlineData("noel father")] // 空格
        [InlineData("noel:father")] // 命名空间分隔符
        [InlineData("noel-")] // 末尾连字符
        [InlineData("")]
        public void Pevt9105_RejectsInvalidActorLocalId(string id)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""{id}"" DisplayName=""X"" />
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9105");
        }

        [Theory]
        [InlineData("noel")]
        [InlineData("noel-father")]
        [InlineData("first-human")]
        [InlineData("a")]
        [InlineData("x_1")]
        public void Pevt9105_AcceptsValidActorLocalId(string id)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""{id}"" DisplayName=""X"" />
</ActorCatalog>";
            ActorCatalogReadResult result = ReadExternal(xml);

            Assert.True(result.Success);
            Assert.Equal(id, result.Catalog.Actors[0].LocalId);
        }

        [Fact]
        public void Pevt9106_RejectsDuplicateActorIdInSameCatalog()
        {
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""iris"" DisplayName=""Iris"" />
  <Actor Id=""iris"" DisplayName=""Iris 2"" />
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(xml), "PEVT9106");
        }

        [Fact]
        public void Pevt9106_TreatsCaseDifferenceAsDistinctIdsButRejectsUppercase()
        {
            // 比较是序数的，但局部 ID 规则本身禁止大写，因此 Iris 只会命中 PEVT9105 而不是 PEVT9106。
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""iris"" DisplayName=""Iris"" />
  <Actor Id=""Iris"" DisplayName=""Iris 2"" />
</ActorCatalog>";

            ActorCatalogReadResult result = ReadExternal(xml);
            AssertDiagnostic(result, "PEVT9105");
            AssertNoDiagnostic(result, "PEVT9106");
        }

        [Theory]
        [InlineData(@"DisplayName="""" DisplayKey=""""")]
        [InlineData("")]
        public void Pevt9107_RejectsActorWithoutAnyDisplayName(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" {attributes} />
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9107");
        }

        [Theory]
        [InlineData(@"DisplayName=""X""")]
        [InlineData(@"DisplayKey=""Talker_X""")]
        public void Pevt9107_AcceptsEitherDisplayNameOrDisplayKey(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" {attributes} />
</ActorCatalog>";
            Assert.True(ReadExternal(xml).Success);
        }

        [Theory]
        [InlineData("DCCAE7")] // 缺少 #
        [InlineData("#DCC")] // 三位简写
        [InlineData("#DCCAE")] // 长度错误
        [InlineData("#DCCAE7FFF")] // 长度错误
        [InlineData("#GGCAE7")] // 非十六进制
        [InlineData("red")]
        [InlineData("rgb(1,2,3)")]
        public void Pevt9108_RejectsInvalidColor(string color)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" Color=""{color}"" />
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9108");
        }

        [Theory]
        [InlineData("#000000", 0x00, 0x00, 0x00, 0xFF)]
        [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF, 0xFF)]
        [InlineData("#dccae7", 0xDC, 0xCA, 0xE7, 0xFF)]
        [InlineData("#DCCAE700", 0xDC, 0xCA, 0xE7, 0x00)]
        [InlineData("#DCCAE7FF", 0xDC, 0xCA, 0xE7, 0xFF)]
        public void Pevt9108_AcceptsBoundaryColors(string color, int r, int g, int b, int a)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" Color=""{color}"" />
</ActorCatalog>";
            ActorCatalogReadResult result = ReadExternal(xml);

            Assert.True(result.Success);
            Assert.Equal(new ActorColor((byte)r, (byte)g, (byte)b, (byte)a), result.Catalog.Actors[0].Color);
        }

        [Fact]
        public void Pevt9109_RejectsUnknownProviderAndProviderUsedByWrongSource()
        {
            const string unknownProvider = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""file"" Resource=""C:/x.png"" />
  </Actor>
</ActorCatalog>";
            const string gamePxlsFromExternal = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""game-pxls"" Asset=""EvImg/__ev_n.pxls"" />
  </Actor>
</ActorCatalog>";
            const string polarisResFromBuiltIn = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""aic"" BuiltIn=""true"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""MyMod.Resources.X"" />
  </Actor>
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(unknownProvider), "PEVT9109");
            AssertDiagnostic(ReadExternal(gamePxlsFromExternal), "PEVT9109");
            AssertDiagnostic(ReadBuiltIn(polarisResFromBuiltIn), "PEVT9109");

            // 同一份 game-pxls 内容由内置来源读取时合法。
            Assert.True(ReadBuiltIn(gamePxlsFromExternal.Replace("Namespace=\"a\"", "Namespace=\"aic\" BuiltIn=\"true\"")).Success);
        }

        [Theory]
        [InlineData(@"Provider=""polaris-res""")] // 既无 Asset 也无 Resource
        [InlineData(@"Provider=""polaris-res"" Asset=""EvImg/__ev_n.pxls""")] // 用错属性
        [InlineData(@"Provider=""polaris-res"" Resource=""MyMod.Resources.X"" Asset=""EvImg/__ev_n.pxls""")] // 两个都写
        [InlineData(@"Provider=""polaris-res"" Resource=""OnlyOneSegment""")] // 缺少类型名
        [InlineData(@"Provider=""polaris-res"" Resource=""MyMod.Resources.Get()""")] // 方法调用
        [InlineData(@"Provider=""polaris-res"" Resource=""MyMod.Resources&lt;T&gt;.X""")] // 泛型
        [InlineData(@"Provider=""polaris-res"" Resource=""MyMod..X""")] // 空段
        [InlineData(@"Provider=""polaris-res"" Resource=""1Mod.X""")] // 非标识符
        public void Pevt9110_RejectsInvalidResourceReference(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" {attributes} />
  </Actor>
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9110");
        }

        [Theory]
        [InlineData(@"Provider=""game-pxls""")]
        [InlineData(@"Provider=""game-pxls"" Asset=""/EvImg/__ev_n.pxls""")] // 绝对路径
        [InlineData(@"Provider=""game-pxls"" Asset=""C:\EvImg\__ev_n.pxls""")] // 磁盘绝对路径
        [InlineData(@"Provider=""game-pxls"" Asset=""../../secret.pxls""")] // 上跳
        [InlineData(@"Provider=""game-pxls"" Asset=""EvImg//__ev_n.pxls""")] // 空段
        [InlineData(@"Provider=""game-pxls"" Resource=""MyMod.Resources.X""")] // 用错属性
        public void Pevt9110_RejectsInvalidGameAssetPath(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""aic"" BuiltIn=""true"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" {attributes} />
  </Actor>
</ActorCatalog>";
            AssertDiagnostic(ReadBuiltIn(xml), "PEVT9110");
        }

        [Fact]
        public void Pevt9112_RejectsDuplicateVisualAppearanceAndAnchorIds()
        {
            const string duplicatePortrait = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.B"" />
  </Actor>
</ActorCatalog>";
            const string duplicateWorldSprite = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">
    <WorldSprite Provider=""polaris-res"" Resource=""M.R.A"" />
    <WorldSprite Provider=""polaris-res"" Resource=""M.R.B"" />
  </Actor>
</ActorCatalog>";
            const string duplicateAnchor = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">
    <Anchor Id=""spot"" X=""0"" Y=""0"" />
    <Anchor Id=""spot"" X=""1"" Y=""1"" />
  </Actor>
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(duplicatePortrait), "PEVT9112");
            AssertDiagnostic(ReadExternal(duplicateWorldSprite), "PEVT9112");
            AssertDiagnostic(ReadExternal(duplicateAnchor), "PEVT9112");
        }

        [Fact]
        public void Pevt9112_AllowsSameIdInDifferentCategories()
        {
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
    <UiPortrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.B"" />
  </Actor>
</ActorCatalog>";

            ActorCatalogReadResult result = ReadExternal(xml);
            Assert.True(result.Success);
            AssertNoDiagnostic(result, "PEVT9112");
        }

        [Fact]
        public void Pevt9113_RejectsMissingOrDanglingDefaultPortrait()
        {
            const string missing = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
  </Actor>
</ActorCatalog>";
            const string dangling = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""missing"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
  </Actor>
</ActorCatalog>";
            const string danglingWithoutPortraits = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"" />
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(missing), "PEVT9113");
            AssertDiagnostic(ReadExternal(dangling), "PEVT9113");
            AssertDiagnostic(ReadExternal(danglingWithoutPortraits), "PEVT9113");
        }

        [Fact]
        public void Pevt9113_AllowsDialogueOnlyProfileWithoutVisuals()
        {
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" />
</ActorCatalog>";

            ActorCatalogReadResult result = ReadExternal(xml);
            Assert.True(result.Success);
            Assert.Null(result.Catalog.Actors[0].DefaultPortraitId);
            Assert.Null(result.Catalog.Actors[0].DefaultPortrait);
        }

        [Theory]
        [InlineData(@"Id=""a"" Portrait=""missing"" Pose=""stand"" Frame=""neutral""")]
        [InlineData(@"Id=""a"" Pose=""stand"" Frame=""neutral""")]
        [InlineData(@"Id=""a"" Portrait=""default"" Frame=""neutral""")]
        [InlineData(@"Id=""a"" Portrait=""default"" Pose=""stand""")]
        [InlineData(@"Id=""a"" Portrait=""default"" Pose="""" Frame=""neutral""")]
        public void Pevt9114_RejectsUnknownOrIncompleteAppearance(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
    <Appearance {attributes} />
  </Actor>
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9114");
        }

        [Fact]
        public void Pevt9115_RejectsLegacyPersonInExternalCatalog()
        {
            const string actorLevel = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" LegacyPerson=""n"" />
</ActorCatalog>";
            const string portraitLevel = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" LegacyPerson=""n"" />
  </Actor>
</ActorCatalog>";

            AssertDiagnostic(ReadExternal(actorLevel), "PEVT9115");
            AssertDiagnostic(ReadExternal(portraitLevel), "PEVT9115");

            // 同一份 Actor 级声明由内置来源读取时合法。
            Assert.True(ReadBuiltIn(actorLevel.Replace("Namespace=\"a\"", "Namespace=\"aic\" BuiltIn=\"true\"")).Success);
        }

        [Theory]
        [InlineData(@"Id=""s"" Y=""0""")] // 缺 X
        [InlineData(@"Id=""s"" X=""0""")] // 缺 Y
        [InlineData(@"Id=""s"" X=""NaN"" Y=""0""")]
        [InlineData(@"Id=""s"" X=""Infinity"" Y=""0""")]
        [InlineData(@"Id=""s"" X=""-Infinity"" Y=""0""")]
        [InlineData(@"Id=""s"" X=""1e400"" Y=""0""")] // 溢出为无穷
        [InlineData(@"Id=""s"" X=""left"" Y=""0""")]
        [InlineData(@"Id=""s"" X=""1,5"" Y=""0""")] // 千分位/异文化小数点
        [InlineData(@"Id=""s"" X=""0"" Y=""0"" EnterX=""1""")] // 只写一半入场坐标
        [InlineData(@"Id=""s"" X=""0"" Y=""0"" EnterY=""1""")]
        public void Pevt9116_RejectsInvalidOrIncompleteAnchor(string attributes)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">
    <Anchor {attributes} />
  </Actor>
</ActorCatalog>";
            AssertDiagnostic(ReadExternal(xml), "PEVT9116");
        }

        [Theory]
        [InlineData("0", "0")]
        [InlineData("-3.5", "2.25")]
        [InlineData("3.4028235E+38", "-3.4028235E+38")] // float 边界
        [InlineData("1E-45", "-1E-45")]
        public void Pevt9116_AcceptsFiniteBoundaryCoordinates(string x, string y)
        {
            string xml = $@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""x"" DisplayName=""X"">
    <Anchor Id=""s"" X=""{x}"" Y=""{y}"" />
  </Actor>
</ActorCatalog>";
            ActorCatalogReadResult result = ReadExternal(xml);

            Assert.True(result.Success);
            ActorAnchor anchor = result.Catalog.Actors[0].Anchors[0];
            Assert.False(float.IsNaN(anchor.X) || float.IsInfinity(anchor.X));
            Assert.Null(anchor.EnterX);
        }

        [Fact]
        public void Reader_CollectsEveryFailureInsteadOfStoppingAtTheFirst()
        {
            const string xml = @"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""a"">
  <Actor Id=""Bad"" DisplayName=""X"" Color=""red"" />
  <Actor Id=""other"" />
</ActorCatalog>";

            ActorCatalogReadResult result = ReadExternal(xml);

            Assert.Null(result.Catalog);
            Assert.Contains(result.Diagnostics, d => d.Id == "PEVT9105");
            Assert.Contains(result.Diagnostics, d => d.Id == "PEVT9108");
            Assert.Contains(result.Diagnostics, d => d.Id == "PEVT9107");
        }

        [Fact]
        public void Reader_RejectsInvalidUtf8Bytes()
        {
            byte[] invalid = { 0x3C, 0xC0, 0x80, 0x3E };

            ActorCatalogReadResult result = ActorCatalogReader.Read(invalid, "Bad.pactor", ActorCatalogSourceKind.External);

            Assert.Null(result.Catalog);
            Assert.Null(result.Source);
            Assert.Contains(result.Diagnostics, d => d.Id == "PEVT9101");
        }

        [Fact]
        public void Reader_IsDeterministicForTheSameBytes()
        {
            ActorCatalogReadResult first = ReadExternal(ValidExternal);
            ActorCatalogReadResult second = ReadExternal(ValidExternal);

            Assert.Equal(
                first.Catalog.Actors.Select(a => a.LocalId),
                second.Catalog.Actors.Select(a => a.LocalId));
            Assert.Equal(first.Diagnostics.Count, second.Diagnostics.Count);
        }

        [Fact]
        public void EveryActorCatalogDiagnosticId_IsRegisteredInTheCatalog()
        {
            for (int number = 9101; number <= 9118; number++)
            {
                string id = "PEVT" + number.ToString();
                Assert.True(DiagnosticCatalog.TryFind(id, out DiagnosticDescriptor descriptor), $"{id} 未登记。");
                Assert.Equal(number == 9118 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, descriptor.Severity);
            }
        }
    }
}
