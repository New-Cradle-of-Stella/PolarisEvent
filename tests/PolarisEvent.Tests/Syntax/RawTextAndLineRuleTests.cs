using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    /// <summary>
    /// 阶段 4 的 golden 测试：三单引号 raw 块、字符串加号续接列对齐规则，以及一行一语句/
    /// 非法跨行的词法/预解析诊断（PEVT1005–1013、8003–8005、8016–8017）。
    /// </summary>
    public class RawTextAndLineRuleTests
    {
        private static (IReadOnlyList<SyntaxToken> Tokens, IReadOnlyList<Diagnostic> Diagnostics) Lex(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            return (tokens, bag.ToReadOnly());
        }

        // ---- raw blocks ----

        [Fact]
        public void Raw_SimpleBlock_ProducesOpenContentCloseWithDecodedText()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''hello world'''");
            Assert.Equal(new[] { SyntaxKind.CmdKeyword, SyntaxKind.TripleQuoteToken, SyntaxKind.RawContentToken, SyntaxKind.TripleQuoteToken, SyntaxKind.EndOfFileToken },
                tokens.Select(t => t.Kind));
            Assert.Equal("hello world", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_EscapedDelimiter_DecodesToLiteralTripleQuoteAndDoesNotClose()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''a\\'''b'''");
            Assert.Equal(SyntaxKind.RawContentToken, tokens[2].Kind);
            Assert.Equal("a'''b", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_UnterminatedBlock_ReportsPEVT8004WithMissingCloseToken()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''abc");
            Assert.Equal("abc", tokens[2].Value.AsString);
            Assert.True(tokens[3].IsMissing);
            Assert.Equal("PEVT8004", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Raw_MalformedDelimiterEscape_ReportsPEVT8017ButKeepsScanning()
        {
            // 反斜杠后只有一个引号，凑不满 \''' ——明显是想转义却没写对形式。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''a\\'b'''");
            Assert.Equal(SyntaxKind.RawContentToken, tokens[2].Kind);
            Assert.Equal("a\\'b", tokens[2].Value.AsString);
            Assert.Equal("PEVT8017", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("$raw cmd end")]
        [InlineData("$raw cs end")]
        public void Raw_DollarRawCmdOrBareCsNotFollowedByBlock_ReportsPEVT8003(string source)
        {
            // 8003 只在 cmd/cs 紧跟在 $raw 后面时才有意义；裸的 "cmd"/"cs"（比如 enable cs 的 cs）
            // 不该触发这条检查——见下面 Raw_BareCmdOrCsWithoutDollarRaw_DoesNotTriggerPEVT8003。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Equal("PEVT8003", Assert.Single(diagnostics).Id);
            Assert.Equal(SyntaxKind.EndKeyword, tokens[2].Kind); // 诊断不影响后续正常分词
        }

        [Theory]
        [InlineData("cmd end")]
        [InlineData("cs end")]
        public void Raw_BareCmdOrCsWithoutDollarRaw_DoesNotTriggerPEVT8003(string source)
        {
            // "cs" 同时是 enable cs 的能力名；没有前面的 $raw，裸 cmd/cs 完全不该触发"漏写 raw 块"检查。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_CsWithArgumentList_SkipsPEVT8003Check()
        {
            // 有参数列表的 cs (...) 需要先跳过整个列表才知道漏没漏写 raw 块，本阶段不检查这种情形
            // （见 CheckRawBlockFollows 的文档注释），因此即使后面确实没有 raw 块也不会误报。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cs (a)end");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_OpenDelimiterNotAdjacentToCmd_ReportsPEVT8016()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd '''x'''");
            Assert.Equal(SyntaxKind.TripleQuoteToken, tokens[1].Kind);
            Assert.Equal("PEVT8016", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Raw_OpenDelimiterAdjacentToArgumentListCloseParen_NoPEVT8016()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cs (a)'''x'''");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_TrailingContentAfterCloseDelimiter_ReportsPEVT8005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''x''' garbage");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal("PEVT8005", diagnostic.Id);
        }

        [Fact]
        public void Raw_NothingAfterCloseDelimiterOnSameLine_NoPEVT8005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''x'''\nend");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_ContentSpanningMultipleLines_PreservesEmbeddedLineBreaks()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''line1\nline2'''");
            Assert.Equal("line1\nline2", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_ContentWithCrLf_PreservesOriginalLineEndingBytes()
        {
            // 12.4 节：分隔符之间的内容按原文保留，不对换行做归一化。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''a\r\nb'''");
            Assert.Equal("a\r\nb", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_EscapedDelimiterAtStartOfContent_DecodesCorrectly()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''\\'''text'''");
            Assert.Equal("'''text", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_MultipleMalformedEscapeAttempts_ReportsOneDiagnosticEach()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''a\\'b\\'c'''");
            Assert.Equal(2, diagnostics.Count(d => d.Id == "PEVT8017"));
        }

        [Fact]
        public void Raw_CommentBetweenArgumentListAndOpenDelimiter_ReportsPEVT8016()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cs (a)/* gap */'''x'''");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8016");
        }

        [Fact]
        public void Raw_CommentBetweenCmdAndOpenDelimiter_ReportsOnlyPEVT8016NotPEVT8003()
        {
            // 前瞻检查（PEVT8003）会跳过注释找到后面确实存在的 '''，所以不会漏报"没写 raw 块"；
            // 但真正产出 open-delimiter token 时，那段注释 trivia 仍然说明它没有紧贴 cmd：PEVT8016。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd/* gap */'''x'''");
            Assert.Equal("PEVT8016", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Raw_ContentContainingParenCharacters_DoesNotAffectParenDepthTracking()
        {
            // raw 内容是不透明字节，不会被重新词法分析；里面的 "(" 不是真正的 OpenParenToken。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''(unbalanced'''\nend");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_FullDollarRawCmdSequence_TokenizesEndToEnd()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$raw cmd'''@dialogue(\"hi\")'''");
            Assert.Equal(
                new[]
                {
                    SyntaxKind.DollarRawToken, SyntaxKind.CmdKeyword, SyntaxKind.TripleQuoteToken,
                    SyntaxKind.RawContentToken, SyntaxKind.TripleQuoteToken, SyntaxKind.EndOfFileToken,
                },
                tokens.Select(t => t.Kind));
            Assert.Equal("@dialogue(\"hi\")", tokens[3].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_FullDollarRawCsWithArgumentsSequence_TokenizesEndToEnd()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$raw cs (count, name)'''count += 1;'''");
            Assert.Equal(
                new[]
                {
                    SyntaxKind.DollarRawToken, SyntaxKind.CsKeyword, SyntaxKind.OpenParenToken,
                    SyntaxKind.IdentifierToken, SyntaxKind.CommaToken, SyntaxKind.IdentifierToken,
                    SyntaxKind.CloseParenToken, SyntaxKind.TripleQuoteToken, SyntaxKind.RawContentToken,
                    SyntaxKind.TripleQuoteToken, SyntaxKind.EndOfFileToken,
                },
                tokens.Select(t => t.Kind));
            Assert.Equal("count += 1;", tokens[8].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_BareCsWithNoArgumentsAdjacentToDelimiter_NoDiagnostics()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$raw cs'''var a = 1;'''");
            Assert.Equal(
                new[]
                {
                    SyntaxKind.DollarRawToken, SyntaxKind.CsKeyword, SyntaxKind.TripleQuoteToken,
                    SyntaxKind.RawContentToken, SyntaxKind.TripleQuoteToken, SyntaxKind.EndOfFileToken,
                },
                tokens.Select(t => t.Kind));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_TokenSpans_CoverExactlyTheirOwnDelimiterOrContent()
        {
            // "raw 内容保持原字节对应的字符文本与完整 span"：开始分隔符、内容、结束分隔符三个 token
            // 的 Span 必须首尾相接、互不重叠，且 Text 与 Span 描述的源码片段完全一致。
            const string source = "cmd'''abc'''";
            (IReadOnlyList<SyntaxToken> tokens, _) = Lex(source);
            SyntaxToken open = tokens[1];
            SyntaxToken content = tokens[2];
            SyntaxToken close = tokens[3];

            Assert.Equal(new TextSpan(3, 3), open.Span);
            Assert.Equal(new TextSpan(6, 3), content.Span);
            Assert.Equal(new TextSpan(9, 3), close.Span);
            Assert.Equal("abc", content.Text);
        }

        [Fact]
        public void Raw_AdjacencyDiagnosticLocation_PointsAtOpenDelimiter()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd '''x'''");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(new TextSpan(4, 3), diagnostic.Location.Span);
        }

        [Fact]
        public void Raw_AdjacencyDiagnosticMessage_MatchesCatalogDefaultMessage()
        {
            // DiagnosticBagCatalogExtensions.AddFromCatalog 应该原样用目录里的默认消息，
            // 不应该在这条路径上产生自定义措辞——这里用一个阶段 4 才会真正触发的诊断验证接线正确。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd '''x'''");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            DiagnosticDescriptor descriptor = DiagnosticCatalog.Find("PEVT8016");
            Assert.Equal(descriptor.DefaultMessage, diagnostic.Message);
            Assert.Equal(descriptor.Severity, diagnostic.Severity);
        }

        [Fact]
        public void Raw_OpenDelimiterOnNextPhysicalLineAfterCmd_StillReportsPEVT8016()
        {
            // 紧贴约束不只是"别有空格"，换行同样破坏紧贴——即使换行本身不违反别的规则。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd\n'''x'''");
            Assert.Equal("PEVT8016", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Raw_EmptyContent_IsValidWithEmptyDecodedString()
        {
            // 12.4 节没有禁止空原始文本块；开始分隔符和结束分隔符直接相邻也应该合法。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd''''''");
            Assert.Equal(SyntaxKind.RawContentToken, tokens[2].Kind);
            Assert.Equal(string.Empty, tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_UnterminatedBlockDiagnosticLocation_SpansTheConsumedContent()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''abc");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal("PEVT8004", diagnostic.Id);
            Assert.Equal(new TextSpan(6, 3), diagnostic.Location.Span);
        }

        [Fact]
        public void Raw_ContentImmediatelyFollowedByEscapedDelimiterThenRealCloser_OnlyLastOneCloses()
        {
            // 连续两次 \''' 之后紧跟真正的结束分隔符：前两次都必须被当成字面内容，只有第三次
            // (未被反斜杠转义) 才真正结束原始文本块。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''\\'''\\'''done'''");
            Assert.Equal("'''" + "'''" + "done", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_BackslashNotFollowedByQuote_IsKeptLiterally()
        {
            // 12.4 节：除 \''' 外，反斜杠在原始文本块中不具有转义意义，按原文保留。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''a\\nb'''");
            Assert.Equal("a\\nb", tokens[2].Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("cmd'''x''' \t ")]
        [InlineData("cmd'''x'''\t")]
        public void Raw_OnlyTrailingWhitespaceAfterCloseDelimiter_NoPEVT8005(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Raw_ExtraArgumentSpanExcludesTrailingLineBreak()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''x''' bogus\nend");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal("PEVT8005", diagnostic.Id);
            Assert.Equal("bogus", diagnostic.Location.Source.GetText(diagnostic.Location.Span));
        }

        [Fact]
        public void Raw_MissingCloseDelimiterAfterEscapedAttempt_StillReportsPEVT8004()
        {
            // 内容以一次没凑够三个引号的转义尝试收尾，紧接着就是文件末尾——8017 和 8004 都要报，
            // 顺序不影响：先报形式不对的转义尝试，再报最终仍然没有找到收尾分隔符。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''abc\\'");
            Assert.Equal(2, diagnostics.Count);
            Assert.Equal("PEVT8017", diagnostics[0].Id);
            Assert.Equal("PEVT8004", diagnostics[1].Id);
            Assert.True(tokens[^2].IsMissing);
        }

        [Fact]
        public void Raw_CsArgumentListWithMultipleParameters_LexesEachIdentifierSeparately()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("$raw cs (a, b, c)'''body'''");
            IReadOnlyList<SyntaxToken> identifiers = tokens.Where(t => t.Kind == SyntaxKind.IdentifierToken).ToList();
            Assert.Equal(new[] { "a", "b", "c" }, identifiers.Select(t => t.Text));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void OneStatementPerLine_ThreeLeadersOnOneLine_ReportsOneDiagnosticPerRepeat()
        {
            // 第一个 end 立起这一行的基准；第二、第三个各自和"当前基准行"比较，各报一次——
            // 报告次数和"多出来的语句数"对应，而不是恒定报一次。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("end end end");
            Assert.Equal(2, diagnostics.Count(d => d.Id == "PEVT1005"));
        }

        [Fact]
        public void OneStatementPerLine_VarDeclarationsOnSeparateLines_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("var a : int\nvar b : int\nend");
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("switch x\ncase 1\nendswitch")]
        [InlineData("while x\nendwhile")]
        public void OneStatementPerLine_StructuredFlowAcrossSeparateLines_NoDiagnostic(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void IllegalLineBreak_MultipleDanglingLinesInsideParens_ReportsOnePerLineBreak()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("@foo(a,\nb,\nc)");
            Assert.Equal(2, diagnostics.Count(d => d.Id == "PEVT1006"));
        }

        [Fact]
        public void IllegalLineBreak_DanglingComparisonOperator_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("if a ==\nb\nendif");
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void IllegalLineBreak_DanglingLogicalOperator_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("if a &\nb\nendif");
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        // ---- multiline string "+" continuation ----

        [Fact]
        public void MultilineString_TwoAlignedSegments_MergeWithLfSeparator()
        {
            const string prefix = "var x : string = ";
            int column = prefix.Length + 1;
            string source = prefix + "\"line1\" +\n" + new string(' ', column - 1) + "\"line2\"";

            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            SyntaxToken stringToken = tokens.Single(t => t.Kind == SyntaxKind.StringLiteralToken);
            Assert.Equal("line1\nline2", stringToken.Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_ThreeAlignedSegments_MergeInOrder()
        {
            const string callPrefix = "@dialogue(\"Alice\", ";
            string indent = new string(' ', callPrefix.Length);
            string source = callPrefix + "\"第一行\" +\n" + indent + "\"第二句\" +\n" + indent + "\"第三句\")";

            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            SyntaxToken merged = tokens.Last(t => t.Kind == SyntaxKind.StringLiteralToken);
            Assert.Equal("第一行\n第二句\n第三句", merged.Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_MisalignedContinuationColumn_ReportsPEVT1011()
        {
            const string prefix = "var x : string = ";
            int column = prefix.Length + 1;
            string source = prefix + "\"line1\" +\n" + new string(' ', column) + "\"line2\""; // 多缩进一格

            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Equal("PEVT1011", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_TabIndentBeforeContinuation_ReportsPEVT1012()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\n\t\"b\"");
            Assert.Equal("PEVT1012", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_BlankLineBetweenSegments_ReportsPEVT1012()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\n\n\"b\"");
            Assert.Equal("PEVT1012", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_CommentLineBetweenSegments_ReportsPEVT1012()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\n// oops\n\"b\"");
            Assert.Equal("PEVT1012", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_NextLineNotAString_ReportsPEVT1012()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\nend");
            Assert.Equal("PEVT1012", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_EndOfFileRightAfterPlus_ReportsPEVT1013()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +");
            Assert.Equal("PEVT1013", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_EndOfFileOnContinuationLine_ReportsPEVT1013()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\n   ");
            Assert.Equal("PEVT1013", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_SameLinePlus_IsOrdinaryBinaryOperator()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" + \"b\"");
            Assert.Equal(new[] { SyntaxKind.StringLiteralToken, SyntaxKind.PlusToken, SyntaxKind.StringLiteralToken, SyntaxKind.EndOfFileToken },
                tokens.Select(t => t.Kind));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_FollowedByUnclosedParenToken_ResumesNormalLexingAfterMerge()
        {
            const string callPrefix = "@dialogue(\"Alice\", ";
            string indent = new string(' ', callPrefix.Length);
            string source = callPrefix + "\"a\" +\n" + indent + "\"b\")";

            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.Equal(SyntaxKind.CloseParenToken, tokens[^2].Kind);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_FourSegmentsAtColumnOne_MergesAllInOrder()
        {
            string source = "\"a\" +\n\"b\" +\n\"c\" +\n\"d\"";
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            SyntaxToken merged = tokens.Single(t => t.Kind == SyntaxKind.StringLiteralToken);
            Assert.Equal("a\nb\nc\nd", merged.Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_SegmentsWithEscapes_DecodeEachSegmentIndependently()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\\n\" +\n\"b\\t\"");
            SyntaxToken merged = tokens.Single(t => t.Kind == SyntaxKind.StringLiteralToken);
            Assert.Equal("a\n\nb\t", merged.Value.AsString);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultilineString_MergedToken_SpanCoversFirstQuoteToLastQuote()
        {
            const string source = "\"a\" +\n\"b\"";
            (IReadOnlyList<SyntaxToken> tokens, _) = Lex(source);
            SyntaxToken merged = tokens.Single(t => t.Kind == SyntaxKind.StringLiteralToken);

            Assert.Equal(0, merged.Span.Start);
            Assert.Equal(source.Length, merged.Span.End);
            Assert.Equal(source, merged.Text);
        }

        [Fact]
        public void MultilineString_ContinuationSegmentItselfUnterminated_ReportsPEVT1002()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +\n\"b");
            Assert.Equal("PEVT1002", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_MisalignedSecondOfThreeSegments_StopsMergingAndReportsPEVT1011()
        {
            const string callPrefix = "@dialogue(\"Alice\", ";
            string correctIndent = new string(' ', callPrefix.Length);
            string wrongIndent = correctIndent + " "; // 多一格
            string source = callPrefix + "\"a\" +\n" + wrongIndent + "\"b\" +\n" + correctIndent + "\"c\")";

            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);

            // 失败后不再尝试合并 "a"："a" 单独成一个 token，"b" 从它自己所在行重新按正常 token 参与
            // 词法分析——但 "b" 之后同样跟着一个行末 "+"，于是词法器把它当成一次新的续接尝试，
            // 拿 "b" 自己的列号去核对 "c"，发现同样对不上列，因此正确地又报了一次 PEVT1011：
            // 一次列错位的输入，级联出两条诊断，根因都可追溯到同一处多出来的缩进空格。
            Assert.All(diagnostics, d => Assert.Equal("PEVT1011", d.Id));
            Assert.Equal(2, diagnostics.Count);

            Assert.Contains(tokens, t => t.Kind == SyntaxKind.StringLiteralToken && t.Value.Kind != TokenValueKind.None && t.Value.AsString == "a");
        }

        // ---- one-statement-per-line (PEVT1005) ----

        [Fact]
        public void OneStatementPerLine_TwoLeaderKeywordsOnSameLine_ReportsPEVT1005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("end end");
            Assert.Equal("PEVT1005", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void OneStatementPerLine_LeaderKeywordsOnSeparateLines_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("if x\nendif");
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("async block _foo()\nendblock")]
        [InlineData("goto #Label")]
        [InlineData("handler h = callevt \"X\"")]
        [InlineData("var x : bool = await a")]
        public void OneStatementPerLine_KnownAmbiguousPairs_DoNotFalsePositive(string source)
        {
            // async/block、goto/#、handler/callevt、var-初始化器里的 await
            // 都会让两个"看起来像语句起始"的 token 落在同一行——这些都是合法的单条语句，
            // 因此故意没把 async、#、callevt、await 计入 StatementLeaders。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        // ---- illegal mid-construct line breaks (PEVT1006) ----

        [Fact]
        public void IllegalLineBreak_InsideUnclosedParens_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("@foo(a,\nb)");
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void IllegalLineBreak_DanglingArithmeticPlus_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("var x : int = 1 +\n2");
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void IllegalLineBreak_DanglingColon_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("var x :\nint");
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void IllegalLineBreak_BalancedParensOnOneLine_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("@foo(a, b)\nend");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void IllegalLineBreak_NestedParensStillOpenAtInnerLevel_ReportsPEVT1006()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("@foo(bar(a,\nb), c)");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1006");
        }

        [Fact]
        public void IllegalLineBreak_UnbalancedCloseParenDoesNotUnderflowDepth()
        {
            // 多出来的右括号不应该把内部计数减到负数，否则后面本该报的 1006 会被吞掉。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex(")\n@foo(a,\nb)");
            Assert.Single(diagnostics, d => d.Id == "PEVT1006");
        }

        [Fact]
        public void OneStatementPerLine_LeaderAfterMultilineRawBlock_UsesRawBlockClosingLine()
        {
            // raw 内容自己的换行不经过 LexLineBreak，也不会写 _lastStatementLeaderLine；
            // 关键是结束分隔符所在行之后的 "end end" 仍然按正常规则各自定位到正确的物理行。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("cmd'''line1\nline2'''\nend end");
            Assert.Equal("PEVT1005", Assert.Single(diagnostics).Id);
        }

        // ---- additional boundary checks ----

        [Fact]
        public void Raw_OpenDelimiterAfterOrdinaryIdentifier_IsNotAdjacencyChecked()
        {
            // 8016 只在紧邻 cmd/cs/参数列表右括号时才有意义；跟在普通标识符后面的 "'''" 不属于
            // 这三种前置 token，即使中间有空格也不该被当成"漏写紧贴"来报错——那本来就不是合法位置，
            // 交给以后的解析阶段去处理"这里出现 raw 块毫无道理"这类语法层面的问题。
            (_, IReadOnlyList<Diagnostic> diagnostics) = Lex("foo '''x'''");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT8016");
        }

        [Fact]
        public void MultilineString_PlusFollowedByCommentBeforeLineEnd_DoesNotTriggerContinuationButStillReportsPEVT1006()
        {
            // "+" 后面除空格外还有别的东西（这里是注释）——不满足续接触发形状，交还给普通 dispatch；
            // 但普通 dispatch 产出的这个裸 "+" 本身仍然是"换行前悬空的运算符"，一样违反 1.2 节，
            // 因此仍然通过通用的 PEVT1006 检查报出来，只是不再经过续接合并逻辑。
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" +// oops\nb");
            Assert.Contains(tokens, t => t.Kind == SyntaxKind.PlusToken);
            Assert.Equal("PEVT1006", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MultilineString_PlusFollowedByExtraTokenOnSameLine_IsOrdinaryBinaryPlusNotContinuation()
        {
            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex("\"a\" + 1");
            Assert.Equal(new[] { SyntaxKind.StringLiteralToken, SyntaxKind.PlusToken, SyntaxKind.IntegerLiteralToken, SyntaxKind.EndOfFileToken },
                tokens.Select(t => t.Kind));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void FullSnippet_RealisticEventWithRawBlockAndMultilineString_TokenizesCleanly()
        {
            const string declPrefix = "var greeting : string = ";
            string indent = new string(' ', declPrefix.Length);
            string source =
                "id \"MuseumEntrance\"\n\n" +
                declPrefix + "\"Welcome to the \" +\n" +
                indent + "\"museum.\"\n" +
                "@dialogue(\"Curator\", greeting)\n" +
                "$raw cmd'''\n" +
                "give_item(42)\n" +
                "'''\n" +
                "end";

            (IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics) = Lex(source);

            Assert.Empty(diagnostics);
            Assert.Equal(SyntaxKind.EndOfFileToken, tokens[^1].Kind);
            Assert.Equal(SyntaxKind.EndKeyword, tokens[^2].Kind);

            SyntaxToken merged = tokens.Single(t =>
                t.Kind == SyntaxKind.StringLiteralToken && t.Value.Kind == TokenValueKind.String && t.Value.AsString.Contains("Welcome"));
            Assert.Equal("Welcome to the \nmuseum.", merged.Value.AsString);

            SyntaxToken rawContent = tokens.Single(t => t.Kind == SyntaxKind.RawContentToken);
            Assert.Equal("\ngive_item(42)\n", rawContent.Value.AsString);
        }
    }
}
