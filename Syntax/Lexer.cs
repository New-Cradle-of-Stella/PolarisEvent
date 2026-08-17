using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 基础词法器：把 SourceText 切成带 trivia 与源码跨度的 token 序列，并完成字面量校验，不解析语法结构也不做绑定。
    /// 语法错误按 <see cref="SyntaxKind.BadToken"/> 恢复，token 序列保持连续，方便后续阶段继续扫描。
    /// </summary>
    public sealed class Lexer
    {
        private readonly SourceText _source;
        private readonly DiagnosticBag _diagnostics;
        private CancellationGate _gate;
        private int _position;
        private readonly Queue<SyntaxToken> _pending = new Queue<SyntaxToken>();
        private int _parenDepth;
        private SyntaxKind _lastRealTokenKind = SyntaxKind.None;
        private int _lastRealTokenEnd;

        public Lexer(SourceText source, DiagnosticBag diagnostics, CancellationToken cancellationToken = default)
        {
            _source = source;
            _diagnostics = diagnostics;
            _gate = new CancellationGate(cancellationToken);
        }

        public static IReadOnlyList<SyntaxToken> Tokenize(SourceText source, DiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
            new Lexer(source, diagnostics, cancellationToken).Tokenize();

        private bool IsAtEnd => _position >= _source.Length;
        private char Current => IsAtEnd ? '\0' : _source[_position];
        private char Peek(int offset = 1) => _position + offset < _source.Length ? _source[_position + offset] : '\0';

        public IReadOnlyList<SyntaxToken> Tokenize()
        {
            var tokens = new List<SyntaxToken>();
            SyntaxToken token;
            do
            {
                token = NextToken();
                tokens.Add(token);
            } while (token.Kind != SyntaxKind.EndOfFileToken);

            return tokens;
        }

        private SyntaxToken NextToken()
        {
            if (_pending.Count > 0)
                return Track(_pending.Dequeue());

            _gate.Tick();
            List<SyntaxTrivia> leading = LexTrivia();

            int start = _position;
            if (IsAtEnd)
                return new SyntaxToken(SyntaxKind.EndOfFileToken, new TextSpan(start, 0), string.Empty, TokenValue.None, leading);

            char c = Current;
            SyntaxToken token = c switch
            {
                _ when c == '_' || IsAsciiLetter(c) => LexIdentifierOrKeyword(leading),
                _ when IsAsciiDigit(c) => LexNumber(leading),
                '"' => LexString(leading),
                '\'' => LexCharOrTripleQuote(leading),
                _ when char.IsLetter(c) => LexInvalidUnicodeIdentifier(leading),
                _ => LexOperatorOrPunctuation(leading),
            };
            return Track(token);
        }

        /// <summary>
        /// 每个真正产出的 token（跳过 EOF）都要在这里登记：更新相邻 token 判定所需的状态
        /// （PEVT1006 悬空运算符、PEVT8016 raw 块左侧紧贴）。<see cref="_pending"/> 里预先算好的
        /// token 同样要经过这里。
        /// </summary>
        private SyntaxToken Track(SyntaxToken token)
        {
            if (token.Kind == SyntaxKind.EndOfFileToken)
                return token;

            _lastRealTokenKind = token.Kind;
            _lastRealTokenEnd = token.Span.End;
            return token;
        }

        // ---- trivia ----

        private List<SyntaxTrivia> LexTrivia()
        {
            var trivia = new List<SyntaxTrivia>();
            while (!IsAtEnd)
            {
                char c = Current;
                if (c == ' ' || c == '\t')
                    trivia.Add(LexRun(TriviaKind.Whitespace, ch => ch == ' ' || ch == '\t'));
                else if (c == '\r' || c == '\n')
                    trivia.Add(LexLineBreak());
                else if (c == '/' && Peek() == '/')
                    trivia.Add(LexLineComment());
                else if (c == '/' && Peek() == '*')
                    trivia.Add(LexBlockComment());
                else
                    break;
            }

            return trivia;
        }

        private SyntaxTrivia LexRun(TriviaKind kind, System.Func<char, bool> isMember)
        {
            int start = _position;
            while (!IsAtEnd && isMember(Current))
                _position++;
            return MakeTrivia(kind, start);
        }

        private SyntaxTrivia LexLineBreak()
        {
            int start = _position;
            ConsumeLineBreak();
            var span = TextSpan.FromBounds(start, _position);

            // 未闭合的括号，或紧跟在"还需要右操作数"的运算符之后换行，都不是 8.9 节允许的续行形式：PEVT1006。
            // 字符串续接的换行由 TryContinueMultilineString 单独消化，不会走到这里。
            if (_parenDepth > 0)
                ReportError("PEVT1006", span);
            else if (ImpliesContinuation(_lastRealTokenKind))
                ReportError("PEVT1006", span);

            return MakeTrivia(TriviaKind.LineBreak, start);
        }

        private void ConsumeLineBreak()
        {
            if (Current == '\r')
            {
                _position++;
                if (!IsAtEnd && Current == '\n')
                    _position++;
            }
            else
            {
                _position++;
            }
        }

        private static bool ImpliesContinuation(SyntaxKind kind) => kind switch
        {
            SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken
                or SyntaxKind.EqualsToken or SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken
                or SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken
                or SyntaxKind.AmpersandToken or SyntaxKind.PipeToken or SyntaxKind.CaretToken or SyntaxKind.ExclamationToken
                or SyntaxKind.ColonToken or SyntaxKind.CommaToken => true,
            _ => false,
        };

        private SyntaxTrivia LexLineComment()
        {
            int start = _position;
            _position += 2; // "//"
            while (!IsAtEnd && Current != '\r' && Current != '\n')
                _position++;
            return MakeTrivia(TriviaKind.LineComment, start);
        }

        private SyntaxTrivia LexBlockComment()
        {
            int start = _position;
            _position += 2; // "/*"
            while (!IsAtEnd)
            {
                _gate.Tick();
                if (Current == '*' && Peek() == '/')
                {
                    _position += 2;
                    return MakeTrivia(TriviaKind.BlockComment, start);
                }

                _position++;
            }

            ReportError("PEVT1010", TextSpan.FromBounds(start, _position));
            return MakeTrivia(TriviaKind.BlockComment, start);
        }

        private SyntaxTrivia MakeTrivia(TriviaKind kind, int start)
        {
            var span = TextSpan.FromBounds(start, _position);
            return new SyntaxTrivia(kind, span, _source.GetText(span));
        }

        // ---- identifiers, keywords ----

        private SyntaxToken LexIdentifierOrKeyword(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            _position++; // 已知首字符是 ASCII 字母或 '_'
            while (!IsAtEnd && (IsAsciiLetter(Current) || IsAsciiDigit(Current) || Current == '_'))
                _position++;

            var span = TextSpan.FromBounds(start, _position);
            string text = _source.GetText(span);

            if (SyntaxFacts.TryGetKeywordKind(text, out SyntaxKind keywordKind))
            {
                TokenValue value = keywordKind == SyntaxKind.TrueKeyword ? TokenValue.FromBoolean(true)
                    : keywordKind == SyntaxKind.FalseKeyword ? TokenValue.FromBoolean(false)
                    : TokenValue.None;

                // "cs" 同时也是 enable cs 的能力名——只有紧跟在 $raw 后面的 cmd/cs 才可能是漏写 raw
                // 块，enable cs 后面本来就不应该有 raw 块，不能套用同一条检查。
                if (_lastRealTokenKind == SyntaxKind.DollarRawToken && (keywordKind == SyntaxKind.CmdKeyword || keywordKind == SyntaxKind.CsKeyword))
                    CheckRawBlockFollows(keywordKind, span);

                return new SyntaxToken(keywordKind, span, text, value, leading);
            }

            return new SyntaxToken(SyntaxKind.IdentifierToken, span, text, TokenValue.None, leading);
        }

        /// <summary>
        /// 12.1/12.2 节：<c>cmd</c> 和不带参数列表的 <c>cs</c> 后面必须紧接着原始文本块。
        /// 带参数列表的形式需要解析器能力才能判断，这里只做前瞻、只处理无歧义的两种情形。
        /// </summary>
        private void CheckRawBlockFollows(SyntaxKind precedingKind, TextSpan precedingSpan)
        {
            int checkpoint = _position;
            SkipTriviaForLookahead();
            bool hasTripleQuote = !IsAtEnd && Current == '\'' && Peek() == '\'' && Peek(2) == '\'';
            bool hasArgListStart = !IsAtEnd && Current == '(';
            _position = checkpoint;

            if (!hasTripleQuote && !(precedingKind == SyntaxKind.CsKeyword && hasArgListStart))
                ReportError("PEVT8003", precedingSpan);
        }

        private void SkipTriviaForLookahead()
        {
            while (!IsAtEnd)
            {
                if (Current == ' ' || Current == '\t' || Current == '\r' || Current == '\n')
                {
                    _position++;
                }
                else if (Current == '/' && Peek() == '/')
                {
                    while (!IsAtEnd && Current != '\r' && Current != '\n')
                        _position++;
                }
                else if (Current == '/' && Peek() == '*')
                {
                    _position += 2;
                    while (!IsAtEnd && !(Current == '*' && Peek() == '/'))
                        _position++;
                    if (!IsAtEnd)
                        _position += 2;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 普通标识符只允许 ASCII（9.5 节）。遇到 Unicode 字母开头但不是 ASCII 字母时，仍按标识符
        /// 形状吞掉整段（字母/数字/下划线），报告 PEVT1008，而不是把每个字符都单独报成 PEVT1001。
        /// </summary>
        private SyntaxToken LexInvalidUnicodeIdentifier(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
                _position++;

            var span = TextSpan.FromBounds(start, _position);
            ReportError("PEVT1008", span);
            return new SyntaxToken(SyntaxKind.IdentifierToken, span, _source.GetText(span), TokenValue.None, leading);
        }

        // ---- numbers ----

        private SyntaxToken LexNumber(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            while (!IsAtEnd && IsAsciiDigit(Current))
                _position++;

            bool isFloat = false;
            if (!IsAtEnd && Current == '.' && IsAsciiDigit(Peek()))
            {
                isFloat = true;
                _position++;
                while (!IsAtEnd && IsAsciiDigit(Current))
                    _position++;
            }

            // 拒绝悬空小数点（"1."）、指数、类型后缀和数字分隔符等 8.9 节禁止的形式：PEVT5019。
            if (!IsAtEnd && (Current == '.' || Current == '_' || Current == 'e' || Current == 'E' || IsAsciiLetter(Current)))
            {
                while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '.' || Current == '_'))
                    _position++;
                return MakeBad("PEVT5019", start, leading);
            }

            var span = TextSpan.FromBounds(start, _position);
            string text = _source.GetText(span);
            return isFloat ? LexFloatLiteral(span, text, leading) : LexIntegerLiteral(span, text, leading);
        }

        /// <summary>
        /// int32 补码范围非对称：裸露的 2147483648 既可能是超范围正数，也可能是 -2147483648 的一部分，
        /// 因此这一个边界幅值既不报错也不产出 Value，留给解析器定夺；严格大于它的数字一律报 PEVT5017。
        /// </summary>
        private SyntaxToken LexIntegerLiteral(TextSpan span, string text, IReadOnlyList<SyntaxTrivia> leading)
        {
            if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong magnitude) || magnitude > 2147483648UL)
            {
                ReportError("PEVT5017", span);
                return new SyntaxToken(SyntaxKind.IntegerLiteralToken, span, text, TokenValue.None, leading);
            }

            if (magnitude == 2147483648UL)
                return new SyntaxToken(SyntaxKind.IntegerLiteralToken, span, text, TokenValue.None, leading);

            return new SyntaxToken(SyntaxKind.IntegerLiteralToken, span, text, TokenValue.FromInteger((int)magnitude), leading);
        }

        private SyntaxToken LexFloatLiteral(TextSpan span, string text, IReadOnlyList<SyntaxTrivia> leading)
        {
            if (!float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float value) || float.IsInfinity(value))
            {
                ReportError("PEVT5018", span);
                return new SyntaxToken(SyntaxKind.FloatLiteralToken, span, text, TokenValue.None, leading);
            }

            return new SyntaxToken(SyntaxKind.FloatLiteralToken, span, text, TokenValue.FromFloat(value), leading);
        }

        // ---- strings, chars, raw triple-quote ----

        private SyntaxToken LexString(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            _position++; // 起始 "
            (string firstValue, bool hasEscapeError, bool terminated) = ScanStringBody();

            if (!terminated)
                return MakeBad("PEVT1002", start, leading);

            int firstColumn = _source.GetLocation(new TextSpan(start, 1)).StartColumn;
            (string value, bool hasError, int end) = TryContinueMultilineString(firstValue, hasEscapeError, firstColumn);

            var span = TextSpan.FromBounds(start, end);
            string text = _source.GetText(span);
            return hasError
                ? new SyntaxToken(SyntaxKind.StringLiteralToken, span, text, TokenValue.None, leading)
                : new SyntaxToken(SyntaxKind.StringLiteralToken, span, text, TokenValue.FromString(value), leading);
        }

        /// <summary>扫描一段引号内容（假设 <c>_position</c> 已经越过起始双引号），直到闭合引号或行尾/文件尾。</summary>
        private (string Value, bool HasEscapeError, bool Terminated) ScanStringBody()
        {
            var decoded = new StringBuilder();
            bool sawEscapeError = false;
            while (!IsAtEnd && Current != '"' && Current != '\r' && Current != '\n')
            {
                if (Current == '\\')
                {
                    if (TryLexEscape(isCharLiteral: false, out char escaped))
                        decoded.Append(escaped);
                    else
                        sawEscapeError = true;
                }
                else
                {
                    decoded.Append(Current);
                    _position++;
                }
            }

            bool terminated = !IsAtEnd && Current == '"';
            if (terminated)
                _position++; // 结尾 "
            return (decoded.ToString(), sawEscapeError, terminated);
        }

        /// <summary>
        /// 8.9 节字符串加号续接：一旦触发，加号、换行和续接行开头都由这里消化掉，绝不留下裸 "+" 让外层重复报 PEVT1006。
        /// 失败时只消费到能判定失败为止，不吞掉续接行本身。
        /// </summary>
        private (string Value, bool HasError, int End) TryContinueMultilineString(string firstValue, bool hasEscapeError, int firstColumn)
        {
            string value = firstValue;
            bool hasError = hasEscapeError;

            while (true)
            {
                int checkpoint = _position;
                while (!IsAtEnd && Current == ' ')
                    _position++;

                if (IsAtEnd || Current != '+')
                {
                    _position = checkpoint; // 同一行 "a" + "b" 不是这条语法糖，交还给普通 dispatch。
                    return (value, hasError, _position);
                }

                int plusStart = _position;
                _position++; // '+'
                while (!IsAtEnd && Current == ' ')
                    _position++;

                if (!IsAtEnd && Current != '\r' && Current != '\n')
                {
                    _position = checkpoint; // '+' 后面还有别的东西，不是续接触发形状，按普通二元 + 处理。
                    return (value, hasError, _position);
                }

                if (IsAtEnd)
                {
                    ReportError("PEVT1013", TextSpan.FromBounds(plusStart, _position));
                    return (value, hasError, _position);
                }

                ConsumeLineBreak();
                int lineStart = _position;
                int probe = lineStart;
                while (probe < _source.Length && _source[probe] == ' ')
                    probe++;

                if (probe >= _source.Length)
                {
                    ReportError("PEVT1013", TextSpan.FromBounds(lineStart, probe));
                    return (value, hasError, probe);
                }

                if (_source[probe] == '\t' || _source[probe] != '"')
                {
                    ReportError("PEVT1012", TextSpan.FromBounds(lineStart, probe + 1));
                    return (value, hasError, lineStart);
                }

                int column = _source.GetLocation(new TextSpan(probe, 1)).StartColumn;
                if (column != firstColumn)
                {
                    ReportError("PEVT1011", new TextSpan(probe, 1));
                    return (value, hasError, lineStart);
                }

                _position = probe + 1; // 跳过续接段的起始 "
                (string segmentValue, bool segmentError, bool segmentTerminated) = ScanStringBody();
                if (!segmentTerminated)
                {
                    ReportError("PEVT1002", TextSpan.FromBounds(probe, _position));
                    return (value, true, _position);
                }

                value += "\n" + segmentValue;
                hasError = hasError || segmentError;
                // 继续循环：这一段末尾可能还有下一个 "行末 + " 续接请求。
            }
        }

        private SyntaxToken LexCharOrTripleQuote(IReadOnlyList<SyntaxTrivia> leading)
        {
            if (Peek() == '\'' && Peek(2) == '\'')
                return LexTripleQuoteAndRawBlock(leading);

            return LexCharLiteral(leading);
        }

        /// <summary>
        /// 12.1/12.2/12.4 节的原始文本块：开始分隔符必须紧贴 cmd/cs 或参数列表右括号（PEVT8016），内容原样保留，不闭合则 PEVT8004。
        /// 一次调用产出三个 token，内容与结束分隔符先放进 <see cref="_pending"/>。
        /// </summary>
        private SyntaxToken LexTripleQuoteAndRawBlock(IReadOnlyList<SyntaxTrivia> leading)
        {
            int openStart = _position;
            _position += 3;
            var openSpan = TextSpan.FromBounds(openStart, _position);

            bool adjacencyRequired = _lastRealTokenKind == SyntaxKind.CmdKeyword
                || _lastRealTokenKind == SyntaxKind.CsKeyword
                || _lastRealTokenKind == SyntaxKind.CloseParenToken;
            if (adjacencyRequired && (leading.Count > 0 || openStart != _lastRealTokenEnd))
                ReportError("PEVT8016", openSpan);

            var openToken = new SyntaxToken(SyntaxKind.TripleQuoteToken, openSpan, "'''", TokenValue.None, leading);

            int contentStart = _position;
            var decoded = new StringBuilder();
            while (true)
            {
                if (IsAtEnd)
                {
                    ReportError("PEVT8004", TextSpan.FromBounds(contentStart, _position));
                    break;
                }

                if (Current == '\'' && Peek() == '\'' && Peek(2) == '\'')
                    break; // 结束分隔符，不属于内容

                if (Current == '\\' && Peek() == '\'')
                {
                    if (Peek(2) == '\'' && Peek(3) == '\'')
                    {
                        decoded.Append("'''");
                        _position += 4;
                        continue;
                    }

                    // 反斜杠后至少有一个引号却凑不满三个——显然想转义结束分隔符但形式不对：PEVT8017。
                    int badStart = _position;
                    _position += 2; // '\' 和第一个 '
                    while (!IsAtEnd && Current == '\'')
                        _position++;
                    var badSpan = TextSpan.FromBounds(badStart, _position);
                    ReportError("PEVT8017", badSpan);
                    decoded.Append(_source.GetText(badSpan));
                    continue;
                }

                decoded.Append(Current);
                _position++;
            }

            var contentSpan = TextSpan.FromBounds(contentStart, _position);
            var contentToken = new SyntaxToken(SyntaxKind.RawContentToken, contentSpan, _source.GetText(contentSpan), TokenValue.FromString(decoded.ToString()));

            SyntaxToken closeToken;
            if (IsAtEnd)
            {
                closeToken = SyntaxToken.CreateMissing(SyntaxKind.TripleQuoteToken, _position);
            }
            else
            {
                int closeStart = _position;
                _position += 3;
                closeToken = new SyntaxToken(SyntaxKind.TripleQuoteToken, TextSpan.FromBounds(closeStart, _position), "'''", TokenValue.None);
                CheckNoTrailingRawArgument();
            }

            _pending.Enqueue(contentToken);
            _pending.Enqueue(closeToken);
            return openToken;
        }

        /// <summary>PEVT8005：结束分隔符所在物理行剩余部分，除空白外不应该还有别的内容。</summary>
        private void CheckNoTrailingRawArgument()
        {
            int probe = _position;
            while (probe < _source.Length && (_source[probe] == ' ' || _source[probe] == '\t'))
                probe++;

            if (probe >= _source.Length || _source[probe] == '\r' || _source[probe] == '\n')
                return;

            int extraStart = probe;
            while (probe < _source.Length && _source[probe] != '\r' && _source[probe] != '\n')
                probe++;
            ReportError("PEVT8005", TextSpan.FromBounds(extraStart, probe));
        }

        private SyntaxToken LexCharLiteral(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            _position++; // 起始 '

            var decoded = new StringBuilder();
            bool sawEscapeError = false;
            while (!IsAtEnd && Current != '\'' && Current != '\r' && Current != '\n')
            {
                if (Current == '\\')
                {
                    if (TryLexEscape(isCharLiteral: true, out char escaped))
                        decoded.Append(escaped);
                    else
                        sawEscapeError = true;
                }
                else
                {
                    decoded.Append(Current);
                    _position++;
                }
            }

            if (IsAtEnd || Current != '\'')
                return MakeBad("PEVT1004", start, leading);

            _position++; // 结尾 '
            var span = TextSpan.FromBounds(start, _position);
            string text = _source.GetText(span);

            if (sawEscapeError)
                return new SyntaxToken(SyntaxKind.CharLiteralToken, span, text, TokenValue.None, leading);

            if (decoded.Length != 1)
            {
                ReportError("PEVT5020", span);
                return new SyntaxToken(SyntaxKind.CharLiteralToken, span, text, TokenValue.None, leading);
            }

            return new SyntaxToken(SyntaxKind.CharLiteralToken, span, text, TokenValue.FromChar(decoded[0]), leading);
        }

        /// <summary>字符串允许 <c>\\ \" \n \r \t \0</c>，字符允许 <c>\\ ' \n \r \t \0</c>（8.9 节，两者的引号转义互斥），其他形式一律 PEVT5021。</summary>
        private bool TryLexEscape(bool isCharLiteral, out char decoded)
        {
            int start = _position;
            _position++; // '\\'

            if (IsAtEnd)
            {
                decoded = '\0';
                ReportError("PEVT5021", TextSpan.FromBounds(start, _position));
                return false;
            }

            char escapeChar = Current;
            bool valid = escapeChar switch
            {
                '\\' or 'n' or 'r' or 't' or '0' => true,
                '"' => !isCharLiteral,
                '\'' => isCharLiteral,
                _ => false,
            };
            _position++;

            if (!valid)
            {
                decoded = '\0';
                ReportError("PEVT5021", TextSpan.FromBounds(start, _position));
                return false;
            }

            decoded = escapeChar switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                _ => escapeChar, // '\\'、'"'、'\''
            };
            return true;
        }

        // ---- operators, punctuation, $raw ----

        private SyntaxToken LexOperatorOrPunctuation(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            switch (Current)
            {
                case '+': _position++; return Make(SyntaxKind.PlusToken, start, leading);
                case '-': _position++; return Make(SyntaxKind.MinusToken, start, leading);
                case '*': _position++; return Make(SyntaxKind.StarToken, start, leading);
                case '/': _position++; return Make(SyntaxKind.SlashToken, start, leading);
                case '%': _position++; return Make(SyntaxKind.PercentToken, start, leading);
                case '(': _position++; _parenDepth++; return Make(SyntaxKind.OpenParenToken, start, leading);
                case ')': _position++; if (_parenDepth > 0) _parenDepth--; return Make(SyntaxKind.CloseParenToken, start, leading);
                case ':': _position++; return Make(SyntaxKind.ColonToken, start, leading);
                case ',': _position++; return Make(SyntaxKind.CommaToken, start, leading);
                case '@': _position++; return Make(SyntaxKind.AtToken, start, leading);
                case '#': _position++; return Make(SyntaxKind.HashToken, start, leading);
                case '&': _position++; return Make(SyntaxKind.AmpersandToken, start, leading);
                case '|': _position++; return Make(SyntaxKind.PipeToken, start, leading);
                case '^': _position++; return Make(SyntaxKind.CaretToken, start, leading);
                case '=':
                    _position++;
                    if (Current == '=') { _position++; return Make(SyntaxKind.EqualsEqualsToken, start, leading); }
                    return Make(SyntaxKind.EqualsToken, start, leading);
                case '!':
                    _position++;
                    if (Current == '=') { _position++; return Make(SyntaxKind.ExclamationEqualsToken, start, leading); }
                    return Make(SyntaxKind.ExclamationToken, start, leading);
                case '<':
                    _position++;
                    if (Current == '=') { _position++; return Make(SyntaxKind.LessThanEqualsToken, start, leading); }
                    return Make(SyntaxKind.LessThanToken, start, leading);
                case '>':
                    _position++;
                    if (Current == '=') { _position++; return Make(SyntaxKind.GreaterThanEqualsToken, start, leading); }
                    return Make(SyntaxKind.GreaterThanToken, start, leading);
                case ';':
                    _position++;
                    return MakeBad("PEVT1007", start, leading);
                case '{':
                case '}':
                    _position++;
                    return MakeBad("PEVT1003", start, leading);
                case '$':
                    return LexDollarRaw(leading);
                default:
                    _position++;
                    return MakeBad("PEVT1001", start, leading);
            }
        }

        private SyntaxToken LexDollarRaw(IReadOnlyList<SyntaxTrivia> leading)
        {
            int start = _position;
            if (Peek() == 'r' && Peek(2) == 'a' && Peek(3) == 'w')
            {
                _position += 4; // "$raw"
                return Make(SyntaxKind.DollarRawToken, start, leading);
            }

            _position++; // 只消费 '$' 本身
            return MakeBad("PEVT1001", start, leading);
        }

        // ---- shared helpers ----

        private SyntaxToken Make(SyntaxKind kind, int start, IReadOnlyList<SyntaxTrivia> leading)
        {
            var span = TextSpan.FromBounds(start, _position);
            return new SyntaxToken(kind, span, _source.GetText(span), TokenValue.None, leading);
        }

        private SyntaxToken MakeBad(string diagnosticId, int start, IReadOnlyList<SyntaxTrivia> leading)
        {
            var span = TextSpan.FromBounds(start, _position);
            ReportError(diagnosticId, span);
            return new SyntaxToken(SyntaxKind.BadToken, span, _source.GetText(span), TokenValue.None, leading);
        }

        private void ReportError(string diagnosticId, TextSpan span) =>
            _diagnostics.AddFromCatalog(diagnosticId, _source.GetLocation(span));

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
