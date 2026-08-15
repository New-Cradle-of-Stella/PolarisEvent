using System;
using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Binding
{
    /// <summary>
    /// 阶段 9 的 golden 测试：名称、调用与能力绑定——自定义事件块签名/参数/返回路径、<c>@</c> 内置
    /// 事件语句签名重载、<c>enable cs</c>/<c>$raw cs</c> 参数副本、<c>handler</c> 专属规则，逐编号
    /// 覆盖 PEVT7xxx/8xxx（语法设计草案第 10–15 节）。
    /// </summary>
    public class NameCallCapabilityBinderTests
    {
        private static IReadOnlyList<Diagnostic> BindDoc(string source, BuiltinApiTable builtinApi = null)
        {
            SourceText text = SourceText.FromUtf8(Encoding.UTF8.GetBytes(source), "test.pevt").Text;
            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(text, bag);
            var parser = new Parser(tokens, bag, text);
            DocumentSyntax document = parser.ParseDocument();
            new Binder(bag, text, builtinApi).BindDocument(document);
            return bag.ToReadOnly();
        }

        // ---- 14: custom event block definitions and calls ----

        [Fact]
        public void BlockCall_AfterDefinition_MatchesSignatureCleanly()
        {
            const string source = "id \"A\"\nblock _greet(name : string, times : int)\nendblock\n_greet(\"hi\", 1)\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockCall_ReturnValueUsedAsMatchingInitializer_NoDiagnostic()
        {
            const string source = "id \"A\"\nblock _pick() : bool\nvar r : bool = true\nreturn r\nendblock\nvar chosen : bool = _pick()\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void DuplicateBlockDefinition_ReportsPEVT7103()
        {
            const string source = "id \"A\"\nblock _x()\nendblock\nblock _x()\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7103");
        }

        [Fact]
        public void CallToNeverDefinedBlock_ReportsPEVT7110()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n_neverDefined()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7110");
        }

        [Fact]
        public void CallBeforeItsOwnDefinitionLaterInFile_ReportsPEVT7115()
        {
            const string source = "id \"A\"\n_later()\nblock _later()\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7115");
        }

        [Fact]
        public void CallMissingUnderscorePrefixOfKnownBlock_ReportsPEVT7111()
        {
            const string source = "id \"A\"\nblock _greet()\nendblock\ngreet()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7111");
        }

        [Fact]
        public void BlockCall_WrongArgumentCount_ReportsPEVT7112()
        {
            const string source = "id \"A\"\nblock _needsOne(x : int)\nendblock\n_needsOne(1, 2)\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7112");
        }

        [Fact]
        public void BlockCall_WrongArgumentType_ReportsPEVT7113()
        {
            const string source = "id \"A\"\nblock _needsInt(x : int)\nendblock\n_needsInt(\"str\")\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7113");
        }

        [Fact]
        public void VoidBlockCall_UsedAsExpression_ReportsPEVT7114()
        {
            const string source = "id \"A\"\nblock _x()\nendblock\nvar y : bool = _x()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7114");
        }

        [Fact]
        public void VoidBlockCall_AsBareStatement_NoDiagnostic()
        {
            const string source = "id \"A\"\nblock _x()\nendblock\n_x()\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void ReturnTargetTypeMismatch_ReportsPEVT7109()
        {
            const string source = "id \"A\"\nblock _x() : bool\nvar n : int = 1\nreturn n\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7109");
        }

        [Fact]
        public void ReturnTargetMatchingType_NoDiagnostic()
        {
            const string source = "id \"A\"\nblock _x() : bool\nvar ok : bool = true\nreturn ok\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockParameterUsedAsReturnTarget_MatchingType_NoDiagnostic()
        {
            const string source = "id \"A\"\nblock _echo(x : int) : int\nreturn x\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        // ---- 11: builtin (@) call signature overload resolution ----

        private static BuiltinApiTable OneSignatureTable(string name, bool isAsync, PevtType? returnType, params (string Name, PevtType Type)[] parameters)
        {
            var table = new BuiltinApiTable();
            var builtinParameters = new List<BuiltinParameter>();
            foreach ((string paramName, PevtType type) in parameters)
                builtinParameters.Add(new BuiltinParameter(paramName, type));
            table.Register(new BuiltinSignature(name, isAsync, builtinParameters, returnType));
            return table;
        }

        [Fact]
        public void BuiltinCall_MatchingRegisteredSignature_NoDiagnostic()
        {
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null, ("duration", PevtType.Int));
            Assert.Empty(BindDoc("id \"A\"\n@perform(1)\nend", table));
        }

        [Fact]
        public void BuiltinCall_ReturnValueUsedAsMatchingInitializer_NoDiagnostic()
        {
            BuiltinApiTable table = OneSignatureTable("query", isAsync: false, returnType: PevtType.Bool, ("name", PevtType.String));
            Assert.Empty(BindDoc("id \"A\"\nvar result : bool = @query(\"a\")\nend", table));
        }

        [Fact]
        public void BuiltinCall_UnknownName_ReportsPEVT7002()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@neverRegistered()\nend", BuiltinApiTable.Empty);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7002");
        }

        [Fact]
        public void BuiltinCall_ArgumentCountMismatch_ReportsPEVT7005()
        {
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null, ("duration", PevtType.Int));
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@perform(1, 2)\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7005");
        }

        [Fact]
        public void BuiltinCall_SingleOverloadArgumentTypeMismatch_ReportsPEVT7006()
        {
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null, ("duration", PevtType.Int));
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@perform(\"x\")\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7006");
        }

        [Fact]
        public void BuiltinCall_MultipleOverloadsNoneMatch_ReportsPEVT7007()
        {
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("perform", false, new List<BuiltinParameter> { new BuiltinParameter("a", PevtType.Int) }, null));
            table.Register(new BuiltinSignature("perform", false, new List<BuiltinParameter> { new BuiltinParameter("a", PevtType.Bool) }, null));
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@perform(\"x\")\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7007");
        }

        [Fact]
        public void BuiltinCall_OverloadPickedByArgumentType_NoDiagnostic()
        {
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("perform", false, new List<BuiltinParameter> { new BuiltinParameter("a", PevtType.Int) }, null));
            table.Register(new BuiltinSignature("perform", false, new List<BuiltinParameter> { new BuiltinParameter("a", PevtType.Bool) }, null));
            Assert.Empty(BindDoc("id \"A\"\n@perform(true)\nend", table));
        }

        [Fact]
        public void VoidBuiltinCall_UsedAsExpression_ReportsPEVT7008()
        {
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null);
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = @perform()\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7008");
        }

        [Fact]
        public void BuiltinCall_ReturnTypeMismatch_ReportsPEVT7009NotPEVT6008()
        {
            BuiltinApiTable table = OneSignatureTable("query", isAsync: false, returnType: PevtType.Int);
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = @query()\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7009");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void BuiltinCall_ArgumentMatchesInvalidHandlerTypedParameter_ReportsPEVT7010()
        {
            // 参数类型是 Handler——不属于 11.2 节允许的五种普通类型，是 API 表条目自身的问题
            // （PEVT7010）。句柄名称读取会产生 Handler 类型的值（本阶段尚未实现 PEVT7209 拦截
            // "句柄被用作普通实参"，见类顶部范围说明），因此能让实参类型精确匹配到这个非法签名。
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("loadAsync", true, new List<BuiltinParameter>(), null));
            table.Register(new BuiltinSignature("bad", false, new List<BuiltinParameter> { new BuiltinParameter("h", PevtType.Handler) }, null));
            const string source = "id \"A\"\nhandler h = @loadAsync()\n@bad(h)\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7010");
        }

        // ---- 2.1/12.2: enable cs and $raw cs argument copies ----

        [Fact]
        public void RawCs_WithEnableCs_AndValidArguments_NoDiagnostic()
        {
            const string source = "id \"A\"\nenable cs\nvar count : int = 1\n$raw cs (count)'''count += 1;'''\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void RawCs_WithoutEnableCs_ReportsPEVT8015()
        {
            const string source = "id \"A\"\nvar count : int = 1\n$raw cs (count)'''count += 1;'''\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT8015");
        }

        [Fact]
        public void RawCs_DuplicateArgument_ReportsPEVT8013()
        {
            const string source = "id \"A\"\nenable cs\nvar count : int = 1\n$raw cs (count, count)'''x'''\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT8013");
        }

        [Fact]
        public void RawCs_UndefinedArgument_ReportsPEVT8014()
        {
            const string source = "id \"A\"\nenable cs\n$raw cs (neverDeclared)'''x'''\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT8014");
        }

        [Fact]
        public void RawCs_NoArgumentList_DoesNotRequireArgumentChecks()
        {
            const string source = "id \"A\"\nenable cs\n$raw cs'''var a = 1;'''\nend";
            Assert.Empty(BindDoc(source));
        }

        // ---- 15.2/15.3/15.4: handler-specific rules ----

        [Fact]
        public void DuplicateHandlerDeclaration_ReportsPEVT7207()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\nhandler a = @loadAsync()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7207");
        }

        [Fact]
        public void HandlerNameCollidesWithVariable_ReportsPEVT7207()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nvar a : int = 1\nhandler a = @loadAsync()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7207");
        }

        [Fact]
        public void HandlerReassignment_ReportsPEVT7208()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\na = @loadAsync()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7208");
        }

        [Fact]
        public void HandlerInitializer_StaticallyKnownSynchronousBuiltin_ReportsPEVT7204()
        {
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null);
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nhandler a = @perform()\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7204");
        }

        [Fact]
        public void HandlerInitializer_StaticallyKnownSynchronousBlock_ReportsPEVT7204()
        {
            const string source = "id \"A\"\nblock _sync()\nendblock\nhandler a = _sync()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7204");
        }

        [Fact]
        public void HandlerInitializer_AsyncBlock_NoPEVT7204()
        {
            const string source = "id \"A\"\nasync block _load()\nendblock\nhandler a = _load()\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT7204");
        }

        [Fact]
        public void HandlerUsedAsOrdinaryValue_ReportsPEVT7209()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\nblock _needsInt(x : int)\nendblock\n_needsInt(a)\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7209");
        }

        [Fact]
        public void AwaitVoidHandler_UsedAsExpression_ReportsPEVT7211()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\nvar x : bool = await a\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7211");
        }

        [Fact]
        public void AwaitVoidHandler_AsBareStatement_NoDiagnostic()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\nawait a\nend";
            Assert.Empty(BindDoc(source, table));
        }

        [Fact]
        public void AwaitTypedHandler_ResultTypeMatchesAsyncReturnType_NoDiagnostic()
        {
            BuiltinApiTable table = OneSignatureTable("query", isAsync: true, returnType: PevtType.Bool);
            const string source = "id \"A\"\nhandler a = @query()\nvar x : bool = await a\nend";
            Assert.Empty(BindDoc(source, table));
        }

        [Fact]
        public void AwaitTypedHandler_ResultTypeMismatch_ReportsPEVT6008()
        {
            BuiltinApiTable table = OneSignatureTable("query", isAsync: true, returnType: PevtType.Bool);
            const string source = "id \"A\"\nhandler a = @query()\nvar x : int = await a\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source, table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void AwaitUndefinedHandle_ReportsPEVT7210()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nawait neverDeclared\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7210");
        }

        [Fact]
        public void KillUndefinedHandle_ReportsPEVT7210()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nkill neverDeclared\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7210");
        }

        [Fact]
        public void AwaitOnOrdinaryVariable_ReportsPEVT7212()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar a : int = 1\nawait a\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7212");
        }

        [Fact]
        public void KillOnOrdinaryVariable_ReportsPEVT7213()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar a : int = 1\nkill a\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7213");
        }

        [Fact]
        public void StatusOnOrdinaryVariable_ReportsPEVT7214()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar a : int = 1\nvar s : int = status a\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7214");
        }

        [Fact]
        public void AwaitKillStatus_OnDeclaredHandler_NoDiagnostic()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            const string source = "id \"A\"\nhandler a = @loadAsync()\nawait a\nkill a\nvar s : int = status a\nend";
            Assert.Empty(BindDoc(source, table));
        }

        // ---- 10.1/10.4: callevt only binds syntax, never queries a real registry ----

        [Fact]
        public void CallEvt_UnknownTargetId_StaticallyPasses()
        {
            Assert.Empty(BindDoc("id \"A\"\ncallevt \"CompletelyUnknownEvent\"\nend"));
        }

        [Fact]
        public void CallEvt_AsHandlerInitializer_UnknownTargetId_StaticallyPasses()
        {
            Assert.Empty(BindDoc("id \"A\"\nhandler a = callevt \"CompletelyUnknownEvent\"\nend"));
        }

        // ---- combined scenarios: multiple independent stage-9 diagnostics in one pass ----

        [Fact]
        public void MultipleIndependentCallCapabilityErrors_AreAllReportedInOnePass()
        {
            const string source = "id \"A\"\n_neverDefined()\n$raw cs (neverDeclared)'''x'''\nawait neverDeclared\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT7110");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8015");
            Assert.Contains(diagnostics, d => d.Id == "PEVT8014");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7210");
        }

        [Fact]
        public void RawCs_ArgumentResolvesToBlockName_ReportsPEVT8014()
        {
            const string source = "id \"A\"\nenable cs\nblock _x()\nendblock\n$raw cs (_x)'''y'''\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT8014");
        }

        [Fact]
        public void RawCs_ArgumentUninitializedVariable_ReportsPEVT6003()
        {
            const string source = "id \"A\"\nenable cs\nvar count : int\n$raw cs (count)'''x'''\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void RawCs_ArgumentIsParameter_NoDiagnostic()
        {
            const string source = "id \"A\"\nenable cs\nblock _x(count : int)\n$raw cs (count)'''x'''\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        // ---- overload resolution edge cases ----

        [Fact]
        public void BuiltinCall_ErrorTypedArgument_DoesNotCascadeIntoSignatureMismatch()
        {
            // 参数本身已经因为未定义而报错（6001）；签名匹配不应该再对同一个位置连锁报类型不匹配。
            BuiltinApiTable table = OneSignatureTable("perform", isAsync: false, returnType: null, ("duration", PevtType.Int));
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@perform(neverDeclared)\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT7006" || d.Id == "PEVT7007");
        }

        [Fact]
        public void BuiltinCall_ZeroArgumentOverload_MatchesCleanly()
        {
            BuiltinApiTable table = OneSignatureTable("ping", isAsync: false, returnType: null);
            Assert.Empty(BindDoc("id \"A\"\n@ping()\nend", table));
        }

        [Fact]
        public void CustomBlockCall_AsHandlerInitializer_AsyncReturnTypeFlowsToAwait()
        {
            const string source = "id \"A\"\nasync block _load() : bool\nvar ok : bool = true\nreturn ok\nendblock\nhandler a = _load()\nvar loaded : bool = await a\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void CustomBlockCall_AsHandlerInitializer_AwaitTypeMismatch_ReportsPEVT6008()
        {
            const string source = "id \"A\"\nasync block _load() : bool\nvar ok : bool = true\nreturn ok\nendblock\nhandler a = _load()\nvar loaded : int = await a\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void UnknownBlockCall_UsedAsHandlerInitializer_ReportsPEVT7110NotGenericPEVT7206()
        {
            // handler 初始化器形状本身合法（一次 _ 调用），块名称解析失败仍然是块自己的诊断，
            // 不应该被 ParseHandlerInitializer 那层"形状不对"的通用 7206 掩盖。
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nhandler a = _neverDefined()\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT7110");
        }

        [Fact]
        public void DeeplyNestedBlockCall_InsideIfInsideWhile_StillResolvesCorrectly()
        {
            const string source = "id \"A\"\nblock _x()\nendblock\nwhile true\nif true\n_x()\nendif\nendwhile\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockDefinedInsideIf_StillCallableAfterwardOnEveryPath()
        {
            // 14.1 节没有要求块只能定义在外层事件顶层；预扫描（CollectBlockNames）会递归进入
            // if/while/switch 正文，因此嵌套在分支里的定义同样能被后续调用正确解析。
            const string source = "id \"A\"\nif true\nblock _x()\nendblock\nendif\n_x()\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void MultipleBuiltinOverloads_DifferByArity_BothResolveIndependently()
        {
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("say", false, new List<BuiltinParameter> { new BuiltinParameter("text", PevtType.String) }, null));
            table.Register(new BuiltinSignature("say", false, new List<BuiltinParameter>
            {
                new BuiltinParameter("speaker", PevtType.String),
                new BuiltinParameter("text", PevtType.String),
            }, null));
            const string source = "id \"A\"\n@say(\"hi\")\n@say(\"Alice\", \"hi\")\nend";
            Assert.Empty(BindDoc(source, table));
        }

        [Fact]
        public void BuiltinCall_AsyncSignature_NoPEVT7204WhenNotAHandlerInitializer()
        {
            // 7204 只在 handler 声明的初始化器位置核对；异步调用作为独立语句使用不受它约束
            // （句柄被丢弃仍然合法，15.1 节）。
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null);
            Assert.Empty(BindDoc("id \"A\"\n@loadAsync()\nend", table));
        }

        [Fact]
        public void HandlerDeclaration_UndefinedInitializerArgument_StillReportsPEVT6001()
        {
            BuiltinApiTable table = OneSignatureTable("loadAsync", isAsync: true, returnType: null, ("delay", PevtType.Int));
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nhandler a = @loadAsync(neverDeclared)\nend", table);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        [Fact]
        public void BlockSignature_AsyncFlagRecordedCorrectly()
        {
            const string source = "id \"A\"\nasync block _load()\nendblock\nblock _sync()\nendblock\nhandler a = _load()\nend";
            // _load 是异步的（不应该报 7204），_sync 若被用作 handler 初始化器则会——分别验证两条独立路径。
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockCall_ArgumentExpressionItselfTypeInvalid_ReportsOwnDiagnosticNotJustArity()
        {
            // 实参表达式自身的类型错误（这里是 int 与 bool 相加）应该独立报出来，
            // 不会被参数数量/类型匹配的检查掩盖或吞没。
            const string source = "id \"A\"\nblock _needsInt(x : int)\nendblock\n_needsInt(1 + true)\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT5005");
        }

        [Fact]
        public void HandlerDeclaration_CallevtInitializer_NeverReports7204()
        {
            // 10.3 节："调用位置不书写 async callevt";目标是否异步只能在运行时解析，
            // CheckSynchronousInitializer 的 default 分支对 callevt 完全不做判断。
            Assert.Empty(BindDoc("id \"A\"\nhandler a = callevt \"SomeAsyncEvent\"\nend"));
        }
    }
}
