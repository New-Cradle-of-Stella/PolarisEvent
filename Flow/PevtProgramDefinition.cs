using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Flow
{
    /// <summary>外层事件环境里的一个变量/常量声明槽位（9.1/9.2/9.4 节）。<c>if</c>/<c>elif</c>/
    /// <c>else</c>/<c>while</c>/<c>switch</c>/<c>case</c>/<c>default</c> 不创建新环境，因此嵌套在
    /// 它们正文里的声明仍然属于外层事件的同一个符号槽表；自定义事件块有自己独立的环境，不在此列。</summary>
    public sealed class ProgramSymbolSlot
    {
        public string Name { get; }

        /// <summary><c>"var"</c> 或 <c>"const"</c>。</summary>
        public string Kind { get; }

        public SyntaxKind DeclaredType { get; }

        public ProgramSymbolSlot(string name, string kind, SyntaxKind declaredType)
        {
            Name = name;
            Kind = kind;
            DeclaredType = declaredType;
        }
    }

    /// <summary>
    /// 一个事件文件完成全部静态检查（词法/语法/绑定/控制流）后的不可变产物。计划要求的"绑定节点/
    /// 源码映射"由已经不可变的 <see cref="Document"/>（语法树本身不可变，且每个节点都携带自己的
    /// <see cref="TextSpan"/>，因此天然就是"源码映射"）承载，而不是另外复制一份平行结构；本类型
    /// 只额外补上运行时真正需要、语法树本身不提供的几样东西：事件 ID、文件级能力标记、外层事件的
    /// 符号槽表（<see cref="TopLevelSymbols"/>，不含各自定义事件块的独立环境），以及用于缓存/变更
    /// 检测的源码哈希。只有零 Error 级诊断（警告不影响）时才允许产出——不可变定义本身就代表"这份
    /// 源码已经通过全部静态门槛，可以安全交给后续阶段（解释器）执行"。
    /// </summary>
    public sealed class PevtProgramDefinition
    {
        public string EventId { get; }
        public bool HasCsCapability { get; }
        public bool HasAsyncCapability { get; }
        public DocumentSyntax Document { get; }
        public SourceText Source { get; }
        public IReadOnlyList<ProgramSymbolSlot> TopLevelSymbols { get; }

        /// <summary>源文本（不含 BOM）UTF-8 字节的 SHA-256，十六进制小写表示。</summary>
        public string SourceHash { get; }

        private PevtProgramDefinition(string eventId, bool hasCsCapability, bool hasAsyncCapability,
            DocumentSyntax document, SourceText source, IReadOnlyList<ProgramSymbolSlot> topLevelSymbols, string sourceHash)
        {
            EventId = eventId;
            HasCsCapability = hasCsCapability;
            HasAsyncCapability = hasAsyncCapability;
            Document = document;
            Source = source;
            TopLevelSymbols = topLevelSymbols;
            SourceHash = sourceHash;
        }

        /// <summary>诊断包里存在任何 Error 级诊断时返回 null——警告（如空正文）不阻止产出。</summary>
        public static PevtProgramDefinition TryBuild(DocumentSyntax document, SourceText source, DiagnosticBag diagnostics)
        {
            if (diagnostics.HasErrors)
                return null;

            string eventId = document.IdDeclaration != null && !document.IdDeclaration.Value.IsMissing
                ? document.IdDeclaration.Value.Value.AsString
                : null;
            bool hasCs = document.EnableDeclarations.Any(e => e.Capability.Kind == SyntaxKind.CsKeyword);
            bool hasAsync = document.EnableDeclarations.Any(e => e.Capability.Kind == SyntaxKind.AsyncKeyword);
            var symbols = new List<ProgramSymbolSlot>();
            CollectTopLevelSymbols(document.Statements, symbols);
            string hash = ComputeHash(source.Content);

            return new PevtProgramDefinition(eventId, hasCs, hasAsync, document, source, symbols, hash);
        }

        /// <summary>递归进入 if/elif/else/while/switch 的正文（它们与外层事件共用同一个环境，9.4 节），
        /// 但不进入 <c>BlockDefinitionStatementSyntax</c> 的块体——那是一个完全独立的符号环境。</summary>
        private static void CollectTopLevelSymbols(IReadOnlyList<StatementSyntax> statements, List<ProgramSymbolSlot> symbols)
        {
            foreach (StatementSyntax statement in statements)
            {
                switch (statement)
                {
                    case VariableDeclarationSyntax variable when !variable.Name.IsMissing:
                        symbols.Add(new ProgramSymbolSlot(variable.Name.Text, "var", variable.Type.Kind));
                        break;
                    case ConstantDeclarationSyntax constant when !constant.Name.IsMissing:
                        symbols.Add(new ProgramSymbolSlot(constant.Name.Text, "const", constant.Type.Kind));
                        break;
                    case IfStatementSyntax ifStatement:
                        CollectTopLevelSymbols(ifStatement.Body, symbols);
                        foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                            CollectTopLevelSymbols(elif.Body, symbols);
                        if (ifStatement.ElseClause != null)
                            CollectTopLevelSymbols(ifStatement.ElseClause.Body, symbols);
                        break;
                    case WhileStatementSyntax whileStatement:
                        CollectTopLevelSymbols(whileStatement.Body, symbols);
                        break;
                    case SwitchStatementSyntax switchStatement:
                        foreach (SwitchArmSyntax arm in switchStatement.Arms)
                            CollectTopLevelSymbols(arm.Body, symbols);
                        break;
                }
            }
        }

        private static string ComputeHash(string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
