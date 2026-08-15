using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 阶段 8 的表达式/变量绑定器（PevtType、符号、词法环境、五种普通类型的表达式类型系统、快照语义、
    /// 只读常量和局部未初始化检查）在此基础上，阶段 9 补上"名称、调用与能力绑定"：跨环境的定义先于
    /// 使用区分（PEVT6001 与 6012）、自定义事件块的签名/参数/返回路径绑定、<c>@</c> 内置事件语句的
    /// 签名重载匹配、<c>enable cs</c>/<c>$raw cs</c> 参数副本绑定，以及 <c>handler</c> 的专属规则
    /// （声明查重 7207、重新赋值 7208、静态已知同步的调用 7204、<c>await</c>/<c>kill</c>/<c>status</c>
    /// 的句柄解析 7210/7212-7214、无返回值 <c>await</c> 用作表达式 7211、句柄用在三者以外的位置 7209）。
    /// 验证目标是 PEVT7xxx/8xxx 逐编号覆盖（callevt 静态阶段刻意保持"只认语法，不查真实 ID"）。
    ///
    /// 仍然明确不在本阶段范围内（留给后续阶段，见各方法上的具体说明，或本段列出原因）：
    /// - PEVT7117（块"是否每条路径都返回"）：真正的控制流可达性分析是阶段 10 的"检测……块返回路径"。
    /// - PEVT7203（AsyncCallUsedAsOrdinaryValue）：PEVT7209 的检查点（<see cref="BindOrdinaryNameRead"/>）
    ///   覆盖的是"读取一个已经声明的句柄名称"，7203 描述的是反过来的方向——异步调用本身（而不是先
    ///   声明成 handler）被直接当普通值使用，例如 <c>var x : bool = @asyncQuery()</c>；这需要在
    ///   <see cref="BindBuiltinCall"/>/<see cref="BindCustomBlockCall"/> 内部再额外区分"匹配到的签名
    ///   是异步的"，目前二者只按同步语义返回声明的返回类型。
    /// - PEVT72xx 集合等待（<c>await all</c>/<c>await any</c>）的句柄列表/绑定列表校验、
    ///   <c>$raw cs</c> 的 C# 内容本身（PEVT8007-8010）、<c>exec</c> 参数的静态字符串类型核对
    ///   （PEVT7403，明确归 13.4 节描述的运行时动态执行阶段）。
    /// </summary>
    public sealed class Binder
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly SourceText _source;
        private readonly BuiltinApiTable _builtinApi;

        /// <summary>整份文件里"曾经在某个环境声明过"的全部名称——只用来把 6001（哪里都不存在）
        /// 和 6012（存在，但在另一个环境）区分开，不参与任何路径敏感判断。</summary>
        private readonly HashSet<string> _everDeclaredAnywhere = new HashSet<string>();

        /// <summary>整份文件里出现过的全部自定义事件块名称（含尚未处理到、甚至非法嵌套的），
        /// 由 <see cref="CollectBlockNames"/> 一次性预扫描得到，用来区分 PEVT7110（哪里都没定义过）
        /// 和 PEVT7115（定义在文件别处，只是调用点还没走到那儿）。</summary>
        private readonly HashSet<string> _allBlockNamesInFile = new HashSet<string>();

        /// <summary>已经完整绑定完毕（含配对 endblock）的块签名——只有调用点在文本顺序上位于
        /// 整个定义之后，才能在这张表里查到，这就是"定义先于调用"检查本身。</summary>
        private readonly Dictionary<string, BlockSignature> _readyBlocks = new Dictionary<string, BlockSignature>();

        /// <summary>当前嵌套打开的块的声明返回类型（null 表示无返回值块），供 <c>return</c> 目标的
        /// 类型核对使用（PEVT7109）。</summary>
        private readonly Stack<PevtType?> _blockReturnTypeStack = new Stack<PevtType?>();

        private bool _hasCsCapability;

        public Binder(DiagnosticBag diagnostics, SourceText source, BuiltinApiTable builtinApi = null)
        {
            _diagnostics = diagnostics;
            _source = source;
            _builtinApi = builtinApi ?? BuiltinApiTable.Empty;
        }

        private void Report(string diagnosticId, TextSpan span) =>
            _diagnostics.AddFromCatalog(diagnosticId, _source.GetLocation(span));

        /// <summary>外层事件用一个全新环境；每个自定义事件块定义各自再用一个全新、互不关联的环境
        /// （9.4 节："自定义事件块不会隐式捕获外层环境"）。绑定前先做一次全文件块名预扫描，
        /// 好让"定义先于调用"（7115）能和"哪里都没定义"（7110）区分开。</summary>
        public void BindDocument(DocumentSyntax document)
        {
            _hasCsCapability = document.EnableDeclarations.Any(e => e.Capability.Kind == SyntaxKind.CsKeyword);
            CollectBlockNames(document.Statements);
            BindStatements(document.Statements, new BoundEnvironment());
        }

        private void CollectBlockNames(IReadOnlyList<StatementSyntax> statements)
        {
            foreach (StatementSyntax statement in statements)
            {
                switch (statement)
                {
                    case BlockDefinitionStatementSyntax block:
                        if (!block.Name.IsMissing)
                            _allBlockNamesInFile.Add(block.Name.Text);
                        CollectBlockNames(block.Body);
                        break;
                    case IfStatementSyntax ifStatement:
                        CollectBlockNames(ifStatement.Body);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            CollectBlockNames(elif.Body);
                        if (ifStatement.ElseClause != null)
                            CollectBlockNames(ifStatement.ElseClause.Body);
                        break;
                    case WhileStatementSyntax whileStatement:
                        CollectBlockNames(whileStatement.Body);
                        break;
                    case SwitchStatementSyntax switchStatement:
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                            CollectBlockNames(arm.Body);
                        break;
                }
            }
        }

        // ---- statements ----

        public void BindStatements(IReadOnlyList<StatementSyntax> statements, BoundEnvironment env)
        {
            foreach (StatementSyntax statement in statements)
                BindStatement(statement, env);
        }

        private void BindStatement(StatementSyntax statement, BoundEnvironment env)
        {
            switch (statement)
            {
                case VariableDeclarationSyntax variable: BindVariableDeclaration(variable, env); break;
                case ConstantDeclarationSyntax constant: BindConstantDeclaration(constant, env); break;
                case AssignmentStatementSyntax assignment: BindAssignment(assignment, env); break;
                case ReturnStatementSyntax returnStatement: BindReturnTarget(returnStatement, env); break;
                case IfStatementSyntax ifStatement: BindIf(ifStatement, env); break;
                case WhileStatementSyntax whileStatement: BindWhile(whileStatement, env); break;
                case SwitchStatementSyntax switchStatement: BindSwitch(switchStatement, env); break;
                case ExpressionStatementSyntax expressionStatement: BindExpression(expressionStatement.Expression, env, isStatementContext: true); break;
                case BlockDefinitionStatementSyntax block: BindBlockDefinition(block, env); break;
                case HandlerDeclarationStatementSyntax handler: BindHandlerDeclaration(handler, env); break;
                case KillStatementSyntax kill: BindHandleOperand(kill.Handle, env, "PEVT7213"); break;
                default: break; // label/goto/end/unknown/$raw cmd 语句：无名称/类型语义可绑定。
            }
        }

        private void Declare(BoundEnvironment env, Symbol symbol, bool initialized)
        {
            env.Declare(symbol, initialized);
            _everDeclaredAnywhere.Add(symbol.Name);
        }

        private void BindVariableDeclaration(VariableDeclarationSyntax node, BoundEnvironment env)
        {
            PevtType declaredType = PevtTypeFacts.FromTypeKeyword(node.Type.Kind);
            bool hasInitializer = node.Initializer != null;
            PevtType initializerType = hasInitializer ? BindExpression(node.Initializer, env) : PevtType.Error;

            if (!DeclareOrReportDuplicate(node.Name, new VariableSymbol(node.Name.Text, declaredType), hasInitializer, env))
                return;

            if (hasInitializer && declaredType.IsOrdinaryType() && initializerType.IsOrdinaryType() && initializerType != declaredType)
                ReportInitializerMismatch(node.Initializer);
        }

        private void BindConstantDeclaration(ConstantDeclarationSyntax node, BoundEnvironment env)
        {
            PevtType declaredType = PevtTypeFacts.FromTypeKeyword(node.Type.Kind);
            PevtType initializerType = BindExpression(node.Initializer, env);

            if (!DeclareOrReportDuplicate(node.Name, new ConstantSymbol(node.Name.Text, declaredType), initialized: true, env))
                return;

            if (declaredType.IsOrdinaryType() && initializerType.IsOrdinaryType() && initializerType != declaredType)
                ReportInitializerMismatch(node.Initializer);
        }

        /// <summary>PEVT6007：同一环境内重复声明。名称一旦在环境里出现过（不论来自哪条分支）就永久占用，
        /// 不做路径敏感判断——见 <see cref="BoundEnvironment"/> 顶部注释里对这个取舍的说明。</summary>
        private bool DeclareOrReportDuplicate(SyntaxToken nameToken, Symbol symbol, bool initialized, BoundEnvironment env)
        {
            if (nameToken.IsMissing)
                return false;

            if (env.IsDeclaredEver(nameToken.Text))
            {
                Report("PEVT6007", nameToken.Span);
                return false;
            }

            Declare(env, symbol, initialized);
            return true;
        }

        /// <summary>PEVT6008 是通用的"初始化表达式类型不同"；当来源直接是一次 <c>@</c> 调用时，
        /// 诊断表已经为这个更具体的场景单独分配了 PEVT7009，按计划规则优先使用更精确的编号。</summary>
        private void ReportInitializerMismatch(ExpressionSyntax valueExpression) =>
            Report(valueExpression is BuiltinCallExpressionSyntax ? "PEVT7009" : "PEVT6008", valueExpression.Span);

        private void BindAssignment(AssignmentStatementSyntax node, BoundEnvironment env)
        {
            PevtType valueType = BindExpression(node.Value, env);
            if (node.Target.IsMissing)
                return;

            if (!env.TryGetSymbol(node.Target.Text, out Symbol symbol))
            {
                ReportUndefinedOrOutsideEnvironment(node.Target);
                return;
            }

            switch (symbol)
            {
                case ConstantSymbol: Report("PEVT6010", node.Target.Span); return;
                case HandlerSymbol: Report("PEVT7208", node.Target.Span); return;
                case BlockSymbol: Report("PEVT6002", node.Target.Span); return;
            }

            env.MarkInitialized(node.Target.Text);
            if (symbol.Type.IsOrdinaryType() && valueType.IsOrdinaryType() && valueType != symbol.Type)
                ReportInitializerMismatch(node.Value);
        }

        /// <summary>9.4 节："变量必须在……作为 return 目标之前完成赋值"——按普通变量读取处理
        /// （PEVT6001/6003/6012）。目标类型是否匹配当前块的声明返回类型是 PEVT7109。</summary>
        private void BindReturnTarget(ReturnStatementSyntax node, BoundEnvironment env)
        {
            if (node.Target == null)
                return;

            PevtType targetType = BindNameRead(node.Target, env);
            if (targetType == PevtType.Error || _blockReturnTypeStack.Count == 0)
                return;

            PevtType? expected = _blockReturnTypeStack.Peek();
            if (expected.HasValue && targetType.IsOrdinaryType() && targetType != expected.Value)
                Report("PEVT7109", node.Target.Span);
        }

        private void BindIf(IfStatementSyntax node, BoundEnvironment env)
        {
            RequireBool(node.Condition, BindExpression(node.Condition, env));
            foreach (ElifClauseSyntax elif in node.ElifClauses)
                RequireBool(elif.Condition, BindExpression(elif.Condition, env));

            Dictionary<string, bool> preState = env.SnapshotFlowState();
            var branchStates = new List<Dictionary<string, bool>>();

            BindBranch(node.Body, env, preState, branchStates);
            foreach (ElifClauseSyntax elif in node.ElifClauses)
                BindBranch(elif.Body, env, preState, branchStates);
            if (node.ElseClause != null)
                BindBranch(node.ElseClause.Body, env, preState, branchStates);

            env.Restore(BoundEnvironment.Merge(branchStates, isExhaustive: node.ElseClause != null, preState));
        }

        /// <summary>5 节：循环体可能一次也不执行，因此循环之后的状态就是循环之前的状态；循环体仍然
        /// 用一份克隆单独走一遍绑定，只是为了在体内本身报告读取/声明相关的诊断（单趟扫描，不做
        /// "下一轮迭代能看到本轮末尾赋值"的不动点分析——已知的简化，见类顶部范围说明的姊妹记录）。</summary>
        private void BindWhile(WhileStatementSyntax node, BoundEnvironment env)
        {
            Dictionary<string, bool> preState = env.SnapshotFlowState();
            RequireBool(node.Condition, BindExpression(node.Condition, env));

            BindStatements(node.Body, env);
            env.Restore(preState);
        }

        private void BindSwitch(SwitchStatementSyntax node, BoundEnvironment env)
        {
            PevtType valueType = BindExpression(node.Value, env);
            Dictionary<string, bool> preState = env.SnapshotFlowState();
            var branchStates = new List<Dictionary<string, bool>>();
            bool sawDefault = false;

            foreach (SwitchArmSyntax arm in node.Arms)
            {
                if (arm is CaseArmSyntax caseArm)
                {
                    PevtType caseType = BindExpression(caseArm.Value, env);
                    if (valueType.IsOrdinaryType() && caseType.IsOrdinaryType() && caseType != valueType)
                        Report("PEVT5009", caseArm.Value.Span);
                }
                else
                {
                    sawDefault = true;
                }

                BindBranch(arm.Body, env, preState, branchStates);
            }

            if (branchStates.Count > 0)
                env.Restore(BoundEnvironment.Merge(branchStates, sawDefault, preState));
        }

        private void BindBranch(IReadOnlyList<StatementSyntax> body, BoundEnvironment env, Dictionary<string, bool> preState, List<Dictionary<string, bool>> branchStates)
        {
            env.Restore(preState);
            BindStatements(body, env);
            branchStates.Add(env.SnapshotFlowState());
        }

        private void RequireBool(ExpressionSyntax condition, PevtType type)
        {
            if (type != PevtType.Error && type != PevtType.Bool)
                Report("PEVT5008", condition.Span);
        }

        /// <summary>
        /// 14.1 节：块体是一个完全独立的环境，形参进入时已经定义且已经初始化（9.4 节）。块名称
        /// 登记进外层环境时绕开 PEVT6007 那条重复检查（用它自己更具体的 PEVT7103），好让"把块名当
        /// 变量赋值"落到 PEVT6002 而不是被误报成 6001。签名只在整段定义（含 <c>endblock</c>）绑定
        /// 完毕后才写入 <see cref="_readyBlocks"/>——这就是"定义先于调用"检查的全部机制。
        /// </summary>
        private void BindBlockDefinition(BlockDefinitionStatementSyntax block, BoundEnvironment enclosingEnv)
        {
            bool duplicateName = !block.Name.IsMissing && _readyBlocks.ContainsKey(block.Name.Text);
            if (duplicateName)
                Report("PEVT7103", block.Name.Span);

            if (!block.Name.IsMissing)
                Declare(enclosingEnv, new BlockSymbol(block.Name.Text), initialized: true);

            var parameterTypes = new List<PevtType>();
            var blockEnv = new BoundEnvironment();
            foreach (ParameterSyntax parameter in block.Parameters.Parameters)
            {
                PevtType parameterType = PevtTypeFacts.FromTypeKeyword(parameter.Type.Kind);
                parameterTypes.Add(parameterType);
                if (!parameter.Name.IsMissing)
                    Declare(blockEnv, new ParameterSymbol(parameter.Name.Text, parameterType), initialized: true);
            }

            PevtType? returnType = block.ReturnType == null ? (PevtType?)null : PevtTypeFacts.FromTypeKeyword(block.ReturnType.Kind);
            _blockReturnTypeStack.Push(returnType);
            BindStatements(block.Body, blockEnv);
            _blockReturnTypeStack.Pop();

            if (!block.Name.IsMissing && !duplicateName)
                _readyBlocks[block.Name.Text] = new BlockSignature(block.Name.Text, block.AsyncKeyword != null, parameterTypes, returnType);
        }

        /// <summary>15.2 节：初始化器本身仍然是一次普通调用绑定（校验签名/参数），只是结果类型被丢弃——
        /// 句柄的返回值只在 <c>await</c> 完成后才可用，不是声明本身的静态类型（15.1 节）。</summary>
        private void BindHandlerDeclaration(HandlerDeclarationStatementSyntax node, BoundEnvironment env)
        {
            // 15.1 节：无返回值的异步调用完全可以作为 handler 初始化器（句柄只记录运行状态）；
            // 这不是"值被丢弃的语句"，但同样不要求调用必须有普通返回值，因此按 isStatementContext
            // 的语义传 true，避免误报 PEVT7008/7114。
            BindExpression(node.Initializer, env, isStatementContext: true);
            PevtType? asyncReturnType = CheckSynchronousInitializer(node.Initializer);

            if (node.Name.IsMissing)
                return;

            if (env.IsDeclaredEver(node.Name.Text))
            {
                Report("PEVT7207", node.Name.Span);
                return;
            }

            Declare(env, new HandlerSymbol(node.Name.Text, asyncReturnType), initialized: true);
        }

        /// <summary>
        /// 15.2 节："handler 声明的初始化器是静态已知为同步的 @ 或 _ 调用"是 PEVT7204；
        /// <c>callevt</c> 的目标是否异步只能在运行时解析（10.3 节），因此不在这里核对。
        /// 顺带返回初始化器对应异步定义的普通返回值类型，供调用方construct <see cref="HandlerSymbol"/>；
        /// 找不到唯一匹配签名时返回 null（对应调用点已经因为其它原因报过错，不再重复猜测）。
        /// </summary>
        private PevtType? CheckSynchronousInitializer(ExpressionSyntax initializer)
        {
            switch (initializer)
            {
                case BuiltinCallExpressionSyntax builtinCall:
                {
                    List<BuiltinSignature> sameArity = _builtinApi.Find(builtinCall.Name.Text)
                        .Where(s => s.Parameters.Count == builtinCall.Arguments.Arguments.Count).ToList();
                    if (sameArity.Count != 1)
                        return null;
                    if (!sameArity[0].IsAsync)
                        Report("PEVT7204", builtinCall.Span);
                    return sameArity[0].ReturnType;
                }

                case CustomBlockCallExpressionSyntax blockCall:
                    if (!_readyBlocks.TryGetValue(blockCall.Name.Text, out BlockSignature signature))
                        return null;
                    if (!signature.IsAsync)
                        Report("PEVT7204", blockCall.Span);
                    return signature.ReturnType;

                default: // callevt，或初始化器本身已经因为别的原因报过错。
                    return null;
            }
        }

        /// <summary><c>kill</c>/<c>status</c> 共用的句柄解析：不存在于任何环境是 PEVT7210；存在但
        /// 不是句柄种类复用调用方指定的、位置专属的"不是句柄"编号（PEVT7213/7214，与阶段 5/7 语法层
        /// "根本不是标识符"的场景共用同一个号）。<c>await</c> 还需要句柄的异步返回值类型，走
        /// <see cref="BindAwait"/> 单独处理。</summary>
        private void BindHandleOperand(SyntaxToken handleToken, BoundEnvironment env, string wrongKindDiagnosticId)
        {
            if (handleToken.IsMissing)
                return;

            if (!env.TryGetSymbol(handleToken.Text, out Symbol symbol))
            {
                Report(_everDeclaredAnywhere.Contains(handleToken.Text) ? "PEVT6012" : "PEVT7210", handleToken.Span);
                return;
            }

            if (!(symbol is HandlerSymbol))
                Report(wrongKindDiagnosticId, handleToken.Span);
        }

        /// <summary>
        /// 15.3 节：<c>await</c> 的表达式类型取决于句柄对应异步定义是否声明了普通返回值——有，
        /// 结果就是那个类型（进而让 <c>var x : T = await a</c> 也能享受 PEVT6008 的类型核对）；
        /// 没有，只能作为独立事件语句使用，此时仍被当表达式用是 PEVT7211。
        /// </summary>
        private PevtType BindAwait(AwaitExpressionSyntax node, BoundEnvironment env, bool isStatementContext)
        {
            if (node.Handle.IsMissing)
                return PevtType.Error;

            if (!env.TryGetSymbol(node.Handle.Text, out Symbol symbol))
            {
                Report(_everDeclaredAnywhere.Contains(node.Handle.Text) ? "PEVT6012" : "PEVT7210", node.Handle.Span);
                return PevtType.Error;
            }

            if (!(symbol is HandlerSymbol handler))
            {
                Report("PEVT7212", node.Handle.Span);
                return PevtType.Error;
            }

            if (!handler.AsyncReturnType.HasValue)
            {
                if (!isStatementContext)
                    Report("PEVT7211", node.Span);
                return PevtType.Error;
            }

            return handler.AsyncReturnType.Value;
        }

        // ---- expressions ----

        /// <summary><paramref name="isStatementContext"/>：这个表达式的值是否会被整条语句丢弃——
        /// 只有这种情况下，无返回值的 <c>@</c>/自定义事件块调用才是合法的（否则 PEVT7008/7114）。
        /// 括号原样传递这个标记（括号不改变"是不是整条语句"），链式运算和调用参数则总是 false
        /// （它们的值总是被外层运算符或调用消费，不会被丢弃）。</summary>
        public PevtType BindExpression(ExpressionSyntax expression, BoundEnvironment env, bool isStatementContext = false)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal: return BindLiteral(literal);
                case NameExpressionSyntax name: return BindOrdinaryNameRead(name, env);
                case UnaryExpressionSyntax unary: return BindUnary(unary, env);
                case ChainedBinaryExpressionSyntax chain: return BindChain(chain, env);
                case ParenthesizedExpressionSyntax paren: return BindExpression(paren.Inner, env, isStatementContext);
                case ConversionExpressionSyntax conversion: return BindConversion(conversion, env);
                case BuiltinCallExpressionSyntax builtinCall: return BindBuiltinCall(builtinCall, env, isStatementContext);
                case CustomBlockCallExpressionSyntax blockCall: return BindCustomBlockCall(blockCall, env, isStatementContext);
                case ExecCallExpressionSyntax execCall: BindArguments(execCall.Arguments, env); return PevtType.Error;
                case RawCsExpressionSyntax rawCs: BindRawCs(rawCs, env); return PevtType.Error;
                case AwaitExpressionSyntax awaitExpr: return BindAwait(awaitExpr, env, isStatementContext);
                case StatusExpressionSyntax statusExpr: BindHandleOperand(statusExpr.Handle, env, "PEVT7214"); return PevtType.Int;

                default: return PevtType.Error; // MissingExpressionSyntax、callevt、await all/any：见类顶部范围说明。
            }
        }

        private List<PevtType> BindArguments(ArgumentListSyntax arguments, BoundEnvironment env)
        {
            var types = new List<PevtType>(arguments.Arguments.Count);
            foreach (ExpressionSyntax argument in arguments.Arguments)
                types.Add(BindExpression(argument, env));
            return types;
        }

        private PevtType BindLiteral(LiteralExpressionSyntax literal) => literal.Token.Kind switch
        {
            SyntaxKind.IntegerLiteralToken => PevtType.Int,
            SyntaxKind.FloatLiteralToken => PevtType.Float,
            SyntaxKind.CharLiteralToken => PevtType.Char,
            SyntaxKind.StringLiteralToken => PevtType.String,
            SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword => PevtType.Bool,
            _ => PevtType.Error,
        };

        /// <summary>PEVT6001（哪个环境都没声明过）、PEVT6012（声明过，但在另一个环境）与 PEVT6003
        /// （本环境声明过，但当前路径上还没有完成初始化）的共用入口——普通变量读取、转换操作数和
        /// <c>return</c> 目标都经过这里。</summary>
        private PevtType BindNameRead(SyntaxToken nameToken, BoundEnvironment env)
        {
            if (nameToken.IsMissing)
                return PevtType.Error;

            if (!env.TryGetSymbol(nameToken.Text, out Symbol symbol))
            {
                ReportUndefinedOrOutsideEnvironment(nameToken);
                return PevtType.Error;
            }

            if (symbol is VariableSymbol)
            {
                env.GetFlowState(nameToken.Text, out bool declaredHere, out bool initializedHere);
                if (!declaredHere)
                {
                    // TryGetSymbol 已经确认这个名称就属于当前环境的永久符号表，只是当前路径没有
                    // 走到那条声明语句——这纯粹是同一环境内的可达性问题，永远是 6001，不是 6012。
                    Report("PEVT6001", nameToken.Span);
                    return symbol.Type;
                }

                if (!initializedHere)
                    Report("PEVT6003", nameToken.Span);
            }

            return symbol.Type;
        }

        /// <summary>9.4 节的环境隔离：一个名称完全没在文件里任何地方声明过是 PEVT6001；
        /// 声明过，但不在当前可见的环境/路径里，是 PEVT6012（引用了别的块或外层环境的名称）。</summary>
        private void ReportUndefinedOrOutsideEnvironment(SyntaxToken nameToken) =>
            Report(_everDeclaredAnywhere.Contains(nameToken.Text) ? "PEVT6012" : "PEVT6001", nameToken.Span);

        /// <summary>15.2 节 PEVT7209："句柄被用于 await、kill、status 以外的表达式、运算、转换或
        /// 调用参数"。这三个合法位置各自直接持有裸标识符 token，从不经过普通表达式解析
        /// （见 <see cref="BindAwait"/>/<see cref="BindHandleOperand"/>），因此任何从这里（普通
        /// <c>NameExpressionSyntax</c> 读取）解析出句柄类型的名称，必然是用在了不允许的位置。</summary>
        private PevtType BindOrdinaryNameRead(NameExpressionSyntax name, BoundEnvironment env)
        {
            PevtType type = BindNameRead(name.Identifier, env);
            if (type == PevtType.Handler)
                Report("PEVT7209", name.Span);
            return type;
        }

        private PevtType BindUnary(UnaryExpressionSyntax node, BoundEnvironment env)
        {
            PevtType operandType = BindExpression(node.Operand, env);
            if (operandType == PevtType.Error)
                return PevtType.Error;

            if (node.OperatorToken.Kind == SyntaxKind.MinusToken)
            {
                if (!operandType.IsNumeric())
                {
                    Report("PEVT5024", node.Span);
                    return PevtType.Error;
                }

                return operandType;
            }

            // ExclamationToken
            if (operandType != PevtType.Bool)
            {
                Report("PEVT5007", node.Span);
                return PevtType.Error;
            }

            return PevtType.Bool;
        }

        private PevtType BindChain(ChainedBinaryExpressionSyntax node, BoundEnvironment env)
        {
            PevtType left = BindExpression(node.First, env);
            foreach (BinaryChainSegment segment in node.Segments)
            {
                PevtType right = BindExpression(segment.Operand, env);
                left = BindBinaryOperator(segment.OperatorToken, left, right, segment.Operand);
            }

            return left;
        }

        private PevtType BindBinaryOperator(SyntaxToken operatorToken, PevtType left, PevtType right, ExpressionSyntax rightNode)
        {
            if (left == PevtType.Error || right == PevtType.Error)
                return PevtType.Error;

            switch (operatorToken.Kind)
            {
                case SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken:
                    if (!left.IsNumeric() || !right.IsNumeric())
                    {
                        Report("PEVT5005", rightNode.Span);
                        return PevtType.Error;
                    }
                    if (left != right)
                    {
                        Report("PEVT5004", rightNode.Span);
                        return PevtType.Error;
                    }
                    return left;

                case SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken:
                    if (left != right)
                    {
                        Report("PEVT5004", rightNode.Span);
                        return PevtType.Error;
                    }
                    return PevtType.Bool;

                case SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken or SyntaxKind.GreaterThanToken:
                    if (!left.IsNumeric() || !right.IsNumeric())
                    {
                        Report("PEVT5011", rightNode.Span);
                        return PevtType.Error;
                    }
                    if (left != right)
                    {
                        Report("PEVT5004", rightNode.Span);
                        return PevtType.Error;
                    }
                    return PevtType.Bool;

                default: // AmpersandToken/PipeToken/CaretToken
                    if (left != PevtType.Bool || right != PevtType.Bool)
                    {
                        Report("PEVT5007", rightNode.Span);
                        return PevtType.Error;
                    }
                    return PevtType.Bool;
            }
        }

        /// <summary>8.3 节：语法层面只有 <c>(float)x</c>/<c>(string)x</c> 两种形状会被解析成
        /// <see cref="ConversionExpressionSyntax"/>（见 Parser.ParseParenthesizedOrConversion）；
        /// 这里只需要按目标类型核对源变量的实际类型是否恰好是 <c>int</c>/<c>char</c>（PEVT5012）。</summary>
        private PevtType BindConversion(ConversionExpressionSyntax node, BoundEnvironment env)
        {
            PevtType targetType = PevtTypeFacts.FromTypeKeyword(node.TargetType.Kind);

            if (node.Variable.IsMissing)
                return targetType;

            if (!env.TryGetSymbol(node.Variable.Text, out Symbol symbol) || !(symbol is VariableSymbol or ConstantSymbol or ParameterSymbol))
            {
                Report("PEVT5013", node.Variable.Span);
                return targetType;
            }

            PevtType sourceType = BindNameRead(node.Variable, env);
            if (sourceType == PevtType.Error)
                return targetType;

            bool valid = targetType == PevtType.Float ? sourceType == PevtType.Int
                : targetType == PevtType.String ? sourceType == PevtType.Char
                : false;

            if (!valid)
                Report("PEVT5012", node.Span);

            return targetType;
        }

        // ---- 14.4: custom block calls ----

        /// <summary>
        /// 14.1/14.4 节。语法层面"标识符(...)"一律搭建成 <see cref="CustomBlockCallExpressionSyntax"/>
        /// （阶段 5 的既有设计），因此这里既要处理带 <c>_</c> 前缀的正常调用，也要识别"漏写前缀"
        /// （PEVT7111）——如果去掉/补上前缀能在已知块名集合里找到匹配，说明用户大概率是想调用那个块。
        /// </summary>
        private PevtType BindCustomBlockCall(CustomBlockCallExpressionSyntax call, BoundEnvironment env, bool isStatementContext)
        {
            List<PevtType> argumentTypes = BindArguments(call.Arguments, env);
            string calledName = call.Name.Text;

            if (_readyBlocks.TryGetValue(calledName, out BlockSignature signature))
            {
                CheckBlockCallArguments(call, argumentTypes, signature);
                if (signature.ReturnType == null && !isStatementContext)
                    Report("PEVT7114", call.Span);
                return signature.ReturnType ?? PevtType.Error;
            }

            string prefixed = calledName.StartsWith("_") ? null : "_" + calledName;
            if (prefixed != null && (_readyBlocks.ContainsKey(prefixed) || _allBlockNamesInFile.Contains(prefixed)))
            {
                Report("PEVT7111", call.Name.Span);
                return PevtType.Error;
            }

            Report(_allBlockNamesInFile.Contains(calledName) ? "PEVT7115" : "PEVT7110", call.Name.Span);
            return PevtType.Error;
        }

        private void CheckBlockCallArguments(CustomBlockCallExpressionSyntax call, IReadOnlyList<PevtType> argumentTypes, BlockSignature signature)
        {
            if (argumentTypes.Count != signature.ParameterTypes.Count)
            {
                Report("PEVT7112", call.Arguments.Span);
                return;
            }

            for (int i = 0; i < argumentTypes.Count; i++)
            {
                if (argumentTypes[i] == PevtType.Error)
                    continue;
                if (argumentTypes[i] != signature.ParameterTypes[i])
                    Report("PEVT7113", call.Arguments.Arguments[i].Span);
            }
        }

        // ---- 11: builtin (@) calls ----

        /// <summary>
        /// 11.2/11.3 节的签名重载匹配：先按参数数量筛出候选，数量都不对就是 PEVT7005；数量对了但
        /// 没有任何候选的参数类型逐一精确匹配，单一候选时是 PEVT7006（类型不对这一件事很明确），
        /// 多个候选时降级为更笼统的 PEVT7007（找不到完全匹配的签名——具体是哪个候选、哪个参数不对
        /// 已经不唯一了）。真实 API 表本身要等阶段 13 才会被登记；本阶段调用方通过构造函数传入
        /// <see cref="BuiltinApiTable"/>，未传入时默认为空表，任何 <c>@</c> 名称都会被判定为未登记。
        /// </summary>
        private PevtType BindBuiltinCall(BuiltinCallExpressionSyntax call, BoundEnvironment env, bool isStatementContext)
        {
            List<PevtType> argumentTypes = BindArguments(call.Arguments, env);
            if (call.Name.IsMissing)
                return PevtType.Error; // PEVT7001 已经在语法阶段报过。

            IReadOnlyList<BuiltinSignature> candidates = _builtinApi.Find(call.Name.Text);
            if (candidates.Count == 0)
            {
                Report("PEVT7002", call.Name.Span);
                return PevtType.Error;
            }

            List<BuiltinSignature> sameArity = candidates.Where(c => c.Parameters.Count == argumentTypes.Count).ToList();
            if (sameArity.Count == 0)
            {
                Report("PEVT7005", call.Arguments.Span);
                return PevtType.Error;
            }

            BuiltinSignature match = sameArity.FirstOrDefault(c => MatchesArgumentTypes(c, argumentTypes));
            if (match == null)
            {
                Report(sameArity.Count == 1 ? "PEVT7006" : "PEVT7007", call.Arguments.Span);
                return PevtType.Error;
            }

            if (!match.HasValidSignatureShape())
            {
                Report("PEVT7010", call.Name.Span);
                return PevtType.Error;
            }

            if (match.ReturnType == null && !isStatementContext)
                Report("PEVT7008", call.Span);

            return match.ReturnType ?? PevtType.Error;
        }

        private static bool MatchesArgumentTypes(BuiltinSignature signature, IReadOnlyList<PevtType> argumentTypes)
        {
            for (int i = 0; i < argumentTypes.Count; i++)
            {
                if (argumentTypes[i] == PevtType.Error)
                    continue; // 已经报过错的实参不参与签名匹配的连锁判断。
                if (argumentTypes[i] != signature.Parameters[i].Type)
                    return false;
            }

            return true;
        }

        // ---- 12.2: $raw cs argument copies ----

        /// <summary>12.2 节："$raw cs 的变量传入列表只能包含已定义且已初始化的 PEVT 变量"，且不能
        /// 重复；文件是否声明了 <c>enable cs</c>（2.1 节）也在这里统一核对（PEVT8015）。</summary>
        private void BindRawCs(RawCsExpressionSyntax node, BoundEnvironment env)
        {
            if (!_hasCsCapability)
                Report("PEVT8015", node.Span);

            if (node.Arguments == null)
                return;

            var seen = new HashSet<string>();
            foreach (SyntaxToken identifier in node.Arguments.Identifiers)
            {
                if (identifier.IsMissing)
                    continue;

                if (!seen.Add(identifier.Text))
                {
                    Report("PEVT8013", identifier.Span);
                    continue;
                }

                if (!env.TryGetSymbol(identifier.Text, out Symbol symbol) || !(symbol is VariableSymbol or ConstantSymbol or ParameterSymbol))
                    Report("PEVT8014", identifier.Span);
                else
                    BindNameRead(identifier, env); // 仍然按普通读取核对是否已经初始化（PEVT6003）。
            }
        }
    }
}
