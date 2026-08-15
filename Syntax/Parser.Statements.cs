using System.Collections.Generic;
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
                case SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.OpenParenToken: return ParseExpressionStatement();
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
                statements.Add(ParseStatement());
            return statements;
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

        private StatementSyntax ParseIfStatement()
        {
            SyntaxToken ifKeyword = Advance();
            ExpressionSyntax condition = ParseExpression();
            List<StatementSyntax> body = ParseStatementList(IfBodyTerminators);
            if (body.Count == 0)
                ReportError("PEVT2301", ifKeyword.Span);

            var elifClauses = new List<ElifClauseSyntax>();
            while (Check(SyntaxKind.ElifKeyword))
            {
                SyntaxToken elifKeyword = Advance();
                ExpressionSyntax elifCondition = ParseExpression();
                List<StatementSyntax> elifBody = ParseStatementList(IfBodyTerminators);
                if (elifBody.Count == 0)
                    ReportError("PEVT2302", elifKeyword.Span);
                elifClauses.Add(new ElifClauseSyntax(elifKeyword, elifCondition, elifBody));
            }

            ElseClauseSyntax elseClause = null;
            if (Check(SyntaxKind.ElseKeyword))
            {
                SyntaxToken elseKeyword = Advance();
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

            SyntaxToken endIf = ExpectClosingKeyword(SyntaxKind.EndIfKeyword, "PEVT2002", "PEVT2010");
            return new IfStatementSyntax(ifKeyword, condition, body, elifClauses, elseClause, endIf);
        }

        // ---- while/endwhile ----

        private StatementSyntax ParseWhileStatement()
        {
            SyntaxToken whileKeyword = Advance();
            ExpressionSyntax condition = ParseExpression();
            List<StatementSyntax> body = ParseStatementList(WhileBodyTerminators);
            if (body.Count == 0)
                ReportError("PEVT2304", whileKeyword.Span);
            SyntaxToken endWhile = ExpectClosingKeyword(SyntaxKind.EndWhileKeyword, "PEVT2102", "PEVT2104");
            return new WhileStatementSyntax(whileKeyword, condition, body, endWhile);
        }

        // ---- switch/case/default/endswitch ----

        private StatementSyntax ParseSwitchStatement()
        {
            SyntaxToken switchKeyword = Advance();
            ExpressionSyntax value = ParseExpression();

            bool startsWithArm = Check(SyntaxKind.CaseKeyword) || Check(SyntaxKind.DefaultKeyword);
            if (!startsWithArm)
                ReportError("PEVT2404", Current.Span);

            var arms = new List<SwitchArmSyntax>();
            var seenCaseTexts = new HashSet<string>();
            bool sawDefault = false;

            while (Check(SyntaxKind.CaseKeyword) || Check(SyntaxKind.DefaultKeyword))
            {
                if (Check(SyntaxKind.CaseKeyword))
                {
                    SyntaxToken caseKeyword = Advance();
                    ExpressionSyntax caseValue = ParseExpression();
                    if (!seenCaseTexts.Add(CanonicalizeSpan(caseValue.Span)))
                        ReportError("PEVT2407", caseValue.Span);
                    List<StatementSyntax> caseBody = ParseStatementList(SwitchArmTerminators);
                    if (caseBody.Count == 0)
                        ReportError("PEVT2408", caseKeyword.Span);
                    arms.Add(new CaseArmSyntax(caseKeyword, caseValue, caseBody));
                }
                else
                {
                    SyntaxToken defaultKeyword = Advance();
                    if (sawDefault)
                        ReportError("PEVT2410", defaultKeyword.Span);
                    sawDefault = true;
                    List<StatementSyntax> defaultBody = ParseStatementList(SwitchArmTerminators);
                    if (defaultBody.Count == 0)
                        ReportError("PEVT2412", defaultKeyword.Span);
                    arms.Add(new DefaultArmSyntax(defaultKeyword, defaultBody));
                }
            }

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
            if (!Check(SyntaxKind.IdentifierToken))
            {
                ReportError("PEVT3001", Current.Span);
                return new LabelStatementSyntax(hash, SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start));
            }

            return new LabelStatementSyntax(hash, Advance());
        }

        private StatementSyntax ParseGotoStatement()
        {
            SyntaxToken gotoKeyword = Advance();
            if (Check(SyntaxKind.HashToken))
            {
                SyntaxToken hash = Advance();
                SyntaxToken label = Expect(SyntaxKind.IdentifierToken, "PEVT3103");
                return new GotoLabelStatementSyntax(gotoKeyword, hash, label);
            }

            if (!CanStartExpression(Current.Kind))
            {
                ReportError("PEVT3101", Current.Span);
                return new UnknownStatementSyntax(gotoKeyword);
            }

            return new GotoCaseStatementSyntax(gotoKeyword, ParseExpression());
        }

        // ---- var/const/assignment ----

        private StatementSyntax ParseVariableDeclarationStatement()
        {
            SyntaxToken varKeyword = Advance();
            SyntaxToken name = ParseDeclarationName();
            SyntaxToken colon = Expect(SyntaxKind.ColonToken, "PEVT6005");
            SyntaxToken type = ParseTypeNameOrMissing();

            SyntaxToken equalsToken = null;
            ExpressionSyntax initializer = null;
            if (Check(SyntaxKind.EqualsToken))
            {
                equalsToken = Advance();
                initializer = ParseInitializerExpression();
            }

            return new VariableDeclarationSyntax(varKeyword, name, colon, type, equalsToken, initializer);
        }

        private StatementSyntax ParseConstantDeclarationStatement()
        {
            SyntaxToken constKeyword = Advance();
            SyntaxToken name = ParseDeclarationName();
            SyntaxToken colon = Expect(SyntaxKind.ColonToken, "PEVT6005");
            SyntaxToken type = ParseTypeNameOrMissing();

            SyntaxToken equalsToken;
            ExpressionSyntax initializer;
            if (Check(SyntaxKind.EqualsToken))
            {
                equalsToken = Advance();
                initializer = ParseInitializerExpression();
            }
            else
            {
                ReportError("PEVT6009", Current.Span);
                equalsToken = SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, Current.Span.Start);
                initializer = new MissingExpressionSyntax(Current.Span.Start);
            }

            return new ConstantDeclarationSyntax(constKeyword, name, colon, type, equalsToken, initializer);
        }

        private SyntaxToken ParseDeclarationName()
        {
            if (IsTypeKeyword(Current.Kind))
            {
                ReportError("PEVT6006", Current.Span);
                return SyntaxToken.CreateMissing(SyntaxKind.IdentifierToken, Current.Span.Start);
            }

            return Expect(SyntaxKind.IdentifierToken, "PEVT6004");
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
            SyntaxToken name = Expect(SyntaxKind.IdentifierToken, "PEVT7205");

            if (!Check(SyntaxKind.EqualsToken))
            {
                ReportError("PEVT7206", Current.Span);
                SyntaxToken missingEquals = SyntaxToken.CreateMissing(SyntaxKind.EqualsToken, Current.Span.Start);
                return new HandlerDeclarationStatementSyntax(handlerKeyword, name, missingEquals, new MissingExpressionSyntax(Current.Span.Start));
            }

            SyntaxToken equalsToken = Advance();
            return new HandlerDeclarationStatementSyntax(handlerKeyword, name, equalsToken, ParseHandlerInitializer());
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

            SyntaxToken cs = Expect(SyntaxKind.CsKeyword, "PEVT8002");
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

            SyntaxToken blockKeyword = Advance();
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
            List<StatementSyntax> body = ParseStatementList(BlockBodyTerminators);
            _blockStack.Pop();

            SyntaxToken endBlock = Expect(SyntaxKind.EndBlockKeyword, "PEVT7116");
            return new BlockDefinitionStatementSyntax(asyncKeyword, blockKeyword, name, parameters, colon, returnType, body, endBlock);
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
