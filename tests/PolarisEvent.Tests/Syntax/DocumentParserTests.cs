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
        public void HeaderDiagnostic_ReportsExpectedSingleId(string source, string diagnosticId)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal(diagnosticId, Assert.Single(diagnostics).Id);
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
    }
}
