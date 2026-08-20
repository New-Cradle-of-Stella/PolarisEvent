using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 名称、调用与能力绑定：跨环境的定义先于使用、自定义事件块签名、<c>@</c> 重载匹配、<c>$raw cs</c> 参数副本，
    /// 以及 <c>handler</c> 的声明、赋值与 await/kill/status 规则。callevt 在静态阶段刻意只认语法、不查真实 ID；
    /// PEVT7117、7203 与集合等待的列表校验留给后续阶段。
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

        /// <summary>
        /// 当前处于"直接位置"的那个表达式节点：整条语句的表达式，或一个声明/句柄声明的初始化器。
        /// </summary>
        private ExpressionSyntax _directExpressionRoot;

        /// <summary>
        /// <c>$raw cs</c> 的 C# 分析器（PEVT8007–8010 与代码块返回类型）。
        /// </summary>
        private readonly Runtime.Raw.IPevtRawCsAnalyzer _rawCsAnalyzer;

        public Binder(
            DiagnosticBag diagnostics,
            SourceText source,
            BuiltinApiTable builtinApi = null,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            _diagnostics = diagnostics;
            _source = source;
            _builtinApi = builtinApi ?? BuiltinApiTable.Empty;
            _rawCsAnalyzer = rawCsAnalyzer;
        }

        private void Report(string diagnosticId, TextSpan span) =>
            _diagnostics.AddFromCatalog(diagnosticId, _source.GetLocation(span));

        /// <summary>外层事件与每个自定义事件块定义各自使用一个全新、互不关联的环境（9.4 节）。
        /// 绑定前先做一次全文件块名预扫描，好让"定义先于调用"（7115）能和"哪里都没定义"（7110）区分开。</summary>
        /// <param name="seedSymbols">
        /// 预先存在于外层环境里的符号，只有 <c>exec</c> 片段会用到——片段允许读写授权的外层变量，
        /// 静态校验必须先知道这些名字，否则合法片段会被判成 PEVT6001。片段里新声明的名字仍然只进片段自己的环境。
        /// </param>
        public void BindDocument(DocumentSyntax document, IEnumerable<Symbol> seedSymbols = null)
        {
            _hasCsCapability = document.EnableDeclarations.Any(e => e.Capability.Kind == SyntaxKind.CsKeyword);
            CollectBlockNames(document.Statements);

            var env = new BoundEnvironment();
            if (seedSymbols != null)
            {
                foreach (Symbol symbol in seedSymbols)
                    Declare(env, symbol, initialized: true);
            }

            // PEVT-E07：事件头参数和自定义事件块形参一样，进入正文时已经定义且已经初始化（9.4 节）——
            // 调用方（callevt 的晚绑定阶段）提供的实参快照，静态侧只管声明和普通读取规则。
            if (document.IdDeclaration?.Parameters != null)
            {
                foreach (ParameterSyntax parameter in document.IdDeclaration.Parameters.Parameters)
                {
                    if (!parameter.Name.IsMissing)
                        Declare(env, new ParameterSymbol(parameter.Name.Text, PevtTypeFacts.FromTypeKeyword(parameter.Type.Kind)), initialized: true);
                }
            }

            BindStatements(document.Statements, env);
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
                case ExpressionStatementSyntax expressionStatement:
                    _directExpressionRoot = expressionStatement.Expression;
                    BindExpression(expressionStatement.Expression, env, isStatementContext: true);
                    break;
                case BlockDefinitionStatementSyntax block: BindBlockDefinition(block, env); break;
                case HandlerDeclarationStatementSyntax handler: BindHandlerDeclaration(handler, env); break;
                case KillStatementSyntax kill: BindHandleOperand(kill.Handle, env, "PEVT7213"); break;
                case ScheduleStatementSyntax schedule: BindScheduleDeclaration(schedule, env); break;
                default: break; // label/goto/end/unknown/$raw cmd/flush schedules/clear schedules 语句：无名称/类型语义可绑定。
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
            _directExpressionRoot = node.Initializer;
            PevtType initializerType = hasInitializer ? BindExpression(node.Initializer, env) : PevtType.Error;

            if (!DeclareOrReportDuplicate(node.Name, new VariableSymbol(node.Name.Text, declaredType), hasInitializer, env))
                return;

            if (hasInitializer && declaredType.IsOrdinaryType() && initializerType.IsOrdinaryType() && initializerType != declaredType)
                ReportInitializerMismatch(node.Initializer);
        }

        private void BindConstantDeclaration(ConstantDeclarationSyntax node, BoundEnvironment env)
        {
            PevtType declaredType = PevtTypeFacts.FromTypeKeyword(node.Type.Kind);
            _directExpressionRoot = node.Initializer;
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

        /// <summary>
        /// 5 节：循环体可能一次也不执行，因此循环之后的状态就是循环之前的状态。
        /// 循环体仍然用一份克隆单独走一遍绑定，只为在体内报告读取与声明相关的诊断，不做不动点分析。
        /// </summary>
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
                    ReportSideEffectingCaseExpression(caseArm.Value);

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

        /// <summary>
        /// PEVT2415：<c>case</c> 表达式不允许包含 <c>@</c>、<c>_</c>、<c>$raw cs</c>、<c>await</c>、
        /// <c>status</c>、<c>callevt</c> 或 <c>exec</c>。
        /// </summary>
        private void ReportSideEffectingCaseExpression(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case BuiltinCallExpressionSyntax _:
                case CustomBlockCallExpressionSyntax _:
                case RawCsExpressionSyntax _:
                case AggregateAwaitExpressionSyntax _:
                case StatusExpressionSyntax _:
                case EventCallExpressionSyntax _:
                case ExecCallExpressionSyntax _:
                    Report("PEVT2415", expression.Span);
                    return;

                case ParenthesizedExpressionSyntax parenthesized:
                    ReportSideEffectingCaseExpression(parenthesized.Inner);
                    return;

                case UnaryExpressionSyntax unary:
                    ReportSideEffectingCaseExpression(unary.Operand);
                    return;

                case ChainedBinaryExpressionSyntax chain:
                    ReportSideEffectingCaseExpression(chain.First);
                    foreach (BinaryChainSegment segment in chain.Segments)
                        ReportSideEffectingCaseExpression(segment.Operand);
                    return;
            }
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
        /// 14.1 节：块体是完全独立的环境，形参进入时已经定义且已经初始化。
        /// 签名只在整段定义（含 <c>endblock</c>）绑定完毕后才写入 <see cref="_readyBlocks"/>，这就是"定义先于调用"检查的全部机制。
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
            // 15.1 节：无返回值的异步调用完全可以作为 handler 初始化器，句柄只记录运行状态。
            // 因此按 isStatementContext 的语义传 true，避免误报 PEVT7008/7114。
            _directExpressionRoot = node.Initializer;
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
        /// 15.2 节：handler 初始化器是静态已知为同步的 <c>@</c>/<c>_</c> 调用时报 PEVT7204；<c>callevt</c> 的异步性只能在运行时解析，不在这里核对。
        /// 顺带返回异步定义的普通返回值类型供构造 <see cref="HandlerSymbol"/>，找不到唯一匹配签名时返回 null。
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

        /// <summary>
        /// PEVT-E05：<c>schedule timelineId after frames call _名称()</c>。
        /// 目标块的存在性、实参数量/类型由 <see cref="BindExpression"/> 走的通用自定义块调用绑定统一核对
        /// （PEVT7110/7112/7113 等），这里只加两条 <c>schedule</c> 专属的额外要求：目标必须是 <c>async</c>
        /// 且必须无参数——两者都不是"这个调用合不合法"，而是"这个块能不能被安全延迟启动"。
        /// </summary>
        private void BindScheduleDeclaration(ScheduleStatementSyntax node, BoundEnvironment env)
        {
            _directExpressionRoot = node.Frames;
            PevtType framesType = BindExpression(node.Frames, env);
            if (framesType != PevtType.Error && framesType != PevtType.Int)
                Report("PEVT7511", node.Frames.Span);

            if (node.Target != null)
            {
                _directExpressionRoot = node.Target;
                BindExpression(node.Target, env, isStatementContext: true);
                CheckScheduleTarget(node.Target);
            }

            if (node.TimelineId.IsMissing)
                return;

            if (env.IsDeclaredEver(node.TimelineId.Text))
            {
                Report(ScheduleStatementSyntax.DuplicateDiagnosticId, node.TimelineId.Span);
                return;
            }

            Declare(env, new ScheduleSymbol(node.TimelineId.Text), initialized: true);
        }

        /// <summary>
        /// <c>schedule</c> 目标必须是已经定义、声明为 <c>async</c> 且无参数的事件块。
        /// 块本身不存在或调用形状不对时 <see cref="BindExpression"/> 已经报过 PEVT7110/7112/7113，
        /// 这里查不到签名就直接放弃，不重复报告。
        /// </summary>
        private void CheckScheduleTarget(CustomBlockCallExpressionSyntax target)
        {
            if (!_readyBlocks.TryGetValue(target.Name.Text, out BlockSignature signature))
                return;

            if (!signature.IsAsync)
                Report("PEVT7507", target.Span);
            if (signature.ParameterTypes.Count != 0)
                Report("PEVT7508", target.Span);
        }

        /// <summary>
        /// <c>kill</c>/<c>status</c> 共用的句柄解析：不存在于任何环境是 PEVT7210，存在但不是句柄种类则用调用方指定的位置专属编号（PEVT7213/7214）。
        /// <c>await</c> 还需要句柄的异步返回值类型，走 <see cref="BindAwait"/> 单独处理。
        /// </summary>
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
        /// 15.3 节：<c>await</c> 的表达式类型取决于句柄对应异步定义是否声明了普通返回值。
        /// 有就是那个类型（进而享受 PEVT6008 的类型核对）；没有则只能作为独立事件语句，仍被当表达式用是 PEVT7211。
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

        /// <summary>
        /// 15.6 节的集合等待：<c>await all/any</c> 的表达式类型固定是 <c>int</c>，绑定器真正要做的是把结果绑定列表里的名字
        /// 当成新声明的普通变量登记，否则后面用到它们会被误报成 PEVT6001。登记成"已初始化"是刻意的——
        /// 哪个句柄会失败只有运行期知道，由 PEVTR3002 在真的读到未初始化槽时报出。
        /// </summary>
        private PevtType BindAggregateAwait(AggregateAwaitExpressionSyntax node, BoundEnvironment env)
        {
            IReadOnlyList<SyntaxToken> handles = node.Handles.Identifiers;
            IReadOnlyList<SyntaxToken> bindings = node.Bindings?.Identifiers ?? new SyntaxToken[0];

            // 逐个解析句柄，并记住它们各自的异步返回类型——结果绑定的类型来自这里。
            var handlerTypes = new List<PevtType?>(handles.Count);
            var seenHandles = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (SyntaxToken handle in handles)
            {
                if (handle.IsMissing)
                {
                    handlerTypes.Add(null);
                    continue;
                }

                if (!env.TryGetSymbol(handle.Text, out Symbol symbol) || !(symbol is HandlerSymbol handler))
                {
                    Report("PEVT7218", handle.Span);
                    handlerTypes.Add(null);
                    continue;
                }

                if (!seenHandles.Add(handle.Text))
                    Report("PEVT7219", handle.Span);

                handlerTypes.Add(handler.AsyncReturnType);
            }

            if (bindings.Count == 0)
                return PevtType.Int;

            // 非空绑定列表会在当前环境引入新变量，因此整个集合等待必须是语句或初始化器的顶层。
            if (!ReferenceEquals(node, _directExpressionRoot))
                Report("PEVT7225", node.Span);

            if (bindings.Count != handles.Count)
                Report("PEVT7221", node.Bindings.Span);

            var seenBindings = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                SyntaxToken binding = bindings[i];
                if (binding.IsMissing)
                    continue;

                // 同一列表内重复，或与当前环境里已有的名字撞车，都是 PEVT7223。
                if (!seenBindings.Add(binding.Text) || env.IsDeclaredEver(binding.Text))
                {
                    Report("PEVT7223", binding.Span);
                    continue;
                }

                PevtType? handlerType = i < handlerTypes.Count ? handlerTypes[i] : null;
                if (!handlerType.HasValue)
                {
                    // 对应句柄没有普通返回值（或根本没解析出来）时不能绑定结果。
                    Report("PEVT7224", binding.Span);
                    continue;
                }

                Declare(env, new VariableSymbol(binding.Text, handlerType.Value), initialized: true);
            }

            return PevtType.Int;
        }

        // ---- expressions ----

        /// <summary>
        /// <paramref name="isStatementContext"/>：这个表达式的值是否会被整条语句丢弃，只有这种情况下无返回值的调用才合法（否则 PEVT7008/7114）。
        /// 括号原样传递这个标记，链式运算和调用参数则总是 false。
        /// </summary>
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
                case EventCallExpressionSyntax eventCall: return BindEventCall(eventCall, env);
                case ExecCallExpressionSyntax execCall: BindArguments(execCall.Arguments, env); return PevtType.Error;
                case RawCsExpressionSyntax rawCs: return BindRawCs(rawCs, env, isStatementContext);
                case AwaitExpressionSyntax awaitExpr: return BindAwait(awaitExpr, env, isStatementContext);
                case StatusExpressionSyntax statusExpr: BindHandleOperand(statusExpr.Handle, env, "PEVT7214"); return PevtType.Int;
                case AggregateAwaitExpressionSyntax aggregate: return BindAggregateAwait(aggregate, env);

                default: return PevtType.Error; // MissingExpressionSyntax：见类顶部范围说明。
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

        /// <summary>
        /// PEVT6001（哪个环境都没声明过）、PEVT6012（声明过但在另一个环境）与 PEVT6003（本环境声明过但当前路径还没初始化）
        /// 三者的共用入口，普通变量读取、转换操作数和 <c>return</c> 目标都经过这里。
        /// </summary>
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

        /// <summary>
        /// 15.2 节 PEVT7209：句柄被用于 await、kill、status 以外的表达式、运算、转换或调用参数。
        /// 那三个合法位置各自直接持有裸标识符 token，从不经过普通表达式解析，所以从这里解析出句柄类型的名称必然是误用。
        /// </summary>
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

        /// <summary>
        /// 8.3 节：语法层面只有 <c>(float)x</c>/<c>(string)x</c> 会被解析成 <see cref="ConversionExpressionSyntax"/>，
        /// 这里只需按目标类型核对源变量的实际类型是否恰好是 <c>int</c>/<c>char</c>（PEVT5012）。
        /// </summary>
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

        /// <summary>
        /// PEVT-E07：<c>callevt "id"(实参...)</c>。目标事件通常在另一个文件甚至另一个模组程序集里，
        /// 静态阶段查不到它的参数签名（这正是"callevt 只认语法、不查真实 ID"的既有原则），
        /// 所以这里只能绑定实参表达式本身（各自的类型、名称可见性等普通规则照常适用）——
        /// 数量、顺序与类型是否匹配目标事件的形参，只能留给运行时的晚绑定阶段核对（PEVTR4305）。
        /// </summary>
        private PevtType BindEventCall(EventCallExpressionSyntax call, BoundEnvironment env)
        {
            if (call.Arguments != null)
                BindArguments(call.Arguments, env);

            return PevtType.Error;
        }

        // ---- 14.4: custom block calls ----

        /// <summary>
        /// 14.1/14.4 节。语法层面"标识符(...)"一律搭建成 <see cref="CustomBlockCallExpressionSyntax"/>，
        /// 所以这里既处理带 <c>_</c> 前缀的正常调用，也识别去掉或补上前缀就能匹配已知块名的"漏写前缀"（PEVT7111）。
        /// </summary>
        private PevtType BindCustomBlockCall(CustomBlockCallExpressionSyntax call, BoundEnvironment env, bool isStatementContext)
        {
            List<PevtType> argumentTypes = BindArguments(call.Arguments, env);
            string calledName = call.Name.Text;

            if (_readyBlocks.TryGetValue(calledName, out BlockSignature signature))
            {
                CheckBlockCallArguments(call, argumentTypes, signature);

                if (signature.IsAsync && !isStatementContext)
                {
                    Report("PEVT7203", call.Span);
                    return PevtType.Error;
                }

                if (signature.ReturnType == null && !isStatementContext)
                    Report("PEVT7114", call.Span);

                return signature.IsAsync ? PevtType.Error : signature.ReturnType ?? PevtType.Error;
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
        /// 11.2/11.3 节的签名重载匹配：参数数量都不对是 PEVT7005，数量对而类型不匹配时单一候选报 PEVT7006、
        /// 多候选降级为更笼统的 PEVT7007。真实 API 表由调用方通过构造函数传入 <see cref="BuiltinApiTable"/>，
        /// 未传入时为空表，任何 <c>@</c> 名称都会被判定为未登记。
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

            // 15.2 节 PEVT7203：异步调用产出的是句柄，不是普通值。合法位置只有 handler 初始化器
            // 和被丢弃的独立语句，两者的 isStatementContext 都是 true。
            if (match.IsAsync && !isStatementContext)
            {
                Report("PEVT7203", call.Span);
                return PevtType.Error;
            }

            if (match.ReturnType == null && !isStatementContext)
                Report("PEVT7008", call.Span);

            // 异步调用的静态类型不是它的普通返回值——那个值只有 await 之后才可用。
            return match.IsAsync ? PevtType.Error : match.ReturnType ?? PevtType.Error;
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

        /// <summary>
        /// 12.2 节：<c>$raw cs</c> 的变量传入列表只能包含已定义且已初始化的 PEVT 变量且不能重复，
        /// 文件是否声明 <c>enable cs</c> 也在这里统一核对（PEVT8015）。传入列表绑定完成后再把代码块交给宿主的
        /// C# 分析器决定 PEVT8007–8010 与表达式类型；没有分析器时返回 <see cref="PevtType.Error"/>，不假装知道类型。
        /// </summary>
        private PevtType BindRawCs(RawCsExpressionSyntax node, BoundEnvironment env, bool isStatementContext)
        {
            if (!_hasCsCapability)
                Report("PEVT8015", node.Span);

            var parameters = new List<Runtime.Raw.PevtRawCsParameter>();
            if (node.Arguments != null)
            {
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
                    {
                        Report("PEVT8014", identifier.Span);
                        continue;
                    }

                    BindNameRead(identifier, env); // 仍然按普通读取核对是否已经初始化（PEVT6003）。

                    if (symbol.Type.IsOrdinaryType())
                        parameters.Add(new Runtime.Raw.PevtRawCsParameter(identifier.Text, symbol.Type));
                }
            }

            if (_rawCsAnalyzer == null || node.Content.IsMissing)
                return PevtType.Error;

            var request = new Runtime.Raw.PevtRawCsRequest(
                node.Content.Value.Kind == TokenValueKind.String ? node.Content.Value.AsString : string.Empty,
                parameters,
                isStatementContext ? Runtime.Raw.PevtRawCsUsage.Statement : Runtime.Raw.PevtRawCsUsage.Expression,
                Runtime.Raw.PevtRawCsSourceMap.Create(_source, node.Content.Text, node.Content.Span.Start));

            PevtType? returnType = _rawCsAnalyzer.Analyze(request, _diagnostics);
            return returnType ?? PevtType.Error;
        }
    }
}
