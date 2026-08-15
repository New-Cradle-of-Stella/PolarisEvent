using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    public class ParserTests
    {
        private static (ExpressionSyntax Expression, IReadOnlyList<Diagnostic> Diagnostics) ParseExpr(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            ExpressionSyntax expression = parser.ParseExpression();
            return (expression, bag.ToReadOnly());
        }

        // ---- the stage's headline requirement ----

        [Fact]
        public void LinearChain_NoParens_IsFlatNotPrecedenceNested()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a + b * c");
            Assert.Equal("Chain(Name(a), + Name(b), * Name(c))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ExplicitParens_FormGenuineNesting()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a + (b * c)");
            Assert.Equal("Chain(Name(a), + Paren(Chain(Name(b), * Name(c))))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ThreeSegmentChain_StaysFlatRegardlessOfOperatorMix()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a + b - c * d");
            Assert.Equal("Chain(Name(a), + Name(b), - Name(c), * Name(d))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void DoubleParens_NestTwoLevelsDeep()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("((a))");
            Assert.Equal("Paren(Paren(Name(a)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        // ---- primaries ----

        [Theory]
        [InlineData("42", "Literal(42)")]
        [InlineData("3.5", "Literal(3.5)")]
        [InlineData("true", "Literal(true)")]
        [InlineData("false", "Literal(false)")]
        [InlineData("'A'", "Literal('A')")]
        [InlineData("\"hi\"", "Literal(\"hi\")")]
        [InlineData("x", "Name(x)")]
        public void ParsesEachPrimaryKind(string source, string expected)
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr(source);
            Assert.Equal(expected, expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void EmptyParens_ReportsPEVT5016()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("()");
            Assert.Equal("Paren(Missing)", expression.ToString());
            Assert.Equal("PEVT5016", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void UnclosedParen_ReportsPEVT5015AndStillProducesNode()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(a");
            Assert.Equal("Paren(Name(a))", expression.ToString());
            Assert.Equal("PEVT5015", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void UnrecognizedStartOfExpression_ReportsPEVT5001AndProducesMissing()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr(")");
            Assert.Equal("Missing", expression.ToString());
            Assert.Equal("PEVT5001", Assert.Single(diagnostics).Id);
        }

        // ---- conversions ----

        [Fact]
        public void Conversion_AdjacentToVariable_NoDiagnostic()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(float)count");
            Assert.Equal("Conversion(float, count)", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Conversion_SpaceBeforeVariable_ReportsPEVT5014()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(float) count");
            Assert.Equal("Conversion(float, count)", expression.ToString());
            Assert.Equal("PEVT5014", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Conversion_TargetNotVariable_ReportsPEVT5013()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(float)1");
            Assert.Equal("PEVT5013", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void Conversion_InsideChain_ParsesCleanly()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(float)a + b");
            Assert.Equal("Chain(Conversion(float, a), + Name(b))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        // ---- unary ----

        [Fact]
        public void UnaryMinus_WrapsOperand()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("-a");
            Assert.Equal("Unary(-, Name(a))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void LogicalNot_WrapsOperand()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("!a");
            Assert.Equal("Unary(!, Name(a))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ChainedUnaryMinus_NestsInsideChainSegment()
        {
            // 8.4 节示例：a - -b 等价于 a - (-b)——二元减法之后紧跟的 "-" 仍然是一元取负。
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a - -b");
            Assert.Equal("Chain(Name(a), - Unary(-, Name(b)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        // ---- calls ----

        [Fact]
        public void BuiltinCall_NoArguments()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo()");
            Assert.Equal("BuiltinCall(@foo())", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void BuiltinCall_WithArguments()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a, b)");
            Assert.Equal("BuiltinCall(@foo(Name(a), Name(b)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void BuiltinCall_ArgumentIsItselfAChain()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a + b)");
            Assert.Equal("BuiltinCall(@foo(Chain(Name(a), + Name(b))))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void BuiltinCall_MissingArgumentList_ReportsPEVT7004()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo");
            Assert.Equal("PEVT7004", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void CustomBlockCall_WithArguments()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("_playScene(name)");
            Assert.Equal("Call(_playScene(Name(name)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ArgumentList_MissingComma_ReportsPEVT5015AndRecovers()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a b)");
            Assert.Equal("PEVT5015", Assert.Single(diagnostics).Id);
            Assert.IsType<BuiltinCallExpressionSyntax>(expression);
        }

        // ---- status / await / raw cs ----

        [Fact]
        public void Status_ParsesHandleName()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("status a");
            Assert.Equal("Status(a)", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Await_ParsesHandleName()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("await a");
            Assert.Equal("Await(a)", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void RawCs_NoArguments_ParsesContent()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw cs'''code'''");
            Assert.Equal("RawCs(, \"code\")", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void RawCs_WithArguments_ParsesIdentifierList()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw cs (a, b)'''code'''");
            Assert.Equal("RawCs((a, b), \"code\")", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void RawCmd_UsedAsExpression_ReportsPEVT8006OnlyOnce()
        {
            // $raw cmd 语法本身合法，只是不产生值；恢复时消费掉 cmd 后继续找真正的 raw 块，
            // 不应该因为"漏写 cs"而再报第二条诊断。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw cmd'''x'''");
            Assert.Equal("PEVT8006", Assert.Single(diagnostics).Id);
        }

        // ---- comparisons across the chain ----

        [Fact]
        public void ComparisonOperators_ChainAcrossMultipleSegments()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a == b != c");
            Assert.Equal("Chain(Name(a), == Name(b), != Name(c))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("a < b", "Chain(Name(a), < Name(b))")]
        [InlineData("a <= b", "Chain(Name(a), <= Name(b))")]
        [InlineData("a > b", "Chain(Name(a), > Name(b))")]
        [InlineData("a >= b", "Chain(Name(a), >= Name(b))")]
        [InlineData("a & b", "Chain(Name(a), & Name(b))")]
        [InlineData("a | b", "Chain(Name(a), | Name(b))")]
        [InlineData("a ^ b", "Chain(Name(a), ^ Name(b))")]
        [InlineData("a % b", "Chain(Name(a), % Name(b))")]
        [InlineData("a / b", "Chain(Name(a), / Name(b))")]
        public void EveryBinaryOperatorKind_FormsAOneSegmentChain(string source, string expected)
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr(source);
            Assert.Equal(expected, expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ParenthesizedExpression_UsedAsChainSegmentOperand()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(a + b) * c");
            Assert.Equal("Chain(Paren(Chain(Name(a), + Name(b))), * Name(c))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void UnaryLogicalNot_OnParenthesizedExpression()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("!(a == b)");
            Assert.Equal("Unary(!, Paren(Chain(Name(a), == Name(b))))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void UnaryMinus_OnCallExpression()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("-_getValue()");
            Assert.Equal("Unary(-, Call(_getValue()))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MinusAfterCompleteOperand_IsBinarySubtractionNotUnary()
        {
            // 8.4 节：左侧已经存在完整操作数时，"-" 解释为二元减法，不是一元取负。
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a - b");
            Assert.Equal("Chain(Name(a), - Name(b))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void NegativeIntegerLiteral_MinInt_ParsesAsUnaryOverLiteral()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("-2147483648");
            Assert.Equal("Unary(-, Literal(2147483648))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        // ---- argument list edge cases ----

        [Fact]
        public void ArgumentList_TrailingCommaBeforeCloseParen_ReportsPEVT5015()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a,)");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5015" || d.Id == "PEVT5001");
        }

        [Fact]
        public void ArgumentList_NestedCallsParseCorrectly()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@outer(@inner(x))");
            Assert.Equal("BuiltinCall(@outer(BuiltinCall(@inner(Name(x)))))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ArgumentList_ThreeArguments_AllParsedInOrder()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a, b, c)");
            Assert.Equal("BuiltinCall(@foo(Name(a), Name(b), Name(c)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ArgumentList_UnclosedAtEndOfFile_ReportsMissingCloseParen()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@foo(a");
            Assert.IsType<BuiltinCallExpressionSyntax>(expression);
            Assert.NotEmpty(diagnostics);
        }

        // ---- raw cs argument list errors ----

        [Fact]
        public void RawCs_ArgumentListMissingIdentifier_ReportsPEVT8012()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw cs (1)'''x'''");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8012");
        }

        [Fact]
        public void RawCs_ArgumentListUnclosed_ReportsPEVT8011()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw cs (a'''x'''");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8011");
        }

        [Fact]
        public void RawCs_MultipleIdentifierArguments_ParsesAll()
        {
            (ExpressionSyntax expression, _) = ParseExpr("$raw cs (a, b, c)'''code'''");
            Assert.Equal("RawCs((a, b, c), \"code\")", expression.ToString());
        }

        [Fact]
        public void RawCs_MissingCsKeyword_ReportsPEVT8002()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("$raw '''x'''");
            Assert.Equal("PEVT8002", Assert.Single(diagnostics).Id);
        }

        // ---- missing operand / EOF recovery ----

        [Fact]
        public void MissingRightOperand_AtEndOfFile_ReportsPEVT5001()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a +");
            Assert.Equal("Chain(Name(a), + Missing)", expression.ToString());
            Assert.Equal("PEVT5001", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void DoubleUnaryMinus_OnLiteral()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("- -5");
            Assert.Equal("Unary(-, Unary(-, Literal(5)))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void LongChain_FiveSegments_StaysFlat()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("a + b - c * d / e");
            Assert.Equal("Chain(Name(a), + Name(b), - Name(c), * Name(d), / Name(e))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void PlainIdentifierNotFollowedByParen_IsJustAName()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("_notActuallyCalled");
            Assert.Equal("Name(_notActuallyCalled)", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ExpressionSpan_CoversFromFirstTokenToLastToken()
        {
            (ExpressionSyntax expression, _) = ParseExpr("a + b");
            Assert.Equal(new TextSpan(0, 5), expression.Span);
        }

        [Fact]
        public void ConversionSpan_CoversFromOpenParenToVariableEnd()
        {
            (ExpressionSyntax expression, _) = ParseExpr("(float)count");
            Assert.Equal(new TextSpan(0, 12), expression.Span);
        }

        [Fact]
        public void StringConversion_AdjacentToVariable_NoDiagnostic()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("(string)letter");
            Assert.Equal("Conversion(string, letter)", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void CustomBlockCall_NoArguments()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("_playOpening()");
            Assert.Equal("Call(_playOpening())", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void BuiltinCall_ChainedInsideAnotherOperand()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("@query(name) == true");
            Assert.Equal("Chain(BuiltinCall(@query(Name(name))), == Literal(true))", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void CharLiteral_WithEscapeSequence_RendersRawText()
        {
            (ExpressionSyntax expression, IReadOnlyList<Diagnostic> diagnostics) = ParseExpr("'\\n'");
            Assert.Equal("Literal('\\n')", expression.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void RawCs_ContentTokenValue_IsExposedThroughTheNode()
        {
            (ExpressionSyntax expression, _) = ParseExpr("$raw cs'''@dialogue(\"hi\")'''");
            var rawCs = Assert.IsType<RawCsExpressionSyntax>(expression);
            Assert.Equal("@dialogue(\"hi\")", rawCs.Content.Value.AsString);
            Assert.Null(rawCs.Arguments);
        }

        [Fact]
        public void BuiltinCallExpression_ExposesNameAndArgumentTokens()
        {
            (ExpressionSyntax expression, _) = ParseExpr("@dialogue(\"Alice\", \"hi\")");
            var call = Assert.IsType<BuiltinCallExpressionSyntax>(expression);
            Assert.Equal("dialogue", call.Name.Text);
            Assert.Equal(2, call.Arguments.Arguments.Count);
            Assert.Single(call.Arguments.Commas);
        }

        [Fact]
        public void ChainedBinaryExpression_ExposesFirstOperandAndSegmentsDirectly()
        {
            (ExpressionSyntax expression, _) = ParseExpr("a + b * c");
            var chain = Assert.IsType<ChainedBinaryExpressionSyntax>(expression);
            Assert.IsType<NameExpressionSyntax>(chain.First);
            Assert.Equal(2, chain.Segments.Count);
            Assert.Equal(SyntaxKind.PlusToken, chain.Segments[0].OperatorToken.Kind);
            Assert.Equal(SyntaxKind.StarToken, chain.Segments[1].OperatorToken.Kind);
        }
    }
}
