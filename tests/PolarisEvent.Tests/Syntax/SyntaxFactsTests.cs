using System.Linq;
using Polaris.Pevt.Syntax;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    public class SyntaxFactsTests
    {
        [Theory]
        [InlineData("if", SyntaxKind.IfKeyword)]
        [InlineData("endblock", SyntaxKind.EndBlockKeyword)]
        [InlineData("await", SyntaxKind.AwaitKeyword)]
        [InlineData("exec", SyntaxKind.ExecKeyword)]
        public void TryGetKeywordKind_RecognizesKeyword(string text, SyntaxKind expected)
        {
            Assert.True(SyntaxFacts.TryGetKeywordKind(text, out SyntaxKind kind));
            Assert.Equal(expected, kind);
        }

        [Fact]
        public void TryGetKeywordKind_RejectsNonKeyword() => Assert.False(SyntaxFacts.TryGetKeywordKind("dialogue", out _));

        [Fact]
        public void ReservedWords_MatchKeywordTableExactly()
        {
            Assert.Equal(37, SyntaxFacts.ReservedWords.Count);
            Assert.True(SyntaxFacts.ReservedWords.ToHashSet().SetEquals(SyntaxFacts.Keywords.Keys));
            Assert.All(SyntaxFacts.Keywords.Keys, keyword => Assert.True(SyntaxFacts.IsReservedWord(keyword)));
        }

        [Fact]
        public void IsReservedWord_FalseForOrdinaryIdentifier() => Assert.False(SyntaxFacts.IsReservedWord("playerName"));

        [Theory]
        [InlineData("int", true)]
        [InlineData("float", true)]
        [InlineData("bool", true)]
        [InlineData("char", true)]
        [InlineData("string", true)]
        [InlineData("handler", false)]
        public void IsTypeName_MatchesTheFiveVariableTypesOnly(string name, bool expected) =>
            Assert.Equal(expected, SyntaxFacts.IsTypeName(name));

        [Theory]
        [InlineData("cs", true)]
        [InlineData("async", true)]
        [InlineData("cmd", false)]
        public void IsEnableCapability_MatchesCsAndAsyncOnly(string name, bool expected) =>
            Assert.Equal(expected, SyntaxFacts.IsEnableCapability(name));
    }
}
