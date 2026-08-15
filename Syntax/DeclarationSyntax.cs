using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>文件头 <c>id "事件ID"</c> 声明（2 节）。不是 <see cref="StatementSyntax"/>：文档语法把它
    /// 单独列在事件语句之外。</summary>
    public sealed class IdDeclarationSyntax : SyntaxNode
    {
        public SyntaxToken IdKeyword { get; }
        public SyntaxToken Value { get; }

        public IdDeclarationSyntax(SyntaxToken idKeyword, SyntaxToken value)
        {
            IdKeyword = idKeyword;
            Value = value;
        }

        public override TextSpan Span => TextSpan.FromBounds(IdKeyword.Span.Start, Value.Span.End);

        public override string ToString() => $"Id({Value.Text})";
    }

    /// <summary>文件级能力声明 <c>enable cs</c> / <c>enable async</c>（2.1 节）。</summary>
    public sealed class EnableDeclarationSyntax : SyntaxNode
    {
        public SyntaxToken EnableKeyword { get; }
        public SyntaxToken Capability { get; }

        public EnableDeclarationSyntax(SyntaxToken enableKeyword, SyntaxToken capability)
        {
            EnableKeyword = enableKeyword;
            Capability = capability;
        }

        public override TextSpan Span => TextSpan.FromBounds(EnableKeyword.Span.Start, Capability.Span.End);

        public override string ToString() => $"Enable({Capability.Text})";
    }

    /// <summary>变量声明 <c>var 名 : 类型 [= 表达式]</c>（9.1 节）；初始化器可选。</summary>
    public sealed class VariableDeclarationSyntax : StatementSyntax
    {
        public SyntaxToken VarKeyword { get; }
        public SyntaxToken Name { get; }
        public SyntaxToken Colon { get; }
        public SyntaxToken Type { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Initializer { get; }

        public VariableDeclarationSyntax(SyntaxToken varKeyword, SyntaxToken name, SyntaxToken colon, SyntaxToken type,
            SyntaxToken equalsToken, ExpressionSyntax initializer)
        {
            VarKeyword = varKeyword;
            Name = name;
            Colon = colon;
            Type = type;
            EqualsToken = equalsToken;
            Initializer = initializer;
        }

        public override TextSpan Span => TextSpan.FromBounds(VarKeyword.Span.Start, (Initializer?.Span ?? Type.Span).End);

        public override string ToString() => Initializer == null ? $"Var({Name.Text}, {Type.Text})" : $"Var({Name.Text}, {Type.Text}, {Initializer})";
    }

    /// <summary>常量声明 <c>const 名 : 类型 = 表达式</c>（9.2 节）；初始化器必须存在。</summary>
    public sealed class ConstantDeclarationSyntax : StatementSyntax
    {
        public SyntaxToken ConstKeyword { get; }
        public SyntaxToken Name { get; }
        public SyntaxToken Colon { get; }
        public SyntaxToken Type { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Initializer { get; }

        public ConstantDeclarationSyntax(SyntaxToken constKeyword, SyntaxToken name, SyntaxToken colon, SyntaxToken type,
            SyntaxToken equalsToken, ExpressionSyntax initializer)
        {
            ConstKeyword = constKeyword;
            Name = name;
            Colon = colon;
            Type = type;
            EqualsToken = equalsToken;
            Initializer = initializer;
        }

        public override TextSpan Span => TextSpan.FromBounds(ConstKeyword.Span.Start, Initializer.Span.End);

        public override string ToString() => $"Const({Name.Text}, {Type.Text}, {Initializer})";
    }
}
