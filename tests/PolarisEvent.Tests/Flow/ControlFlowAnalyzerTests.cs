using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Flow
{
    /// <summary>
    /// 阶段 10 的 golden 测试：控制流分析（PEVT4xxx 与之前遗留的标签/goto 语义诊断 PEVT3xxx）
    /// 覆盖嵌套结构、向后 goto、switch 专用 goto 和不可达警告（语法设计草案第 6.5/7/16 节）。
    /// </summary>
    public class ControlFlowAnalyzerTests
    {
        private static IReadOnlyList<Diagnostic> AnalyzeDoc(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            DocumentSyntax document = parser.ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);
            return bag.ToReadOnly();
        }

        private static PevtProgramDefinition BuildDefinition(string source)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            DocumentSyntax document = new Parser(Lexer.Tokenize(text, bag), bag, text).ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);
            return PevtProgramDefinition.TryBuild(document, text, bag);
        }

        // ---- 16: every event path must reach end ----

        [Fact]
        public void SimpleEvent_EndsCleanly_NoDiagnostic()
        {
            Assert.Empty(AnalyzeDoc("id \"A\"\nvar x : int = 1\nend"));
        }

        [Fact]
        public void MissingEnd_ReportsPEVT4001()
        {
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc("id \"A\"\nvar x : int = 1");
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void IfElse_BothBranchesEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nif true\nend\nelse\nend\nendif";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void IfWithoutElse_EndInsideBody_StillReportsPEVT4001()
        {
            // 没有 else 时"跳过整个 if"本身就是一条不终止的路径，即使 if 正文里有 end。
            const string source = "id \"A\"\nif true\nend\nendif";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void IfElifElse_AllThreeBranchesEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nif true\nend\nelif false\nend\nelse\nend\nendif";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void IfElifElse_OneBranchMissesEnd_ReportsPEVT4001()
        {
            const string source = "id \"A\"\nif true\nend\nelif false\nvar x : int = 1\nelse\nend\nendif";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void SwitchWithDefault_AllArmsEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nend\ndefault\nend\nendswitch";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void SwitchWithoutDefault_ReportsPEVT4001()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nend\nendswitch";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void WhileLoop_AlwaysReachableAfterward_ReportsPEVT4001WhenNothingFollows()
        {
            // while 之后永远可达（不证明死循环），因此这里的 4001 来自"while 之后没有 end"，
            // 不是循环体本身的问题。
            const string source = "id \"A\"\nwhile true\nend\nendwhile";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void WhileLoop_FollowedByEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nwhile true\nvar x : int = 1\nendwhile\nend";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void NestedIfInsideWhile_OuterEndAfterLoop_NoPEVT4001()
        {
            const string source = "id \"A\"\nwhile true\nif true\nend\nendif\nendwhile\nend";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        // ---- 4002: unreachable statements ----

        [Fact]
        public void StatementAfterEnd_ReportsPEVT4002()
        {
            const string source = "id \"A\"\nend\nvar x : int = 1";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4002");
        }

        [Fact]
        public void StatementAfterUnconditionalGoto_NoTargetingLabel_ReportsPEVT4002()
        {
            const string source = "id \"A\"\n#Skip\ngoto #Skip\nvar x : int = 1\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4002");
        }

        [Fact]
        public void LabelTargetedByGoto_AfterEnd_RestoresReachability_NoPEVT4002()
        {
            const string source = "id \"A\"\nif false\ngoto #Resume\nendif\nend\n#Resume\nvar x : int = 1\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT4002");
        }

        // ---- 7.2: label declaration and goto #Label resolution ----

        [Fact]
        public void BackwardGoto_WithinSameBody_ResolvesWithoutDiagnostic()
        {
            // "覆盖……向后 goto"：标签在 goto 之前，同一条路径，属于合法情形。
            const string source = "id \"A\"\n#Start\nvar x : int = 1\nif x == 1\ngoto #Start\nendif\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3104" || d.Id == "PEVT3106" || d.Id == "PEVT3107");
        }

        [Fact]
        public void DuplicateLabel_ReportsPEVT3003()
        {
            const string source = "id \"A\"\n#Same\n#Same\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3003");
        }

        [Fact]
        public void GotoUndefinedLabel_ReportsPEVT3104()
        {
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc("id \"A\"\ngoto #NeverDeclared\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT3104");
        }

        [Fact]
        public void GotoLabelDefinedInDifferentBlock_ReportsPEVT3106()
        {
            const string source = "id \"A\"\nblock _x()\n#Inside\nendblock\ngoto #Inside\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3106");
        }

        [Fact]
        public void GotoLabelDefinedInOuterEvent_FromInsideBlock_ReportsPEVT3106()
        {
            const string source = "id \"A\"\n#Outer\nblock _x()\ngoto #Outer\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3106");
        }

        [Fact]
        public void GotoLabelInSiblingIfBranch_ReportsPEVT3107()
        {
            // 标签在 else 分支，goto 在 if 分支——两条路径互不包含，不是前缀关系。
            const string source = "id \"A\"\nif true\ngoto #InElse\nelse\n#InElse\nend\nendif\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3107");
        }

        [Fact]
        public void GotoLabelInDeeperNestedStructure_ReportsPEVT3107()
        {
            // 标签在 if 内部，goto 在 if 之外（更浅的层级）——标签路径不是 goto 路径的前缀（反过来才是）。
            const string source = "id \"A\"\nif true\n#Inner\nend\nendif\ngoto #Inner\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3107");
        }

        [Fact]
        public void GotoLabelInEnclosingOuterScope_FromNestedIf_ResolvesWithoutDiagnostic()
        {
            // 标签路径（outer）是 goto 路径（outer + if-body）的前缀——允许跳出到外层。
            const string source = "id \"A\"\n#Outer\nif true\ngoto #Outer\nendif\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3107" || d.Id == "PEVT3104" || d.Id == "PEVT3106");
        }

        // ---- 6.5: goto <expr> only valid inside a switch, must match a case ----

        [Fact]
        public void GotoCase_MatchingCaseValue_NoDiagnostic()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\ngoto 1\ncase 2\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3111" || d.Id == "PEVT3112");
        }

        [Fact]
        public void GotoCase_OutsideAnySwitch_ReportsPEVT3111()
        {
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc("id \"A\"\ngoto 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT3111");
        }

        [Fact]
        public void GotoCase_NoMatchingCaseInEnclosingSwitch_ReportsPEVT3112()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\ngoto 99\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3112");
        }

        [Fact]
        public void GotoCase_MatchesNearestEnclosingSwitchNotOuterOne()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nswitch 2\ncase 2\ngoto 2\nend\nendswitch\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3112");
        }

        // ---- 14.3: every path through a typed block must return ----

        [Fact]
        public void TypedBlock_IfElseBothReturn_NoPEVT7117()
        {
            const string source = "id \"A\"\nblock _x() : bool\nvar ok : bool = true\nif ok\nreturn ok\nelse\nreturn ok\nendif\nendblock\nend";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT7117");
        }

        [Fact]
        public void TypedBlock_MissingReturnOnOnePath_ReportsPEVT7117()
        {
            const string source = "id \"A\"\nblock _x() : bool\nvar ok : bool = true\nif ok\nreturn ok\nendif\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7117");
        }

        [Fact]
        public void TypedBlock_NoReturnAtAll_ReportsPEVT7117()
        {
            const string source = "id \"A\"\nblock _x() : bool\nvar ok : bool = true\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7117");
        }

        [Fact]
        public void VoidBlock_NoReturnAtAll_NeverChecked()
        {
            const string source = "id \"A\"\nblock _x()\nvar ok : bool = true\nendblock\nend";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT7117");
        }

        [Fact]
        public void TypedBlock_SwitchWithDefaultAllArmsReturn_NoPEVT7117()
        {
            const string source = "id \"A\"\nblock _x() : int\nswitch 1\ncase 1\nvar a : int = 1\nreturn a\ndefault\nvar b : int = 2\nreturn b\nendswitch\nendblock\nend";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT7117");
        }

        // ---- immutable program definition ----

        [Fact]
        public void TryBuild_WithErrors_ReturnsNull()
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"A\"\nvar x : int = 1"), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            DocumentSyntax document = parser.ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document); // 报出 PEVT4001（Error）
            Assert.Null(PevtProgramDefinition.TryBuild(document, text, bag));
        }

        [Fact]
        public void TryBuild_CleanDocument_ReturnsPopulatedDefinition()
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"MyEvent\"\nenable cs\nenable async\nend"), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            DocumentSyntax document = parser.ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);

            PevtProgramDefinition definition = PevtProgramDefinition.TryBuild(document, text, bag);
            Assert.NotNull(definition);
            Assert.Equal("MyEvent", definition.EventId);
            Assert.True(definition.HasCsCapability);
            Assert.True(definition.HasAsyncCapability);
            Assert.Equal(64, definition.SourceHash.Length);
        }

        [Fact]
        public void TryBuild_SameSource_ProducesSameHash()
        {
            const string source = "id \"A\"\nend";
            SourceText textA = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "a.pevt").Text;
            SourceText textB = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "b.pevt").Text;
            var bagA = new DiagnosticBag();
            var bagB = new DiagnosticBag();
            DocumentSyntax documentA = new Parser(Lexer.Tokenize(textA, bagA), bagA, textA).ParseDocument();
            DocumentSyntax documentB = new Parser(Lexer.Tokenize(textB, bagB), bagB, textB).ParseDocument();
            new ControlFlowAnalyzer(bagA, textA).AnalyzeDocument(documentA);
            new ControlFlowAnalyzer(bagB, textB).AnalyzeDocument(documentB);

            Assert.Equal(
                PevtProgramDefinition.TryBuild(documentA, textA, bagA).SourceHash,
                PevtProgramDefinition.TryBuild(documentB, textB, bagB).SourceHash);
        }

        [Fact]
        public void TryBuild_DifferentSource_ProducesDifferentHash()
        {
            SourceText textA = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"A\"\nend"), "a.pevt").Text;
            SourceText textB = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"B\"\nend"), "b.pevt").Text;
            var bagA = new DiagnosticBag();
            var bagB = new DiagnosticBag();
            DocumentSyntax documentA = new Parser(Lexer.Tokenize(textA, bagA), bagA, textA).ParseDocument();
            DocumentSyntax documentB = new Parser(Lexer.Tokenize(textB, bagB), bagB, textB).ParseDocument();
            new ControlFlowAnalyzer(bagA, textA).AnalyzeDocument(documentA);
            new ControlFlowAnalyzer(bagB, textB).AnalyzeDocument(documentB);

            Assert.NotEqual(
                PevtProgramDefinition.TryBuild(documentA, textA, bagA).SourceHash,
                PevtProgramDefinition.TryBuild(documentB, textB, bagB).SourceHash);
        }

        [Fact]
        public void TryBuild_WarningOnly_StillReturnsDefinition()
        {
            // 空 if 正文只是警告（PEVT2301），不阻止产出不可变定义。
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"A\"\nif true\nendif\nend"), "test.pevt").Text;
            var bag = new DiagnosticBag();
            DocumentSyntax document = new Parser(Lexer.Tokenize(text, bag), bag, text).ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);
            Assert.NotNull(PevtProgramDefinition.TryBuild(document, text, bag));
        }

        [Fact]
        public void TryBuild_NoEnableDeclarations_CapabilitiesAreFalse()
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"Plain\"\nend"), "test.pevt").Text;
            var bag = new DiagnosticBag();
            DocumentSyntax document = new Parser(Lexer.Tokenize(text, bag), bag, text).ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);
            PevtProgramDefinition definition = PevtProgramDefinition.TryBuild(document, text, bag);
            Assert.NotNull(definition);
            Assert.False(definition.HasCsCapability);
            Assert.False(definition.HasAsyncCapability);
        }

        // ---- more nested-structure golden coverage ----

        [Fact]
        public void DeeplyNestedIfInsideWhileInsideIf_AllPathsEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nif true\nwhile true\nif true\nend\nelse\nend\nendif\nendwhile\nend\nelse\nend\nendif";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void SwitchNestedInsideIf_AllPathsEnd_NoPEVT4001()
        {
            const string source = "id \"A\"\nif true\nswitch 1\ncase 1\nend\ndefault\nend\nendswitch\nelse\nend\nendif";
            Assert.DoesNotContain(AnalyzeDoc(source), d => d.Id == "PEVT4001");
        }

        [Fact]
        public void IfNestedInsideSwitchCase_MissingEndOnOnePath_ReportsPEVT4001()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nif true\nend\nendif\ndefault\nend\nendswitch";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void MultipleUnreachableStatementsAfterEnd_EachReportedIndependently()
        {
            const string source = "id \"A\"\nend\nvar x : int = 1\nvar y : int = 2\nvar z : int = 3";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            int unreachableCount = 0;
            foreach (Diagnostic d in diagnostics)
                if (d.Id == "PEVT4002")
                    unreachableCount++;
            Assert.Equal(3, unreachableCount);
        }

        [Fact]
        public void UnreachableIfStatement_ReportsPEVT4002OnlyOnceNotForItsBody()
        {
            // 整个 if 语句本身已经报过一次不可达；不应该再为它正文里的每条语句连锁报第二次。
            const string source = "id \"A\"\nend\nif true\nvar a : int = 1\nvar b : int = 2\nendif";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            int unreachableCount = 0;
            foreach (Diagnostic d in diagnostics)
                if (d.Id == "PEVT4002")
                    unreachableCount++;
            Assert.Equal(1, unreachableCount);
        }

        [Fact]
        public void GotoBetweenTwoDifferentBlocks_ReportsPEVT3106()
        {
            const string source = "id \"A\"\nblock _first()\n#InFirst\nendblock\nblock _second()\ngoto #InFirst\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3106");
        }

        [Fact]
        public void SameLabelNameInDifferentBlocks_NoDuplicateReport()
        {
            // 标签命名空间按环境（外层事件/各个块）各自独立，不同块里同名标签互不冲突。
            const string source = "id \"A\"\nblock _first()\n#Same\nendblock\nblock _second()\n#Same\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3003");
        }

        [Fact]
        public void GotoInsideBlock_TargetingSameBlockLabel_ResolvesWithoutDiagnostic()
        {
            const string source = "id \"A\"\nblock _x()\n#Retry\nvar n : int = 1\nif n == 1\ngoto #Retry\nendif\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3104" || d.Id == "PEVT3106" || d.Id == "PEVT3107");
        }

        [Fact]
        public void GotoLabelInWhileBody_FromOutsideWhile_ReportsPEVT3107()
        {
            const string source = "id \"A\"\nwhile true\n#InLoop\nend\nendwhile\ngoto #InLoop\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3107");
        }

        [Fact]
        public void GotoLabelBeforeWhile_FromInsideWhileBody_ResolvesWithoutDiagnostic()
        {
            const string source = "id \"A\"\n#Top\nwhile true\ngoto #Top\nendwhile\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3104" || d.Id == "PEVT3106" || d.Id == "PEVT3107");
        }

        [Fact]
        public void GotoCase_InsideIfInsideSwitchCase_StillMatchesEnclosingSwitch()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nif true\ngoto 1\nendif\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3111" || d.Id == "PEVT3112");
        }

        [Fact]
        public void TypedBlock_WhileLoopBeforeReturn_StillRequiresReturnAfterLoop()
        {
            // while 之后永远视为可达；即使循环体里有 return，块本身仍然需要循环之后的路径也返回。
            const string source = "id \"A\"\nblock _x() : bool\nwhile true\nvar r : bool = true\nreturn r\nendwhile\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7117");
        }

        [Fact]
        public void TypedBlock_NestedBlockDefinitionInside_DoesNotAffectOuterBlockReturnCheck()
        {
            // 外层块自己每条路径都返回；内部（非法）嵌套定义的返回完整性单独判定，互不影响。
            const string source = "id \"A\"\nblock _outer() : bool\nblock _inner() : bool\nvar x : bool = true\nreturn x\nendblock\nvar y : bool = true\nreturn y\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT7117");
        }

        [Fact]
        public void MultipleTypedBlocks_EachCheckedIndependently()
        {
            const string source = "id \"A\"\nblock _good() : bool\nvar x : bool = true\nreturn x\nendblock\nblock _bad() : bool\nvar y : bool = true\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Single(diagnostics, d => d.Id == "PEVT7117");
        }

        [Fact]
        public void SwitchGotoOutsideSwitch_ButInsideWhile_StillReportsPEVT3111()
        {
            const string source = "id \"A\"\nwhile true\ngoto 1\nendwhile\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3111");
        }

        [Fact]
        public void SwitchGoto_AfterEnclosingSwitchEnds_ReportsPEVT3111()
        {
            // switch 已经用 endswitch 闭合；这条 goto 位于 switch 之外，不该匹配到之前那个 switch 的 case。
            const string source = "id \"A\"\nswitch 1\ncase 1\nend\nendswitch\ngoto 1\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3111");
        }

        [Fact]
        public void LabelAndGoto_BothInsideSameCaseBody_ResolveWithoutDiagnostic()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\n#Retry\nvar n : int = 1\nif n == 1\ngoto #Retry\nendif\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3104" || d.Id == "PEVT3106" || d.Id == "PEVT3107");
        }

        [Fact]
        public void LabelInOneCaseBody_GotoFromSiblingCaseBody_ReportsPEVT3107()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\n#InCaseOne\nend\ncase 2\ngoto #InCaseOne\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3107");
        }

        // ---- immutable program definition: symbol slots ----

        [Fact]
        public void TopLevelSymbols_CollectsVarAndConstDeclarations()
        {
            const string source = "id \"A\"\nvar x : int = 1\nconst y : bool = true\nend";
            PevtProgramDefinition definition = BuildDefinition(source);
            Assert.NotNull(definition);
            Assert.Equal(2, definition.TopLevelSymbols.Count);
            Assert.Equal("x", definition.TopLevelSymbols[0].Name);
            Assert.Equal("var", definition.TopLevelSymbols[0].Kind);
            Assert.Equal(SyntaxKind.IntKeyword, definition.TopLevelSymbols[0].DeclaredType);
            Assert.Equal("y", definition.TopLevelSymbols[1].Name);
            Assert.Equal("const", definition.TopLevelSymbols[1].Kind);
        }

        [Fact]
        public void TopLevelSymbols_IncludesDeclarationsNestedInsideIfAndWhile()
        {
            // if/while 不创建新环境（9.4 节），其中的声明仍然属于外层事件同一个符号槽表。
            const string source = "id \"A\"\nif true\nvar a : int = 1\nendif\nwhile true\nvar b : int = 2\nendwhile\nend";
            PevtProgramDefinition definition = BuildDefinition(source);
            Assert.NotNull(definition);
            Assert.Contains(definition.TopLevelSymbols, s => s.Name == "a");
            Assert.Contains(definition.TopLevelSymbols, s => s.Name == "b");
        }

        [Fact]
        public void TopLevelSymbols_ExcludesVariablesDeclaredInsideCustomBlocks()
        {
            // 块有自己独立的环境（9.4 节），块内部的声明不出现在外层事件的符号槽表里。
            const string source = "id \"A\"\nblock _x()\nvar inner : int = 1\nendblock\nend";
            PevtProgramDefinition definition = BuildDefinition(source);
            Assert.NotNull(definition);
            Assert.DoesNotContain(definition.TopLevelSymbols, s => s.Name == "inner");
        }

        [Fact]
        public void TopLevelSymbols_EmptyWhenNoDeclarations()
        {
            PevtProgramDefinition definition = BuildDefinition("id \"A\"\nend");
            Assert.NotNull(definition);
            Assert.Empty(definition.TopLevelSymbols);
        }

        [Fact]
        public void TopLevelSymbols_IncludesDeclarationsNestedInsideSwitchCases()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nvar a : int = 1\ndefault\nconst b : bool = true\nendswitch\nend";
            PevtProgramDefinition definition = BuildDefinition(source);
            Assert.NotNull(definition);
            Assert.Contains(definition.TopLevelSymbols, s => s.Name == "a" && s.Kind == "var");
            Assert.Contains(definition.TopLevelSymbols, s => s.Name == "b" && s.Kind == "const");
        }

        [Fact]
        public void TopLevelSymbols_PreservesDeclarationOrder()
        {
            const string source = "id \"A\"\nvar first : int = 1\nvar second : int = 2\nvar third : int = 3\nend";
            PevtProgramDefinition definition = BuildDefinition(source);
            Assert.NotNull(definition);
            Assert.Equal(new[] { "first", "second", "third" }, System.Linq.Enumerable.Select(definition.TopLevelSymbols, s => s.Name));
        }

        [Fact]
        public void Document_IsExposedOnDefinition_ForDownstreamSourceMapping()
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes("id \"MapMe\"\nend"), "test.pevt").Text;
            var bag = new DiagnosticBag();
            DocumentSyntax document = new Parser(Lexer.Tokenize(text, bag), bag, text).ParseDocument();
            new ControlFlowAnalyzer(bag, text).AnalyzeDocument(document);
            PevtProgramDefinition definition = PevtProgramDefinition.TryBuild(document, text, bag);
            Assert.NotNull(definition);
            Assert.Same(document, definition.Document);
            Assert.Same(text, definition.Source);
        }

        // ---- one more nested combination, and a document with several independent flow issues at once ----

        [Fact]
        public void MultipleIndependentFlowDiagnostics_AreAllReportedInOnePass()
        {
            // 三个问题分别放在互不吞没彼此的位置：if 正文内的无条件 goto 只影响它自己那条内部路径
            // （if 没有 else，外层仍然可达），switch 没有 default 所以也不会让外层"终止"。
            const string source = "id \"A\"\nif true\ngoto #NeverDeclared\nendif\nswitch 1\ncase 1\ngoto 99\nend\nendswitch";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT3104");
            Assert.Contains(diagnostics, d => d.Id == "PEVT3112");
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void EmptySwitchArms_DoesNotCrashAndStillReportsPEVT4001WhenNothingFollows()
        {
            // switch 一个 case/default 都没有时（已经在阶段 6 报过 PEVT2403），控制流分析
            // 仍然要能正常处理零臂 switch，不把它误判成"总是终止"。
            const string source = "id \"A\"\nswitch 1\nendswitch";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT4001");
        }

        [Fact]
        public void GotoCaseTarget_CanonicalizationIgnoresWhitespaceDifferences()
        {
            // 6.2 节的近似比较规则（忽略空白）在这里复用给 goto 表达式匹配：
            // case 表达式和 goto 表达式书写时的空格差异不应该造成"没有匹配到"。
            const string source = "id \"A\"\nswitch 1 + 2\ncase 1 + 2\ngoto 1+2\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3112");
        }

        [Fact]
        public void GotoCase_MultipleCandidateCasesOnlyOneMatches_ResolvesWithoutDiagnostic()
        {
            const string source = "id \"A\"\nswitch 1\ncase 1\nend\ncase 2\ngoto 1\nend\nendswitch\nend";
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3112");
        }

        [Fact]
        public void GotoLabel_MissingNameToken_DoesNotThrow()
        {
            // 目标标签标识符缺失时（阶段 6 已报 PEVT3103），本阶段的解析不应该再抛异常或连锁报错。
            IReadOnlyList<Diagnostic> diagnostics = AnalyzeDoc("id \"A\"\ngoto #\nend");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT3104" || d.Id == "PEVT3106" || d.Id == "PEVT3107");
        }
    }
}
