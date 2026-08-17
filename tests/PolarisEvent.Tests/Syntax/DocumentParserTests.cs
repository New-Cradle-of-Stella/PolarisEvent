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
    /// 阶段 6 的 golden 测试：最小合法文件、文件头顺序规则、每类孤立闭合符、错位分支和嵌套恢复快照。
    /// </summary>
    public class DocumentParserTests
    {
        private static (DocumentSyntax Document, IReadOnlyList<Diagnostic> Diagnostics) ParseDoc(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            DocumentSyntax document = parser.ParseDocument();
            return (document, bag.ToReadOnly());
        }

        // ---- minimal legal file ----

        [Fact]
        public void MinimalLegalFile_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"MuseumEntrance\"\n\nend");
            Assert.Equal("Document(Id(\"MuseumEntrance\"), [End])", document.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MinimalLegalFileWithEnableDeclarations_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"X\"\nenable cs\nenable async\nend");
            Assert.Equal(2, document.EnableDeclarations.Count);
            Assert.Empty(diagnostics);
        }

        // ---- file header ordering ----

        [Fact]
        public void MissingEventId_ReportsPEVT1101()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("end");
            Assert.Null(document.IdDeclaration);
            Assert.Equal("PEVT1101", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void IdNotFirst_ReportsPEVT1102()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("end\nid \"X\"\nend");
            Assert.Equal("PEVT1102", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("id \"A\"\nid \"B\"\nend", "PEVT1103")]
        [InlineData("id\nend", "PEVT1104")]
        [InlineData("id 42\nend", "PEVT1105")]
        [InlineData("id \"A\" \"B\"\nend", "PEVT1106")]
        [InlineData("id \"A\"\nend\nenable cs\nend", "PEVT1107")]
        [InlineData("id \"A\"\nenable cs\nenable cs\nend", "PEVT1108")]
        [InlineData("id \"A\"\nenable foo\nend", "PEVT1109")]
        [InlineData("id \"\"\nend", "PEVT1110")]
        [InlineData("id \"a!\"\nend", "PEVT1111")]
        public void HeaderDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("id \"MuseumEntrance\"\nend")]
        [InlineData("id \"博物馆入口\"\nend")]
        [InlineData("id \"Museum博物馆1\"\nend")]
        // CJK 统一表意文字扩展 A（仍在 BMP 内）：U+3400。
        [InlineData("id \"㐀\"\nend")]
        // CJK 统一表意文字扩展 B（补充平面，一对 UTF-16 代理项才能表示一个标量值）：U+20000。
        [InlineData("id \"𠀀\"\nend")]
        [InlineData("id \"\uFA0E\"\nend")]
        [InlineData("id \"\U0002EBF0\"\nend")]
        [InlineData("id \"\U00031350\"\nend")]
        public void EventId_ValidAsciiChineseOrMixedForms_ParseCleanly(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
        }

        [Theory]
        // 10A-R01：Unified_Ideograph 必须按精确的 Unicode 标量值区段判断，不能近似成
        // UnicodeCategory.OtherLetter——那个类别同时也覆盖日文假名、韩文谚文等其他文字。
        [InlineData("id \"あ\"\nend")] // 平假名 あ (U+3042)：OtherLetter，但不是 Unified_Ideograph。
        [InlineData("id \"ア\"\nend")] // 片假名 ア (U+30A2)。
        [InlineData("id \"가\"\nend")] // 韩文谚文音节 가 (U+AC00)。
        [InlineData("id \"ا\"\nend")] // 阿拉伯字母 ا (U+0627)。
        [InlineData("id \" \"\nend")] // 空白。
        [InlineData("id \"a b\"\nend")] // 标点/空白混入。
        [InlineData("id \"a_b\"\nend")] // 下划线。
        [InlineData("id \"\U0002B81E\"\nend")]
        [InlineData("id \"\U0003134B\"\nend")]
        public void EventId_NonUnifiedIdeographs_ReportPEVT1111(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal("PEVT1111", Assert.Single(diagnostics).Id);
        }

        // ---- var/const/assignment ----

        [Fact]
        public void VariableDeclaration_WithoutInitializer_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int\nend");
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.Equal("Var(x, int)", decl.ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void VariableDeclaration_WithInitializer_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int = 1\nend");
            Assert.Equal("Var(x, int, Literal(1))", document.Statements[0].ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ConstantDeclaration_MissingInitializer_ReportsPEVT6009()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nconst x : int\nend");
            Assert.Equal("PEVT6009", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void ConstantDeclaration_WithInitializer_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nconst x : int = 1\nend");
            Assert.Equal("Const(x, int, Literal(1))", document.Statements[0].ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MissingDeclarationName_ReportsPEVT6004()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar : int\nend");
            Assert.Equal("PEVT6004", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void TypeWrittenBeforeName_ReportsPEVT6006AndStillRecoversType()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar int x\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6006");
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.Equal("int", decl.Type.Text);
        }

        [Fact]
        public void Assignment_ParsesTargetAndValue()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int\nx = 5\nend");
            Assert.Equal("Assign(x, Literal(5))", document.Statements[1].ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void UnknownTypeName_ReportsPEVT5010()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : foo\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5010");
        }

        [Fact]
        public void UnknownTypeName_ConsumesCandidate_DoesNotLeakIntoNextStatement()
        {
            // 10A-R04：无效的类型候选（"foo"）必须被消费掉并同步到物理行结束，不能原样留在流里
            // 被外层循环当成一条全新的、同样莫名其妙的语句——只应该有 PEVT5010 这一条主诊断。
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : foo\nend");
            Assert.Equal("PEVT5010", Assert.Single(diagnostics).Id);
            Assert.Equal(2, document.Statements.Count);
            Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.IsType<EndStatementSyntax>(document.Statements[1]);
        }

        [Fact]
        public void AssignmentNestedInsideAnotherAssignment_ReportsPEVT5022()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int\nx = x = 1\nend");
            Assert.Equal("PEVT5022", Assert.Single(diagnostics).Id);
            Assert.Equal("Assign(x, Name(x))", document.Statements[1].ToString());
        }

        [Fact]
        public void AssignmentNestedInsideInitializer_ReportsPEVT5022()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int\nvar y : int = x = 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5022");
        }

        [Fact]
        public void AssignmentNestedInsideCallArgument_ReportsPEVT5022()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : int\n@foo(x = 1)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5022");
        }

        [Fact]
        public void BooleanDeclaration_NumericInitializer_ReportsPEVT5023()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar b : bool = 1\nend");
            Assert.Equal("PEVT5023", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void BooleanDeclaration_TrueOrFalseInitializer_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar b : bool = true\nend");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void ReservedKeywordAsVariableName_ReportsPEVT6013()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar if : int\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6013");
        }

        [Fact]
        public void ReservedKeywordAsVariableName_ConsumesCandidate_DoesNotReparseAsFakeIfStatement()
        {
            // "if" 必须被消费掉并同步到物理行结束（连带 ": int"），不能原样留在流里被外层循环重新当成一个真正的 if 语句起始。
            // 只应该有 PEVT6013 这一条主诊断，且第二条语句必须仍然是 EndStatementSyntax。
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar if : int\nend");
            Assert.Equal("PEVT6013", Assert.Single(diagnostics).Id);
            Assert.Equal(2, document.Statements.Count);
            Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.IsType<EndStatementSyntax>(document.Statements[1]);
        }

        [Fact]
        public void ReservedKeywordAsVariableName_TwoIndependentDeclarationErrors_BothReported()
        {
            // 10A-R04 的第三个反例：同文件两个独立的声明错误必须各自单独报出来，互不吞没。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar if : int\nvar goto : int\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6013");
            Assert.Equal(2, diagnostics.Count(d => d.Id == "PEVT6013"));
        }

        [Fact]
        public void ReservedKeywordAsHandlerName_ReportsPEVT6013()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nhandler goto = @a()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6013");
        }

        // ---- if/elif/else/endif ----

        [Fact]
        public void IfStatement_FullForm_ParsesCleanly()
        {
            const string source = "id \"A\"\nif a\nvar x : int = 1\nelif b\nvar y : int = 2\nelse\nvar z : int = 3\nendif\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            var ifStatement = Assert.IsType<IfStatementSyntax>(document.Statements[0]);
            Assert.Single(ifStatement.ElifClauses);
            Assert.NotNull(ifStatement.ElseClause);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void EmptyIfBody_ReportsPEVT2301Warning()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nendif\nend");
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal("PEVT2301", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public void EmptyElifAndElseBody_ReportBothWarnings()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nvar x : int = 1\nelif b\nelse\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2302");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2303");
        }

        [Fact]
        public void OrphanElif_ReportsPEVT2003()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nelif a\nend");
            Assert.Equal("PEVT2003", Assert.Single(diagnostics).Id);
        }

        [Theory]
        [InlineData("id \"A\"\nelse\nend", "PEVT2006")]
        [InlineData("id \"A\"\nendif\nend", "PEVT2009")]
        [InlineData("id \"A\"\nif a\nend", "PEVT2002")]
        public void IfRelatedDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void ElifAfterElse_ReportsPEVT2005AndStillFindsEndIf()
        {
            // if/else 正文都特意留空，因此 2301/2303 warning 也会一起出现——这里只关心 2005 确实报了。
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nelse\nelif b\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2005");
            var ifStatement = Assert.IsType<IfStatementSyntax>(document.Statements[0]);
            Assert.False(ifStatement.EndIf.IsMissing);
        }

        [Fact]
        public void DuplicateElse_ReportsPEVT2007AndStillFindsEndIf()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nelse\nelse\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2007");
            var ifStatement = Assert.IsType<IfStatementSyntax>(document.Statements[0]);
            Assert.False(ifStatement.EndIf.IsMissing);
        }

        [Fact]
        public void MissingIfExpression_ReportsPEVT2001()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2001");
        }

        [Fact]
        public void MissingElifExpression_ReportsPEVT2004()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nvar x : int = 1\nelif\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2004");
        }

        [Fact]
        public void ElseWithTrailingExpression_ReportsPEVT2008AndRecovers()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a\nvar x : int = 1\nelse b\nvar y : int = 2\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2008");
            var ifStatement = Assert.IsType<IfStatementSyntax>(document.Statements[0]);
            Assert.False(ifStatement.EndIf.IsMissing);
        }

        // ---- while/endwhile ----

        [Fact]
        public void WhileStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nwhile a\nvar x : int = 1\nendwhile\nend");
            Assert.Equal("While(Name(a), [Var(x, int, Literal(1))])", document.Statements[0].ToString());
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("id \"A\"\nwhile a\nendwhile\nend", "PEVT2304")]
        [InlineData("id \"A\"\nendwhile\nend", "PEVT2103")]
        [InlineData("id \"A\"\nwhile a\nend", "PEVT2102")]
        public void WhileRelatedDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void MissingWhileExpression_ReportsPEVT2101()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nwhile\nendwhile\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2101");
        }

        // ---- switch/case/default/endswitch ----

        [Fact]
        public void SwitchStatement_WithCaseAndDefault_ParsesCleanly()
        {
            const string source = "id \"A\"\nswitch x\ncase 1\nvar a : int = 1\ndefault\nvar b : int = 2\nendswitch\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            var switchStatement = Assert.IsType<SwitchStatementSyntax>(document.Statements[0]);
            Assert.Equal(2, switchStatement.Arms.Count);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void SwitchMustStartWithArm_ReportsPEVT2404()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\nvar y : int = 1\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2404");
        }

        [Fact]
        public void EmptySwitch_ReportsBothPEVT2403AndPEVT2404()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2403");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2404");
        }

        [Fact]
        public void DuplicateCaseExpression_ReportsPEVT2407()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase 1\nvar a : int = 1\ncase 1\nvar b : int = 2\nendswitch\nend");
            Assert.Equal("PEVT2407", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void DuplicateCaseExpression_IgnoresWhitespaceDifferences()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(
                "id \"A\"\nswitch x\ncase a + b\nvar p : int = 1\ncase a  +  b\nvar q : int = 2\nendswitch\nend");
            Assert.Equal("PEVT2407", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void DuplicateDefault_ReportsPEVT2410()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ndefault\nvar a : int = 1\ndefault\nvar b : int = 2\nendswitch\nend");
            Assert.Equal("PEVT2410", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void EmptyCaseAndDefaultBody_ReportWarnings()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase 1\ndefault\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2408");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2412");
        }

        [Fact]
        public void MissingSwitchExpression_ReportsPEVT2401()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch\ncase 1\nend\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2401");
        }

        [Fact]
        public void MissingCaseExpression_ReportsPEVT2406()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase\nend\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2406");
        }

        [Fact]
        public void DefaultWithTrailingExpression_ReportsPEVT2411AndRecovers()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase 1\nend\ndefault 2\nend\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2411");
            var switchStatement = Assert.IsType<SwitchStatementSyntax>(document.Statements[0]);
            Assert.False(switchStatement.EndSwitch.IsMissing);
        }

        [Theory]
        [InlineData("id \"A\"\ncase 1\nend", "PEVT2405")]
        [InlineData("id \"A\"\ndefault\nend", "PEVT2409")]
        [InlineData("id \"A\"\nendswitch\nend", "PEVT2413")]
        [InlineData("id \"A\"\nswitch x\ncase 1\nend", "PEVT2402")]
        public void SwitchRelatedDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
        }

        // ---- labels, goto, end ----

        [Fact]
        public void LabelAndGotoLabel_ParseCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ngoto #Start\n#Start\nend");
            Assert.Equal("GotoLabel(#Start)", document.Statements[0].ToString());
            Assert.Equal("Label(#Start)", document.Statements[1].ToString());
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void GotoCaseExpression_InsideSwitch_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase 1\ngoto 1\nendswitch\nend");
            var switchStatement = Assert.IsType<SwitchStatementSyntax>(document.Statements[0]);
            var caseArm = Assert.IsType<CaseArmSyntax>(switchStatement.Arms[0]);
            Assert.Equal("GotoCase(Literal(1))", caseArm.Body[0].ToString());
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("id \"A\"\n#\nend", "PEVT3001")]
        [InlineData("id \"A\"\ngoto\nend", "PEVT3101")]
        [InlineData("id \"A\"\nend 1", "PEVT2201")]
        public void LabelGotoOrEndDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void LabelWithNonIdentifierCandidate_ReportsPEVT3002()
        {
            // "#" 后确实有内容（数字字面量 "123"），只是不是合法标识符形状——区别于 "#" 后面
            // 什么都没有的 PEVT3001。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\n#123\nend");
            Assert.Equal("PEVT3002", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void GotoBareExpression_OutsideSwitch_ReportsPEVT3102()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ngoto 1\nend");
            Assert.Equal("PEVT3102", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void GotoBareExpression_InsideSwitch_DoesNotReportPEVT3102()
        {
            // 已由 GotoCaseExpression_InsideSwitch_ParsesCleanly 验证 switch 内部完全没有诊断；
            // 这里再明确断言这一条不会被误判成 PEVT3102。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch x\ncase 1\ngoto 1\nendswitch\nend");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3102");
        }

        [Fact]
        public void GotoLabel_ExtraArgumentOnSameLine_ReportsPEVT3105()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ngoto #Start extra\n#Start\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT3105");
        }

        [Fact]
        public void GotoLabel_NoTrailingArgument_DoesNotReportPEVT3105()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ngoto #Start\n#Start\nend");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3105");
        }

        // ---- unrecognized statements ----

        [Fact]
        public void UnrecognizedStatement_ReportsPEVT1201AndRecoversAtNextLine()
        {
            // "%" 不是任何语句起始 token（既不是关键字，也不是 @/标识符(/await 等阶段 7 新增形态）。
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\n% garbage\nend");
            Assert.Equal("PEVT1201", Assert.Single(diagnostics).Id);
            Assert.Equal(2, document.Statements.Count);
            Assert.IsType<UnknownStatementSyntax>(document.Statements[0]);
            Assert.IsType<EndStatementSyntax>(document.Statements[1]);
        }

        [Fact]
        public void BuiltinCallStatement_AsBareStatement_ParsesCleanly()
        {
            // @ 调用作为独立语句是阶段 7 新增范围（11.1 节）：丢弃调用结果，不再是 PEVT1201。
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\n@dialogue(\"hi\")\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("BuiltinCall(@dialogue(Literal(\"hi\")))", document.Statements[0].ToString());
        }

        // ---- one-statement-per-line (PEVT1005) ----
        // 判断由 Parser 在语句解析完成、知道每条语句真实边界之后做：比较上一条语句结束的物理行与下一条语句起始 token 所在的行，
        // 不再依赖词法阶段那份只登记部分关键字的 StatementLeaders 清单。

        [Fact]
        public void OneStatementPerLine_ThreeDeclarationsOnOneLine_ReportsOneDiagnosticPerRepeat()
        {
            // 第一条声明立起这一行的基准，第二、第三条各自和上一条语句结束的行比较，各报一次——报告次数与多出来的语句数对应。
            // 不用连续 "end end end"：end 自身的"多余参数"恢复会把同行的下一个 end 当成参数吞掉，那测的是另一条规则。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar a : int var b : int var c : int\nend");
            Assert.Equal(2, diagnostics.Count(d => d.Id == "PEVT1005"));
        }

        [Fact]
        public void OneStatementPerLine_TwoDeclarationsOnSameLine_ReportsPEVT1005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar a : int var b : int\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_LeaderKeywordsOnSeparateLines_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif x\nendif\nend");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_VarDeclarationsOnSeparateLines_NoDiagnostic()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar a : int\nvar b : int\nend");
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData("id \"A\"\nswitch x\ncase 1\nendswitch\nend")]
        [InlineData("id \"A\"\nwhile x\nendwhile\nend")]
        public void OneStatementPerLine_StructuredFlowAcrossSeparateLines_NoDiagnostic(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_LeaderAfterMultilineRawBlock_UsesRawBlockClosingLine()
        {
            // raw 内容自己的换行不影响判断，关键是后面那条语句仍然定位到结束分隔符所在的物理行，而不是 raw 内容内部的某一行。
            // 结束分隔符所在行还会独立触发 PEVT8005，两条诊断根因不同，都应该出现。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\n$raw cmd'''line1\nline2''' var x : int\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8005");
        }

        [Theory]
        [InlineData("id \"A\"\n@a() @b()\nend")]
        [InlineData("id \"A\"\nvar x : int\nvar y : int\nx = 1 y = 2\nend")]
        [InlineData("id \"A\"\nhandler h = @a()\nawait h kill h\nend")]
        [InlineData("id \"A\"\n#Start end")]
        [InlineData("id \"A\"\ncallevt \"X\" callevt \"Y\"\nend")]
        public void OneStatementPerLine_CoversEveryStatementStartForm_ReportsPEVT1005(string source)
        {
            // R03 要求覆盖全部语句起始形态：内置调用、赋值、await+kill、标签、callevt，
            // 而不只是阶段 4 遗留的那份部分关键字清单。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Theory]
        [InlineData("id \"A\"\nasync block _foo()\nendblock\nend")]
        [InlineData("id \"A\"\ngoto #Label\n#Label\nend")]
        [InlineData("id \"A\"\nhandler h = callevt \"X\"\nend")]
        [InlineData("id \"A\"\nvar x : bool = await a\nend")]
        public void OneStatementPerLine_KnownAmbiguousPairs_DoNotFalsePositive(string source)
        {
            // async+block、goto+#、handler+callevt、var 初始化器里的 await 都会让两个"看起来像语句起始"的 token 落在同一行。
            // 但按完整语句边界判断时，它们只是一条语句内部的形状（只有一次 ParseStatement 调用），不会触发 PEVT1005。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        // ---- one-statement-per-line: structural boundaries (10A-R02 补正) ----

        [Fact]
        public void OneStatementPerLine_HeaderAndFirstBodyStatementOnSameLine_ReportsPEVT1005()
        {
            // "if true @a()"：if 的条件和正文第一条语句挤在同一行。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif true @a()\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_LastBodyStatementAndEndIfOnSameLine_ReportsPEVT1005()
        {
            // "@a() endif"：正文最后一条语句和闭合关键字挤在同一行。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif true\n@a() endif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_SwitchValueAndFirstArmOnSameLine_ReportsPEVT1005()
        {
            // "switch 1 case 1"：switch 表达式和第一个 case 挤在同一行。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch 1 case 1\nend\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_WhileHeaderAndFirstBodyStatementOnSameLine_ReportsPEVT1005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nwhile true @a()\nendwhile\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_LastBodyStatementAndEndWhileOnSameLine_ReportsPEVT1005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nwhile true\n@a() endwhile\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_LastArmBodyStatementAndEndSwitchOnSameLine_ReportsPEVT1005()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nswitch 1\ncase 1\n@a() endswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT1005");
        }

        [Fact]
        public void OneStatementPerLine_EmptyIfBody_HeaderDirectlyFollowedByEndIf_DoesNotFalsePositive()
        {
            // "if a endif"：条件直接跟着闭合关键字，中间没有任何语句——这是空产生式
            // （已经由 PEVT2301 覆盖），不是"两条语句"挤在一行，不应该被误判成 PEVT1005。
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nif a endif\nend");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        [Theory]
        [InlineData("id \"A\"\n@foo(a,\nb)\nend")] // 调用参数跨行不应误报（且不因为参数在同行而误报）。
        [InlineData("id \"A\"\nif (a + b) > 0\nvar x : int = 1\nendif\nend")] // 括号子表达式。
        [InlineData("id \"A\"\nif true\n$raw cmd'''line1\nline2'''\nendif\nend")] // 字符串/raw 内容跨行。
        public void OneStatementPerLine_RegressionCasesFromContract_DoNotFalsePositive(string source)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT1005");
        }

        // ---- nested recovery ----

        [Fact]
        public void NestedIfInsideWhile_MissingInnerEndIf_StillRecoversOuterEndWhile()
        {
            const string source = "id \"A\"\nwhile a\nif b\nvar x : int = 1\nendwhile\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);

            // 内层 if 一直找不到 endif，就把 "endwhile" 也当成自己的正文一部分吞掉，
            // 直到真正的文件结尾才报"缺 endif"；外层 while 因此反而变成缺 endwhile。
            Assert.Contains(diagnostics, d => d.Id == "PEVT2002");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2102");
            var whileStatement = Assert.IsType<WhileStatementSyntax>(document.Statements[0]);
            var ifStatement = Assert.IsType<IfStatementSyntax>(whileStatement.Body[0]);
            Assert.True(ifStatement.EndIf.IsMissing);
            Assert.True(whileStatement.EndWhile.IsMissing);
        }

        [Fact]
        public void NestedSwitchInsideIf_BothCloseCorrectly()
        {
            const string source = "id \"A\"\nif a\nswitch x\ncase 1\nvar y : int = 1\nendswitch\nendif\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            var ifStatement = Assert.IsType<IfStatementSyntax>(document.Statements[0]);
            var switchStatement = Assert.IsType<SwitchStatementSyntax>(ifStatement.Body[0]);
            Assert.False(switchStatement.EndSwitch.IsMissing);
            Assert.False(ifStatement.EndIf.IsMissing);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultipleTopLevelErrors_AreAllReportedInOnePass()
        {
            // 一行一语句：多种毫不相关的问题应该在同一次解析里各自单独报出来，互不吞没。
            const string source = "id \"A\"\nelif a\nvar : int\nendwhile\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT2003");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6004");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2103");
        }

        [Fact]
        public void MultipleIndependentNewDiagnostics_OnSeparateLines_AreAllReportedInOnePass()
        {
            // 四类诊断分别放在互不相关的物理行：事件 ID 非法字符、空 if 条件、goto 裸表达式在 switch 外、保留字用作变量名。
            // 精确诊断不应该让恢复提前停止，四处根因都必须在同一次解析里各自单独报出来。
            const string source = "id \"a!\"\nif\nendif\ngoto 1\nvar if : int\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT1111");
            Assert.Contains(diagnostics, d => d.Id == "PEVT2001");
            Assert.Contains(diagnostics, d => d.Id == "PEVT3102");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6013");
        }
    }
}
