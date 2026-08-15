using System.Collections.Generic;
using System.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 递归下降解析器。本文件是表达式部分：按 8.8 节的线性从左到右规则解析二元链，括号内递归形成
    /// 独立子表达式；不做名称/类型绑定（后续阶段的工作）。语句/文档层面的解析在 Parser.Statements.cs
    /// （同一个 partial class）。<see cref="ParseExpression"/> 是表达式解析的唯一入口，供语句解析复用。
    /// </summary>
    public sealed partial class Parser
    {
        private readonly IReadOnlyList<SyntaxToken> _tokens;
        private readonly DiagnosticBag _diagnostics;
        private readonly SourceText _source;
        private int _position;

        /// <summary>
        /// 当前嵌套打开的自定义事件块，每层只记录"是否声明了返回值类型"——这就是
        /// <c>return</c>（PEVT7105/7106/7107）、块内 <c>end</c>（PEVT7120）和嵌套定义（PEVT7104）
        /// 三处检查唯一需要的上下文，不需要完整符号表。见 Parser.Statements.cs 的块解析部分。
        /// </summary>
        private readonly Stack<bool> _blockStack = new Stack<bool>();

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

        private SyntaxToken Advance()
        {
            SyntaxToken token = Current;
            if (token.Kind != SyntaxKind.EndOfFileToken)
                _position++;
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
        /// 8.8 节：先取第一个操作数，再反复读"二元运算符 + 下一个操作数"；没有运算符时直接返回
        /// 那一个操作数本身，不额外包一层链节点——只有真正出现至少一个运算符才产出
        /// <see cref="ChainedBinaryExpressionSyntax"/>，这样 "a + b * c" 天然是一个两段的平铺链。
        /// </summary>
        public ExpressionSyntax ParseExpression()
        {
            ExpressionSyntax first = ParseOperand();
            List<BinaryChainSegment> segments = null;

            while (IsBinaryOperator(Current.Kind))
            {
                SyntaxToken operatorToken = Advance();
                ExpressionSyntax operand = ParseOperand();
                (segments ??= new List<BinaryChainSegment>()).Add(new BinaryChainSegment(operatorToken, operand));
            }

            return segments == null ? first : new ChainedBinaryExpressionSyntax(first, segments);
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
                return new UnaryExpressionSyntax(operatorToken, ParseOperand());
            }

            return ParsePrimaryOperand();
        }

        private ExpressionSyntax ParsePrimaryOperand()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.IntegerLiteralToken:
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

                // callevt/exec 都有各自专属的合法位置（独立语句、handler 初始化器）；到达这里
                // 说明它们被用在了普通表达式位置，分别是 PEVT7304/7404（10.1/13.1 节）。仍然把
                // 调用结构解析出来再报错，而不是直接放弃，好让恢复继续沿用同一套语句同步点。
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
            if (!Check(SyntaxKind.OpenParenToken))
                return new NameExpressionSyntax(name);

            // 语法层面 "标识符(...)" 一律按自定义事件块调用搭建节点；有没有 "_" 前缀、是否对应
            // 一个真实存在的块定义，都是需要跨源码查找定义的语义问题，留给后续阶段核对。
            return new CustomBlockCallExpressionSyntax(name, ParseArgumentList());
        }

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
            return new BuiltinCallExpressionSyntax(at, name, ParseArgumentList());
        }

        /// <summary>15.3/15.6 节共用入口：紧跟 <c>all</c>/<c>any</c> 走集合等待，否则是单句柄形式。</summary>
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
            if (content.Length == 0 || !content.All(IsValidEventIdCharacter))
                ReportError("PEVT7302", target.Span);
            return target;
        }

        /// <summary>2 节事件 ID 字符规则：ASCII 字母/数字，或 Unicode 中文汉字。汉字用
        /// <see cref="System.Globalization.UnicodeCategory.OtherLetter"/> 近似 Unified_Ideograph——
        /// 常见 CJK 统一表意文字都落在这个类别，足以覆盖本阶段的语法层校验。</summary>
        private static bool IsValidEventIdCharacter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.OtherLetter;

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
                cs = Expect(SyntaxKind.CsKeyword, "PEVT8002");
            }

            IdentifierListSyntax arguments = Check(SyntaxKind.OpenParenToken) ? ParseIdentifierList() : null;

            SyntaxToken open = Expect(SyntaxKind.TripleQuoteToken, "PEVT8003");
            SyntaxToken content = Check(SyntaxKind.RawContentToken) ? Advance() : SyntaxToken.CreateMissing(SyntaxKind.RawContentToken, Current.Span.Start);
            SyntaxToken close = Expect(SyntaxKind.TripleQuoteToken, "PEVT8004");
            return new RawCsExpressionSyntax(dollarRaw, cs, arguments, open, content, close);
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
        /// 11.1/14.4 节通用参数列表。错误恢复的同步点是本阶段唯一的恢复策略：一旦某个参数后面
        /// 既不是 "," 也不是 ")"，就跳到 ")" 或物理行结束为止，保证一次调用最多只为这一处结构性
        /// 错误报一次 PEVT5015，而不会连锁报出一长串误导性的后续错误。
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
