using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Flow
{
    /// <summary>
    /// 静态控制流分析：收集标签并解析 <c>goto</c> 目标，再用同一套"这段语句列表是否保证终止"算法检测外层事件
    /// 是否每条路径都到达 <c>end</c>（PEVT4001/4002）、每个有返回值声明的块是否每条路径都返回（PEVT7117）。
    /// 不证明 <c>while</c>/<c>goto</c> 是否死循环：<c>while</c> 之后永远视为可达，<c>goto</c> 只当成这条路径的终结点。
    /// </summary>
    public sealed class ControlFlowAnalyzer
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly SourceText _source;

        /// <summary>
        /// 整份文件里（含每个块内部）出现过的全部标签名，只用来把 PEVT3104（哪里都没有这个标签）和
        /// PEVT3106（标签存在，只是在另一个事件或块环境里）区分开。
        /// </summary>
        private readonly HashSet<string> _allLabelNamesEverywhere = new HashSet<string>();

        public ControlFlowAnalyzer(DiagnosticBag diagnostics, SourceText source)
        {
            _diagnostics = diagnostics;
            _source = source;
        }

        private void Report(string diagnosticId, TextSpan span) =>
            _diagnostics.AddFromCatalog(diagnosticId, _source.GetLocation(span));

        public void AnalyzeDocument(DocumentSyntax document)
        {
            CollectAllLabelNames(document.Statements);

            HashSet<LabelStatementSyntax> outerTargets = AnalyzeRoot(document.Statements);
            bool terminates = AnalyzeSequence(document.Statements, IsEventTerminator, outerTargets, "PEVT4002");
            if (!terminates)
                Report("PEVT4001", document.EndOfFile.Span);

            AnalyzeBlocksRecursively(document.Statements);
        }

        private static bool IsEventTerminator(StatementSyntax statement) =>
            statement is EndStatementSyntax || statement is GotoLabelStatementSyntax || statement is GotoCaseStatementSyntax;

        private static bool IsReturnTerminator(StatementSyntax statement) =>
            (statement is ReturnStatementSyntax returnStatement && returnStatement.Target != null)
            || statement is GotoLabelStatementSyntax || statement is GotoCaseStatementSyntax;

        // ---- 7117: every path through a typed block must return a value ----

        private void AnalyzeBlocksRecursively(IReadOnlyList<StatementSyntax> statements)
        {
            foreach (StatementSyntax statement in statements)
            {
                switch (statement)
                {
                    case BlockDefinitionStatementSyntax block:
                        HashSet<LabelStatementSyntax> blockTargets = AnalyzeRoot(block.Body);
                        if (block.ReturnType != null && !AnalyzeSequence(block.Body, IsReturnTerminator, blockTargets, null))
                            Report("PEVT7117", block.EndBlockKeyword.Span);
                        AnalyzeBlocksRecursively(block.Body); // 嵌套定义本身已经是 7104 的错误，但仍然值得继续分析。
                        break;
                    case IfStatementSyntax ifStatement:
                        AnalyzeBlocksRecursively(ifStatement.Body);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            AnalyzeBlocksRecursively(elif.Body);
                        if (ifStatement.ElseClause != null)
                            AnalyzeBlocksRecursively(ifStatement.ElseClause.Body);
                        break;
                    case IfDefStatementSyntax ifDefStatement:
                        AnalyzeBlocksRecursively(ifDefStatement.Body);
                        if (ifDefStatement.HasElse)
                            AnalyzeBlocksRecursively(ifDefStatement.ElseBody);
                        break;
                    case WhileStatementSyntax whileStatement:
                        AnalyzeBlocksRecursively(whileStatement.Body);
                        break;
                    case SwitchStatementSyntax switchStatement:
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                            AnalyzeBlocksRecursively(arm.Body);
                        break;
                }
            }
        }

        /// <summary>
        /// 递归判断一段语句列表是否在每条可达路径落到列表末尾之前都遇到了 <paramref name="isTerminator"/> 认定的终结语句。
        /// 同时报告终结语句或无条件 goto 之后、且不是任何 goto 目标的不可达语句（PEVT4002），
        /// 一旦遇到属于 <paramref name="jumpTargets"/> 的标签，可达性重新恢复。
        /// </summary>
        private bool AnalyzeSequence(IReadOnlyList<StatementSyntax> statements, System.Func<StatementSyntax, bool> isTerminator,
            HashSet<LabelStatementSyntax> jumpTargets, string unreachableDiagnosticId)
        {
            bool reachable = true;
            foreach (StatementSyntax statement in statements)
            {
                if (!reachable)
                {
                    if (statement is LabelStatementSyntax label && jumpTargets.Contains(label))
                        reachable = true;
                    else if (unreachableDiagnosticId != null)
                        Report(unreachableDiagnosticId, statement.Span);
                }

                if (!reachable)
                    continue;

                if (isTerminator(statement))
                {
                    reachable = false;
                    continue;
                }

                switch (statement)
                {
                    case IfStatementSyntax ifStatement:
                    {
                        bool allTerminate = AnalyzeSequence(ifStatement.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            allTerminate &= AnalyzeSequence(elif.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                        if (ifStatement.ElseClause != null)
                            allTerminate &= AnalyzeSequence(ifStatement.ElseClause.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                        else
                            allTerminate = false; // 跳过整个 if（没有 else）本身就是一条不终止的路径。
                        if (allTerminate)
                            reachable = false;
                        break;
                    }

                    case IfDefStatementSyntax ifDefStatement:
                    {
                        bool allTerminate = AnalyzeSequence(ifDefStatement.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                        if (ifDefStatement.HasElse)
                            allTerminate &= AnalyzeSequence(ifDefStatement.ElseBody, isTerminator, jumpTargets, unreachableDiagnosticId);
                        else
                            allTerminate = false;
                        if (allTerminate)
                            reachable = false;
                        break;
                    }

                    case WhileStatementSyntax whileStatement:
                        // 只为了标记体内的不可达语句；循环之后永远可达——不证明 while 会不会真正跑完。
                        AnalyzeSequence(whileStatement.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                        break;

                    case SwitchStatementSyntax switchStatement:
                    {
                        bool allTerminate = true;
                        bool hasDefault = false;
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                        {
                            allTerminate &= AnalyzeSequence(arm.Body, isTerminator, jumpTargets, unreachableDiagnosticId);
                            hasDefault |= arm is DefaultArmSyntax;
                        }
                        if (!hasDefault)
                            allTerminate = false;
                        if (allTerminate)
                            reachable = false;
                        break;
                    }
                }
            }

            return !reachable;
        }

        // ---- labels and goto: per-root (outer event, or each block) resolution ----

        private void CollectAllLabelNames(IReadOnlyList<StatementSyntax> statements)
        {
            foreach (StatementSyntax statement in statements)
            {
                switch (statement)
                {
                    case LabelStatementSyntax label:
                        if (!label.Name.IsMissing)
                            _allLabelNamesEverywhere.Add(label.Name.Text);
                        break;
                    case IfStatementSyntax ifStatement:
                        CollectAllLabelNames(ifStatement.Body);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            CollectAllLabelNames(elif.Body);
                        if (ifStatement.ElseClause != null)
                            CollectAllLabelNames(ifStatement.ElseClause.Body);
                        break;
                    case IfDefStatementSyntax ifDefStatement:
                        CollectAllLabelNames(ifDefStatement.Body);
                        if (ifDefStatement.HasElse)
                            CollectAllLabelNames(ifDefStatement.ElseBody);
                        break;
                    case WhileStatementSyntax whileStatement:
                        CollectAllLabelNames(whileStatement.Body);
                        break;
                    case SwitchStatementSyntax switchStatement:
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                            CollectAllLabelNames(arm.Body);
                        break;
                    case BlockDefinitionStatementSyntax block:
                        CollectAllLabelNames(block.Body); // 全局集合必须能看到全部环境，才能正确判定 3106。
                        break;
                }
            }
        }

        /// <summary>处理一个独立的标签/goto 环境（外层事件，或某一个自定义事件块），返回被至少一个
        /// 合法 <c>goto</c> 引用的标签集合（供 <see cref="AnalyzeSequence"/> 恢复可达性使用）。</summary>
        private HashSet<LabelStatementSyntax> AnalyzeRoot(IReadOnlyList<StatementSyntax> rootStatements)
        {
            var labelsByName = new Dictionary<string, LabelStatementSyntax>();
            var pathByLabel = new Dictionary<LabelStatementSyntax, List<IReadOnlyList<StatementSyntax>>>();
            var gotos = new List<(GotoLabelStatementSyntax Goto, List<IReadOnlyList<StatementSyntax>> Path)>();
            var caseGotos = new List<(GotoCaseStatementSyntax Goto, SwitchStatementSyntax EnclosingSwitch)>();
            var rootPath = new List<IReadOnlyList<StatementSyntax>> { rootStatements };

            CollectLabelsAndGotos(rootStatements, rootPath, null, labelsByName, pathByLabel, gotos, caseGotos);

            var jumpTargets = new HashSet<LabelStatementSyntax>();
            foreach ((GotoLabelStatementSyntax gotoStatement, List<IReadOnlyList<StatementSyntax>> gotoPath) in gotos)
            {
                if (gotoStatement.Name.IsMissing)
                    continue;

                if (!labelsByName.TryGetValue(gotoStatement.Name.Text, out LabelStatementSyntax target))
                {
                    Report(_allLabelNamesEverywhere.Contains(gotoStatement.Name.Text) ? "PEVT3106" : "PEVT3104", gotoStatement.Name.Span);
                    continue;
                }

                if (!IsPrefix(pathByLabel[target], gotoPath))
                {
                    Report("PEVT3107", gotoStatement.Span);
                    continue;
                }

                jumpTargets.Add(target);
            }

            foreach ((GotoCaseStatementSyntax caseGoto, SwitchStatementSyntax enclosingSwitch) in caseGotos)
            {
                if (enclosingSwitch == null)
                {
                    Report("PEVT3111", caseGoto.Span);
                    continue;
                }

                string wanted = Canonicalize(caseGoto.Target.Span);
                bool matched = enclosingSwitch.Arms.OfType<CaseArmSyntax>().Any(arm => Canonicalize(arm.Value.Span) == wanted);
                if (!matched)
                    Report("PEVT3112", caseGoto.Span);
            }

            return jumpTargets;
        }

        private void CollectLabelsAndGotos(IReadOnlyList<StatementSyntax> statements, List<IReadOnlyList<StatementSyntax>> path, SwitchStatementSyntax enclosingSwitch,
            Dictionary<string, LabelStatementSyntax> labelsByName, Dictionary<LabelStatementSyntax, List<IReadOnlyList<StatementSyntax>>> pathByLabel,
            List<(GotoLabelStatementSyntax, List<IReadOnlyList<StatementSyntax>>)> gotos, List<(GotoCaseStatementSyntax, SwitchStatementSyntax)> caseGotos)
        {
            foreach (StatementSyntax statement in statements)
            {
                switch (statement)
                {
                    case LabelStatementSyntax label:
                        pathByLabel[label] = path;
                        if (label.Name.IsMissing)
                            break;
                        if (labelsByName.ContainsKey(label.Name.Text))
                            Report("PEVT3003", label.Name.Span);
                        else
                            labelsByName[label.Name.Text] = label;
                        break;

                    case GotoLabelStatementSyntax gotoLabel:
                        gotos.Add((gotoLabel, path));
                        break;

                    case GotoCaseStatementSyntax gotoCase:
                        caseGotos.Add((gotoCase, enclosingSwitch));
                        break;

                    case IfStatementSyntax ifStatement:
                        CollectLabelsAndGotos(ifStatement.Body, Extend(path, ifStatement.Body), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            CollectLabelsAndGotos(elif.Body, Extend(path, elif.Body), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        if (ifStatement.ElseClause != null)
                            CollectLabelsAndGotos(ifStatement.ElseClause.Body, Extend(path, ifStatement.ElseClause.Body), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        break;

                    case IfDefStatementSyntax ifDefStatement:
                        CollectLabelsAndGotos(ifDefStatement.Body, Extend(path, ifDefStatement.Body), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        if (ifDefStatement.HasElse)
                            CollectLabelsAndGotos(ifDefStatement.ElseBody, Extend(path, ifDefStatement.ElseBody), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        break;

                    case WhileStatementSyntax whileStatement:
                        CollectLabelsAndGotos(whileStatement.Body, Extend(path, whileStatement.Body), enclosingSwitch, labelsByName, pathByLabel, gotos, caseGotos);
                        break;

                    case SwitchStatementSyntax switchStatement:
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                            CollectLabelsAndGotos(arm.Body, Extend(path, arm.Body), switchStatement, labelsByName, pathByLabel, gotos, caseGotos);
                        break;

                    // BlockDefinitionStatementSyntax：块体是独立的根，由 AnalyzeBlocksRecursively 另行调用 AnalyzeRoot。
                }
            }
        }

        private static List<IReadOnlyList<StatementSyntax>> Extend(List<IReadOnlyList<StatementSyntax>> path, IReadOnlyList<StatementSyntax> next)
        {
            var extended = new List<IReadOnlyList<StatementSyntax>>(path) { next };
            return extended;
        }

        /// <summary>PEVT3107 的核心判定：标签所在的结构路径必须是 goto 所在路径的前缀——只允许向外/
        /// 向同层跳，不允许跳进兄弟分支或更深的嵌套结构。路径以语句列表的对象引用逐段比较。</summary>
        private static bool IsPrefix(List<IReadOnlyList<StatementSyntax>> possiblePrefix, List<IReadOnlyList<StatementSyntax>> path)
        {
            if (possiblePrefix.Count > path.Count)
                return false;
            for (int i = 0; i < possiblePrefix.Count; i++)
            {
                if (!ReferenceEquals(possiblePrefix[i], path[i]))
                    return false;
            }
            return true;
        }

        /// <summary>6.2 节重复 case 检测（阶段 6）用的同一种"忽略空白比较 token 序列"近似实现，
        /// 这里复用给 6.5 节的 <c>goto</c> 表达式匹配。</summary>
        private string Canonicalize(TextSpan span)
        {
            string text = _source.GetText(span);
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            return builder.ToString();
        }
    }
}
