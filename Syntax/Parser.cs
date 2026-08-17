using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 递归下降解析器的表达式部分：按 8.8 节从左到右解析二元链，括号内递归形成独立子表达式，不做名称/类型绑定。
    /// 语句与文档层面的解析在同一个 partial class 的 Parser.Statements.cs。
    /// </summary>
    public sealed partial class Parser
    {
        private readonly IReadOnlyList<SyntaxToken> _tokens;
        private readonly DiagnosticBag _diagnostics;
        private readonly SourceText _source;
        private int _position;

        /// <summary>
        /// 当前嵌套打开的自定义事件块，每层只记录是否声明了返回值类型。
        /// 这就是 <c>return</c>、块内 <c>end</c> 和嵌套定义三处检查唯一需要的上下文，不需要完整符号表。
        /// </summary>
        private readonly Stack<bool> _blockStack = new Stack<bool>();

        /// <summary>
        /// 当前嵌套打开的 <c>switch</c> 层数，只用来判断裸表达式形式的 <c>goto 表达式</c>（6.5 节）语法上是否允许出现。
        /// 进入自定义事件块时清零并在块结束后恢复，因为块拥有独立的标签与跳转环境。
        /// </summary>
        private int _switchDepth;

        public Parser(IReadOnlyList<SyntaxToken> tokens, DiagnosticBag diagnostics, SourceText source)
        {
            _tokens = tokens;
            _diagnostics = diagnostics;
            _source = source;
        }

        private SyntaxToken Current => _tokens[_position];

        private SyntaxToken Peek(int offset)
        {
            int index = _position + offset;
            return index < _tokens.Count ? _tokens[index] : _tokens[_tokens.Count - 1];
        }

        /// <summary>
        /// 最近一次真正被消费（Advance）的 token 结束位置，PEVT1005 的同行判断依据它而不是语句节点的 <see cref="SyntaxNode.Span"/>。
        /// 缺失 token 从不经过 <see cref="Advance"/>，因此恢复路径不会把这个位置拖到下一行造成误报。
        /// </summary>
        private int _lastConsumedTokenEnd;

        private SyntaxToken Advance()
        {
            SyntaxToken token = Current;
            if (token.Kind != SyntaxKind.EndOfFileToken)
            {
                _position++;
                _lastConsumedTokenEnd = token.Span.End;
            }
            return token;
        }

        private bool Check(SyntaxKind kind) => Current.Kind == kind;

        private SyntaxToken Expect(SyntaxKind kind, string diagnosticId)
        {
            if (Check(kind))
                return Advance();

            ReportError(diagnosticId, Current.Span);
            return SyntaxToken.CreateMissing(kind, Current.Span.Start);
        }

        private void ReportError(string diagnosticId, TextSpan span) =>
            _diagnostics.AddFromCatalog(diagnosticId, _source.GetLocation(span));

        private int LineOf(SyntaxToken token) => _source.GetLocation(token.Span).StartLine;

        // ---- expressions ----

        /// <summary>
        /// 8.8 节：先取第一个操作数，再反复读"二元运算符 + 下一个操作数"。没有运算符时直接返回那一个操作数，
        /// 只有真正出现至少一个运算符才产出 <see cref="ChainedBinaryExpressionSyntax"/>。
        /// </summary>
        public ExpressionSyntax ParseExpression()
        {
            ExpressionSyntax first = ParseFirstOperand();
            List<BinaryChainSegment> segments = null;

            while (IsBinaryOperator(Current.Kind))
            {
                SyntaxToken operatorToken = Advance();
                ExpressionSyntax operand = ParseRequiredOperand("PEVT5003");
                (segments ??= new List<BinaryChainSegment>()).Add(new BinaryChainSegment(operatorToken, operand));
            }

            return segments == null ? first : new ChainedBinaryExpressionSyntax(first, segments);
        }

        /// <summary>
        /// 表达式的第一个操作数要单独处理"二元运算符左侧根本没有操作数"（PEVT5002）。这里不消费该 token，
        /// 让外层的二元链循环把它当成链的第一个运算符，避免退化成泛泛的 PEVT5001 并连带吞掉右侧操作数。
        /// </summary>
        private ExpressionSyntax ParseFirstOperand()
        {
            // MinusToken 既是二元减法，也是 8.4 节允许的一元取负前缀，所以在"读取第一个操作数"这个位置上裸露的 "-" 永远合法。
            // 真正的二元运算符（*、==、& 等）出现在这里才是 PEVT5002。
            if (Current.Kind != SyntaxKind.MinusToken && IsBinaryOperator(Current.Kind))
            {
                ReportError("PEVT5002", Current.Span);
                return new MissingExpressionSyntax(Current.Span.Start);
            }

            return CloseBareIntegerBoundary(ParseOperand());
        }

        /// <summary>二元运算符右侧要求必须存在操作数（PEVT5003）；判断标准与
        /// <see cref="CanStartExpression"/> 一致，避免退化成泛泛的 PEVT5001。</summary>
        private ExpressionSyntax ParseRequiredOperand(string missingOperandDiagnosticId)
        {
            if (!CanStartExpression(Current.Kind))
            {
                ReportError(missingOperandDiagnosticId, Current.Span);
                return new MissingExpressionSyntax(Advance().Span.Start);
            }

            return CloseBareIntegerBoundary(ParseOperand());
        }

        /// <summary>
        /// PEVT5017 的闭合点之一：到这里说明操作数没有被任何一元运算符包裹，裸露的边界量级字面量只能当正数用，超出 int32 范围。
        /// 带一元运算符的情形早已在 <see cref="ParseOperand"/> 内部处理过，不会重复触发。
        /// </summary>
        private ExpressionSyntax CloseBareIntegerBoundary(ExpressionSyntax operand)
        {
            if (operand is LiteralExpressionSyntax literal && IsUnresolvedIntegerBoundaryMagnitude(literal.Token))
                ReportError("PEVT5017", literal.Token.Span);
            return operand;
        }

        private static bool IsBinaryOperator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken
                or SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken
                or SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken
                or SyntaxKind.AmpersandToken or SyntaxKind.PipeToken or SyntaxKind.CaretToken => true,
            _ => false,
        };

        /// <summary>
        /// 每一个"需要读取一个操作数"的位置都在这里：一元 <c>-</c>/<c>!</c> 只在这个位置才有意义
        /// （8.4 节），而且可以连续嵌套（<c>a - -b</c>），因为取到的操作数本身又递归回这个方法。
        /// </summary>
        private ExpressionSyntax ParseOperand()
        {
            if (Check(SyntaxKind.MinusToken) || Check(SyntaxKind.ExclamationToken))
            {
                SyntaxToken operatorToken = Advance();
                ExpressionSyntax operand = ParseOperand();

                if (operatorToken.Kind == SyntaxKind.MinusToken)
                    operand = CloseUnaryMinusOperandIntegerBoundary(operatorToken, operand);
                else
                    operand = CloseBareIntegerBoundary(operand); // "!" 从不把边界量级收作 int.MinValue，裸值一律越界。

                return new UnaryExpressionSyntax(operatorToken, operand);
            }

            return ParsePrimaryOperand();
        }

        /// <summary>
        /// PEVT5017 的闭合点：词法阶段对裸露的 2147483648 既不报错也不产出 <see cref="TokenValue"/>，
        /// 只有解析器知道紧邻的一元负号是否真的只有一层。恰好一层时重建带正确值的字面量 token（合法 int.MinValue），
        /// 双重取负则会再次越界，必须在这里报 PEVT5017。
        /// </summary>
        private ExpressionSyntax CloseUnaryMinusOperandIntegerBoundary(SyntaxToken minusToken, ExpressionSyntax operand)
        {
            if (operand is LiteralExpressionSyntax literal && IsUnresolvedIntegerBoundaryMagnitude(literal.Token))
            {
                SyntaxToken resolved = new SyntaxToken(literal.Token.Kind, literal.Token.Span, literal.Token.Text,
                    TokenValue.FromInteger(int.MinValue), literal.Token.LeadingTrivia, literal.Token.TrailingTrivia);
                return new LiteralExpressionSyntax(resolved);
            }

            if (IsResolvedIntegerBoundaryNegation(operand))
                ReportError("PEVT5017", TextSpan.FromBounds(minusToken.Span.Start, operand.Span.End));

            return operand;
        }

        private static bool IsUnresolvedIntegerBoundaryMagnitude(SyntaxToken token) =>
            token.Kind == SyntaxKind.IntegerLiteralToken && token.Value.Kind == TokenValueKind.None && token.Text == "2147483648";

        private static bool IsResolvedIntegerBoundaryNegation(ExpressionSyntax expression) =>
            expression is UnaryExpressionSyntax unary && unary.OperatorToken.Kind == SyntaxKind.MinusToken
                && unary.Operand is LiteralExpressionSyntax literal && literal.Token.Text == "2147483648";

        private ExpressionSyntax ParsePrimaryOperand()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.IntegerLiteralToken:
                    // 这里故意不判断边界量级字面量（2147483648）：本位置区分不了"外面正好只有一层一元负号"与"完全裸露使用"。
                    // 闭合逻辑统一放在 ParseOperand/ParseFirstOperand/ParseRequiredOperand 三处真正知道上下文的地方。
                    return new LiteralExpressionSyntax(Advance());
                case SyntaxKind.FloatLiteralToken:
                case SyntaxKind.CharLiteralToken:
                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                    return new LiteralExpressionSyntax(Advance());

                case SyntaxKind.IdentifierToken:
                    return ParseIdentifierStartedOperand();

                case SyntaxKind.OpenParenToken:
                    return ParseParenthesizedOrConversion();

                case SyntaxKind.AtToken:
                    return ParseBuiltinCall();

                case SyntaxKind.AwaitKeyword:
                    return ParseAwaitOperand();

                case SyntaxKind.StatusKeyword:
                    return ParseStatus();

                case SyntaxKind.DollarRawToken:
                    return ParseRawCsExpression();

                // callevt/exec 各有专属的合法位置（独立语句、handler 初始化器），到达这里说明它们被用在了普通表达式位置，
                // 分别是 PEVT7304/7404。仍然把调用结构解析出来再报错，好让恢复继续沿用同一套语句同步点。
                case SyntaxKind.CallEvtKeyword:
                {
                    ExpressionSyntax call = ParseEventCallExpression();
                    ReportError("PEVT7304", call.Span);
                    return call;
                }

                case SyntaxKind.ExecKeyword:
                {
                    ExpressionSyntax exec = ParseExecCallCore();
                    ReportError("PEVT7404", exec.Span);
                    return exec;
                }

                default:
                    ReportError("PEVT5001", Current.Span);
                    return new MissingExpressionSyntax(Advance().Span.Start);
            }
        }

        private ExpressionSyntax ParseIdentifierStartedOperand()
        {
            SyntaxToken name = Advance();

            // 8.9 节只允许全小写的 true/false，而关键字匹配区分大小写，所以 True/FALSE/FaLsE 在词法阶段已变成普通标识符。
            // 语言把这两个拼写保留给布尔字面量，不允许它们被当成"碰巧同名的变量"静默放行（PEVT5023）。
            if (IsCaseVariantBooleanSpelling(name.Text))
            {
                ReportError("PEVT5023", name.Span);
                return new LiteralExpressionSyntax(name);
            }

            if (Check(SyntaxKind.OpenParenToken))
                // 语法层面 "标识符(...)" 一律按自定义事件块调用搭建节点；有没有 "_" 前缀、是否对应
                // 一个真实存在的块定义，都是需要跨源码查找定义的语义问题，留给后续阶段核对。
                return new CustomBlockCallExpressionSyntax(name, ParseArgumentList());

            var nameExpression = new NameExpressionSyntax(name);

            // 8.5 节：赋值语句只能独立成句（PEVT5022）；顶层赋值在 ParseStatement 就走了 ParseAssignmentStatement，
            // 到这里说明 "=" 出现在表达式内部。仍然把右侧表达式消费掉，避免它被外层当成一条新语句再报一次错。
            if (Check(SyntaxKind.EqualsToken))
            {
                SyntaxToken equalsToken = Advance();
                ReportError("PEVT5022", TextSpan.FromBounds(name.Span.Start, equalsToken.Span.End));
                ParseExpression();
            }

            return nameExpression;
        }

        /// <summary>
        /// 精确大小写判断：合法的 true/false 在词法阶段已经变成关键字 token，不会走到这个方法，
        /// 所以这里只匹配 True、FALSE 这类字母组成相同的变体，既不用忽略大小写的比较，也不静默接受它们。
        /// </summary>
        private static bool IsCaseVariantBooleanSpelling(string text) =>
            !string.Equals(text, "true", StringComparison.Ordinal) &&
            !string.Equals(text, "false", StringComparison.Ordinal) &&
            (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(text, "false", StringComparison.OrdinalIgnoreCase));

        private ExpressionSyntax ParseParenthesizedOrConversion()
        {
            if ((Peek(1).Kind == SyntaxKind.FloatKeyword || Peek(1).Kind == SyntaxKind.StringKeyword) && Peek(2).Kind == SyntaxKind.CloseParenToken)
                return ParseConversion();

            SyntaxToken open = Advance();
            if (Check(SyntaxKind.CloseParenToken))
            {
                ReportError("PEVT5016", TextSpan.FromBounds(open.Span.Start, Current.Span.End));
                SyntaxToken emptyClose = Advance();
                return new ParenthesizedExpressionSyntax(open, new MissingExpressionSyntax(emptyClose.Span.Start), emptyClose);
            }

            ExpressionSyntax inner = ParseExpression();
            SyntaxToken close = Expect(SyntaxKind.CloseParenToken, "PEVT5015");
            return new ParenthesizedExpressionSyntax(open, inner, close);
        }

        /// <summary>8.3 节：转换标记必须紧贴变量名（PEVT5014），目标只能是裸变量（PEVT5013）。</summary>
        private ExpressionSyntax ParseConversion()
        {
            SyntaxToken open = Advance();
            SyntaxToken targetType = Advance();
            SyntaxToken close = Advance();

            if (!Check(SyntaxKind.IdentifierToken))
            {
                ReportError("PEVT5013", Current.Span);
                return new ConversionExpressionSyntax(open, targetType, close, SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start));
            }

            bool adjacent = Current.LeadingTrivia.Count == 0 && Current.Span.Start == close.Span.End;
            if (!adjacent)
                ReportError("PEVT5014", Current.Span);

            return new ConversionExpressionSyntax(open, targetType, close, Advance());
        }

        private ExpressionSyntax ParseBuiltinCall()
        {
            SyntaxToken at = Advance();
            SyntaxToken name = Expect(SyntaxKind.IdentifierToken, "PEVT7001");

            // 11.1 节："@" 后必须紧跟内置事件语句名称"——名称已经存在（否则上面已经报了更精确的
            // PEVT7001）但和 "@" 之间隔着空白或注释时，调用整体的形状本身就不合法：PEVT7003。
            if (!name.IsMissing && (name.LeadingTrivia.Count > 0 || name.Span.Start != at.Span.End))
                ReportError("PEVT7003", TextSpan.FromBounds(at.Span.Start, name.Span.End));

            return new BuiltinCallExpressionSyntax(at, name, ParseArgumentList());
        }

        /// <summary>
        /// 15.3/15.6 节共用入口：紧跟 <c>all</c>/<c>any</c> 走集合等待，紧跟其他标识符加左括号是集合等待模式拼错了（PEVT7216），
        /// 其余情形是单句柄形式。拼错时仍按集合等待的形状继续解析，避免恢复时连锁报错。
        /// </summary>
        private ExpressionSyntax ParseAwaitOperand()
        {
            if (Peek(1).Kind == SyntaxKind.AllKeyword || Peek(1).Kind == SyntaxKind.AnyKeyword)
            {
                SyntaxToken awaitKeyword = Advance();
                SyntaxToken mode = Advance();
                IdentifierListSyntax handles = ParseAggregateHandleList();
                IdentifierListSyntax bindings = ParseAggregateBindingList();
                return new AggregateAwaitExpressionSyntax(awaitKeyword, mode, handles, bindings);
            }

            if (Peek(1).Kind == SyntaxKind.IdentifierToken && Peek(2).Kind == SyntaxKind.OpenParenToken)
            {
                SyntaxToken awaitKeyword = Advance();
                ReportError("PEVT7216", Current.Span);
                SyntaxToken mode = Advance();
                IdentifierListSyntax handles = ParseAggregateHandleList();
                IdentifierListSyntax bindings = ParseAggregateBindingList();
                return new AggregateAwaitExpressionSyntax(awaitKeyword, mode, handles, bindings);
            }

            return ParseAwait();
        }

        private ExpressionSyntax ParseAwait()
        {
            SyntaxToken keyword = Advance();
            SyntaxToken handle = Expect(SyntaxKind.IdentifierToken, "PEVT7212");
            return new AwaitExpressionSyntax(keyword, handle);
        }

        /// <summary>句柄列表 <c>(a, b, c)</c>：至少一个句柄，空列表是 PEVT7217。</summary>
        private IdentifierListSyntax ParseAggregateHandleList()
        {
            SyntaxToken open = Expect(SyntaxKind.OpenParenToken, "PEVT7217");
            var identifiers = new List<SyntaxToken>();
            var commas = new List<SyntaxToken>();
            if (Check(SyntaxKind.IdentifierToken))
            {
                identifiers.Add(Advance());
                while (Check(SyntaxKind.CommaToken))
                {
                    commas.Add(Advance());
                    identifiers.Add(Expect(SyntaxKind.IdentifierToken, "PEVT7218"));
                }
            }
            else
            {
                ReportError("PEVT7217", Current.Span);
            }

            SyntaxToken close = Expect(SyntaxKind.CloseParenToken, "PEVT7217");
            return new IdentifierListSyntax(open, identifiers, commas, close);
        }

        /// <summary>结果绑定列表 <c>(resultA, resultB)</c>：允许为空 <c>()</c>（放弃全部返回值），
        /// 但整组括号本身必须存在（PEVT7220）。</summary>
        private IdentifierListSyntax ParseAggregateBindingList()
        {
            if (!Check(SyntaxKind.OpenParenToken))
            {
                ReportError("PEVT7220", Current.Span);
                SyntaxToken missingOpen = SyntaxToken.CreateMissing(SyntaxKind.OpenParenToken, Current.Span.Start);
                SyntaxToken missingClose = SyntaxToken.CreateMissing(SyntaxKind.CloseParenToken, Current.Span.Start);
                return new IdentifierListSyntax(missingOpen, new List<SyntaxToken>(), new List<SyntaxToken>(), missingClose);
            }

            SyntaxToken open = Advance();
            var identifiers = new List<SyntaxToken>();
            var commas = new List<SyntaxToken>();
            if (!Check(SyntaxKind.CloseParenToken))
            {
                identifiers.Add(Expect(SyntaxKind.IdentifierToken, "PEVT7222"));
                while (Check(SyntaxKind.CommaToken))
                {
                    commas.Add(Advance());
                    identifiers.Add(Expect(SyntaxKind.IdentifierToken, "PEVT7222"));
                }
            }

            SyntaxToken close = Expect(SyntaxKind.CloseParenToken, "PEVT7222");
            return new IdentifierListSyntax(open, identifiers, commas, close);
        }

        /// <summary>10.1 节：<c>callevt "事件ID"</c>。目标存在性/异步性只在运行时解析（10.4 节）。</summary>
        private ExpressionSyntax ParseEventCallExpression()
        {
            SyntaxToken callEvtKeyword = Advance();
            SyntaxToken target = ParseEventCallTarget();
            return new EventCallExpressionSyntax(callEvtKeyword, target);
        }

        /// <summary>区分"根本没有目标"（PEVT7301）与"目标是变量/表达式而非字符串字面量"（PEVT7303）——
        /// 后者仍把那个动态值当表达式消费掉做恢复，与 <see cref="ParseOrphanTokenWithExpression"/> 同一思路。</summary>
        private SyntaxToken ParseEventCallTarget()
        {
            if (!Check(SyntaxKind.StringLiteralToken))
            {
                bool dynamicTargetPresent = CanStartExpression(Current.Kind);
                ReportError(dynamicTargetPresent ? "PEVT7303" : "PEVT7301", Current.Span);
                if (dynamicTargetPresent)
                    ParseExpression();
                return SyntaxToken.CreateMissing(SyntaxKind.StringLiteralToken, Current.Span.Start);
            }

            SyntaxToken target = Advance();
            string content = target.Value.AsString;
            if (content.Length == 0 || !IsValidEventIdContent(content))
                ReportError("PEVT7302", target.Span);
            return target;
        }

        /// <summary>
        /// 2 节事件 ID 字符规则：ASCII 字母数字，或按 Unicode <c>Unified_Ideograph</c> 属性识别的中文汉字，
        /// 不用更宽泛的 OtherLetter 类别（那会把假名、韩文也放行）。必须按标量值逐个判断，因为补充平面的汉字需要一对代理项 char。
        /// </summary>
        private static bool IsValidEventIdContent(string content)
        {
            int i = 0;
            while (i < content.Length)
            {
                char c = content[i];
                int codePoint;
                if (char.IsHighSurrogate(c) && i + 1 < content.Length && char.IsLowSurrogate(content[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, content[i + 1]);
                    i += 2;
                }
                else if (char.IsSurrogate(c))
                {
                    return false; // 孤立代理项：不对应任何合法 Unicode 标量值。
                }
                else
                {
                    codePoint = c;
                    i += 1;
                }

                if (!IsValidEventIdCodePoint(codePoint))
                    return false;
            }

            return true;
        }

        private static bool IsValidEventIdCodePoint(int codePoint) =>
            (codePoint >= 'a' && codePoint <= 'z') || (codePoint >= 'A' && codePoint <= 'Z') || (codePoint >= '0' && codePoint <= '9') ||
            IsUnifiedIdeograph(codePoint);

        /// <summary>
        /// Unicode 17.0 <c>PropList.txt</c> 中 <c>Unified_Ideograph=Yes</c> 的精确区段与单点。
        /// </summary>
        private static bool IsUnifiedIdeograph(int codePoint) =>
            (codePoint >= 0x3400 && codePoint <= 0x4DBF) ||   // CJK 统一表意文字扩展 A
            (codePoint >= 0x4E00 && codePoint <= 0x9FFF) ||   // CJK 统一表意文字（基本区）
            codePoint == 0xFA0E || codePoint == 0xFA0F || codePoint == 0xFA11 ||
            codePoint == 0xFA13 || codePoint == 0xFA14 || codePoint == 0xFA1F ||
            codePoint == 0xFA21 || codePoint == 0xFA23 || codePoint == 0xFA24 ||
            (codePoint >= 0xFA27 && codePoint <= 0xFA29) ||   // 具有 Unified_Ideograph 属性的兼容汉字
            (codePoint >= 0x20000 && codePoint <= 0x2A6DF) || // 扩展 B
            (codePoint >= 0x2A700 && codePoint <= 0x2B81D) || // 扩展 C/D
            (codePoint >= 0x2B820 && codePoint <= 0x2CEAD) || // 扩展 E
            (codePoint >= 0x2CEB0 && codePoint <= 0x2EBE0) || // 扩展 F
            (codePoint >= 0x2EBF0 && codePoint <= 0x2EE5D) || // 扩展 I
            (codePoint >= 0x30000 && codePoint <= 0x3134A) || // 扩展 G
            (codePoint >= 0x31350 && codePoint <= 0x33479);   // 扩展 H/J

        /// <summary>13.1 节：<c>exec(source)</c>。参数数量校验为 PEVT7401/7402；参数是否为
        /// <c>string</c> 类型（PEVT7403）需要类型绑定，留给后续阶段。</summary>
        private ExpressionSyntax ParseExecCallCore()
        {
            SyntaxToken execKeyword = Advance();
            ArgumentListSyntax arguments = ParseArgumentList();
            if (arguments.Arguments.Count == 0)
                ReportError("PEVT7401", arguments.Span);
            else if (arguments.Arguments.Count > 1)
                ReportError("PEVT7402", arguments.Span);
            return new ExecCallExpressionSyntax(execKeyword, arguments);
        }

        private ExpressionSyntax ParseStatus()
        {
            SyntaxToken keyword = Advance();
            SyntaxToken handle = Expect(SyntaxKind.IdentifierToken, "PEVT7214");
            return new StatusExpressionSyntax(keyword, handle);
        }

        /// <summary>
        /// 12.1 节：<c>$raw cmd</c> 永远不产生值，出现在表达式位置本身就是错误（PEVT8006）；
        /// 这里仍然把 <c>cmd</c> 之后的原始文本块吞掉，让解析在物理行内继续，而不是直接放弃。
        /// </summary>
        private ExpressionSyntax ParseRawCsExpression()
        {
            SyntaxToken dollarRaw = Advance();
            SyntaxToken cs;
            if (Check(SyntaxKind.CmdKeyword))
            {
                // "$raw cmd" 本身语法上完全合法，只是永远不产生值（8006）；把 cmd 消费掉继续往下找
                // 真正的原始文本块，避免额外再报一次"漏写 cs"（PEVT8002）这种同根因的重复诊断。
                SyntaxToken cmd = Advance();
                ReportError("PEVT8006", TextSpan.FromBounds(dollarRaw.Span.Start, cmd.Span.End));
                cs = SyntaxToken.CreateMissing(SyntaxKind.CsKeyword, cmd.Span.End);
            }
            else
            {
                cs = ExpectRawCsTarget(dollarRaw);
            }

            IdentifierListSyntax arguments = Check(SyntaxKind.OpenParenToken) ? ParseIdentifierList() : null;

            SyntaxToken open = Expect(SyntaxKind.TripleQuoteToken, "PEVT8003");
            SyntaxToken content = Check(SyntaxKind.RawContentToken) ? Advance() : SyntaxToken.CreateMissing(SyntaxKind.RawContentToken, Current.Span.Start);
            SyntaxToken close = Expect(SyntaxKind.TripleQuoteToken, "PEVT8004");
            return new RawCsExpressionSyntax(dollarRaw, cs, arguments, open, content, close);
        }

        /// <summary>
        /// 12.1 节：<c>$raw</c> 后必须是 <c>cmd</c> 或 <c>cs</c>，<c>cmd</c> 分支由调用方单独处理过。
        /// 这里只区分"确实没有目标"（PEVT8001）与"目标存在但不是 cs"（PEVT8002）。
        /// </summary>
        private SyntaxToken ExpectRawCsTarget(SyntaxToken dollarRaw)
        {
            if (Check(SyntaxKind.CsKeyword))
                return Advance();

            bool targetPresent = !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(dollarRaw);
            ReportError(targetPresent ? "PEVT8002" : "PEVT8001", Current.Span);
            return SyntaxToken.CreateMissing(SyntaxKind.CsKeyword, Current.Span.Start);
        }

        private IdentifierListSyntax ParseIdentifierList()
        {
            SyntaxToken open = Advance();
            var identifiers = new List<SyntaxToken> { Expect(SyntaxKind.IdentifierToken, "PEVT8012") };
            var commas = new List<SyntaxToken>();
            while (Check(SyntaxKind.CommaToken))
            {
                commas.Add(Advance());
                identifiers.Add(Expect(SyntaxKind.IdentifierToken, "PEVT8012"));
            }

            SyntaxToken close = Expect(SyntaxKind.CloseParenToken, "PEVT8011");
            return new IdentifierListSyntax(open, identifiers, commas, close);
        }

        /// <summary>
        /// 11.1/14.4 节通用参数列表。某个参数后面既不是 "," 也不是 ")" 时跳到 ")" 或物理行结束，
        /// 保证一次调用最多只为这一处结构性错误报一次 PEVT5015。
        /// </summary>
        private ArgumentListSyntax ParseArgumentList()
        {
            SyntaxToken open = Expect(SyntaxKind.OpenParenToken, "PEVT7004");
            int startLine = LineOf(open);
            var arguments = new List<ExpressionSyntax>();
            var commas = new List<SyntaxToken>();

            if (!Check(SyntaxKind.CloseParenToken) && !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == startLine)
            {
                arguments.Add(ParseExpression());
                while (Check(SyntaxKind.CommaToken))
                {
                    commas.Add(Advance());
                    arguments.Add(ParseExpression());
                }

                if (!Check(SyntaxKind.CloseParenToken))
                {
                    ReportError("PEVT5015", Current.Span);
                    while (!Check(SyntaxKind.CloseParenToken) && !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == startLine)
                        Advance();
                }
            }

            SyntaxToken close = Check(SyntaxKind.CloseParenToken) ? Advance() : SyntaxToken.CreateMissing(SyntaxKind.CloseParenToken, Current.Span.Start);
            return new ArgumentListSyntax(open, arguments, commas, close);
        }
    }
}
