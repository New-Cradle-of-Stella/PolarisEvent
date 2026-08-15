using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    /// <summary>
    /// 阶段 7 的 golden 测试：自定义事件块、handler/await/kill/status、callevt、exec、
    /// $raw 语句形态的合法与非法样例快照（语法设计草案第 10–15 节）。
    /// </summary>
    public class BlockAsyncCallParserTests
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

        // ---- 14: custom event blocks ----

        [Fact]
        public void BlockDefinition_NoParamsNoReturn_ThenCall_ParsesCleanly()
        {
            const string source = "id \"A\"\nblock _playOpening()\n@say()\nendblock\n_playOpening()\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            Assert.Equal("Block(_playOpening(), [BuiltinCall(@say())])", document.Statements[0].ToString());
            Assert.Equal("Call(_playOpening())", document.Statements[1].ToString());
        }

        [Fact]
        public void BlockDefinition_WithParamsAndReturn_ParsesSignature()
        {
            const string source = "id \"A\"\nblock _selectLine(name : string) : bool\nvar selected : bool = false\nreturn selected\nendblock\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var block = Assert.IsType<BlockDefinitionStatementSyntax>(document.Statements[0]);
            Assert.Equal("name: string", block.Parameters.Parameters[0].ToString());
            Assert.Equal("bool", block.ReturnType.Text);
            Assert.Equal("Return(selected)", block.Body[1].ToString());
        }

        [Fact]
        public void AsyncBlockDefinition_ParsesWithAsyncPrefix()
        {
            const string source = "id \"A\"\nasync block _loadScene(name : string) : bool\nvar loaded : bool = @doLoad(name)\nreturn loaded\nendblock\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            Assert.StartsWith("AsyncBlock(_loadScene", document.Statements[0].ToString());
        }

        [Fact]
        public void NestedBlockDefinition_ReportsPEVT7104()
        {
            const string source = "id \"A\"\nblock _outer()\nblock _inner()\nendblock\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7104");
        }

        [Fact]
        public void BlockDefinition_UnderscoreAlone_ReportsPEVT7101()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nblock _()\nendblock\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7101");
        }

        [Fact]
        public void BlockDefinition_NameMissingUnderscorePrefix_ReportsPEVT7102()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nblock playOpening()\nendblock\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7102");
        }

        [Fact]
        public void BlockDefinition_MissingEndBlock_ReportsPEVT7116()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nblock _x()\n@say()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7116");
        }

        [Fact]
        public void OrphanEndBlock_ReportsPEVT7118()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nendblock\nend");
            Assert.Equal("PEVT7118", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void EndInsideCustomBlock_ReportsPEVT7120AndDoesNotCloseBlock()
        {
            const string source = "id \"A\"\nblock _x()\nend\nendblock\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal("PEVT7120", Assert.Single(diagnostics).Id);
            var block = Assert.IsType<BlockDefinitionStatementSyntax>(document.Statements[0]);
            Assert.False(block.EndBlockKeyword.IsMissing);
            Assert.IsType<EndStatementSyntax>(document.Statements[1]);
        }

        // ---- 14.3: return ----

        [Fact]
        public void ReturnOutsideBlock_ReportsPEVT7105()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nreturn\nend");
            Assert.Equal("PEVT7105", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void VoidBlock_ReturnWithValue_ReportsPEVT7107()
        {
            const string source = "id \"A\"\nblock _x()\nvar y : int = 1\nreturn y\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7107");
        }

        [Fact]
        public void TypedBlock_ReturnWithoutValue_ReportsPEVT7106()
        {
            const string source = "id \"A\"\nblock _x() : bool\nreturn\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7106");
        }

        [Fact]
        public void TypedBlock_ReturnLiteral_ReportsPEVT7108()
        {
            const string source = "id \"A\"\nblock _x() : bool\nreturn true\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7108");
        }

        [Fact]
        public void TypedBlock_ReturnCallExpression_ReportsPEVT7108()
        {
            const string source = "id \"A\"\nblock _x() : bool\nreturn _y()\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7108");
        }

        // ---- 15.2/15.4: handler, kill ----

        [Theory]
        [InlineData("handler a = @doThing()", "Handler(a, BuiltinCall(@doThing()))")]
        [InlineData("handler b = _doBlock()", "Handler(b, Call(_doBlock()))")]
        [InlineData("handler c = callevt \"AsyncEvent\"", "Handler(c, CallEvt(\"AsyncEvent\"))")]
        public void HandlerDeclaration_ValidInitializerForms_ParseCleanly(string statement, string expected)
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc($"id \"A\"\n{statement}\nend");
            Assert.Empty(diagnostics);
            Assert.Equal(expected, document.Statements[0].ToString());
        }

        [Fact]
        public void HandlerDeclaration_MissingName_ReportsPEVT7205()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nhandler = @doThing()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7205");
        }

        [Fact]
        public void HandlerDeclaration_NonAsyncInitializerShape_ReportsPEVT7206()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nhandler a = 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7206");
        }

        [Fact]
        public void HandlerDeclaration_ExecInitializer_ReportsPEVT7406()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nhandler a = exec(source)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7406");
        }

        [Fact]
        public void KillStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nkill a\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("Kill(a)", document.Statements[0].ToString());
        }

        [Fact]
        public void KillStatement_MissingHandle_ReportsPEVT7213()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nkill\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7213");
        }

        // ---- 15.3/15.6: await, await all/any ----

        [Fact]
        public void BareAwait_AsStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nawait a\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("Await(a)", document.Statements[0].ToString());
        }

        [Fact]
        public void AwaitAll_WithBindings_ParsesCleanly()
        {
            const string source = "id \"A\"\nvar completed : int = await all(a, b, c)(resultA, resultB, resultC)\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.Equal("AwaitAggregate(all, (a, b, c), (resultA, resultB, resultC))", decl.Initializer.ToString());
        }

        [Fact]
        public void AwaitAny_DiscardingBindings_AsStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nawait any(a, b)()\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("AwaitAggregate(any, (a, b), ())", document.Statements[0].ToString());
        }

        [Fact]
        public void AwaitAll_EmptyHandleList_ReportsPEVT7217()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nawait all()()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7217");
        }

        [Fact]
        public void AwaitAll_MissingBindingParens_ReportsPEVT7220()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nawait all(a, b)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7220");
        }

        // ---- 10: callevt ----

        [Fact]
        public void CallEvt_BareStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ncallevt \"OtherEvent\"\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("CallEvt(\"OtherEvent\")", document.Statements[0].ToString());
        }

        [Fact]
        public void CallEvt_ChineseTarget_ParsesCleanly()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ncallevt \"博物馆开场\"\nend");
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void CallEvt_MissingTarget_ReportsPEVT7301()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ncallevt\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7301");
        }

        [Theory]
        [InlineData("callevt \"\"")]
        [InlineData("callevt \"Other_Event\"")]
        [InlineData("callevt \"Other Event\"")]
        public void CallEvt_InvalidTargetContent_ReportsPEVT7302(string statement)
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc($"id \"A\"\n{statement}\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7302");
        }

        [Fact]
        public void CallEvt_VariableTarget_ReportsPEVT7303()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\ncallevt x\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7303");
        }

        [Fact]
        public void CallEvt_UsedAsInitializerExpression_ReportsPEVT7304()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : bool = callevt \"Y\"\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7304");
        }

        [Fact]
        public void AsyncCallEvt_ReportsPEVT7305()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nasync callevt \"Y\"\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7305");
        }

        // ---- 13: exec ----

        [Fact]
        public void Exec_OneStringArgument_AsStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nexec(source)\nend");
            Assert.Empty(diagnostics);
            Assert.Equal("Exec(Name(source))", document.Statements[0].ToString());
        }

        [Fact]
        public void Exec_NoArguments_ReportsPEVT7401()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nexec()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7401");
        }

        [Fact]
        public void Exec_TwoArguments_ReportsPEVT7402()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nexec(a, b)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7402");
        }

        [Fact]
        public void Exec_UsedAsInitializerExpression_ReportsPEVT7404()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar x : bool = exec(a)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7404");
        }

        [Fact]
        public void AsyncExec_ReportsPEVT7405()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nasync exec(a)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7405");
        }

        // ---- 12: $raw as a statement ----

        [Fact]
        public void RawCmd_AsStatement_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\n$raw cmd'''原始游戏 DSL'''\nend");
            Assert.Empty(diagnostics);
            Assert.IsType<RawCmdStatementSyntax>(document.Statements[0]);
        }

        [Fact]
        public void RawCs_WithArguments_AsStatement_ParsesCleanly()
        {
            const string source = "id \"A\"\nvar count : int = 1\n$raw cs (count)'''count += 1;'''\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var statement = Assert.IsType<ExpressionStatementSyntax>(document.Statements[1]);
            Assert.IsType<RawCsExpressionSyntax>(statement.Expression);
        }

        // ---- async prefix on non-block targets ----

        [Fact]
        public void AsyncBuiltinCall_ReportsPEVT7215()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nasync @doThing()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7215");
        }

        [Fact]
        public void AsyncCustomBlockCall_ReportsPEVT7215()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nasync _doBlock()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7215");
        }

        [Fact]
        public void AsyncOnUnrelatedStatement_ReportsPEVT7201()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nasync var x : int = 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7201");
        }

        // ---- 14.4: call with arguments, forward reference ----

        [Fact]
        public void CustomBlockCall_TwoArguments_MatchesSpecExample()
        {
            const string source = "id \"A\"\nblock _playLine(name : string, duration : int)\nendblock\n_playLine(name, duration)\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            Assert.Equal("Call(_playLine(Name(name), Name(duration)))", document.Statements[1].ToString());
        }

        [Fact]
        public void CustomBlockCall_BeforeDefinition_ParsesWithoutDiagnostic()
        {
            // 14.1 节："定义先于调用"是名称解析问题（PEVT7115），本阶段只搭语法结构，
            // 调用节点本身不关心块是否已经在源码更早处完成定义——留给后续绑定阶段核对。
            const string source = "id \"A\"\n_notYetDefined()\nblock _notYetDefined()\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void CustomBlockCall_WrongArgumentCount_ParsesWithoutDiagnosticYet()
        {
            // PEVT7112（实参数量不匹配）需要对照块定义的形参数量，属于后续绑定阶段。
            const string source = "id \"A\"\nblock _needsOne(x : int)\nendblock\n_needsOne(1, 2)\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
        }

        // ---- nested block context: return/end apply to the innermost open block ----

        [Fact]
        public void NestedBlockDefinitions_ReturnAppliesToInnermostBlock()
        {
            // 外层块无返回值，内层块有；虽然嵌套定义本身是错误（7104），return 仍应按各自最近的
            // 块签名校验，而不是被外层块的"无返回值"规则连累误报 7107。
            const string source = "id \"A\"\nblock _outer()\nblock _inner() : bool\nvar r : bool = true\nreturn r\nendblock\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7104");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT7106" || d.Id == "PEVT7107" || d.Id == "PEVT7108");
        }

        [Fact]
        public void OrphanEndBlock_InsideIfBody_StillReportsPEVT7118()
        {
            // endblock 不属于 if 正文的合法闭合符，即使写在 if 内部也只是孤立闭合符。
            const string source = "id \"A\"\nif a\nendblock\nendif\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7118");
        }

        // ---- composite flow: handler declare, await, kill in one document ----

        [Fact]
        public void HandlerAwaitKillSequence_ParsesAsThreeIndependentStatements()
        {
            const string source = "id \"A\"\nhandler a = @loadAsync()\nvar r : bool = await a\nkill a\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            Assert.IsType<HandlerDeclarationStatementSyntax>(document.Statements[0]);
            var awaitDecl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[1]);
            Assert.Equal("Await(a)", awaitDecl.Initializer.ToString());
            Assert.IsType<KillStatementSyntax>(document.Statements[2]);
        }

        [Fact]
        public void AwaitNonIdentifierOperand_ReportsPEVT7212()
        {
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nawait 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7212");
        }

        [Fact]
        public void StatusExpression_AsVariableInitializer_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nhandler a = @loadAsync()\nvar s : int = status a\nend");
            Assert.Empty(diagnostics);
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[1]);
            Assert.Equal("Status(a)", decl.Initializer.ToString());
        }

        [Fact]
        public void AwaitAll_SingleHandleSingleBinding_ParsesCleanly()
        {
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc("id \"A\"\nvar n : int = await all(a)(resultA)\nend");
            Assert.Empty(diagnostics);
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[0]);
            Assert.Equal("AwaitAggregate(all, (a), (resultA))", decl.Initializer.ToString());
        }

        [Fact]
        public void RawCmd_MultilineBlock_AsStatement_ParsesCleanly()
        {
            const string source = "id \"A\"\n$raw cmd'''\n第一行\n第二行\n'''\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var raw = Assert.IsType<RawCmdStatementSyntax>(document.Statements[0]);
            Assert.Contains("第一行", raw.Content.Value.AsString);
        }

        // ---- block body can contain the same structured-flow statements as the outer event ----

        [Fact]
        public void BlockBody_ContainsIfAndWhile_ParsesAsOrdinaryNestedStatements()
        {
            const string source = "id \"A\"\nblock _x(flag : bool)\nif flag\nwhile flag\nkill dummy\nendwhile\nendif\nendblock\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var block = Assert.IsType<BlockDefinitionStatementSyntax>(document.Statements[0]);
            var ifStatement = Assert.IsType<IfStatementSyntax>(block.Body[0]);
            var whileStatement = Assert.IsType<WhileStatementSyntax>(ifStatement.Body[0]);
            Assert.IsType<KillStatementSyntax>(whileStatement.Body[0]);
        }

        [Fact]
        public void EndInsideWhileInsideBlock_StillReportsPEVT7120()
        {
            // 7120 的检查只看"是否在任意自定义事件块内部"，不受块内再嵌套 if/while/switch 影响。
            const string source = "id \"A\"\nblock _x()\nwhile true\nend\nendwhile\nendblock\nend";
            (_, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Equal("PEVT7120", Assert.Single(diagnostics).Id);
        }

        [Fact]
        public void AsyncBlockCall_SameAsOrdinaryCall_NoAsyncKeywordAtCallSite()
        {
            // 15.1 节："调用语法不重复书写 async"；异步块的调用形态和同步块完全一样，
            // 是否异步只由定义处的签名决定，本阶段不区分（留给绑定阶段）。
            const string source = "id \"A\"\nasync block _loadScene() : bool\nvar loaded : bool = true\nreturn loaded\nendblock\nvar r : bool = _loadScene()\nend";
            (DocumentSyntax document, IReadOnlyList<Diagnostic> diagnostics) = ParseDoc(source);
            Assert.Empty(diagnostics);
            var decl = Assert.IsType<VariableDeclarationSyntax>(document.Statements[1]);
            Assert.Equal("Call(_loadScene())", decl.Initializer.ToString());
        }
    }
}
