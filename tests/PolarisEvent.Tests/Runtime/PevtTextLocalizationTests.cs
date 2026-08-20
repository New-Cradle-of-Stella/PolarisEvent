using Polaris.Pevt.Actors;
using Polaris.Pevt.Core.Tests.Runtime.Fakes;
using Polaris.Pevt.Runtime;
using Polaris.Pevt.Runtime.Routines;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Runtime
{
    /// <summary>
    /// 带 <c>text</c> 参数域的实参在进入处理器之前的显示文案解析。
    ///
    /// 这一层是集中做的（见 <c>PevtTextArguments</c>），因此测试盯的是"处理器看到的是最终文案"
    /// 而不是"某个处理器记得调一次解析"——后者才是漏一条就会在游戏里显示成 <c>&amp;key</c> 的那种错。
    /// </summary>
    public class PevtTextLocalizationTests
    {
        private const string Source = "id \"T\"\n@say(\"aic:noel\", \"{0}\")\nend\n";

        private static PevtTestHost Host()
        {
            var host = new PevtTestHost { Actors = BuiltinActorCatalog.CreateDirectoryBuilder().Build() };
            P0CommandRoutines.RegisterAll(host.Commands);
            return host;
        }

        /// <summary>跑一句 <c>@say</c>，返回处理器实际交给对话服务的那串文本。</summary>
        private static string SaidBy(PevtTestHost host, string literal)
        {
            PevtExecution execution = host.Start(Source.Replace("{0}", literal));
            host.Step(execution);
            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            foreach (string call in host.Dialogue.Calls)
            {
                if (call.StartsWith("OpenText(", System.StringComparison.Ordinal))
                    return call.Substring("OpenText(".Length, call.Length - "OpenText(".Length - 1);
            }

            return null;
        }

        [Fact]
        public void TextArgumentsAreResolvedBeforeTheRoutineSeesThem()
        {
            PevtTestHost host = Host();
            host.Localization.Add("mymod.greet", "早上好");

            Assert.Equal("早上好", SaidBy(host, "&mymod.greet"));
        }

        [Fact]
        public void PlainTextPassesThroughUnchanged() =>
            Assert.Equal("早上好", SaidBy(Host(), "早上好"));

        /// <summary>没登记的键显示成 key 本身，作者一眼就能看出是哪条文案漏了。</summary>
        [Fact]
        public void AnUnregisteredKeyFallsBackToTheKeyItself() =>
            Assert.Equal("mymod.missing", SaidBy(Host(), "&mymod.missing"));

        /// <summary><c>&amp;&amp;</c> 开头是转义的字面 <c>&amp;</c>，不查表。</summary>
        [Fact]
        public void AnEscapedAmpersandIsUnescapedInsteadOfLookedUp() =>
            Assert.Equal("&mymod.greet", SaidBy(Host(), "&&mymod.greet"));

        /// <summary>非文案形参不进解析：actorId 必须原样送到人物目录，否则人物 ID 会被当文案查一遍。</summary>
        [Fact]
        public void NonTextParametersAreNotAskedAboutLocalization()
        {
            PevtTestHost host = Host();
            host.Localization.Add("mymod.greet", "早上好");

            SaidBy(host, "&mymod.greet");

            Assert.Equal(new[] { "&mymod.greet" }, host.Localization.Asked);
        }

        /// <summary>选项文案也是文案：<c>@choose</c> 的提示与每个选项都要过解析。</summary>
        [Fact]
        public void ChoicePromptAndOptionsAreResolved()
        {
            PevtTestHost host = Host();
            host.Localization.Add("q", "去哪里？").Add("a", "森林").Add("b", "村子");

            PevtExecution execution = host.Start("id \"T\"\n@choose(\"&q\", \"&a\", \"&b\")\nend\n");
            host.Step(execution);
            host.Choice.PresentSignal.Signal(1);
            host.RunToCompletion(execution);

            Assert.Contains("Begin(去哪里？)", host.Choice.Calls);
            Assert.Contains("AddIndex(森林)", host.Choice.Calls);
            Assert.Contains("AddIndex(村子)", host.Choice.Calls);
        }

        /// <summary>宿主没接解析器时实参原样通过——可移植场景里显示 <c>&amp;key</c> 恰好也是最有用的行为。</summary>
        [Fact]
        public void WithoutALocalizationServiceTheRawTextReachesTheRoutine()
        {
            PevtTestHost host = Host();

            var bare = new PevtServices(
                host.Clock, new PevtEventSession("T"),
                new FakeActorCatalogService(host.Actors),
                dialogue: host.Dialogue);

            var execution = new PevtExecution(
                host.Compile(Source.Replace("{0}", "&mymod.greet")), bare, host.Commands, host.Limits);

            execution.Resume();
            host.Dialogue.AdvanceSignal.Signal(0);
            host.RunToCompletion(execution);

            Assert.Contains("OpenText(&mymod.greet)", host.Dialogue.Calls);
        }
    }
}
