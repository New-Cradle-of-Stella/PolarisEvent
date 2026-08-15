using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 语句与文档解析：文件头（id/enable）、声明（var/const/赋值）、结构化流程
    /// （if/elif/else/endif、while/endwhile、switch/case/default/endswitch）、标签与 goto、end。
    /// 恢复策略统一以物理行或对应闭合符为同步点（阶段 6 要求），保证一次加载能报告多处错误。
    /// </summary>
    public sealed partial class Parser
    {
        private static readonly SyntaxKind[] IfBodyTerminators = { SyntaxKind.ElifKeyword, SyntaxKind.ElseKeyword, SyntaxKind.EndIfKeyword };
        private static readonly SyntaxKind[] WhileBodyTerminators = { SyntaxKind.EndWhileKeyword };
        // else 的正文遇到 elif/else 也要停下来，好让错位分支恢复（2005/2007）接手，
        // 而不是把它们当成 else 正文里的普通语句、落到孤立闭合符分支报错。
        private static readonly SyntaxKind[] ElseBodyTerminators = { SyntaxKind.EndIfKeyword, SyntaxKind.ElifKeyword, SyntaxKind.ElseKeyword };
        private static readonly SyntaxKind[] SwitchArmTerminators = { SyntaxKind.CaseKeyword, SyntaxKind.DefaultKeyword, SyntaxKind.EndSwitchKeyword };
        private static readonly SyntaxKind[] BlockBodyTerminators = { SyntaxKind.EndBlockKeyword };

        /// <summary>文档级解析入口：<c>id-declaration, {enable}, {event-statement}, EOF</c>（§19）。</summary>
        public DocumentSyntax ParseDocument()
        {
            IdDeclarationSyntax idDeclaration = null;
            bool anyIdSeen = false;

            if (Check(SyntaxKind.IdKeyword))
            {
                idDeclaration = ParseIdDeclaration();
                anyIdSeen = true;
            }

            var seenCapabilities = new HashSet<SyntaxKind>();
            var enables = new List<EnableDeclarationSyntax>();
            while (Check(SyntaxKind.EnableKeyword))
                enables.Add(ParseEnableDeclaration(seenCapabilities));

            var statements = new List<StatementSyntax>();
            while (!Check(SyntaxKind.EndOfFileToken))
            {
                if (Check(SyntaxKind.IdKeyword))
                {
                    IdDeclarationSyntax misplaced = ParseIdDeclaration();
                    ReportError(anyIdSeen ? "PEVT1103" : "PEVT1102", misplaced.Span);
                    anyIdSeen = true;
                    continue;
                }

                if (Check(SyntaxKind.EnableKeyword))
                {
                    ReportError("PEVT1107", Current.Span);
                    ParseEnableDeclaration(new HashSet<SyntaxKind>());
                    continue;
                }

                if (statements.Count > 0)
                    CheckOneStatementPerLine();
                statements.Add(ParseStatement());
            }

            if (!anyIdSeen)
                ReportError("PEVT1101", new TextSpan(0, 0));

            return new DocumentSyntax(idDeclaration, enables, statements, Current);
        }

        private IdDeclarationSyntax ParseIdDeclaration()
        {
            SyntaxToken idKeyword = Advance();
            if (!Check(SyntaxKind.StringLiteralToken))
            {
                bool trulyMissing = Check(SyntaxKind.EndOfFileToken) || LineOf(Current) != LineOf(idKeyword);
                ReportError(trulyMissing ? "PEVT1104" : "PEVT1105", Current.Span);
                SyntaxToken missing = SyntaxToken.CreateMissing(SyntaxKind.StringLiteralToken, Current.Span.Start);
                if (!trulyMissing)
                    SkipRestOfLine(idKeyword); // 消费掉这个错误 token，避免它又被外层循环当成新语句
                return new IdDeclarationSyntax(idKeyword, missing);
            }

            SyntaxToken value = Advance();
            if (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(idKeyword))
            {
                ReportError("PEVT1106", Current.Span);
                SkipRestOfLine(idKeyword);
            }

            // 2 节：事件 ID 不能是空字符串（PEVT1110），也只能包含 ASCII 字母数字或 Unicode 中文汉字
            // （PEVT1111）。转义出错的字符串字面量 Value.Kind 为 None，内容本身不可靠，不在这里重复判断
            // ——那种情形已经由词法阶段的 PEVT5021 覆盖。
            if (value.Value.Kind == TokenValueKind.String)
            {
                string content = value.Value.AsString;
                if (content.Length == 0)
                    ReportError("PEVT1110", value.Span);
                else if (!IsValidEventIdContent(content))
                    ReportError("PEVT1111", value.Span);
            }

            return new IdDeclarationSyntax(idKeyword, value);
        }

        private EnableDeclarationSyntax ParseEnableDeclaration(HashSet<SyntaxKind> seenCapabilities)
        {
            SyntaxToken enableKeyword = Advance();
            if (!Check(SyntaxKind.CsKeyword) && !Check(SyntaxKind.AsyncKeyword))
            {
                ReportError("PEVT1109", Current.Span);
                SyntaxToken missing = SyntaxToken.CreateMissing(SyntaxKind.CsKeyword, Current.Span.Start);
                if (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(enableKeyword))
                    SkipRestOfLine(enableKeyword);
                return new EnableDeclarationSyntax(enableKeyword, missing);
            }

            SyntaxToken capability = Advance();
            if (!seenCapabilities.Add(capability.Kind))
                ReportError("PEVT1108", capability.Span);
            return new EnableDeclarationSyntax(enableKeyword, capability);
        }

        // ---- statement dispatch ----

        private StatementSyntax ParseStatement()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.EndKeyword: return ParseEndOrBlockErrorStatement();
                case SyntaxKind.IfKeyword: return ParseIfStatement();
                case SyntaxKind.WhileKeyword: return ParseWhileStatement();
                case SyntaxKind.SwitchKeyword: return ParseSwitchStatement();
                case SyntaxKind.HashToken: return ParseLabelStatement();
                case SyntaxKind.GotoKeyword: return ParseGotoStatement();
                case SyntaxKind.VarKeyword: return ParseVariableDeclarationStatement();
                case SyntaxKind.ConstKeyword: return ParseConstantDeclarationStatement();
                case SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.EqualsToken: return ParseAssignmentStatement();
                case SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.OpenParenToken:
                    return LooksLikeBlockSignatureMissingKeyword() ? ParseBlockDefinitionStatement(null) : ParseExpressionStatement();
                case SyntaxKind.AtToken: return ParseExpressionStatement();
                case SyntaxKind.AwaitKeyword: return ParseExpressionStatement();
                case SyntaxKind.KillKeyword: return ParseKillStatement();
                case SyntaxKind.HandlerKeyword: return ParseHandlerDeclarationStatement();
                case SyntaxKind.CallEvtKeyword: return new ExpressionStatementSyntax(ParseEventCallExpression());
                case SyntaxKind.ExecKeyword: return new ExpressionStatementSyntax(ParseExecCallCore());
                case SyntaxKind.DollarRawToken: return ParseRawStatement();
                case SyntaxKind.BlockKeyword: return ParseBlockDefinitionStatement(null);
                case SyntaxKind.AsyncKeyword: return ParseAsyncPrefixedStatement();
                case SyntaxKind.ReturnKeyword: return ParseReturnStatement();
                case SyntaxKind.EndBlockKeyword: return ParseOrphanToken("PEVT7118");
                case SyntaxKind.ElifKeyword: return ParseOrphanTokenWithExpression("PEVT2003");
                case SyntaxKind.ElseKeyword: return ParseOrphanToken("PEVT2006");
                case SyntaxKind.EndIfKeyword: return ParseOrphanToken("PEVT2009");
                case SyntaxKind.EndWhileKeyword: return ParseOrphanToken("PEVT2103");
                case SyntaxKind.CaseKeyword: return ParseOrphanTokenWithExpression("PEVT2405");
                case SyntaxKind.DefaultKeyword: return ParseOrphanToken("PEVT2409");
                case SyntaxKind.EndSwitchKeyword: return ParseOrphanToken("PEVT2413");
                default: return ParseUnknownStatement();
            }
        }

        private StatementSyntax ParseExpressionStatement() => new ExpressionStatementSyntax(ParseExpression());

        private StatementSyntax ParseOrphanToken(string diagnosticId)
        {
            SyntaxToken token = Advance();
            ReportError(diagnosticId, token.Span);
            return new UnknownStatementSyntax(token);
        }

        /// <summary>孤立的 <c>elif</c>/<c>case</c> 后面通常还跟着一个表达式；恢复时把它也吞掉，
        /// 否则表达式会单独留下来，被外层循环当成一条全新的、同样莫名其妙的语句再报一次错。</summary>
        private StatementSyntax ParseOrphanTokenWithExpression(string diagnosticId)
        {
            SyntaxToken token = Advance();
            ReportError(diagnosticId, token.Span);
            if (CanStartExpression(Current.Kind))
                ParseExpression();
            return new UnknownStatementSyntax(token);
        }

        /// <summary>
        /// PEVT1201：行首内容不属于任何已定义的事件语句形态。恢复同步点是物理行末尾，
        /// 避免一处陌生语法连锁报出一长串误导性错误。
        /// </summary>
        private StatementSyntax ParseUnknownStatement()
        {
            SyntaxToken start = Current;
            ReportError("PEVT1201", start.Span);
            SkipRestOfLine(start);
            return new UnknownStatementSyntax(start);
        }

        private void SkipRestOfLine(SyntaxToken anchor)
        {
            int line = LineOf(anchor);
            while (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == line)
                Advance();
        }

        private List<StatementSyntax> ParseStatementList(SyntaxKind[] terminators)
        {
            var statements = new List<StatementSyntax>();
            while (!Check(SyntaxKind.EndOfFileToken) && !IsAnyOf(Current.Kind, terminators))
            {
                // 10A-R02：不跳过第一条语句的检测。<see cref="_lastConsumedTokenEnd"/> 在进入这个
                // 列表之前就已经正确反映了外层结构（if/while/switch 的头部条件、case 的表达式等）
                // 真正消费到的位置，因此第一条 body 语句和头部同行时（比如 "if true @a()"）同样能被
                // 检出，不需要单独区分"这是不是列表里的第一条"。
                CheckOneStatementPerLine();
                statements.Add(ParseStatement());
            }

            return statements;
        }

        /// <summary>
        /// PEVT1005（10A 补正）：按完整语句边界判断"同一物理行是否出现了不止一条语句"——只要
        /// 上一条语句真正消费到的最后一个 token（<see cref="_lastConsumedTokenEnd"/>，不是
        /// 语句节点的 <see cref="SyntaxNode.Span"/>：很多恢复路径会把缺失 token 的零长度位置钉在
        /// 隔了一整行的下一个真实 token 上，用 Span.End 会把这一行错误地"拖"过去）所在的物理行，
        /// 和下一条语句起始 token 所在的物理行相同，就报告。覆盖全部语句起始形态（内置调用、赋值、
        /// await、标签、callevt、raw 语句等），不再依赖一份只登记部分关键字的 StatementLeaders 清单。
        ///
        /// 这个基于语句边界而非 token 种类的判定天然不会误报既有的几组"看起来像两个语句起始，
        /// 实际只是同一条语句内部形状"的合法组合——它们从来都只走一次 <see cref="ParseStatement"/>：
        /// <c>async block ...</c>、<c>goto #Label</c>、<c>handler h = callevt ...</c>、
        /// <c>var x : bool = await a</c>。
        /// </summary>
        private void CheckOneStatementPerLine()
        {
            int previousEndLine = _source.GetLocation(new TextSpan(_lastConsumedTokenEnd, 0)).StartLine;
            if (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == previousEndLine)
                ReportError("PEVT1005", Current.Span);
        }

        private static bool IsAnyOf(SyntaxKind kind, SyntaxKind[] candidates)
        {
            foreach (SyntaxKind candidate in candidates)
                if (candidate == kind)
                    return true;
            return false;
        }

        /// <summary>把 <paramref name="kind"/> 期望的闭合关键字消费掉；缺失报 <paramref name="missingId"/>，
        /// 同一物理行上还有多余内容则报 <paramref name="extraArgsId"/>（end/endif/endwhile/endswitch 共用形状）。</summary>
        private SyntaxToken ExpectClosingKeyword(SyntaxKind kind, string missingId, string extraArgsId)
        {
            SyntaxToken token = Expect(kind, missingId);
            if (!token.IsMissing && !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(token))
            {
                ReportError(extraArgsId, Current.Span);
                SkipRestOfLine(token);
            }

            return token;
        }

        // ---- end ----

        /// <summary>14.2/14.3 节：<c>end</c> 只终止文件外层事件；出现在任何自定义事件块内部时
        /// （即使嵌套在块内的 if/while/switch 里）只形成一个错误节点（PEVT7120），不终止块，
        /// 也不消费掉块体继续解析的能力——外层 <see cref="ParseStatementList"/> 循环照常继续。</summary>
        private StatementSyntax ParseEndOrBlockErrorStatement()
        {
            if (_blockStack.Count > 0)
            {
                SyntaxToken end = Advance();
                ReportError("PEVT7120", end.Span);
                return new UnknownStatementSyntax(end);
            }

            return new EndStatementSyntax(ExpectClosingKeyword(SyntaxKind.EndKeyword, "PEVT2201", "PEVT2201"));
        }

        // ---- if/elif/else/endif ----

        /// <summary>流程语句的条件表达式必须存在（PEVT2001/2004/2101/2401）；判断标准与
        /// <see cref="CanStartExpression"/> 一致，避免退化成泛泛的 PEVT5001。不消费任何 token，
        /// 让调用方紧接着按各自的语句列表继续解析，交给统一的恢复机制处理。</summary>
        private ExpressionSyntax ParseRequiredCondition(string missingDiagnosticId)
        {
            if (!CanStartExpression(Current.Kind))
            {
                ReportError(missingDiagnosticId, Current.Span);
                return new MissingExpressionSyntax(Current.Span.Start);
            }

            return ParseExpression();
        }

        /// <summary><c>else</c>/<c>default</c> 后不允许出现表达式或其他参数（PEVT2008/2411）：
        /// 只在关键字所在物理行发现能起始表达式的内容时才报告，报告后跳到行尾，避免这段多余内容
        /// 被外层循环当成一条全新的、同样莫名其妙的语句再报一次错。</summary>
        private void CheckNoTrailingExpression(SyntaxToken keyword, string diagnosticId)
        {
            if (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(keyword) && CanStartExpression(Current.Kind))
            {
                ReportError(diagnosticId, Current.Span);
                SkipRestOfLine(keyword);
            }
        }

        private StatementSyntax ParseIfStatement()
        {
            SyntaxToken ifKeyword = Advance();
            ExpressionSyntax condition = ParseRequiredCondition("PEVT2001");
            List<StatementSyntax> body = ParseStatementList(IfBodyTerminators);
            if (body.Count == 0)
                ReportError("PEVT2301", ifKeyword.Span);

            var elifClauses = new List<ElifClauseSyntax>();
            while (Check(SyntaxKind.ElifKeyword))
            {
                SyntaxToken elifKeyword = Advance();
                ExpressionSyntax elifCondition = ParseRequiredCondition("PEVT2004");
                List<StatementSyntax> elifBody = ParseStatementList(IfBodyTerminators);
                if (elifBody.Count == 0)
                    ReportError("PEVT2302", elifKeyword.Span);
                elifClauses.Add(new ElifClauseSyntax(elifKeyword, elifCondition, elifBody));
            }

            ElseClauseSyntax elseClause = null;
            if (Check(SyntaxKind.ElseKeyword))
            {
                SyntaxToken elseKeyword = Advance();
                CheckNoTrailingExpression(elseKeyword, "PEVT2008");
                List<StatementSyntax> elseBody = ParseStatementList(ElseBodyTerminators);
                if (elseBody.Count == 0)
                    ReportError("PEVT2303", elseKeyword.Span);
                elseClause = new ElseClauseSyntax(elseKeyword, elseBody);
            }

            // 错位分支恢复：elif 出现在 else 之后（2005），或者出现了不止一个 else（2007）。
            // 两种情况都吞掉完整的 "条件+正文" 形状，不计入正式结构，只为了能继续找到 endif。
            while (Check(SyntaxKind.ElifKeyword) || Check(SyntaxKind.ElseKeyword))
            {
                if (Check(SyntaxKind.ElifKeyword))
                {
                    SyntaxToken misplacedElif = Advance();
                    ReportError("PEVT2005", misplacedElif.Span);
                    ParseExpression();
                    ParseStatementList(IfBodyTerminators);
                }
                else
                {
                    SyntaxToken duplicateElse = Advance();
                    ReportError("PEVT2007", duplicateElse.Span);
                    ParseStatementList(ElseBodyTerminators);
                }
            }

            // 10A-R02："@a() endif" 一类的正文最后一条语句和闭合关键字同行：只在正文/elif/else
            // 里确实有过内容时才检查——如果全都是空的（比如 "if a endif"，条件直接跟着闭合关键字，
            // 中间连一条语句都没有），_lastConsumedTokenEnd 还停留在条件表达式上，那不是"两条语句"
            // 挤在一行，是空产生式，已经由 PEVT2301 等空正文警告覆盖，不应该被误判成 PEVT1005。
            bool ifBodyHadContent = body.Count > 0 || elifClauses.Any(e => e.Body.Count > 0) || (elseClause != null && elseClause.Body.Count > 0);
            if (ifBodyHadContent)
                CheckOneStatementPerLine();
            SyntaxToken endIf = ExpectClosingKeyword(SyntaxKind.EndIfKeyword, "PEVT2002", "PEVT2010");
            return new IfStatementSyntax(ifKeyword, condition, body, elifClauses, elseClause, endIf);
        }

        // ---- while/endwhile ----

        private StatementSyntax ParseWhileStatement()
        {
            SyntaxToken whileKeyword = Advance();
            ExpressionSyntax condition = ParseRequiredCondition("PEVT2101");
            List<StatementSyntax> body = ParseStatementList(WhileBodyTerminators);
            if (body.Count == 0)
                ReportError("PEVT2304", whileKeyword.Span);
            if (body.Count > 0)
                CheckOneStatementPerLine();
            SyntaxToken endWhile = ExpectClosingKeyword(SyntaxKind.EndWhileKeyword, "PEVT2102", "PEVT2104");
            return new WhileStatementSyntax(whileKeyword, condition, body, endWhile);
        }

        // ---- switch/case/default/endswitch ----

        private StatementSyntax ParseSwitchStatement()
        {
            SyntaxToken switchKeyword = Advance();
            ExpressionSyntax value = ParseRequiredCondition("PEVT2401");

            bool startsWithArm = Check(SyntaxKind.CaseKeyword) || Check(SyntaxKind.DefaultKeyword);
            if (!startsWithArm)
                ReportError("PEVT2404", Current.Span);
            else
                // 10A-R02："switch 1 case 1"：switch 表达式和第一个 case/default 挤在同一行。
                CheckOneStatementPerLine();

            var arms = new List<SwitchArmSyntax>();
            var seenCaseTexts = new HashSet<string>();
            bool sawDefault = false;

            _switchDepth++;
            while (Check(SyntaxKind.CaseKeyword) || Check(SyntaxKind.DefaultKeyword))
            {
                if (Check(SyntaxKind.CaseKeyword))
                {
                    SyntaxToken caseKeyword = Advance();
                    ExpressionSyntax caseValue = ParseRequiredCondition("PEVT2406");
                    if (!seenCaseTexts.Add(CanonicalizeSpan(caseValue.Span)))
                        ReportError("PEVT2407", caseValue.Span);
                    List<StatementSyntax> caseBody = ParseStatementList(SwitchArmTerminators);
                    if (caseBody.Count == 0)
                        ReportError("PEVT2408", caseKeyword.Span);
                    // 同一处检查天然覆盖"这个 arm 的正文和下一个 case/default 同行"与"和 endswitch
                    // 同行"（比如 "@a() endswitch"）两种情形——Current 到时候是哪一个都行。
                    if (caseBody.Count > 0)
                        CheckOneStatementPerLine();
                    arms.Add(new CaseArmSyntax(caseKeyword, caseValue, caseBody));
                }
                else
                {
                    SyntaxToken defaultKeyword = Advance();
                    if (sawDefault)
                        ReportError("PEVT2410", defaultKeyword.Span);
                    sawDefault = true;
                    CheckNoTrailingExpression(defaultKeyword, "PEVT2411");
                    List<StatementSyntax> defaultBody = ParseStatementList(SwitchArmTerminators);
                    if (defaultBody.Count == 0)
                        ReportError("PEVT2412", defaultKeyword.Span);
                    if (defaultBody.Count > 0)
                        CheckOneStatementPerLine();
                    arms.Add(new DefaultArmSyntax(defaultKeyword, defaultBody));
                }
            }
            _switchDepth--;

            // 2403（一个 case/default 都没有）与 2404（第一条语句不是 case/default）测的是两件不同
            // 的事，真正全空的 switch 会同时满足两者——不像 raw 块那组诊断，这里不需要互斥。
            if (arms.Count == 0)
                ReportError("PEVT2403", switchKeyword.Span);

            SyntaxToken endSwitch = ExpectClosingKeyword(SyntaxKind.EndSwitchKeyword, "PEVT2402", "PEVT2414");
            return new SwitchStatementSyntax(switchKeyword, value, arms, endSwitch);
        }

        /// <summary>6.2 节"忽略空白后 token 序列完全相同"的近似实现：取表达式原始源码切片并去掉空白字符。</summary>
        private string CanonicalizeSpan(TextSpan span)
        {
            string text = _source.GetText(span);
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            return builder.ToString();
        }

        // ---- labels, goto ----

        private StatementSyntax ParseLabelStatement()
        {
            SyntaxToken hash = Advance();
            if (Check(SyntaxKind.IdentifierToken))
                return new LabelStatementSyntax(hash, Advance());

            // 区分"# 后面确实什么都没有"（PEVT3001，下一个 token 在别的物理行或已经是文件尾）
            // 与"# 后面有内容，只是它不是合法标识符形状"（PEVT3002，比如 #123 或 #"str"）。
            bool candidatePresent = !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(hash);
            if (candidatePresent)
            {
                ReportError("PEVT3002", Current.Span);
                SyntaxToken invalid = SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start);
                SkipRestOfLine(hash);
                return new LabelStatementSyntax(hash, invalid);
            }

            ReportError("PEVT3001", Current.Span);
            return new LabelStatementSyntax(hash, SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start));
        }

        private StatementSyntax ParseGotoStatement()
        {
            SyntaxToken gotoKeyword = Advance();
            if (Check(SyntaxKind.HashToken))
            {
                SyntaxToken hash = Advance();
                SyntaxToken label = Expect(SyntaxKind.IdentifierToken, "PEVT3103");
                if (!label.IsMissing && !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(gotoKeyword))
                {
                    ReportError("PEVT3105", Current.Span);
                    SkipRestOfLine(gotoKeyword);
                }

                return new GotoLabelStatementSyntax(gotoKeyword, hash, label);
            }

            if (!CanStartExpression(Current.Kind))
            {
                ReportError("PEVT3101", Current.Span);
                return new UnknownStatementSyntax(gotoKeyword);
            }

            // 7.2 节：不在 switch 内部时，goto 只能使用 "#LabelName" 形式；裸表达式形式的
            // "goto 表达式"（6.5 节）语法上只在 switch 内部才被允许，PEVT3111 是 Flow 阶段针对
            // 同一根因的语义复核，这里先在语法层面就报出来（PEVT3102），但仍然按集合等待/case-goto
            // 的通常形状继续解析，让恢复保持一致。
            if (_switchDepth == 0)
                ReportError("PEVT3102", Current.Span);

            return new GotoCaseStatementSyntax(gotoKeyword, ParseExpression());
        }

        // ---- var/const/assignment ----

        private StatementSyntax ParseVariableDeclarationStatement()
        {
            SyntaxToken varKeyword = Advance();
            SyntaxToken name = ParseDeclarationName(out bool nameRecoveredWholeLine);
            if (nameRecoveredWholeLine)
                return new VariableDeclarationSyntax(varKeyword, name,
                    SyntaxToken.CreateMissing(SyntaxKind.ColonToken, name.Span.End),
                    SyntaxToken.CreateMissing(SyntaxKind.IntKeyword, name.Span.End), null, null);

            SyntaxToken colon = Expect(SyntaxKind.ColonToken, "PEVT6005");
            SyntaxToken type = ParseDeclaredTypeOrRecover(varKeyword, out bool typeRecoveredWholeLine);
            if (typeRecoveredWholeLine)
                return new VariableDeclarationSyntax(varKeyword, name, colon, type, null, null);

            SyntaxToken equalsToken = null;
            ExpressionSyntax initializer = null;
            if (Check(SyntaxKind.EqualsToken))
            {
                equalsToken = Advance();
                initializer = ParseInitializerExpression();
                CheckBooleanLiteralForm(type, initializer);
            }

            return new VariableDeclarationSyntax(varKeyword, name, colon, type, equalsToken, initializer);
        }

        private StatementSyntax ParseConstantDeclarationStatement()
        {
            SyntaxToken constKeyword = Advance();
            SyntaxToken name = ParseDeclarationName(out bool nameRecoveredWholeLine);
            if (nameRecoveredWholeLine)
                return new ConstantDeclarationSyntax(constKeyword, name,
                    SyntaxToken.CreateMissing(SyntaxKind.ColonToken, name.Span.End),
                    SyntaxToken.CreateMissing(SyntaxKind.IntKeyword, name.Span.End),
                    SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, name.Span.End),
                    new MissingExpressionSyntax(name.Span.End));

            SyntaxToken colon = Expect(SyntaxKind.ColonToken, "PEVT6005");
            SyntaxToken type = ParseDeclaredTypeOrRecover(constKeyword, out bool typeRecoveredWholeLine);
            if (typeRecoveredWholeLine)
                return new ConstantDeclarationSyntax(constKeyword, name, colon, type,
                    SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, type.Span.End),
                    new MissingExpressionSyntax(type.Span.End));

            SyntaxToken equalsToken;
            ExpressionSyntax initializer;
            if (Check(SyntaxKind.EqualsToken))
            {
                equalsToken = Advance();
                initializer = ParseInitializerExpression();
                CheckBooleanLiteralForm(type, initializer);
            }
            else
            {
                ReportError("PEVT6009", Current.Span);
                equalsToken = SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, Current.Span.Start);
                initializer = new MissingExpressionSyntax(Current.Span.Start);
            }

            return new ConstantDeclarationSyntax(constKeyword, name, colon, type, equalsToken, initializer);
        }

        /// <summary>
        /// 8.9 节："布尔字面量只能是全小写的 true 或 false；其他大小写或数值形式均无效"（PEVT5023）。
        /// 大小写变体（<c>True</c>/<c>TRUE</c>）在词法阶段就已经变成普通标识符，语法层面无法区分
        /// "打算写布尔字面量却拼错大小写"和"引用一个碰巧叫这个名字的变量"——那需要环境绑定信息，
        /// 留给后续阶段。这里只处理不需要绑定就能确定违规的形状：声明类型是 <c>bool</c>，
        /// 初始化器却是裸的数值字面量。声明类型和初始化器在同一条语句内，不需要跨语句查找。
        /// </summary>
        private void CheckBooleanLiteralForm(SyntaxToken type, ExpressionSyntax initializer)
        {
            if (type.Kind != SyntaxKind.BoolKeyword)
                return;

            if (initializer is LiteralExpressionSyntax literal &&
                (literal.Token.Kind == SyntaxKind.IntegerLiteralToken || literal.Token.Kind == SyntaxKind.FloatLiteralToken))
                ReportError("PEVT5023", literal.Span);
        }

        /// <param name="recoveredWholeLine">
        /// true 表示名称候选本身不可挽救（PEVT6013）：已经把这个 token 连同它所在物理行剩余部分
        /// 一起消费掉，调用方必须直接放弃 colon/type/初始化器的解析，不能再尝试——否则那些 Expect
        /// 会对着一个已经跳过的位置重复报错，而真正的病灶 token（比如 <c>var if : int</c> 里的
        /// <c>if</c>）如果不在这里连带同步掉，会原样留在流里，被外层循环当成一条全新的语句，
        /// 让 <c>if</c> 被重新解析成一个虚假的 <see cref="IfStatementSyntax"/>。
        /// 类型关键字写在名称位置（PEVT6006）不受这个字段影响：既有测试依赖它保持"不消费"，
        /// 让这个 token 继续留给 <see cref="ParseDeclaredTypeOrRecover"/> 当作类型识别出来。
        /// </param>
        private SyntaxToken ParseDeclarationName(out bool recoveredWholeLine)
        {
            recoveredWholeLine = false;

            if (IsTypeKeyword(Current.Kind))
            {
                ReportError("PEVT6006", Current.Span);
                return SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start);
            }

            // 9.6 节：保留关键字不能被用作变量/常量名称。类型关键字已经在上面拿到了更精确的
            // PEVT6006（"类型写在了名称位置"）；其余保留字（if、true、goto……）落在这里报 PEVT6013。
            if (Current.Kind != SyntaxKind.IdentifierToken && SyntaxFacts.IsReservedWord(Current.Text))
            {
                ReportError("PEVT6013", Current.Span);
                SyntaxToken invalid = Advance();
                SkipRestOfLine(invalid);
                recoveredWholeLine = true;
                return SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, invalid.Span.Start);
            }

            return Expect(SyntaxKind.IdentifierToken, "PEVT6004");
        }

        /// <summary>
        /// 形参列表和自定义事件块返回类型共用的类型解析（<see cref="ParseTypeNameOrMissing"/>）不能
        /// 在遇到无效类型时消费掉整行——那两个位置的"整行"往往还包含后续形参或事件块正文，消费掉
        /// 会连带吞掉完全无关的合法内容。var/const 声明的类型位置没有这个顾虑：它右边只可能是可选
        /// 的 <c>= 初始化器</c>，一旦类型本身就不合法，整条声明已经不可能再被正确解读，因此在这里
        /// 消费掉无效候选并同步到物理行结束（PEVT5010），避免它（比如 <c>var x : foo</c> 里的
        /// <c>foo</c>）原样留在流里，被外层循环当成一条全新的、同样莫名其妙的语句。
        /// </summary>
        private SyntaxToken ParseDeclaredTypeOrRecover(SyntaxToken declarationKeyword, out bool recoveredWholeLine)
        {
            recoveredWholeLine = false;

            if (IsTypeKeyword(Current.Kind))
                return Advance();

            ReportError("PEVT5010", Current.Span);
            SyntaxToken invalid = Advance();
            SkipRestOfLine(declarationKeyword);
            recoveredWholeLine = true;
            return SyntaxToken.CreateMissing(SyntaxKind.IntKeyword, invalid.Span.Start);
        }

        private SyntaxToken ParseTypeNameOrMissing()
        {
            if (IsTypeKeyword(Current.Kind))
                return Advance();

            ReportError("PEVT5010", Current.Span);
            return SyntaxToken.CreateMissing(SyntaxKind.IntKeyword, Current.Span.Start);
        }

        private ExpressionSyntax ParseInitializerExpression()
        {
            if (!CanStartExpression(Current.Kind))
            {
                ReportError("PEVT6011", Current.Span);
                return new MissingExpressionSyntax(Current.Span.Start);
            }

            return ParseExpression();
        }

        private StatementSyntax ParseAssignmentStatement()
        {
            SyntaxToken name = Advance();
            SyntaxToken equalsToken = Advance();
            return new AssignmentStatementSyntax(name, equalsToken, ParseExpression());
        }

        private static bool IsTypeKeyword(SyntaxKind kind) => kind switch
        {
            SyntaxKind.IntKeyword or SyntaxKind.FloatKeyword or SyntaxKind.BoolKeyword or SyntaxKind.CharKeyword or SyntaxKind.StringKeyword => true,
            _ => false,
        };

        /// <summary>与 <see cref="ParsePrimaryOperand"/> 能处理的种类保持同步——用于在调用
        /// <see cref="ParseExpression"/> 前判断"这里到底有没有表达式"，从而报出更精确的诊断
        /// （如 PEVT6011）而不是泛泛的 PEVT5001。</summary>
        private static bool CanStartExpression(SyntaxKind kind) => kind switch
        {
            SyntaxKind.IntegerLiteralToken or SyntaxKind.FloatLiteralToken or SyntaxKind.CharLiteralToken or SyntaxKind.StringLiteralToken
                or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword
                or SyntaxKind.IdentifierToken or SyntaxKind.OpenParenToken or SyntaxKind.AtToken
                or SyntaxKind.AwaitKeyword or SyntaxKind.StatusKeyword or SyntaxKind.DollarRawToken
                or SyntaxKind.CallEvtKeyword or SyntaxKind.ExecKeyword
                or SyntaxKind.MinusToken or SyntaxKind.ExclamationToken => true,
            _ => false,
        };

        // ---- handler, kill ----

        private StatementSyntax ParseHandlerDeclarationStatement()
        {
            SyntaxToken handlerKeyword = Advance();
            SyntaxToken name = ParseHandlerName();

            if (!Check(SyntaxKind.EqualsToken))
            {
                ReportError("PEVT7206", Current.Span);
                SyntaxToken missingEquals = SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, Current.Span.Start);
                return new HandlerDeclarationStatementSyntax(handlerKeyword, name, missingEquals, new MissingExpressionSyntax(Current.Span.Start));
            }

            SyntaxToken equalsToken = Advance();
            return new HandlerDeclarationStatementSyntax(handlerKeyword, name, equalsToken, ParseHandlerInitializer());
        }

        /// <summary>9.6 节：保留关键字同样不能被用作句柄名称（PEVT6013）。</summary>
        private SyntaxToken ParseHandlerName()
        {
            if (Current.Kind != SyntaxKind.IdentifierToken && SyntaxFacts.IsReservedWord(Current.Text))
            {
                ReportError("PEVT6013", Current.Span);
                return SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start);
            }

            return Expect(SyntaxKind.IdentifierToken, "PEVT7205");
        }

        /// <summary>15.2 节：初始化器只能是 <c>@</c> 调用、<c>_</c> 调用或 <c>callevt</c>。
        /// <c>exec</c> 有专属诊断（PEVT7406）；其余任何形状都是泛泛的 PEVT7206。</summary>
        private ExpressionSyntax ParseHandlerInitializer()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.AtToken:
                    return ParseBuiltinCall();
                case SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.OpenParenToken:
                    return ParseIdentifierStartedOperand();
                case SyntaxKind.CallEvtKeyword:
                    return ParseEventCallExpression();
                case SyntaxKind.ExecKeyword:
                {
                    ExpressionSyntax exec = ParseExecCallCore();
                    ReportError("PEVT7406", exec.Span);
                    return exec;
                }
                default:
                    ReportError("PEVT7206", Current.Span);
                    return new MissingExpressionSyntax(Current.Span.Start);
            }
        }

        private StatementSyntax ParseKillStatement()
        {
            SyntaxToken killKeyword = Advance();
            SyntaxToken handle = Expect(SyntaxKind.IdentifierToken, "PEVT7213");
            return new KillStatementSyntax(killKeyword, handle);
        }

        // ---- $raw cmd / $raw cs as a statement ----

        /// <summary>
        /// 12.1/12.2 节的语句形态。<c>$raw cmd</c> 永远只是语句（没有对应表达式形态）；
        /// <c>$raw cs</c> 作为纯调用语句时同样合法（12.2 节："没有返回值的 $raw cs 是纯调用"），
        /// 复用既有的 <see cref="RawCsExpressionSyntax"/> 节点，只是外层包一层
        /// <see cref="ExpressionStatementSyntax"/> 丢弃其可能的返回值。
        /// </summary>
        private StatementSyntax ParseRawStatement()
        {
            SyntaxToken dollarRaw = Advance();
            if (Check(SyntaxKind.CmdKeyword))
            {
                SyntaxToken cmd = Advance();
                SyntaxToken open = Expect(SyntaxKind.TripleQuoteToken, "PEVT8003");
                SyntaxToken content = Check(SyntaxKind.RawContentToken) ? Advance() : SyntaxToken.CreateMissing(SyntaxKind.RawContentToken, Current.Span.Start);
                SyntaxToken close = Expect(SyntaxKind.TripleQuoteToken, "PEVT8004");
                return new RawCmdStatementSyntax(dollarRaw, cmd, open, content, close);
            }

            SyntaxToken cs = ExpectRawCsTarget(dollarRaw);
            IdentifierListSyntax arguments = Check(SyntaxKind.OpenParenToken) ? ParseIdentifierList() : null;
            SyntaxToken rawOpen = Expect(SyntaxKind.TripleQuoteToken, "PEVT8003");
            SyntaxToken rawContent = Check(SyntaxKind.RawContentToken) ? Advance() : SyntaxToken.CreateMissing(SyntaxKind.RawContentToken, Current.Span.Start);
            SyntaxToken rawClose = Expect(SyntaxKind.TripleQuoteToken, "PEVT8004");
            return new ExpressionStatementSyntax(new RawCsExpressionSyntax(dollarRaw, cs, arguments, rawOpen, rawContent, rawClose));
        }

        // ---- async prefix ----

        /// <summary>
        /// 15.1 节：<c>async</c> 合法的唯一目标是紧跟的 <c>block</c>（自定义事件块定义）。
        /// 用在别处时按诊断表已分配的最具体编号报告——<c>callevt</c>/<c>exec</c> 各有专属编号
        /// （PEVT7305/7405），<c>@</c>/<c>_</c> 调用位置合用 PEVT7215，其余任意目标落回通用的
        /// PEVT7201。这是诊断表里几个编号明显重叠时的一次显式取舍：优先用范围更窄的编号。
        /// </summary>
        private StatementSyntax ParseAsyncPrefixedStatement()
        {
            SyntaxToken asyncKeyword = Advance();
            if (Check(SyntaxKind.BlockKeyword))
            {
                if (Current.LeadingTrivia.Count == 0)
                    ReportError("PEVT7202", Current.Span);
                return ParseBlockDefinitionStatement(asyncKeyword);
            }

            string diagnosticId = Current.Kind switch
            {
                SyntaxKind.CallEvtKeyword => "PEVT7305",
                SyntaxKind.ExecKeyword => "PEVT7405",
                SyntaxKind.AtToken => "PEVT7215",
                SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.OpenParenToken => "PEVT7215",
                _ => "PEVT7201",
            };
            ReportError(diagnosticId, asyncKeyword.Span);
            return ParseStatement();
        }

        // ---- custom event blocks ----

        private StatementSyntax ParseBlockDefinitionStatement(SyntaxToken asyncKeyword)
        {
            if (_blockStack.Count > 0)
                ReportError("PEVT7104", Current.Span);

            SyntaxToken blockKeyword;
            if (Check(SyntaxKind.BlockKeyword))
            {
                blockKeyword = Advance();
            }
            else
            {
                // 调用方（PEVT7119 的识别路径，见 LooksLikeBlockSignatureMissingKeyword）已经确认
                // 这其实是一份遗漏了 block 关键字的定义签名，而不是一次普通的自定义事件块调用——
                // 报告后补一个缺失 token，继续按完整定义签名解析名称、参数、返回类型、正文和 endblock。
                ReportError("PEVT7119", Current.Span);
                blockKeyword = SyntaxToken.CreateMissing(SyntaxKind.BlockKeyword, Current.Span.Start);
            }

            SyntaxToken name = ParseCustomBlockDefinitionName();
            ParameterListSyntax parameters = ParseParameterList();

            SyntaxToken colon = null;
            SyntaxToken returnType = null;
            if (Check(SyntaxKind.ColonToken))
            {
                colon = Advance();
                returnType = ParseTypeNameOrMissing();
            }

            _blockStack.Push(returnType != null);
            int savedSwitchDepth = _switchDepth;
            _switchDepth = 0; // 自定义事件块拥有独立的标签/跳转环境，不应该让外层 switch 泄漏进来。
            List<StatementSyntax> body = ParseStatementList(BlockBodyTerminators);
            _switchDepth = savedSwitchDepth;
            _blockStack.Pop();

            SyntaxToken endBlock = Expect(SyntaxKind.EndBlockKeyword, "PEVT7116");
            return new BlockDefinitionStatementSyntax(asyncKeyword, blockKeyword, name, parameters, colon, returnType, body, endBlock);
        }

        /// <summary>
        /// PEVT7119：识别"看起来是自定义事件块定义签名，只是漏写了 block 关键字"的形状——
        /// 调用参数是普通表达式，定义参数却是 "名 : 类型" 或声明了返回类型的 "() : 类型"，
        /// 两者在语法上不会混淆。只在这个形状出现时才把它当定义处理，否则仍按普通调用解析。
        /// </summary>
        private bool LooksLikeBlockSignatureMissingKeyword()
        {
            if (Peek(2).Kind == SyntaxKind.CloseParenToken)
                return Peek(3).Kind == SyntaxKind.ColonToken; // "_foo() : type"

            return Peek(2).Kind == SyntaxKind.IdentifierToken && Peek(3).Kind == SyntaxKind.ColonToken; // "_foo(name : type"
        }

        /// <summary>14.1 节：完整名称必须以 <c>_</c> 开头。词法阶段把 <c>_playScene</c> 整体
        /// 识别成一个 <see cref="SyntaxKind.IdentifierToken"/>（阶段 2 的既有设计），所以这里只需要
        /// 检查该 token 的文本形状，不需要额外的词法支持。</summary>
        private SyntaxToken ParseCustomBlockDefinitionName()
        {
            if (!Check(SyntaxKind.IdentifierToken))
            {
                ReportError("PEVT7102", Current.Span);
                return SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start);
            }

            SyntaxToken name = Current;
            if (name.Text == "_")
                ReportError("PEVT7101", name.Span);
            else if (name.Text.Length == 0 || name.Text[0] != '_')
                ReportError("PEVT7102", name.Span);

            return Advance();
        }

        private ParameterListSyntax ParseParameterList()
        {
            SyntaxToken open = Expect(SyntaxKind.OpenParenToken, "PEVT7102");
            var parameters = new List<ParameterSyntax>();
            var commas = new List<SyntaxToken>();

            if (!Check(SyntaxKind.CloseParenToken) && !Check(SyntaxKind.EndOfFileToken))
            {
                parameters.Add(ParseParameter());
                while (Check(SyntaxKind.CommaToken))
                {
                    commas.Add(Advance());
                    parameters.Add(ParseParameter());
                }
            }

            SyntaxToken close = Expect(SyntaxKind.CloseParenToken, "PEVT7102");
            return new ParameterListSyntax(open, parameters, commas, close);
        }

        private ParameterSyntax ParseParameter()
        {
            SyntaxToken name = Expect(SyntaxKind.IdentifierToken, "PEVT7102");
            SyntaxToken colon = Expect(SyntaxKind.ColonToken, "PEVT7102");
            SyntaxToken type = ParseTypeNameOrMissing();
            return new ParameterSyntax(name, colon, type);
        }

        /// <summary>
        /// 14.3 节。<c>return</c> 目标只能是裸标识符（不允许字面量/运算/调用表达式，PEVT7108），
        /// 是否与块声明的返回值类型匹配（PEVT7109）需要类型绑定，留给后续阶段。
        /// </summary>
        private StatementSyntax ParseReturnStatement()
        {
            SyntaxToken returnKeyword = Advance();

            if (_blockStack.Count == 0)
            {
                ReportError("PEVT7105", returnKeyword.Span);
                if (!Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(returnKeyword))
                    SkipRestOfLine(returnKeyword);
                return new ReturnStatementSyntax(returnKeyword, null);
            }

            bool hasReturnType = _blockStack.Peek();
            bool hasTargetCandidate = !Check(SyntaxKind.EndOfFileToken) && LineOf(Current) == LineOf(returnKeyword) && CanStartExpression(Current.Kind);

            if (!hasReturnType)
            {
                if (hasTargetCandidate)
                {
                    ReportError("PEVT7107", Current.Span);
                    SkipRestOfLine(returnKeyword);
                }
                return new ReturnStatementSyntax(returnKeyword, null);
            }

            if (!hasTargetCandidate)
            {
                ReportError("PEVT7106", returnKeyword.Span);
                return new ReturnStatementSyntax(returnKeyword, null);
            }

            bool isBareIdentifier = Check(SyntaxKind.IdentifierToken) && Peek(1).Kind != SyntaxKind.OpenParenToken;
            if (!isBareIdentifier)
            {
                ReportError("PEVT7108", Current.Span);
                SkipRestOfLine(returnKeyword);
                return new ReturnStatementSyntax(returnKeyword, null);
            }

            return new ReturnStatementSyntax(returnKeyword, Advance());
        }
    }
}
