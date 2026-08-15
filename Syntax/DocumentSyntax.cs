using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 整份 .pevt 源文件的解析结果：<c>id</c> 声明（可能因为缺失/错位而为 null）、紧随其后的
    /// <c>enable</c> 声明区，以及文件外层事件的语句列表。<see cref="IdDeclaration"/> 为 null 时，
    /// 诊断包里已经有对应的 PEVT1101/1102 说明原因——本节点不重复编码"为什么"。
    /// </summary>
    public sealed class DocumentSyntax : SyntaxNode
    {
        public IdDeclarationSyntax IdDeclaration { get; }
        public IReadOnlyList<EnableDeclarationSyntax> EnableDeclarations { get; }
        public IReadOnlyList<StatementSyntax> Statements { get; }
        public SyntaxToken EndOfFile { get; }

        public DocumentSyntax(IdDeclarationSyntax idDeclaration, IReadOnlyList<EnableDeclarationSyntax> enableDeclarations,
            IReadOnlyList<StatementSyntax> statements, SyntaxToken endOfFile)
        {
            IdDeclaration = idDeclaration;
            EnableDeclarations = enableDeclarations;
            Statements = statements;
            EndOfFile = endOfFile;
        }

        public override TextSpan Span => new TextSpan(0, EndOfFile.Span.End);

        public override string ToString()
        {
            var builder = new StringBuilder("Document(");
            builder.Append(IdDeclaration == null ? "<no-id>" : IdDeclaration.ToString());
            foreach (EnableDeclarationSyntax enable in EnableDeclarations)
                builder.Append(", ").Append(enable);
            builder.Append(", [").Append(string.Join(", ", Statements)).Append("])");
            return builder.ToString();
        }
    }
}
