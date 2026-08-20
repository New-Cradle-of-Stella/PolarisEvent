using System.Collections.Generic;
using System.Text;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 整份 .pevt 源文件的解析结果：<c>id</c> 声明、紧随其后的 <c>enable</c> 声明区，以及文件外层事件的语句列表。
    /// <see cref="IdDeclaration"/> 为 null 的原因由诊断包里的 PEVT1101/1102 说明。
    /// </summary>
    public sealed class DocumentSyntax : SyntaxNode
    {
        public IdDeclarationSyntax IdDeclaration { get; }
        public IReadOnlyList<EnableDeclarationSyntax> EnableDeclarations { get; }

        /// <summary>PEVT-E08：文件头资源预载组声明，紧跟 <c>enable</c> 区域之后。</summary>
        public IReadOnlyList<ResourcesDeclarationSyntax> ResourceGroups { get; }

        public IReadOnlyList<StatementSyntax> Statements { get; }
        public SyntaxToken EndOfFile { get; }

        public DocumentSyntax(IdDeclarationSyntax idDeclaration, IReadOnlyList<EnableDeclarationSyntax> enableDeclarations,
            IReadOnlyList<ResourcesDeclarationSyntax> resourceGroups,
            IReadOnlyList<StatementSyntax> statements, SyntaxToken endOfFile)
        {
            IdDeclaration = idDeclaration;
            EnableDeclarations = SyntaxCollections.Freeze(enableDeclarations);
            ResourceGroups = SyntaxCollections.Freeze(resourceGroups);
            Statements = SyntaxCollections.Freeze(statements);
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
