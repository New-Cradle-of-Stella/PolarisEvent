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

        /// <summary>终止整个事件执行。</summary>
        End,

        /// <summary>从自定义事件块返回；<c>Flag</c> 为 true 表示带返回值。</summary>
        Return,

        /// <summary>调用同步 <c>@</c>，弹出 <c>Index</c> 个实参。</summary>
        CallBuiltin,

        /// <summary>调用自定义事件块，弹出 <c>Index</c> 个实参并压入新帧。</summary>
        CallBlock,
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
            bool flag = false)
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
                PevtOpCode.CallBuiltin => $"CallBuiltin @{Descriptor?.Name} /{Index}",
                PevtOpCode.CallBlock => $"CallBlock #{Index}",
                _ => OpCode.ToString(),
            };
    }

    /// <summary>一个自定义事件块的编译结果。</summary>
    public sealed class PevtBlockInfo
    {
        public string Name { get; }

        public int EntryPoint { get; }

        /// <summary>形参名与类型，按声明顺序。</summary>
        public IReadOnlyList<KeyValuePair<string, PevtType>> Parameters { get; }

        /// <summary>返回类型；null 表示无返回值。</summary>
        public PevtType? ReturnType { get; }

        public TextSpan Span { get; }

        internal PevtBlockInfo(string name, int entryPoint, IReadOnlyList<KeyValuePair<string, PevtType>> parameters, PevtType? returnType, TextSpan span)
        {
            Name = name;
            EntryPoint = entryPoint;
            Parameters = parameters;
            ReturnType = returnType;
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
    ///
    /// 把语法树编译成指令序列有两个理由，都是计划里的硬要求：跳转目标必须"预绑定"，
    /// 而事件与块调用必须使用显式帧而不是 C# 递归。线性指令天然满足两者，而且让
    /// "表达式中间出现一个会跨帧的 <c>@</c> 调用"变成可恢复的——求值栈保存在帧上，
    /// 协程挂起后可以从同一条指令继续。
    ///
    /// 编译是纯函数：同一份定义永远得到同一份指令。
    /// </summary>
    public sealed class PevtCompiledProgram
    {
        public string EventId { get; }

        public IReadOnlyList<PevtInstruction> Code { get; }

        public IReadOnlyList<PevtBlockInfo> Blocks { get; }

        /// <summary>声明语句总数，作为环境里"声明执行标记"的定义域。</summary>
        public int DeclarationCount { get; }

        /// <summary>一帧同时需要的 switch 值槽数量（等于最大 switch 嵌套深度）。</summary>
        public int SwitchSlotCount { get; }

        public SourceText Source { get; }

        private readonly Dictionary<string, PevtBlockInfo> _blocksByName;

        private PevtCompiledProgram(
            string eventId,
            SourceText source,
            IReadOnlyList<PevtInstruction> code,
            IReadOnlyList<PevtBlockInfo> blocks,
            int declarationCount,
            int switchSlotCount)
        {
            EventId = eventId;
            Source = source;
            Code = code;
            Blocks = blocks;
            DeclarationCount = declarationCount;
            SwitchSlotCount = switchSlotCount;

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
                    _declarationCount,
                    _maxSwitchDepth);

                return new PevtCompileResult(program, Array.AsReadOnly(Array.Empty<string>()));
            }

            private TextSpan DocumentEndSpan()
            {
                int length = _definition.Source?.Length ?? 0;
                return new TextSpan(length, 0);
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
                if (definition.AsyncKeyword != null && !definition.AsyncKeyword.IsMissing)
                {
                    Unsupported($"`async block {definition.Name.Text}`");
                    return;
                }

                var parameters = new List<KeyValuePair<string, PevtType>>();
                if (definition.Parameters != null)
                {
                    foreach (ParameterSyntax parameter in definition.Parameters.Parameters)
                    {
                        parameters.Add(new KeyValuePair<string, PevtType>(
                            parameter.Name.Text,
                            PevtTypeFacts.FromTypeKeyword(parameter.Type.Kind)));
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
                    new ReadOnlyCollection<KeyValuePair<string, PevtType>>(parameters),
                    returnType,
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
                        Unsupported($"`handler {handler.Name.Text}`");
                        return;

                    case KillStatementSyntax kill:
                        Unsupported($"`kill {kill.Handle.Text}`");
                        return;

                    case RawCmdStatementSyntax _:
                        Unsupported("`$raw cmd`");
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
                        return _blockDefinitions.TryGetValue(block.Name.Text, out BlockDefinitionStatementSyntax definition)
                            && definition.ReturnType != null && !definition.ReturnType.IsMissing;
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
                            // 解析器把 `-2147483648` 的负号折进了字面量的值里（见 Parser 的
                            // CloseUnaryMinusOperandIntegerBoundary），但语法树上仍然留着外面那层
                            // 一元负号。照常再取一次负会立刻越界，所以这里只压已经折好的字面量。
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
                        Unsupported($"`callevt {eventCall.Target.Text}`");
                        return;

                    case ExecCallExpressionSyntax _:
                        Unsupported("`exec`");
                        return;

                    case RawCsExpressionSyntax _:
                        Unsupported("`$raw cs`");
                        return;

                    case StatusExpressionSyntax status:
                        Unsupported($"`status {status.Handle.Text}`");
                        return;

                    case AggregateAwaitExpressionSyntax aggregate:
                        Unsupported($"`await {aggregate.ModeKeyword.Text}`");
                        return;

                    case MissingExpressionSyntax _:
                        Unsupported("缺失的表达式");
                        return;

                    default:
                        Unsupported(expression.GetType().Name);
                        return;
                }
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

                if (descriptor.IsAsync)
                {
                    Unsupported($"异步调用 `@{call.Name.Text}`");
                    return;
                }

                Emit(PevtOpCode.CallBuiltin, call.Span, index: call.Arguments.Arguments.Count, descriptor: descriptor, name: call.Name.Text);
            }

            private void EmitBlockCall(CustomBlockCallExpressionSyntax call)
            {
                foreach (ExpressionSyntax argument in call.Arguments.Arguments)
                    EmitExpression(argument);

                if (!_blockDefinitions.ContainsKey(call.Name.Text))
                {
                    Unsupported($"未定义的事件块 `{call.Name.Text}`");
                    return;
                }

                Emit(PevtOpCode.CallBlock, call.Span, index: call.Arguments.Arguments.Count, name: call.Name.Text);
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
                    target, original.Index, original.Descriptor, original.Operator, original.Flag);
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
                bool flag = false) =>
                _code.Add(new PevtInstruction(opCode, span, constant, name, type, target, index, descriptor, op, flag));

            private void Unsupported(string feature)
            {
                if (!_unsupported.Contains(feature))
                    _unsupported.Add(feature);
            }
        }
    }
}
