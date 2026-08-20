using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>解释器指令码。</summary>
    public enum PevtOpCode
    {
        /// <summary>把一个字面量压入求值栈。</summary>
        PushLiteral,

        /// <summary>读取变量/常量并压栈；未初始化时报 PEVTR3002。</summary>
        PushName,

        /// <summary>显式类型转换（只有 int→float 与 char→string）。</summary>
        Convert,

        /// <summary>一元取负。</summary>
        Negate,

        /// <summary>逻辑非。</summary>
        Not,

        /// <summary>二元运算，弹两个压一个。</summary>
        Binary,

        /// <summary>丢弃栈顶（语句位置的有值调用）。</summary>
        Pop,

        /// <summary>执行一次变量/常量声明。重复执行报 PEVTR3001。</summary>
        Declare,

        /// <summary>把栈顶写入一个已声明的槽位。</summary>
        Store,

        /// <summary>无条件跳转。</summary>
        Jump,

        /// <summary>栈顶为 false 时跳转。</summary>
        JumpIfFalse,

        /// <summary>栈顶为 true 时跳转。</summary>
        JumpIfTrue,

        /// <summary>把栈顶存入本帧的 switch 值槽（switch 值只求值一次）。</summary>
        StoreSwitch,

        /// <summary>把本帧的 switch 值槽压栈，用于与各 case 表达式比较。</summary>
        LoadSwitch,

        /// <summary>检查一个原版全局旗标键是否已登记，并把结果压栈；不读取旗标值。</summary>
        PushGlobalFlagDefined,

        /// <summary>终止整个事件执行。</summary>
        End,

        /// <summary>从自定义事件块返回；<c>Flag</c> 为 true 表示带返回值。</summary>
        Return,

        /// <summary>调用同步 <c>@</c>，弹出 <c>Index</c> 个实参。</summary>
        CallBuiltin,

        /// <summary>调用自定义事件块，弹出 <c>Index</c> 个实参并压入新帧。</summary>
        CallBlock,

        /// <summary>
        /// 启动一个异步调用（<c>@name_start</c> 或 <c>async block</c>），立即返回。
        /// <c>HandlerName</c> 非空时把句柄写进环境；为空表示句柄被丢弃，但协程仍受事件所有权管辖。
        /// </summary>
        CallAsync,

        /// <summary>
        /// <c>callevt</c>：运行时按事件 ID 查全局注册表并压入子事件。
        /// <c>HandlerName</c> 为空表示同步调用（调用方等它结束），非空表示要一个异步句柄。
        /// </summary>
        CallEvent,

        /// <summary>把 <c>Name</c> 句柄的 <c>status</c>（0/1/2）压栈。</summary>
        Status,

        /// <summary>单句柄 <c>await</c>。<c>Flag</c> 为 true 时把结果压栈。</summary>
        AwaitHandle,

        /// <summary><c>await all</c>：<c>Names</c> 是句柄列表，<c>Bindings</c> 是结果绑定名。</summary>
        AwaitAll,

        /// <summary><c>await any</c>：同上，结果是首个成功句柄的序号。</summary>
        AwaitAny,

        /// <summary><c>kill</c>：取消 <c>Name</c> 句柄并等它确认停下。</summary>
        Kill,

        /// <summary><c>exec</c>：弹出片段源码字符串，运行时解析、绑定并执行。</summary>
        Exec,

        /// <summary>
        /// <c>$raw cmd</c>：把 <c>Constant</c> 里的原版 DSL 文本提交给进程级原版会话通道。
        /// 永远不产生值。
        /// </summary>
        RawCmd,

        /// <summary>
        /// <c>$raw cs</c>：执行 <c>Constant</c> 里的 C# 代码块。<c>Names</c> 是按顺序传入的
        /// PEVT 变量名，<c>Flag</c> 为 true 表示用在表达式位置、结果要压栈。
        /// </summary>
        RawCs,

        /// <summary>
        /// PEVT-E05 <c>schedule timelineId after frames call _name()</c>：弹出栈顶的 frames，
        /// 排入一个延迟启动项。<c>Name</c> 是 timelineId（仅供 F8 展示），<c>HandlerName</c> 是目标块名。
        /// </summary>
        ScheduleAfter,

        /// <summary><c>flush schedules</c>：立即启动当前执行实例全部尚未触发的调度项。</summary>
        FlushSchedules,

        /// <summary><c>clear schedules</c>：丢弃当前执行实例全部尚未触发的调度项。</summary>
        ClearSchedules,
    }

    /// <summary>一条已绑定的不可变指令。跳转目标在编译期解析，运行期不再查找。</summary>
    public sealed class PevtInstruction
    {
        public PevtOpCode OpCode { get; }

        /// <summary>对应源码位置，用于诊断与调用栈。</summary>
        public TextSpan Span { get; }

        public PevtValue Constant { get; }

        public string Name { get; }

        public PevtType Type { get; }

        /// <summary>跳转目标指令下标；不适用时为 -1。</summary>
        public int Target { get; }

        /// <summary>switch 槽号 / 事件块下标 / 声明序号 / 实参数量。</summary>
        public int Index { get; }

        public CommandDescriptor Descriptor { get; }

        public SyntaxKind Operator { get; }

        public bool Flag { get; }

        /// <summary>异步调用要写入的句柄名；丢弃句柄或同步调用时为 null。</summary>
        public string HandlerName { get; }

        /// <summary>集合等待的句柄名列表；其它指令为 null。</summary>
        public IReadOnlyList<string> Names { get; }

        /// <summary>集合等待的结果绑定名列表；没有绑定时为空。</summary>
        public IReadOnlyList<string> Bindings { get; }

        internal PevtInstruction(
            PevtOpCode opCode,
            TextSpan span,
            PevtValue constant = default,
            string name = null,
            PevtType type = PevtType.Error,
            int target = -1,
            int index = -1,
            CommandDescriptor descriptor = null,
            SyntaxKind op = SyntaxKind.None,
            bool flag = false,
            string handlerName = null,
            IReadOnlyList<string> names = null,
            IReadOnlyList<string> bindings = null)
        {
            OpCode = opCode;
            Span = span;
            Constant = constant;
            Name = name;
            Type = type;
            Target = target;
            Index = index;
            Descriptor = descriptor;
            Operator = op;
            Flag = flag;
            HandlerName = handlerName;
            Names = names;
            Bindings = bindings;
        }

        public override string ToString() =>
            OpCode switch
            {
                PevtOpCode.PushLiteral => $"PushLiteral {Constant}",
                PevtOpCode.PushName => $"PushName {Name}",
                PevtOpCode.Convert => $"Convert {Type.DisplayName()}",
                PevtOpCode.Binary => $"Binary {Operator}",
                PevtOpCode.Declare => $"Declare {Name} : {Type.DisplayName()} (#{Index})",
                PevtOpCode.Store => $"Store {Name}",
                PevtOpCode.Jump => $"Jump -> {Target}",
                PevtOpCode.JumpIfFalse => $"JumpIfFalse -> {Target}",
                PevtOpCode.JumpIfTrue => $"JumpIfTrue -> {Target}",
                PevtOpCode.StoreSwitch => $"StoreSwitch {Index}",
                PevtOpCode.LoadSwitch => $"LoadSwitch {Index}",
                PevtOpCode.PushGlobalFlagDefined => $"PushGlobalFlagDefined {Name}",
                PevtOpCode.CallBuiltin => $"CallBuiltin @{Descriptor?.Name} /{Index}",
                PevtOpCode.CallBlock => $"CallBlock #{Index}",
                PevtOpCode.CallAsync => $"CallAsync {Name} -> {HandlerName ?? "<discard>"}",
                PevtOpCode.CallEvent => $"CallEvent {Name} -> {HandlerName ?? "<sync>"}",
                PevtOpCode.Status => $"Status {Name}",
                PevtOpCode.AwaitHandle => $"AwaitHandle {Name}",
                PevtOpCode.AwaitAll => $"AwaitAll /{Names?.Count ?? 0}",
                PevtOpCode.AwaitAny => $"AwaitAny /{Names?.Count ?? 0}",
                PevtOpCode.Kill => $"Kill {Name}",
                PevtOpCode.ScheduleAfter => $"ScheduleAfter {Name} -> _{HandlerName}()",
                _ => OpCode.ToString(),
            };
    }

    /// <summary>一个自定义事件块的编译结果。</summary>
    public sealed class PevtBlockInfo
    {
        public string Name { get; }

        public int EntryPoint { get; }

        /// <summary>形参名与类型，按声明顺序。</summary>
        public IReadOnlyList<PevtParameterInfo> Parameters { get; }

        /// <summary>返回类型；null 表示无返回值。</summary>
        public PevtType? ReturnType { get; }

        /// <summary>是否声明为 <c>async block</c>。异步块只能作为 handler 初始化器或被丢弃的异步语句启动。</summary>
        public bool IsAsync { get; }

        public TextSpan Span { get; }

        internal PevtBlockInfo(string name, int entryPoint, IReadOnlyList<PevtParameterInfo> parameters, PevtType? returnType, bool isAsync, TextSpan span)
        {
            Name = name;
            EntryPoint = entryPoint;
            Parameters = parameters;
            ReturnType = returnType;
            IsAsync = isAsync;
            Span = span;
        }

        public override string ToString() => $"{Name}/{Parameters.Count}";
    }

    /// <summary>编译结果。存在本阶段尚未支持的构造时 <see cref="Program"/> 为 null。</summary>
    public sealed class PevtCompileResult
    {
        public PevtCompiledProgram Program { get; }

        /// <summary>本阶段尚未支持的构造（异步、原始桥、事件间调用、动态执行）。</summary>
        public IReadOnlyList<string> UnsupportedFeatures { get; }

        public bool Success => Program != null;

        internal PevtCompileResult(PevtCompiledProgram program, IReadOnlyList<string> unsupported)
        {
            Program = program;
            UnsupportedFeatures = unsupported;
        }
    }

    /// <summary>
    /// 已绑定程序定义的线性执行形式。
    /// </summary>
    public sealed class PevtCompiledProgram
    {
        public string EventId { get; }

        public IReadOnlyList<PevtInstruction> Code { get; }

        public IReadOnlyList<PevtBlockInfo> Blocks { get; }

        /// <summary>
        /// PEVT-E07：事件头声明的参数，按声明顺序。空列表表示旧语法（无参事件）。
        /// <c>callevt</c> 的晚绑定阶段用它核对调用方实参的数量与类型（PEVTR4305）。
        /// </summary>
        public IReadOnlyList<PevtParameterInfo> Parameters { get; }

        /// <summary>PEVT-E08：文件头声明的资源预载组，按声明顺序。空列表表示没有声明任何组。</summary>
        public IReadOnlyList<PevtResourceGroupInfo> ResourceGroups { get; }

        /// <summary>声明语句总数，作为环境里"声明执行标记"的定义域。</summary>
        public int DeclarationCount { get; }

        /// <summary>一帧同时需要的 switch 值槽数量（等于最大 switch 嵌套深度）。</summary>
        public int SwitchSlotCount { get; }

        public SourceText Source { get; }

        private readonly Dictionary<string, PevtBlockInfo> _blocksByName;

        /// <summary>源文件是否声明了 <c>enable cs</c>。<c>exec</c> 片段继承它，且不得扩大。</summary>
        public bool HasCsCapability { get; }

        /// <summary>源文件是否声明了 <c>enable async</c>。<c>callevt</c> 的异步形式与 <c>exec</c> 都要看它。</summary>
        public bool HasAsyncCapability { get; }

        /// <summary>源文件是否声明了 <c>enable cmdarg</c>；动态 <c>exec</c> 片段继承此解析模式。</summary>
        public bool HasCmdArgCapability { get; }

        private PevtCompiledProgram(
            string eventId,
            SourceText source,
            IReadOnlyList<PevtInstruction> code,
            IReadOnlyList<PevtBlockInfo> blocks,
            IReadOnlyList<PevtParameterInfo> parameters,
            IReadOnlyList<PevtResourceGroupInfo> resourceGroups,
            int declarationCount,
            int switchSlotCount,
            bool hasCsCapability,
            bool hasAsyncCapability,
            bool hasCmdArgCapability)
        {
            EventId = eventId;
            Source = source;
            Code = code;
            Blocks = blocks;
            Parameters = parameters;
            ResourceGroups = resourceGroups;
            DeclarationCount = declarationCount;
            SwitchSlotCount = switchSlotCount;
            HasCsCapability = hasCsCapability;
            HasAsyncCapability = hasAsyncCapability;
            HasCmdArgCapability = hasCmdArgCapability;

            _blocksByName = new Dictionary<string, PevtBlockInfo>(StringComparer.Ordinal);
            foreach (PevtBlockInfo block in blocks)
                _blocksByName[block.Name] = block;
        }

        public bool TryGetBlock(string name, out PevtBlockInfo block) =>
            _blocksByName.TryGetValue(name ?? string.Empty, out block);

        public static PevtCompileResult Compile(PevtProgramDefinition definition, CommandDescriptorCatalog catalog = null)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var compiler = new Compiler(definition, catalog ?? CommandDescriptorCatalog.Builtin);
            return compiler.Run();
        }

        private sealed class Compiler
        {
            private readonly PevtProgramDefinition _definition;
            private readonly CommandDescriptorCatalog _catalog;
            private readonly List<PevtInstruction> _code = new List<PevtInstruction>();
            private readonly List<PevtBlockInfo> _blocks = new List<PevtBlockInfo>();
            private readonly List<string> _unsupported = new List<string>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<KeyValuePair<int, string>> _pendingLabelJumps = new List<KeyValuePair<int, string>>();
            private readonly Dictionary<string, BlockDefinitionStatementSyntax> _blockDefinitions = new Dictionary<string, BlockDefinitionStatementSyntax>(StringComparer.Ordinal);
            private int _declarationCount;
            private int _switchDepth;
            private int _maxSwitchDepth;

            public Compiler(PevtProgramDefinition definition, CommandDescriptorCatalog catalog)
            {
                _definition = definition;
                _catalog = catalog;
            }

            public PevtCompileResult Run()
            {
                CollectBlockDefinitions(_definition.Document.Statements);

                EmitStatements(_definition.Document.Statements);
                Emit(PevtOpCode.End, DocumentEndSpan());

                // 事件正文之后依次追加每个事件块的正文，块入口指向自己的第一条指令。
                foreach (KeyValuePair<string, BlockDefinitionStatementSyntax> entry in _blockDefinitions)
                    EmitBlock(entry.Value);

                ResolveLabelJumps();

                if (_unsupported.Count > 0)
                    return new PevtCompileResult(null, new ReadOnlyCollection<string>(_unsupported));

                var program = new PevtCompiledProgram(
                    _definition.EventId,
                    _definition.Source,
                    new ReadOnlyCollection<PevtInstruction>(_code),
                    new ReadOnlyCollection<PevtBlockInfo>(_blocks),
                    new ReadOnlyCollection<PevtParameterInfo>(CollectEventParameters()),
                    new ReadOnlyCollection<PevtResourceGroupInfo>(CollectResourceGroups()),
                    _declarationCount,
                    _maxSwitchDepth,
                    _definition.HasCsCapability,
                    _definition.HasAsyncCapability,
                    _definition.HasCmdArgCapability);

                return new PevtCompileResult(program, Array.AsReadOnly(Array.Empty<string>()));
            }

            private TextSpan DocumentEndSpan()
            {
                int length = _definition.Source?.Length ?? 0;
                return new TextSpan(length, 0);
            }

            /// <summary>PEVT-E07：事件头声明的参数，按声明顺序；旧语法（无参事件）返回空列表。</summary>
            private List<PevtParameterInfo> CollectEventParameters()
            {
                var parameters = new List<PevtParameterInfo>();
                ParameterListSyntax declared = _definition.Document.IdDeclaration?.Parameters;
                if (declared == null)
                    return parameters;

                foreach (ParameterSyntax parameter in declared.Parameters)
                {
                    if (!parameter.Name.IsMissing)
                        parameters.Add(CompileParameter(parameter));
                }

                return parameters;
            }

            private static PevtParameterInfo CompileParameter(ParameterSyntax parameter)
            {
                PevtType type = PevtTypeFacts.FromTypeKeyword(parameter.Type.Kind);
                if (!parameter.HasDefaultValue)
                    return new PevtParameterInfo(parameter.Name.Text, type);

                // Binder 已经保证这是类型匹配的编译期常量；这里保留防御性检查，避免构造损坏的编译产物。
                if (!PevtParameterDefaults.TryEvaluate(parameter.DefaultValue, out PevtValue defaultValue)
                    || defaultValue.Type != type)
                    throw new InvalidOperationException($"形参 `{parameter.Name.Text}` 的默认值没有通过静态绑定。");

                return new PevtParameterInfo(parameter.Name.Text, type, defaultValue);
            }

            /// <summary>
            /// PEVT-E08：把语法层已经验证过形状的 <see cref="ResourcesDeclarationSyntax"/> 原样搬进编译产物。
            /// 只有解析成功（<c>Kind</c> 非空）的成员才会进入编译结果——形状错误的引用已经在解析阶段
            /// 报过 PEVT1114-1116，这里再带进去只会让运行时对着一份本来就该被拒绝的数据白费功夫。
            /// </summary>
            private List<PevtResourceGroupInfo> CollectResourceGroups()
            {
                var groups = new List<PevtResourceGroupInfo>();
                foreach (ResourcesDeclarationSyntax declaration in _definition.Document.ResourceGroups)
                {
                    if (declaration.GroupId.IsMissing || declaration.GroupId.Value.Kind != TokenValueKind.String)
                        continue;

                    var members = new List<PevtResourceMember>();
                    foreach (ResourceReferenceSyntax member in declaration.Members)
                    {
                        if (member.Kind != null)
                            members.Add(new PevtResourceMember(member.Kind, member.ActorId, member.AppearanceId, member.AssetId, member.Literal.Text));
                    }

                    groups.Add(new PevtResourceGroupInfo(declaration.GroupId.Value.AsString, members));
                }

                return groups;
            }

            private void CollectBlockDefinitions(IReadOnlyList<StatementSyntax> statements)
            {
                foreach (StatementSyntax statement in statements)
                {
                    if (statement is BlockDefinitionStatementSyntax block && !block.Name.IsMissing)
                        _blockDefinitions[block.Name.Text] = block;
                }
            }

            private void EmitBlock(BlockDefinitionStatementSyntax definition)
            {
                bool isAsync = definition.AsyncKeyword != null && !definition.AsyncKeyword.IsMissing;

                var parameters = new List<PevtParameterInfo>();
                if (definition.Parameters != null)
                {
                    foreach (ParameterSyntax parameter in definition.Parameters.Parameters)
                    {
                        parameters.Add(CompileParameter(parameter));
                    }
                }

                PevtType? returnType = definition.ReturnType != null && !definition.ReturnType.IsMissing
                    ? PevtTypeFacts.FromTypeKeyword(definition.ReturnType.Kind)
                    : (PevtType?)null;

                int entry = _code.Count;

                // 事件块有自己的标签环境；进入前后各换一份标签表。
                Dictionary<string, int> outerLabels = new Dictionary<string, int>(_labels, StringComparer.Ordinal);
                List<KeyValuePair<int, string>> outerPending = new List<KeyValuePair<int, string>>(_pendingLabelJumps);
                _labels.Clear();
                _pendingLabelJumps.Clear();

                EmitStatements(definition.Body);

                // 没有显式 return 的块正文走到末尾时按无值返回处理。
                Emit(PevtOpCode.Return, definition.EndBlockKeyword.Span, flag: false);
                ResolveLabelJumps();

                _labels.Clear();
                foreach (KeyValuePair<string, int> label in outerLabels)
                    _labels[label.Key] = label.Value;
                _pendingLabelJumps.Clear();
                _pendingLabelJumps.AddRange(outerPending);

                _blocks.Add(new PevtBlockInfo(
                    definition.Name.Text,
                    entry,
                    new ReadOnlyCollection<PevtParameterInfo>(parameters),
                    returnType,
                    isAsync,
                    definition.Span));
            }

            private void EmitStatements(IReadOnlyList<StatementSyntax> statements)
            {
                foreach (StatementSyntax statement in statements)
                    EmitStatement(statement);
            }

            private void EmitStatement(StatementSyntax statement)
            {
                switch (statement)
                {
                    case BlockDefinitionStatementSyntax _:
                        return; // 定义不是可执行语句，正文单独编译。

                    case LabelStatementSyntax label:
                        _labels[label.Name.Text] = _code.Count;
                        return;

                    case EndStatementSyntax end:
                        Emit(PevtOpCode.End, end.Span);
                        return;

                    case VariableDeclarationSyntax variable:
                        EmitDeclaration(variable.Name.Text, variable.Type.Kind, variable.Initializer, variable.Span, isConstant: false);
                        return;

                    case ConstantDeclarationSyntax constant:
                        EmitDeclaration(constant.Name.Text, constant.Type.Kind, constant.Initializer, constant.Span, isConstant: true);
                        return;

                    case AssignmentStatementSyntax assignment:
                        EmitExpression(assignment.Value);
                        Emit(PevtOpCode.Store, assignment.Span, name: assignment.Target.Text);
                        return;

                    case IfStatementSyntax ifStatement:
                        EmitIf(ifStatement);
                        return;

                    case IfDefStatementSyntax ifDefStatement:
                        EmitIfDef(ifDefStatement);
                        return;

                    case WhileStatementSyntax whileStatement:
                        EmitWhile(whileStatement);
                        return;

                    case SwitchStatementSyntax switchStatement:
                        EmitSwitch(switchStatement);
                        return;

                    case GotoLabelStatementSyntax gotoLabel:
                        EmitLabelJump(gotoLabel.Name.Text, gotoLabel.Span);
                        return;

                    case GotoCaseStatementSyntax gotoCase:
                        EmitGotoCase(gotoCase);
                        return;

                    case ReturnStatementSyntax returnStatement:
                        EmitReturn(returnStatement);
                        return;

                    case ExpressionStatementSyntax expressionStatement:
                        EmitExpressionStatement(expressionStatement);
                        return;

                    case HandlerDeclarationStatementSyntax handler:
                        EmitAsyncStart(handler.Initializer, handler.Name.Text);
                        return;

                    case KillStatementSyntax kill:
                        Emit(PevtOpCode.Kill, kill.Span, name: kill.Handle.Text);
                        return;

                    case ScheduleStatementSyntax schedule:
                        EmitSchedule(schedule);
                        return;

                    case FlushSchedulesStatementSyntax flush:
                        Emit(PevtOpCode.FlushSchedules, flush.Span);
                        return;

                    case ClearSchedulesStatementSyntax clear:
                        Emit(PevtOpCode.ClearSchedules, clear.Span);
                        return;

                    case RawCmdStatementSyntax rawCmd:
                        Emit(PevtOpCode.RawCmd, rawCmd.Span, constant: RawContentOf(rawCmd.Content));
                        return;

                    case UnknownStatementSyntax unknown:
                        Unsupported($"无法识别的语句 `{unknown.LeadingToken.Text}`");
                        return;

                    default:
                        Unsupported(statement.GetType().Name);
                        return;
                }
            }

            private void EmitDeclaration(string name, SyntaxKind typeKeyword, ExpressionSyntax initializer, TextSpan span, bool isConstant)
            {
                int declarationId = _declarationCount++;
                PevtType type = PevtTypeFacts.FromTypeKeyword(typeKeyword);

                if (initializer != null)
                    EmitExpression(initializer);

                Emit(PevtOpCode.Declare, span, name: name, type: type, index: declarationId, flag: initializer != null, op: isConstant ? SyntaxKind.ConstKeyword : SyntaxKind.VarKeyword);
            }

            private void EmitIf(IfStatementSyntax statement)
            {
                var endJumps = new List<int>();

                EmitExpression(statement.Condition);
                int nextJump = EmitPlaceholder(PevtOpCode.JumpIfFalse, statement.IfKeyword.Span);
                EmitStatements(statement.Body);
                endJumps.Add(EmitPlaceholder(PevtOpCode.Jump, statement.IfKeyword.Span));
                Patch(nextJump, _code.Count);

                foreach (ElifClauseSyntax elif in statement.ElifClauses)
                {
                    EmitExpression(elif.Condition);
                    nextJump = EmitPlaceholder(PevtOpCode.JumpIfFalse, elif.ElifKeyword.Span);
                    EmitStatements(elif.Body);
                    endJumps.Add(EmitPlaceholder(PevtOpCode.Jump, elif.ElifKeyword.Span));
                    Patch(nextJump, _code.Count);
                }

                if (statement.ElseClause != null)
                    EmitStatements(statement.ElseClause.Body);

                foreach (int jump in endJumps)
                    Patch(jump, _code.Count);
            }

            private void EmitIfDef(IfDefStatementSyntax statement)
            {
                Emit(PevtOpCode.PushGlobalFlagDefined, statement.IfDefKeyword.Span, name: statement.FlagKey);
                int elseJump = EmitPlaceholder(PevtOpCode.JumpIfFalse, statement.IfDefKeyword.Span);
                EmitStatements(statement.Body);

                if (!statement.HasElse)
                {
                    Patch(elseJump, _code.Count);
                    return;
                }

                int endJump = EmitPlaceholder(PevtOpCode.Jump, statement.ElseDefKeyword.Span);
                Patch(elseJump, _code.Count);
                EmitStatements(statement.ElseBody);
                Patch(endJump, _code.Count);
            }

            private void EmitWhile(WhileStatementSyntax statement)
            {
                int conditionStart = _code.Count;
                EmitExpression(statement.Condition);
                int exitJump = EmitPlaceholder(PevtOpCode.JumpIfFalse, statement.WhileKeyword.Span);
                EmitStatements(statement.Body);
                EmitJump(conditionStart, statement.EndWhile.Span);
                Patch(exitJump, _code.Count);
            }

            private void EmitSwitch(SwitchStatementSyntax statement)
            {
                int slot = _switchDepth++;
                if (_switchDepth > _maxSwitchDepth)
                    _maxSwitchDepth = _switchDepth;

                // switch 值只求值一次：算完立刻存进本帧的槽，后面的比较全部读槽。
                EmitExpression(statement.Value);
                Emit(PevtOpCode.StoreSwitch, statement.SwitchKeyword.Span, index: slot);

                int dispatchStart = _code.Count;
                _switchDispatchStarts[slot] = dispatchStart;

                var armJumps = new List<int>();
                var armStarts = new int[statement.Arms.Count];
                int defaultArm = -1;

                for (int i = 0; i < statement.Arms.Count; i++)
                {
                    if (statement.Arms[i] is CaseArmSyntax caseArm)
                    {
                        Emit(PevtOpCode.LoadSwitch, caseArm.CaseKeyword.Span, index: slot);
                        EmitExpression(caseArm.Value);
                        Emit(PevtOpCode.Binary, caseArm.CaseKeyword.Span, op: SyntaxKind.EqualsEqualsToken);
                        armJumps.Add(EmitPlaceholder(PevtOpCode.JumpIfTrue, caseArm.CaseKeyword.Span));
                    }
                    else
                    {
                        armJumps.Add(-1);
                        defaultArm = i;
                    }
                }

                int noMatchJump = EmitPlaceholder(PevtOpCode.Jump, statement.SwitchKeyword.Span);

                var endJumps = new List<int>();
                for (int i = 0; i < statement.Arms.Count; i++)
                {
                    armStarts[i] = _code.Count;
                    if (armJumps[i] >= 0)
                        Patch(armJumps[i], armStarts[i]);

                    EmitStatements(statement.Arms[i].Body);
                    endJumps.Add(EmitPlaceholder(PevtOpCode.Jump, statement.EndSwitch.Span));
                }

                Patch(noMatchJump, defaultArm >= 0 ? armStarts[defaultArm] : _code.Count);
                foreach (int jump in endJumps)
                    Patch(jump, _code.Count);

                _switchDispatchStarts.Remove(slot);
                _switchDepth--;
            }

            private readonly Dictionary<int, int> _switchDispatchStarts = new Dictionary<int, int>();

            private void EmitGotoCase(GotoCaseStatementSyntax statement)
            {
                if (_switchDepth == 0 || !_switchDispatchStarts.TryGetValue(_switchDepth - 1, out int dispatchStart))
                {
                    Unsupported("`goto 表达式` 出现在 switch 之外");
                    return;
                }

                // 换一个 switch 值再跑一遍同一条派发链，等价于"跳到匹配的 case"，
                // 而且跳转目标仍然是编译期固定的。
                EmitExpression(statement.Target);
                Emit(PevtOpCode.StoreSwitch, statement.Span, index: _switchDepth - 1);
                EmitJump(dispatchStart, statement.Span);
            }

            private void EmitReturn(ReturnStatementSyntax statement)
            {
                bool hasValue = statement.Target != null && !statement.Target.IsMissing;
                if (hasValue)
                    Emit(PevtOpCode.PushName, statement.Target.Span, name: statement.Target.Text);

                Emit(PevtOpCode.Return, statement.Span, flag: hasValue);
            }

            private void EmitExpressionStatement(ExpressionStatementSyntax statement)
            {
                // 单句柄 await 作为独立语句时不产出值，所以干脆不压栈，而不是压完再 Pop——
                // 无返回值的异步定义本来就没有值可压。
                if (statement.Expression is AwaitExpressionSyntax awaitStatement)
                {
                    Emit(PevtOpCode.AwaitHandle, awaitStatement.Span, name: awaitStatement.Handle.Text, flag: false);
                    return;
                }

                // 语句位置的 `$raw cs` 是纯调用：代码块可能根本没有返回类型，压完再 Pop 会让求值栈欠一格。
                if (statement.Expression is RawCsExpressionSyntax rawCsStatement)
                {
                    EmitRawCs(rawCsStatement, producesValue: false);
                    return;
                }

                EmitExpression(statement.Expression);

                // 语句位置的调用如果有返回值，结果直接丢弃。
                if (ProducesValue(statement.Expression))
                    Emit(PevtOpCode.Pop, statement.Span);
            }

            private bool ProducesValue(ExpressionSyntax expression)
            {
                switch (expression)
                {
                    case BuiltinCallExpressionSyntax builtin:
                        return TryResolveDescriptor(builtin, out CommandDescriptor descriptor) && descriptor.ReturnType.HasValue;
                    case CustomBlockCallExpressionSyntax block:
                        // 异步块作为语句时只启动、不产出值；同步块看它有没有声明返回类型。
                        return _blockDefinitions.TryGetValue(block.Name.Text, out BlockDefinitionStatementSyntax definition)
                            && (definition.AsyncKeyword == null || definition.AsyncKeyword.IsMissing)
                            && definition.ReturnType != null && !definition.ReturnType.IsMissing;

                    // callevt 与 exec 作为语句都不留值；集合等待固定产出一个 int。
                    case EventCallExpressionSyntax _:
                    case ExecCallExpressionSyntax _:
                        return false;

                    default:
                        return true;
                }
            }

            private void EmitExpression(ExpressionSyntax expression)
            {
                switch (expression)
                {
                    case LiteralExpressionSyntax literal:
                        Emit(PevtOpCode.PushLiteral, literal.Span, constant: ToValue(literal.Token));
                        return;

                    case NameExpressionSyntax name:
                        Emit(PevtOpCode.PushName, name.Span, name: name.Identifier.Text);
                        return;

                    case ParenthesizedExpressionSyntax parenthesized:
                        EmitExpression(parenthesized.Inner);
                        return;

                    case ConversionExpressionSyntax conversion:
                        Emit(PevtOpCode.PushName, conversion.Variable.Span, name: conversion.Variable.Text);
                        Emit(PevtOpCode.Convert, conversion.Span, type: PevtTypeFacts.FromTypeKeyword(conversion.TargetType.Kind));
                        return;

                    case UnaryExpressionSyntax unary:
                        if (IsFoldedIntegerMinValue(unary))
                        {
                            // 解析器把 `-2147483648` 的负号折进了字面量的值里（见 Parser 的 CloseUnaryMinusOperandIntegerBoundary），
                            // 但语法树上仍然留着外面那层一元负号；照常再取一次负会立刻越界，所以这里只压已经折好的字面量。
                            Emit(PevtOpCode.PushLiteral, unary.Span, constant: PevtValue.FromInt(int.MinValue));
                            return;
                        }

                        EmitExpression(unary.Operand);
                        Emit(unary.OperatorToken.Kind == SyntaxKind.ExclamationToken ? PevtOpCode.Not : PevtOpCode.Negate, unary.Span);
                        return;

                    case ChainedBinaryExpressionSyntax chain:
                        // 8.8 节：无括号链式表达式严格从左到右，运算符之间没有隐式优先级。
                        EmitExpression(chain.First);
                        foreach (BinaryChainSegment segment in chain.Segments)
                        {
                            EmitExpression(segment.Operand);
                            Emit(PevtOpCode.Binary, segment.OperatorToken.Span, op: segment.OperatorToken.Kind);
                        }
                        return;

                    case BuiltinCallExpressionSyntax builtin:
                        EmitBuiltinCall(builtin);
                        return;

                    case CustomBlockCallExpressionSyntax blockCall:
                        EmitBlockCall(blockCall);
                        return;

                    case EventCallExpressionSyntax eventCall:
                        // 语句位置的 callevt 是同步子事件调用：HandlerName 为 null 表示调用方要等它。
                        EmitEventCallArguments(eventCall);
                        Emit(PevtOpCode.CallEvent, eventCall.Span,
                            name: EventTargetOf(eventCall), index: eventCall.Arguments?.Arguments.Count ?? 0);
                        return;

                    case ExecCallExpressionSyntax exec:
                        EmitExec(exec);
                        return;

                    case RawCsExpressionSyntax rawCs:
                        EmitRawCs(rawCs, producesValue: true);
                        return;

                    case StatusExpressionSyntax status:
                        Emit(PevtOpCode.Status, status.Span, name: status.Handle.Text);
                        return;

                    case AwaitExpressionSyntax awaitExpression:
                        Emit(PevtOpCode.AwaitHandle, awaitExpression.Span, name: awaitExpression.Handle.Text, flag: true);
                        return;

                    case AggregateAwaitExpressionSyntax aggregate:
                        EmitAggregateAwait(aggregate);
                        return;

                    case MissingExpressionSyntax _:
                        Unsupported("缺失的表达式");
                        return;

                    default:
                        Unsupported(expression.GetType().Name);
                        return;
                }
            }

            /// <summary>
            /// 取原始文本块的解码内容。词法器已经把 <c>\'''</c> 折成 <c>'''</c>，这里拿到的就是
            /// 该提交给原版解释器或 C# 编译器的文本；缺失的内容 token 退化成空串。
            /// </summary>
            private static PevtValue RawContentOf(SyntaxToken content) =>
                PevtValue.FromString(
                    !content.IsMissing && content.Value.Kind == TokenValueKind.String ? content.Value.AsString : string.Empty);

            /// <summary>
            /// <c>$raw cs</c>。传入变量名保存在 <c>Names</c> 里，运行时按名字从当前环境取值快照——
            /// 不预先压栈，因为形参类型要在执行点按环境里的声明类型确定（缓存键含参数类型）。
            /// </summary>
            private void EmitRawCs(RawCsExpressionSyntax node, bool producesValue)
            {
                var names = new List<string>();
                if (node.Arguments != null)
                {
                    foreach (SyntaxToken identifier in node.Arguments.Identifiers)
                    {
                        if (!identifier.IsMissing)
                            names.Add(identifier.Text);
                    }
                }

                Emit(
                    PevtOpCode.RawCs,
                    node.Span,
                    constant: RawContentOf(node.Content),
                    flag: producesValue,
                    names: new ReadOnlyCollection<string>(names));
            }

            private void EmitBuiltinCall(BuiltinCallExpressionSyntax call)
            {
                foreach (ExpressionSyntax argument in call.Arguments.Arguments)
                    EmitExpression(argument);

                if (!TryResolveDescriptor(call, out CommandDescriptor descriptor))
                {
                    Unsupported($"未登记的 `@{call.Name.Text}` 重载");
                    return;
                }

                // 没有 handler 接的异步调用仍然启动，句柄丢弃；协程照样归事件所有（第 7 节）。
                Emit(
                    descriptor.IsAsync ? PevtOpCode.CallAsync : PevtOpCode.CallBuiltin,
                    call.Span,
                    index: call.Arguments.Arguments.Count,
                    descriptor: descriptor,
                    name: call.Name.Text);
            }

            private void EmitBlockCall(CustomBlockCallExpressionSyntax call)
            {
                foreach (ExpressionSyntax argument in call.Arguments.Arguments)
                    EmitExpression(argument);

                if (!_blockDefinitions.TryGetValue(call.Name.Text, out BlockDefinitionStatementSyntax definition))
                {
                    Unsupported($"未定义的事件块 `{call.Name.Text}`");
                    return;
                }

                bool isAsync = definition.AsyncKeyword != null && !definition.AsyncKeyword.IsMissing;
                Emit(
                    isAsync ? PevtOpCode.CallAsync : PevtOpCode.CallBlock,
                    call.Span,
                    index: call.Arguments.Arguments.Count,
                    name: call.Name.Text);
            }

            /// <summary>
            /// <c>handler h = ...</c>：照常求值实参，再按初始化器种类启动异步操作并把句柄名带给指令。
            /// 三种合法初始化器（异步 <c>@</c>、<c>async block</c>、<c>callevt</c>）复用的都是同步调用那条路径上的同一份描述条目或块定义。
            /// </summary>
            private void EmitAsyncStart(ExpressionSyntax initializer, string handlerName)
            {
                switch (initializer)
                {
                    case BuiltinCallExpressionSyntax builtin:
                    {
                        foreach (ExpressionSyntax argument in builtin.Arguments.Arguments)
                            EmitExpression(argument);

                        if (!TryResolveDescriptor(builtin, out CommandDescriptor descriptor))
                        {
                            Unsupported($"未登记的 `@{builtin.Name.Text}` 重载");
                            return;
                        }

                        Emit(PevtOpCode.CallAsync, builtin.Span,
                            index: builtin.Arguments.Arguments.Count, descriptor: descriptor,
                            name: builtin.Name.Text, handlerName: handlerName);
                        return;
                    }

                    case CustomBlockCallExpressionSyntax blockCall:
                    {
                        foreach (ExpressionSyntax argument in blockCall.Arguments.Arguments)
                            EmitExpression(argument);

                        if (!_blockDefinitions.ContainsKey(blockCall.Name.Text))
                        {
                            Unsupported($"未定义的事件块 `{blockCall.Name.Text}`");
                            return;
                        }

                        Emit(PevtOpCode.CallAsync, blockCall.Span,
                            index: blockCall.Arguments.Arguments.Count,
                            name: blockCall.Name.Text, handlerName: handlerName);
                        return;
                    }

                    case EventCallExpressionSyntax eventCall:
                        EmitEventCallArguments(eventCall);
                        Emit(PevtOpCode.CallEvent, eventCall.Span,
                            name: EventTargetOf(eventCall), index: eventCall.Arguments?.Arguments.Count ?? 0, handlerName: handlerName);
                        return;

                    default:
                        Unsupported($"`handler {handlerName}` 的初始化器不是异步调用");
                        return;
                }
            }

            /// <summary>
            /// PEVT-E05：frames 表达式照常求值；目标块名只用来在触发时重新查表（<see cref="PevtCompiledProgram.TryGetBlock"/>），
            /// 不在这里内联块信息——排入的是"名字"，触发时才需要真正的入口点和签名。
            /// </summary>
            private void EmitSchedule(ScheduleStatementSyntax schedule)
            {
                EmitExpression(schedule.Frames);

                if (schedule.Target == null)
                {
                    Unsupported("`schedule` 缺少合法的调用目标");
                    return;
                }

                if (!_blockDefinitions.ContainsKey(schedule.Target.Name.Text))
                {
                    Unsupported($"未定义的事件块 `{schedule.Target.Name.Text}`");
                    return;
                }

                Emit(PevtOpCode.ScheduleAfter, schedule.Span,
                    name: schedule.TimelineId.Text, handlerName: schedule.Target.Name.Text);
            }

            /// <summary>PEVT-E07：省略括号时零实参，否则按声明顺序求值。签名匹配是运行时晚绑定的职责，这里只管求值顺序。</summary>
            private void EmitEventCallArguments(EventCallExpressionSyntax call)
            {
                if (call.Arguments == null)
                    return;

                foreach (ExpressionSyntax argument in call.Arguments.Arguments)
                    EmitExpression(argument);
            }

            /// <summary><c>callevt "ID"</c> 的目标是一个字符串字面量 token；取它的值而不是原始文本。</summary>
            private static string EventTargetOf(EventCallExpressionSyntax call) =>
                call.Target.Value.Kind == TokenValueKind.String ? call.Target.Value.AsString : call.Target.Text;

            /// <summary>
            /// 集合等待。句柄名与结果绑定名都在编译期固定成两份只读列表，运行期不再解析标识符。
            /// </summary>
            private void EmitAggregateAwait(AggregateAwaitExpressionSyntax aggregate)
            {
                var handles = new List<string>();
                foreach (SyntaxToken handle in aggregate.Handles.Identifiers)
                    handles.Add(handle.Text);

                var bindings = new List<string>();
                if (aggregate.Bindings != null)
                {
                    foreach (SyntaxToken binding in aggregate.Bindings.Identifiers)
                        bindings.Add(binding.Text);
                }

                bool isAny = aggregate.ModeKeyword.Kind == SyntaxKind.AnyKeyword;
                Emit(isAny ? PevtOpCode.AwaitAny : PevtOpCode.AwaitAll, aggregate.Span,
                    names: new ReadOnlyCollection<string>(handles),
                    bindings: new ReadOnlyCollection<string>(bindings));
            }

            /// <summary>
            /// <c>exec</c>：实参照常求值，片段源码是第一个实参。片段内容只有运行时才知道，
            /// 因此这里不做任何静态分析——它的词法/语法/绑定校验全部在运行时重做一遍。
            /// </summary>
            private void EmitExec(ExecCallExpressionSyntax exec)
            {
                foreach (ExpressionSyntax argument in exec.Arguments.Arguments)
                    EmitExpression(argument);

                Emit(PevtOpCode.Exec, exec.Span, index: exec.Arguments.Arguments.Count);
            }

            /// <summary>
            /// 按名称与实参数量在描述目录中定位重载。参数的静态类型已由绑定器验证过，
            /// 因此这里只需要数量就能唯一确定——同名同数量不同类型的重载在目录建表时已被拒绝。
            /// </summary>
            private bool TryResolveDescriptor(BuiltinCallExpressionSyntax call, out CommandDescriptor descriptor)
            {
                descriptor = null;
                int arity = call.Arguments.Arguments.Count;

                foreach (CommandDescriptor candidate in _catalog.Find(call.Name.Text))
                {
                    if (candidate.Parameters.Count != arity)
                        continue;
                    if (descriptor != null)
                        return false;
                    descriptor = candidate;
                }

                return descriptor != null;
            }

            /// <summary>
            /// 识别"负号已经折进字面量"的 <c>-2147483648</c>：token 文本仍然是无符号的
            /// <c>2147483648</c>，但它携带的值已经是 <see cref="int.MinValue"/>。
            /// </summary>
            private static bool IsFoldedIntegerMinValue(UnaryExpressionSyntax unary) =>
                unary.OperatorToken.Kind == SyntaxKind.MinusToken
                && unary.Operand is LiteralExpressionSyntax literal
                && literal.Token.Kind == SyntaxKind.IntegerLiteralToken
                && literal.Token.Text == "2147483648"
                && literal.Token.Value.Kind == TokenValueKind.Integer
                && literal.Token.Value.AsInteger == int.MinValue;

            private static PevtValue ToValue(SyntaxToken token)
            {
                switch (token.Value.Kind)
                {
                    case TokenValueKind.Integer: return PevtValue.FromInt(token.Value.AsInteger);
                    case TokenValueKind.Float: return PevtValue.FromFloat(token.Value.AsFloat);
                    case TokenValueKind.Boolean: return PevtValue.FromBool(token.Value.AsBoolean);
                    case TokenValueKind.Char: return PevtValue.FromChar(token.Value.AsChar);
                    case TokenValueKind.String: return PevtValue.FromString(token.Value.AsString);
                    default: return PevtValue.None;
                }
            }

            private void EmitLabelJump(string labelName, TextSpan span)
            {
                _pendingLabelJumps.Add(new KeyValuePair<int, string>(_code.Count, labelName));
                Emit(PevtOpCode.Jump, span);
            }

            private void ResolveLabelJumps()
            {
                foreach (KeyValuePair<int, string> pending in _pendingLabelJumps)
                {
                    if (!_labels.TryGetValue(pending.Value, out int target))
                    {
                        Unsupported($"未定义的标签 `#{pending.Value}`");
                        continue;
                    }

                    Patch(pending.Key, target);
                }

                _pendingLabelJumps.Clear();
            }

            private int EmitPlaceholder(PevtOpCode opCode, TextSpan span)
            {
                int index = _code.Count;
                Emit(opCode, span);
                return index;
            }

            private void EmitJump(int target, TextSpan span) => Emit(PevtOpCode.Jump, span, target: target);

            private void Patch(int instructionIndex, int target)
            {
                PevtInstruction original = _code[instructionIndex];
                _code[instructionIndex] = new PevtInstruction(
                    original.OpCode, original.Span, original.Constant, original.Name, original.Type,
                    target, original.Index, original.Descriptor, original.Operator, original.Flag,
                    original.HandlerName, original.Names, original.Bindings);
            }

            private void Emit(
                PevtOpCode opCode,
                TextSpan span,
                PevtValue constant = default,
                string name = null,
                PevtType type = PevtType.Error,
                int target = -1,
                int index = -1,
                CommandDescriptor descriptor = null,
                SyntaxKind op = SyntaxKind.None,
                bool flag = false,
                string handlerName = null,
                IReadOnlyList<string> names = null,
                IReadOnlyList<string> bindings = null) =>
                _code.Add(new PevtInstruction(opCode, span, constant, name, type, target, index, descriptor, op, flag, handlerName, names, bindings));

            private void Unsupported(string feature)
            {
                if (!_unsupported.Contains(feature))
                    _unsupported.Add(feature);
            }
        }
    }
}
