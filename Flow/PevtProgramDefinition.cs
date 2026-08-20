using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Flow
{
    /// <summary>
    /// 外层事件环境里的一个变量/常量声明槽位（9.1/9.2/9.4 节）。分支与循环语句不创建新环境，
    /// 因此嵌套在它们正文里的声明仍属于同一张符号槽表；自定义事件块有自己独立的环境，不在此列。
    /// </summary>
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
    /// 一个事件文件通过全部静态检查后的不可变产物：事件 ID、文件级能力标记、外层事件的符号槽表和源码哈希。
    /// 语法树本身已经不可变并携带源码跨度，因此不再复制一份平行的绑定节点结构；只有零 Error 级诊断时才允许产出。
    /// </summary>
    public sealed class PevtProgramDefinition
    {
        public string EventId { get; }
        public bool HasCsCapability { get; }
        public bool HasAsyncCapability { get; }
        public bool HasCmdArgCapability { get; }
        public DocumentSyntax Document { get; }
        public SourceText Source { get; }
        public IReadOnlyList<ProgramSymbolSlot> TopLevelSymbols { get; }

        /// <summary>源文本（不含 BOM）UTF-8 字节的 SHA-256，十六进制小写表示。</summary>
        public string SourceHash { get; }

        private PevtProgramDefinition(string eventId, bool hasCsCapability, bool hasAsyncCapability, bool hasCmdArgCapability,
            DocumentSyntax document, SourceText source, IReadOnlyList<ProgramSymbolSlot> topLevelSymbols, string sourceHash)
        {
            EventId = eventId;
            HasCsCapability = hasCsCapability;
            HasAsyncCapability = hasAsyncCapability;
            HasCmdArgCapability = hasCmdArgCapability;
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
            bool hasCmdArg = document.EnableDeclarations.Any(e => e.Capability.Kind == SyntaxKind.CmdArgKeyword);
            var symbols = new List<ProgramSymbolSlot>();
            CollectTopLevelSymbols(document.Statements, symbols);
            string hash = ComputeHash(source.Content);

            return new PevtProgramDefinition(eventId, hasCs, hasAsync, hasCmdArg, document, source, symbols, hash);
        }

        /// <summary>
        /// 递归进入 if/elif/else/while/switch 的正文（它们与外层事件共用同一个环境，9.4 节），
        /// 但不进入 <c>BlockDefinitionStatementSyntax</c> 的块体——那是一个完全独立的符号环境。
        /// </summary>
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
                    case IfDefStatementSyntax ifDefStatement:
                        CollectTopLevelSymbols(ifDefStatement.Body, symbols);
                        if (ifDefStatement.HasElse)
                            CollectTopLevelSymbols(ifDefStatement.ElseBody, symbols);
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
