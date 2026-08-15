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
    /// 阶段 8 的 golden 测试：表达式类型矩阵与 PEVT5xxx/6xxx 逐编号覆盖（语法设计草案第 8/9 节）。
    /// </summary>
    public class BinderTests
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

        // ---- 8.4/8.7: binary operator type matrix ----

        [Fact]
        public void MathOperator_SameNumericType_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int = 1 + 2\nend"));
        }

        [Fact]
        public void MathOperator_MixedIntFloat_ReportsPEVT5004()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : float = 1 + 2.0\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5004");
        }

        [Fact]
        public void MathOperator_NonNumericOperands_ReportsPEVT5005()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = true + false\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5005");
        }

        [Theory]
        [InlineData("var x : bool = 1 == 1")]
        [InlineData("var x : bool = 1.0 == 1.0")]
        [InlineData("var x : bool = true == true")]
        [InlineData("var x : bool = 'a' == 'a'")]
        [InlineData("var x : bool = \"a\" == \"a\"")]
        public void EqualityOperator_AnyOfFiveTypes_NoDiagnostic(string statement)
        {
            Assert.Empty(BindDoc($"id \"A\"\n{statement}\nend"));
        }

        [Fact]
        public void EqualityOperator_MismatchedTypes_ReportsPEVT5004()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = 1 == 1.0\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5004");
        }

        [Fact]
        public void OrderedComparison_SameNumericType_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : bool = 1 < 2\nend"));
        }

        [Fact]
        public void OrderedComparison_NonNumericOperand_ReportsPEVT5011()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = true < false\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5011");
        }

        [Fact]
        public void OrderedComparison_IntVsFloat_ReportsPEVT5004()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = 1 < 2.0\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5004");
        }

        [Fact]
        public void LogicalOperator_BothBool_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : bool = true & false\nend"));
            Assert.Empty(BindDoc("id \"A\"\nvar x : bool = true | false\nend"));
            Assert.Empty(BindDoc("id \"A\"\nvar x : bool = true ^ false\nend"));
        }

        [Fact]
        public void LogicalOperator_NonBoolOperand_ReportsPEVT5007()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = 1 & true\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5007");
        }

        [Fact]
        public void LogicalNot_NonBoolOperand_ReportsPEVT5007()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = !1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5007");
        }

        [Fact]
        public void UnaryMinus_NonNumericOperand_ReportsPEVT5024()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = -true\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5024");
        }

        [Fact]
        public void UnaryMinus_Numeric_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int = -1\nend"));
            Assert.Empty(BindDoc("id \"A\"\nvar x : float = -1.0\nend"));
        }

        // ---- 8.3: explicit conversion ----

        [Fact]
        public void Conversion_IntToFloat_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar count : int = 1\nvar x : float = (float)count\nend"));
        }

        [Fact]
        public void Conversion_CharToString_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar letter : char = 'a'\nvar x : string = (string)letter\nend"));
        }

        [Fact]
        public void Conversion_FloatSourceNotInt_ReportsPEVT5012()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar text : string = \"a\"\nvar x : float = (float)text\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5012");
        }

        [Fact]
        public void Conversion_StringSourceNotChar_ReportsPEVT5012()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar count : int = 1\nvar x : string = (string)count\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5012");
        }

        [Fact]
        public void Conversion_UndefinedTarget_ReportsPEVT5013()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : float = (float)neverDeclared\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5013");
        }

        // ---- 4/6.1: condition and switch-case type checks ----

        [Fact]
        public void IfCondition_NotBool_ReportsPEVT5008()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nif 1\nend\nendif\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5008");
        }

        [Fact]
        public void WhileCondition_NotBool_ReportsPEVT5008()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nwhile 1\nendwhile\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5008");
        }

        [Fact]
        public void SwitchCase_MismatchedType_ReportsPEVT5009()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = 1\nswitch x\ncase \"a\"\nend\nendswitch\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5009");
        }

        [Fact]
        public void SwitchCase_MatchingType_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int = 1\nswitch x\ncase 1\nend\nendswitch\nend"));
        }

        // ---- 9.1/9.2: declaration and initialization ----

        [Fact]
        public void UndefinedVariableRead_ReportsPEVT6001()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = neverDeclared\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        [Fact]
        public void ReadBeforeAssignment_ReportsPEVT6003()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int\nvar y : int = x\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void ReadAfterAssignment_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int\nx = 1\nvar y : int = x\nend"));
        }

        [Fact]
        public void DuplicateDeclaration_ReportsPEVT6007()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = 1\nvar x : int = 2\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6007");
        }

        [Fact]
        public void VariableInitializerTypeMismatch_ReportsPEVT6008()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = \"str\"\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void ConstantInitializerTypeMismatch_ReportsPEVT6008()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nconst x : int = \"str\"\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void LaterAssignmentTypeMismatch_AlsoReportsPEVT6008()
        {
            // 9.3 节把普通赋值描述成"再一次初始化并保存快照"，因此复用同一个诊断编号，而不是另造一个。
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = 1\nx = \"str\"\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6008");
        }

        [Fact]
        public void ConstantReassignment_ReportsPEVT6010()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nconst x : int = 1\nx = 2\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6010");
        }

        [Fact]
        public void ConstantUsedAsExpressionOperand_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nconst x : int = 1\nvar y : int = x + 1\nend"));
        }

        [Fact]
        public void AssignToBlockName_ReportsPEVT6002()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nblock _foo()\nendblock\n_foo = 1\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6002");
        }

        [Fact]
        public void SnapshotSemantics_LaterMutationDoesNotAffectAlreadyBoundConstant()
        {
            // 9.3 节的示例：const 保存的是求值时的快照，绑定器本身不建立任何动态引用，
            // 因此后续对 source 的重新赋值不应该让这条早已绑定过的语句重新报错。
            const string source = "id \"A\"\nvar source : int = 1\nconst snapshot : int = source\nsource = 2\nend";
            Assert.Empty(BindDoc(source));
        }

        // ---- branch merge: definite-assignment across if/while/switch ----

        [Fact]
        public void BothIfAndElseInitialize_UsableAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nif true\nx = 1\nelse\nx = 2\nendif\nvar y : int = x\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void OnlyIfBranchInitializes_NoElse_StillUninitializedAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nif true\nx = 1\nendif\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void DeclaredOnlyInsideOneBranch_UndefinedAfterward()
        {
            // 声明本身也要求"所有可达路径"都执行到，不只是初始化状态。
            const string source = "id \"A\"\nif true\nvar x : int = 1\nendif\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        [Fact]
        public void WhileBody_AssignsVariable_StillUninitializedAfterLoop()
        {
            // while 正文可能一次也不执行，循环之后的状态等于循环之前的状态。
            const string source = "id \"A\"\nvar x : int\nwhile true\nx = 1\nendwhile\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void SwitchWithDefault_AllArmsInitialize_UsableAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nswitch 1\ncase 1\nx = 1\ndefault\nx = 2\nendswitch\nvar y : int = x\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void SwitchWithoutDefault_StillUninitializedAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nswitch 1\ncase 1\nx = 1\nendswitch\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        // ---- custom block bodies: isolated environments ----

        [Fact]
        public void BlockParameter_IsAlreadyDefinedAndInitializedInsideBody()
        {
            const string source = "id \"A\"\nblock _greet(name : string)\nvar copy : string = name\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockBody_CannotSeeOuterVariable()
        {
            // 9.4 节："自定义事件块不会隐式捕获外层环境中的变量"——从块体内看，外层变量属于
            // 另一个环境，报的是 6012（存在，只是在别的环境），不是"哪里都不存在"的 6001。
            const string source = "id \"A\"\nvar outer : int = 1\nblock _foo()\nvar y : int = outer\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6012");
        }

        [Fact]
        public void OuterEnvironment_CannotSeeBlockLocalVariable()
        {
            const string source = "id \"A\"\nblock _foo()\nvar inner : int = 1\nendblock\nvar y : int = inner\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6012");
        }

        // ---- calls/handles ----

        [Fact]
        public void BuiltinCallArgument_UndefinedVariable_StillReportsPEVT6001()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\n@say(neverDeclared)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        [Fact]
        public void HandlerAwaitKillCallevtExec_WithRegisteredSignature_BindCleanly()
        {
            // 阶段 7 引入的这些语句形态到阶段 9 才真正绑定：句柄声明/await/kill 走句柄专属规则，
            // @ 调用按注册好的签名匹配，callevt 只认语法（未知目标 ID 静态通过，10.4 节）。
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("loadAsync", isAsync: true, System.Array.Empty<BuiltinParameter>(), returnType: null));
            const string source = "id \"A\"\nhandler a = @loadAsync()\nawait a\nkill a\ncallevt \"Other\"\nexec(\"x\")\nend";
            Assert.Empty(BindDoc(source, table));
        }

        [Fact]
        public void CustomBlockCallArgument_UndefinedVariable_StillReportsPEVT6001()
        {
            const string source = "id \"A\"\nblock _greet(name : string)\nendblock\n_greet(neverDeclared)\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        [Fact]
        public void ExecArgument_UndefinedVariable_StillReportsPEVT6001()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nexec(neverDeclared)\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        // ---- parenthesized expressions propagate the inner type, including errors ----

        [Fact]
        public void ParenthesizedExpression_PropagatesInnerType_NoDiagnostic()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int = (1 + 2)\nend"));
        }

        [Fact]
        public void ParenthesizedExpression_WrapsMismatch_ReportsPEVT5004Once()
        {
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : float = (1 + 2.0)\nend");
            Assert.Single(diagnostics, d => d.Id == "PEVT5004");
        }

        [Fact]
        public void ErrorInsideChain_DoesNotCascadeIntoASecondDiagnostic()
        {
            // 左操作数已经是未定义变量（6001），链上后续运算不应该因为 Error 类型再连锁报一次类型错误。
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : int = neverDeclared + 1\nend");
            Assert.Single(diagnostics, d => d.Id == "PEVT6001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "PEVT5004" || d.Id == "PEVT5005");
        }

        // ---- flat left-to-right chains mix operator families without implicit precedence ----

        [Fact]
        public void FlatChain_ComparisonThenMath_TreatsBoolResultAsLeftOperand()
        {
            // 8.8 节：没有隐式优先级，"a == b + c" 等价于 "(a == b) + c"——bool 链接 + 不合法。
            IReadOnlyList<Diagnostic> diagnostics = BindDoc("id \"A\"\nvar x : bool = 1 == 1 + 2\nend");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5005");
        }

        [Fact]
        public void FlatChain_AllMathSameType_ChainsCleanly()
        {
            Assert.Empty(BindDoc("id \"A\"\nvar x : int = 1 + 2 - 3 * 4\nend"));
        }

        // ---- if/elif/else with three branches: merge across every arm ----

        [Fact]
        public void IfElifElse_AllThreeBranchesInitialize_UsableAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nif true\nx = 1\nelif false\nx = 2\nelse\nx = 3\nendif\nvar y : int = x\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void IfElifElse_OnlyElifInitializes_StillUninitializedAfterward()
        {
            const string source = "id \"A\"\nvar x : int\nif true\nelif false\nx = 2\nelse\nend\nendif\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void NestedIfInsideWhile_InnerAssignment_StillUninitializedAfterOuterWhile()
        {
            // 循环体本身已经不保证执行；循环体内部再嵌套一层 if/else 双路都赋值，也不改变
            // "整个循环可能一次都不跑"这个事实。
            const string source = "id \"A\"\nvar x : int\nwhile true\nif true\nx = 1\nelse\nx = 2\nendif\nendwhile\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void DeclarationInsideWhileBody_UndefinedAfterLoop()
        {
            const string source = "id \"A\"\nwhile true\nvar x : int = 1\nendwhile\nvar y : int = x\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
        }

        // ---- parameters and constants inside a block body share the block's own environment ----

        [Fact]
        public void BlockBody_MultipleParameters_AllUsableWithoutInitializationDiagnostic()
        {
            const string source = "id \"A\"\nblock _tell(name : string, count : int)\nvar x : bool = count == 1\nvar y : string = name\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void BlockBody_LocalConstant_UsableImmediately()
        {
            const string source = "id \"A\"\nblock _x()\nconst limit : int = 3\nvar ok : bool = limit == 3\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void TwoSiblingBlocks_DoNotShareEnvironment()
        {
            // 每个自定义事件块各自独立一个环境；_second 看不到 _first 块体内声明的局部变量
            // （名称在文件里存在，只是在另一个环境——6012，而不是哪里都不存在的 6001）。
            const string source = "id \"A\"\nblock _first()\nvar local : int = 1\nendblock\nblock _second()\nvar y : int = local\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6012");
        }

        // ---- full expression type matrix across every ordinary type, both directions ----

        [Theory]
        [InlineData("int", "1", "float", "1.0")]
        [InlineData("float", "1.0", "bool", "true")]
        [InlineData("bool", "true", "char", "'a'")]
        [InlineData("char", "'a'", "string", "\"a\"")]
        [InlineData("string", "\"a\"", "int", "1")]
        public void EqualityAcrossDifferentTypes_AlwaysReportsPEVT5004(string typeA, string literalA, string typeB, string literalB)
        {
            string source = $"id \"A\"\nvar a : {typeA} = {literalA}\nvar b : {typeB} = {literalB}\nvar r : bool = a == b\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT5004");
        }

        [Theory]
        [InlineData("int", "1")]
        [InlineData("float", "1.0")]
        [InlineData("bool", "true")]
        [InlineData("char", "'a'")]
        [InlineData("string", "\"a\"")]
        public void EqualityWithinSameType_NoDiagnostic(string type, string literal)
        {
            string source = $"id \"A\"\nvar a : {type} = {literal}\nvar b : {type} = {literal}\nvar r : bool = a != b\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void MultipleIndependentErrors_AreAllReportedInOnePass()
        {
            // 一次绑定应该把多个互不相关的问题都独立报出来，互不吞没。
            const string source = "id \"A\"\nvar x : int = neverDeclared\nvar y : bool = 1 & true\nconst z : int = 1\nz = 2\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6001");
            Assert.Contains(diagnostics, d => d.Id == "PEVT5007");
            Assert.Contains(diagnostics, d => d.Id == "PEVT6010");
        }

        [Fact]
        public void ConstantDeclaration_DuplicateAgainstVariable_ReportsPEVT6007()
        {
            // 6007 覆盖的是"变量或常量"共用同一命名空间，不区分声明形式。
            const string source = "id \"A\"\nvar x : int = 1\nconst x : int = 2\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6007");
        }

        [Fact]
        public void ParameterName_DuplicateAsLocalVariable_ReportsPEVT6007()
        {
            const string source = "id \"A\"\nblock _x(name : string)\nvar name : int = 1\nendblock\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6007");
        }

        [Fact]
        public void UninitializedVariable_UsedAsConversionSource_ReportsPEVT6003()
        {
            const string source = "id \"A\"\nvar count : int\nvar x : float = (float)count\nend";
            IReadOnlyList<Diagnostic> diagnostics = BindDoc(source);
            Assert.Contains(diagnostics, d => d.Id == "PEVT6003");
        }

        [Fact]
        public void ConstantUsedAsConversionSource_NoDiagnostic()
        {
            // 常量声明即初始化（9.2 节），因此可以立即作为转换来源使用。
            const string source = "id \"A\"\nconst count : int = 1\nvar x : float = (float)count\nend";
            Assert.Empty(BindDoc(source));
        }

        [Fact]
        public void ParameterUsedAsConversionSource_NoDiagnostic()
        {
            const string source = "id \"A\"\nblock _x(count : int)\nvar y : float = (float)count\nendblock\nend";
            Assert.Empty(BindDoc(source));
        }
    }
}
