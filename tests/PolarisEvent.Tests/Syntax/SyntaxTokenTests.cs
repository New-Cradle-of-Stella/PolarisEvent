using System;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    public class SyntaxTokenTests
    {
        [Fact]
        public void Constructor_DefaultsToEmptyTriviaAndCarriesGivenTrivia()
        {
            var plain = new SyntaxToken(SyntaxKind.IfKeyword, new TextSpan(0, 2), "if", TokenValue.None);
            Assert.Empty(plain.LeadingTrivia);
            Assert.Empty(plain.TrailingTrivia);
            Assert.False(plain.IsMissing);

            var leading = new[] { new SyntaxTrivia(TriviaKind.Whitespace, new TextSpan(0, 1), " ") };
            var trailing = new[] { new SyntaxTrivia(TriviaKind.LineComment, new TextSpan(3, 4), "// x") };
            var withTrivia = new SyntaxToken(SyntaxKind.EndKeyword, new TextSpan(1, 3), "end", TokenValue.None, leading, trailing);
            Assert.Same(leading, withTrivia.LeadingTrivia);
            Assert.Same(trailing, withTrivia.TrailingTrivia);
        }

        [Fact]
        public void CreateMissing_ProducesZeroLengthMarkedToken()
        {
            SyntaxToken token = SyntaxToken.CreateMissing(SyntaxKind.EndIfKeyword, 12);

            Assert.True(token.IsMissing);
            Assert.Equal(SyntaxKind.EndIfKeyword, token.Kind);
            Assert.Equal(0, token.Span.Length);
            Assert.Equal(12, token.Span.Start);
            Assert.Equal(string.Empty, token.Text);
            Assert.Contains("missing", token.ToString());
        }

        [Fact]
        public void Constructor_RejectsNullText() =>
            Assert.Throws<ArgumentNullException>(() =>
                new SyntaxToken(SyntaxKind.EndKeyword, new TextSpan(0, 0), null, TokenValue.None));
    }

    public class TokenValueTests
    {
        [Fact]
        public void FactoryMethods_RoundTripEachLiteralKind()
        {
            Assert.Equal(42, TokenValue.FromInteger(42).AsInteger);
            Assert.Equal(3.5f, TokenValue.FromFloat(3.5f).AsFloat);
            Assert.True(TokenValue.FromBoolean(true).AsBoolean);
            Assert.Equal('A', TokenValue.FromChar('A').AsChar);
            Assert.Equal("hello", TokenValue.FromString("hello").AsString);
            Assert.Equal(TokenValueKind.None, TokenValue.None.Kind);
        }

        [Fact]
        public void AsInteger_ThrowsWhenKindDoesNotMatch() =>
            Assert.Throws<InvalidOperationException>(() => TokenValue.FromString("x").AsInteger);

        [Fact]
        public void Equals_ComparesByKindAndPayload()
        {
            Assert.Equal(TokenValue.FromInteger(1), TokenValue.FromInteger(1));
            Assert.NotEqual(TokenValue.FromInteger(1), TokenValue.FromInteger(2));
            Assert.NotEqual(TokenValue.FromInteger(1), TokenValue.FromFloat(1f));
        }
    }
}
