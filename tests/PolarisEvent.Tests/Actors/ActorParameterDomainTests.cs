using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Actors
{
    /// <summary>
    /// 人物相关参数域。关键约束有两条：参数域不是第六种 PEVT 类型；未知跨模组人物只影响补全，
    /// 绝不产生静态诊断——存在性是运行期事实，缺失由 PEVTR4401/PEVTR4402 负责。
    /// </summary>
    public class ActorParameterDomainTests
    {
        private static ActorCatalog ModCatalog() =>
            ActorCatalogReader.ReadText(@"<ActorCatalog xmlns=""urn:polaris:pevt:actors:v1"" Version=""1"" Namespace=""example.mod"">
  <Actor Id=""iris"" DisplayName=""Iris"" DefaultPortrait=""default"">
    <Portrait Id=""default"" Provider=""polaris-res"" Resource=""M.R.A"" />
    <UiPortrait Id=""bust"" Provider=""polaris-res"" Resource=""M.R.B"" />
    <Appearance Id=""neutral"" Portrait=""default"" Pose=""stand"" Frame=""neutral"" />
    <Appearance Id=""surprised"" Portrait=""default"" Pose=""stand"" Frame=""surprise"" />
    <Anchor Id=""balcony"" X=""4"" Y=""2"" />
  </Actor>
</ActorCatalog>", "Actors/Mod.pactor", ActorCatalogSourceKind.External).Catalog;

        private static ActorParameterResolver Resolver()
        {
            ActorDirectoryBuilder builder = BuiltinActorCatalog.CreateDirectoryBuilder();
            builder.Add(ModCatalog(), "ExampleMod");
            return new ActorParameterResolver(builder.Build());
        }

        [Fact]
        public void ParameterDomains_AreStringDomainsNotNewPevtTypes()
        {
            ParameterDomain[] domains =
            {
                ParameterDomain.ActorId,
                ParameterDomain.ActorAppearance,
                ParameterDomain.ActorAnchor,
                ParameterDomain.ActorPortrait,
                ParameterDomain.ActorUiPortrait,
            };

            Assert.All(domains, domain =>
            {
                Assert.Equal(PevtType.String, domain.UnderlyingType);
                Assert.True(domain.UnderlyingType.IsOrdinaryType());
                Assert.False(domain.RejectsUnknownValues);
            });

            // 普通类型仍然只有五种：参数域没有往枚举里加成员。
            Assert.Equal(5, Enum.GetValues(typeof(PevtType)).Cast<PevtType>().Count(t => t.IsOrdinaryType()));
        }

        [Fact]
        public void ParameterDomain_RejectsNonOrdinaryUnderlyingType()
        {
            Assert.Throws<ArgumentException>(() => new ParameterDomain("bad", PevtType.Handler, false));
            Assert.Throws<ArgumentException>(() => new ParameterDomain("bad", PevtType.Void, false));
            Assert.Throws<ArgumentException>(() => new ParameterDomain("", PevtType.String, false));
        }

        [Fact]
        public void BuiltinParameter_RejectsDomainThatDoesNotMatchTheParameterType()
        {
            Assert.Throws<ArgumentException>(() => new BuiltinParameter("actor", PevtType.Int, ParameterDomain.ActorId));

            var parameter = new BuiltinParameter("actor", PevtType.String, ParameterDomain.ActorId);
            Assert.Same(ParameterDomain.ActorId, parameter.Domain);
            Assert.Null(new BuiltinParameter("text", PevtType.String).Domain);
        }

        [Fact]
        public void ParameterDomain_DoesNotChangeOverloadSelectionOrSignatureShape()
        {
            var signature = new BuiltinSignature("actor_enter", false, new List<BuiltinParameter>
            {
                new BuiltinParameter("actor", PevtType.String, ParameterDomain.ActorId),
                new BuiltinParameter("anchor", PevtType.String, ParameterDomain.ActorAnchor),
            }, null);

            Assert.True(signature.HasValidSignatureShape());
        }

        [Theory]
        [InlineData("aic:noel", ActorParameterStatus.Known)]
        [InlineData("example.mod:iris", ActorParameterStatus.Known)]
        [InlineData("other.mod:someone", ActorParameterStatus.Unknown)] // 跨模组人物可以晚注册
        [InlineData("aic:nobody", ActorParameterStatus.Unknown)]
        [InlineData("noel", ActorParameterStatus.Malformed)] // 缺命名空间
        [InlineData("n", ActorParameterStatus.Malformed)] // 原版短键不是公开 ID
        [InlineData("aic:Noel", ActorParameterStatus.Malformed)]
        [InlineData(null, ActorParameterStatus.Malformed)]
        public void ActorIdDomain_ClassifiesValuesWithoutFailing(string value, ActorParameterStatus expected) =>
            Assert.Equal(expected, Resolver().Check(ParameterDomain.ActorId, value));

        [Theory]
        [InlineData("neutral", ActorParameterStatus.Known)]
        [InlineData("surprised", ActorParameterStatus.Known)]
        [InlineData("angry", ActorParameterStatus.Unknown)]
        [InlineData("a_3/a0__", ActorParameterStatus.Malformed)] // 不接受原版组合串
        public void AppearanceDomain_IsScopedToTheGivenActor(string value, ActorParameterStatus expected) =>
            Assert.Equal(expected, Resolver().Check(ParameterDomain.ActorAppearance, value, "example.mod:iris"));

        [Fact]
        public void AppearanceDomain_IsUnknownWhenTheActorItselfIsUnknown()
        {
            ActorParameterResolver resolver = Resolver();

            Assert.Equal(ActorParameterStatus.Unknown, resolver.Check(ParameterDomain.ActorAppearance, "neutral", "other.mod:someone"));
            Assert.Equal(ActorParameterStatus.Unknown, resolver.Check(ParameterDomain.ActorAppearance, "neutral"));
        }

        [Theory]
        [InlineData("left")]
        [InlineData("center")]
        [InlineData("right")]
        [InlineData("near-left")]
        [InlineData("near-right")]
        [InlineData("far-left")]
        [InlineData("far-right")]
        [InlineData("off-left")]
        [InlineData("off-right")]
        public void AnchorDomain_KnowsEveryBuiltinSemanticAnchorWithoutAnActor(string anchor)
        {
            Assert.Equal(ActorParameterStatus.Known, Resolver().Check(ParameterDomain.ActorAnchor, anchor));
            Assert.Contains(anchor, BuiltinActorAnchors.All);
        }

        [Fact]
        public void AnchorDomain_AlsoResolvesActorSpecificAnchors()
        {
            ActorParameterResolver resolver = Resolver();

            Assert.Equal(ActorParameterStatus.Known, resolver.Check(ParameterDomain.ActorAnchor, "balcony", "example.mod:iris"));
            Assert.Equal(ActorParameterStatus.Unknown, resolver.Check(ParameterDomain.ActorAnchor, "balcony"));
            Assert.Equal(ActorParameterStatus.Unknown, resolver.Check(ParameterDomain.ActorAnchor, "balcony", "aic:noel"));
        }

        [Fact]
        public void Completion_ListsVisibleActorsIncludingBuiltinAndMod()
        {
            IReadOnlyList<string> candidates = Resolver().Complete(ParameterDomain.ActorId);

            Assert.Contains("aic:noel", candidates);
            Assert.Contains("aic:narrator", candidates);
            Assert.Contains("example.mod:iris", candidates);
            Assert.DoesNotContain("other.mod:someone", candidates);
            Assert.Equal(17, candidates.Count);
        }

        [Fact]
        public void Completion_ListsBuiltinAnchorsFirstThenActorSpecificOnes()
        {
            IReadOnlyList<string> candidates = Resolver().Complete(ParameterDomain.ActorAnchor, "example.mod:iris");

            Assert.Equal(BuiltinActorAnchors.All, candidates.Take(BuiltinActorAnchors.All.Count));
            Assert.Equal("balcony", candidates.Last());
        }

        [Fact]
        public void Completion_ForAppearanceAndPortraitIsScopedToTheActor()
        {
            ActorParameterResolver resolver = Resolver();

            Assert.Equal(new[] { "neutral", "surprised" }, resolver.Complete(ParameterDomain.ActorAppearance, "example.mod:iris"));
            Assert.Equal(new[] { "default" }, resolver.Complete(ParameterDomain.ActorPortrait, "example.mod:iris"));
            Assert.Equal(new[] { "bust" }, resolver.Complete(ParameterDomain.ActorUiPortrait, "example.mod:iris"));
            Assert.Equal(new[] { "default", "bass", "epbench" }, resolver.Complete(ParameterDomain.ActorPortrait, "aic:noel"));
            Assert.Empty(resolver.Complete(ParameterDomain.ActorAppearance, "other.mod:someone"));
        }

        [Fact]
        public void UnknownActorArgument_ProducesNoStaticDiagnostic()
        {
            // 绑定一段引用了未知跨模组人物的源码，全过程不得出现任何人物相关诊断。
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("say", false, new List<BuiltinParameter>
            {
                new BuiltinParameter("actor", PevtType.String, ParameterDomain.ActorId),
                new BuiltinParameter("text", PevtType.String),
            }, null));

            const string code = "id \"Demo\"\n@say(\"other.mod:someone\", \"hi\")\nend\n";
            SourceText source = SourceText.FromUtf8(new System.Text.UTF8Encoding(false).GetBytes(code), "Demo.pevt").Text;
            var diagnostics = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(source, diagnostics);
            DocumentSyntax document = new Parser(tokens, diagnostics, source).ParseDocument();
            new Binder(diagnostics, source, table).BindDocument(document);

            Assert.DoesNotContain(diagnostics.ToReadOnly(), d => d.Id.StartsWith("PEVT91", StringComparison.Ordinal));
            Assert.False(diagnostics.HasErrors);

            // 解析器同样只把它标成 Unknown，而不是失败。
            Assert.Equal(ActorParameterStatus.Unknown, Resolver().Check(ParameterDomain.ActorId, "other.mod:someone"));
        }
    }
}
