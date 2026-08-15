using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    public class LexerTests
    {
        private static (IReadOnlyList<SyntaxToken> Tokens, IReadOnlyList<Diagnostic> Diagnostics) Lex(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            return (tokens, bag.ToReadOnly());
        }

        private static SyntaxToken LexSingle(string source)
        {
            (IReadOnlyList<SyntaxToken> tokens, _) = Lex(source);
            Assert.Equal(2, tokens.Count); // 目标 token + EndOfFileToken
            return tokens[0];
        }

        public static IEnumerable<object[]> KeywordCases() =>
            SyntaxFacts.Keywords.Select(kv => new object[] { kv.Key, kv.Value });

        [Theory]
        [MemberData(nameof(KeywordCases))]
        public void Lex_RecognizesEveryKeyword(string text, SyntaxKind expectedKind)
        {
            SyntaxToken token = LexSingle(text);
            Assert.Equal(expectedKind, token.Kind);
            Assert.Equal(text, token.Text);
        }

        [Theory]
        [InlineData("+", SyntaxKind.PlusToken)]
        [InlineData("-", SyntaxKind.MinusToken)]
        [InlineData("*", SyntaxKind.StarToken)]
        [InlineData("/", SyntaxKind.SlashToken)]
        [InlineData("%", SyntaxKind.PercentToken)]
        [InlineData("=", SyntaxKind.EqualsToken)]
        [InlineData("==", SyntaxKind.EqualsEqualsToken)]
        [InlineData("!", SyntaxKind.ExclamationToken)]
        [InlineData("!=", SyntaxKind.ExclamationEqualsToken)]
        [InlineData("<", SyntaxKind.LessThanToken)]
        [InlineData("<=", SyntaxKind.LessThanEqualsToken)]
        [InlineData(">", SyntaxKind.GreaterThanToken)]
        [InlineData(">=", SyntaxKind.GreaterThanEqualsToken)]
        [InlineData("&", SyntaxKind.AmpersandToken)]
        [InlineData("|", SyntaxKind.PipeToken)]
        [InlineData("^", SyntaxKind.CaretToken)]
        [InlineData("(", SyntaxKind.OpenParenToken)]
        [InlineData(")", SyntaxKind.CloseParenToken)]
        [InlineData(":", SyntaxKind.ColonToken)]
        [InlineData(",", SyntaxKind.CommaToken)]
        [InlineData("@", SyntaxKind.AtToken)]
        [InlineData("#", SyntaxKind.HashToken)]
        [InlineData("$raw", SyntaxKind.DollarRawToken)]
        public void Lex_RecognizesOperatorsAndPunctuation(string text, SyntaxKind expectedKind)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(expectedKind, tokens[0].Kind);
            Assert.Equal(text, tokens[0].Text);
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("playerName")]
        [InlineData("_hidden")]
        [InlineData("a1")]
        [InlineData("_playScene")]
        public void Lex_RecognizesOrdinaryIdentifier(string text)
        {
            SyntaxToken token = LexSingle(text);
            Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
            Assert.Equal(text, token.Text);
        }

        [Fact]
        public void Lex_EmptySource_ProducesOnlyEndOfFileToken()
        {
            (IReadOnlyList<SyntaxToken> tokens, _) = Lex(string.Empty);
            Assert.Single(tokens);
            Assert.Equal(SyntaxKind.EndOfFileToken, tokens[0].Kind);
            Assert.Equal(0, tokens[0].Span.Length);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("42", 42)]
        [InlineData("2147483647", int.MaxValue)]
        public void Lex_RecognizesValidIntegerLiteral(string text, int expected)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[0].Kind);
            Assert.Equal(expected, tokens[0].Value.AsInteger);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_Integer2147483648_DefersToUnaryMinusStageWithoutDiagnosticOrValue()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("2147483648");
            Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[0].Kind);
            Assert.Equal(TokenValueKind.None, tokens[0].Value.Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_IntegerBeyondInt32Magnitude_ReportsPEVT5017()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("2147483649");
            Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[0].Kind);
            Assert.Equal(TokenValueKind.None, tokens[0].Value.Kind);
            Assert.Equal("PEVT5017", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("0.0", 0f)]
        [InlineData("3.5", 3.5f)]
        public void Lex_RecognizesValidFloatLiteral(string text, float expected)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.FloatLiteralToken, tokens[0].Kind);
            Assert.Equal(expected, tokens[0].Value.AsFloat);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_FloatBeyondFiniteRange_ReportsPEVT5018()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("3" + new string('4', 45) + ".0");
            Assert.Equal(SyntaxKind.FloatLiteralToken, tokens[0].Kind);
            Assert.Equal("PEVT5018", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("1.")]
        [InlineData("1.2.3")]
        [InlineData("1e5")]
        [InlineData("1_000")]
        [InlineData("123abc")]
        public void Lex_MalformedNumericLiteral_ReportsPEVT5019(string text)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal(text, tokens[0].Text);
            Assert.Equal("PEVT5019", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_StringLiteral_DecodesStandardEscapes()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\\n\\t\\\"b\"");
            Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
            Assert.Equal("a\n\t\"b", tokens[0].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_UnterminatedString_ReportsPEVT1002()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"abc");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1002", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_StringWithInvalidEscape_ReportsPEVT5021()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\\qb\"");
            Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
            Assert.Equal(TokenValueKind.None, tokens[0].Value.Kind);
            Assert.Equal("PEVT5021", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("'A'", 'A')]
        [InlineData("'\\n'", '\n')]
        [InlineData("'\\''", '\'')]
        public void Lex_CharLiteral_DecodesStandardEscapes(string text, char expected)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.CharLiteralToken, tokens[0].Kind);
            Assert.Equal(expected, tokens[0].Value.AsChar);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_UnterminatedCharLiteral_ReportsPEVT1004()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("'A");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1004", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("''")]
        [InlineData("'AB'")]
        public void Lex_CharLiteralWithWrongLength_ReportsPEVT5020(string text)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.CharLiteralToken, tokens[0].Kind);
            Assert.Equal("PEVT5020", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_CharLiteralWithInvalidEscape_ReportsPEVT5021()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("'\\q'");
            Assert.Equal(SyntaxKind.CharLiteralToken, tokens[0].Kind);
            Assert.Equal("PEVT5021", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_LineComment_BecomesTrailingLeadingTriviaOfNextToken()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("// hi\nend");
            SyntaxToken endToken = tokens[0];
            Assert.Equal(SyntaxKind.EndKeyword, endToken.Kind);
            Assert.Equal(TriviaKind.LineComment, endToken.LeadingTrivia[0].Kind);
            Assert.Equal("// hi", endToken.LeadingTrivia[0].Text);
            Assert.Equal(TriviaKind.LineBreak, endToken.LeadingTrivia[1].Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_BlockComment_CapturesExactSpanAcrossLines()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("/*\nabc\n*/end");
            SyntaxToken endToken = tokens[0];
            Assert.Equal("/*\nabc\n*/", Assert.Single(endToken.LeadingTrivia).Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_UnterminatedBlockComment_ReportsPEVT1010()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("/* never closes");
            Assert.Equal(SyntaxKind.EndOfFileToken, tokens[0].Kind);
            Assert.Equal("PEVT1010", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("}")]
        public void Lex_EventBlockBraces_ReportPEVT1003(string text)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(text);
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1003", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_Semicolon_ReportsPEVT1007()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(";");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1007", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_UnrecognizedCharacter_ReportsPEVT1001()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("~");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1001", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_DollarNotFollowedByRaw_ReportsPEVT1001()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$other");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("$", tokens[0].Text);
            Assert.Equal("PEVT1001", Assert.Single(diagnostics).Id);
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[1].Kind);
        }

        [Fact]
        public void Lex_NonAsciiLetterUsedAsIdentifier_ReportsPEVT1008()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("变量");
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[0].Kind);
            Assert.Equal("变量", tokens[0].Text);
            Assert.Equal("PEVT1008", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_MinusBeforeNumber_TokenizesAsSeparateMinusAndIntegerTokens()
        {
            // 8.9/19 节：一元负号只是位置相关的语法解释，词法阶段永远把 '-' 单独 token 化，
            // 不把它和后面的数字字面量拼成一个 token——即便结果将是合法的 int.MinValue。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("-2147483648");
            Assert.Equal(SyntaxKind.MinusToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[1].Kind);
            Assert.Equal("2147483648", tokens[1].Text);
            Assert.Equal(TokenValueKind.None, tokens[1].Value.Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_MinusBeforeOrdinaryNumber_TokenizesAsSeparateTokensWithValue()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("-5");
            Assert.Equal(SyntaxKind.MinusToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[1].Kind);
            Assert.Equal(5, tokens[1].Value.AsInteger);
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("a\r\nb")]
        [InlineData("a\rb")]
        [InlineData("a\nb")]
        public void Lex_RecognizesAllThreeLineBreakForms(string source)
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            SyntaxToken second = tokens[1];
            Assert.Equal(TriviaKind.LineBreak, Assert.Single(second.LeadingTrivia).Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_LeadingWhitespace_IsCapturedAsWhitespaceTrivia()
        {
            (IReadOnlyList<SyntaxToken> tokens, _) = Lex("  \tend");
            SyntaxTrivia trivia = Assert.Single(tokens[0].LeadingTrivia);
            Assert.Equal(TriviaKind.Whitespace, trivia.Kind);
            Assert.Equal("  \t", trivia.Text);
        }

        [Fact]
        public void Lex_BlockCommentsDoNotNest_FirstClosingDelimiterEndsIt()
        {
            // 1.3 节：块注释不允许嵌套；内部再次出现的 "/*" 只是普通内容，第一个 "*/" 就结束注释。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("/* /* nested */ end");
            Assert.Equal("/* /* nested */", tokens[0].LeadingTrivia[0].Text);
            Assert.Equal(SyntaxKind.EndKeyword, tokens[0].Kind);
            Assert.Equal("end", tokens[0].Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_CommentMarkersInsideStringLiteral_AreOrdinaryContent()
        {
            // 1.3 节：字符串内部的 "//"、"/*"、"*/" 是普通内容，不会开始或结束注释。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a // b /* c */ d\"");
            Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
            Assert.Equal("a // b /* c */ d", tokens[0].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_LabelReference_TokenizesAsHashThenIdentifier()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("#Opening");
            Assert.Equal(SyntaxKind.HashToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[1].Kind);
            Assert.Equal("Opening", tokens[1].Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_BuiltinCallName_TokenizesAsAtThenIdentifier()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("@dialogue_show");
            Assert.Equal(SyntaxKind.AtToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[1].Kind);
            Assert.Equal("dialogue_show", tokens[1].Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_KeywordsAreCaseSensitive_UppercaseFormIsAnOrdinaryIdentifier()
        {
            SyntaxToken token = LexSingle("IF");
            Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
            Assert.Equal("IF", token.Text);
        }

        [Fact]
        public void Lex_DollarRawIsCaseSensitive_UppercaseFormIsUnexpectedTokenPlusIdentifier()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$RAW");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("$", tokens[0].Text);
            Assert.Equal("PEVT1001", Assert.Single(diagnostics).Id);
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[1].Kind);
            Assert.Equal("RAW", tokens[1].Text);
        }

        [Fact]
        public void Lex_FourSingleQuotes_OpensRawBlockThatNeverCloses()
        {
            // 阶段 4：裸露的 "'''" 一旦出现就会触发原始文本块内容扫描；第四个 "'" 单独凑不出结束
            // 分隔符，于是被吞进内容里，最终因为找不到闭合分隔符报 PEVT8004。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("''''");
            Assert.Equal(SyntaxKind.TripleQuoteToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.RawContentToken, tokens[1].Kind);
            Assert.Equal("'", tokens[1].Value.AsString);
            Assert.True(tokens[2].IsMissing);
            Assert.Equal("PEVT8004", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_TripleEquals_IsEqualsEqualsThenEquals()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("===");
            Assert.Equal(SyntaxKind.EqualsEqualsToken, tokens[0].Kind);
            Assert.Equal(SyntaxKind.EqualsToken, tokens[1].Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_BlockCommentAcrossCrLfLines_CapturesExactSpan()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("/*\r\nabc\r\n*/end");
            Assert.Equal("/*\r\nabc\r\n*/", Assert.Single(tokens[0].LeadingTrivia).Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_DiagnosticLocation_ReflectsCorrectLineAndColumn()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("end\n~");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(2, diagnostic.Location.StartLine);
            Assert.Equal(1, diagnostic.Location.StartColumn);
        }

        [Fact]
        public void Lex_ApostropheInsideStringLiteral_DoesNotStartCharLiteral()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"it's fine\"");
            Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
            Assert.Equal("it's fine", tokens[0].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_QuoteInsideCharLiteral_KeptAsOrdinaryContent()
        {
            // char 允许把 " 原样放进内容里（只有转义形式 \" 对 char 无效，裸字符没有这个限制）。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("'\"'");
            Assert.Equal(SyntaxKind.CharLiteralToken, tokens[0].Kind);
            Assert.Equal('"', tokens[0].Value.AsChar);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_NonAsciiDigit_IsNotTreatedAsPartOfANumber()
        {
            // 8.9 节的 digit 只允许 ASCII 0-9；char.IsDigit 会认出更多 Unicode 十进制数字，
            // 词法器必须用自己的 ASCII 判定，拒绝把阿拉伯数字 U+0660 当成数字字面量的一部分。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("٠");
            Assert.Equal(SyntaxKind.BadToken, tokens[0].Kind);
            Assert.Equal("PEVT1001", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Lex_UnderscoreOnlyFollowedByDigits_IsOrdinaryIdentifier()
        {
            SyntaxToken token = LexSingle("_1");
            Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
            Assert.Equal("_1", token.Text);
        }

        [Fact]
        public void Lex_MinusBeforeZero_TokenizesAsSeparateTokens()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("-0");
            Assert.Equal(SyntaxKind.MinusToken, tokens[0].Kind);
            Assert.Equal(0, tokens[1].Value.AsInteger);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_ConsecutiveParens_ProducesOneTokenEach()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("(((");
            Assert.Equal(new[] { SyntaxKind.OpenParenToken, SyntaxKind.OpenParenToken, SyntaxKind.OpenParenToken, SyntaxKind.EndOfFileToken },
                tokens.Select(t => t.Kind));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_WhitespaceOnlySource_ProducesOnlyEndOfFileTokenWithLeadingTrivia()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("   ");
            SyntaxToken eof = Assert.Single(tokens);
            Assert.Equal(SyntaxKind.EndOfFileToken, eof.Kind);
            Assert.Equal(TriviaKind.Whitespace, Assert.Single(eof.LeadingTrivia).Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_TrailingLineCommentWithNoNewline_AttachesToEndOfFileToken()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("end // done");
            SyntaxToken eof = tokens[1];
            Assert.Equal(SyntaxKind.EndOfFileToken, eof.Kind);
            Assert.Contains(eof.LeadingTrivia, t => t.Kind == TriviaKind.LineComment && t.Text == "// done");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_EmptyStringLiteral_IsValidWithEmptyValue()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"\"");
            Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
            Assert.Equal(string.Empty, tokens[0].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_EmptyBlockComment_IsValid()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("/**/end");
            Assert.Equal("/**/", Assert.Single(tokens[0].LeadingTrivia).Text);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_MinimalLegalFile_TokenizesCleanly()
        {
            // 语法设计草案 §17 的最小合法文件：id 声明 + end。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("id \"MuseumEntrance\"\n\nend");
            Assert.Equal(new[] { SyntaxKind.IdKeyword, SyntaxKind.StringLiteralToken, SyntaxKind.EndKeyword, SyntaxKind.EndOfFileToken },
                tokens.Select(t => t.Kind));
            Assert.Equal("MuseumEntrance", tokens[1].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Lex_MultiTokenSequence_ProducesExactSpansAndOrder()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("if x\nendif");
            Assert.Equal(new[]
            {
                SyntaxKind.IfKeyword, SyntaxKind.IdentifierToken, SyntaxKind.EndIfKeyword, SyntaxKind.EndOfFileToken,
            }, tokens.Select(t => t.Kind));
            Assert.Equal(new TextSpan(0, 2), tokens[0].Span);
            Assert.Equal(new TextSpan(3, 1), tokens[1].Span);
            Assert.Empty(diagnostics);
        }
    }
}
