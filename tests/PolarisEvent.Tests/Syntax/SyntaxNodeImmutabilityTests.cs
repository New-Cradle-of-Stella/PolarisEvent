using System.Collections.Generic;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Syntax
{
    /// <summary>
    /// G5 深不可变门：每个持有集合的语法节点都必须防御"调用者事后修改自己手里的 List/array"这种别名攻击，
    /// 而不能只有 get-only 属性。这里用可变 <see cref="List{T}"/> 直接构造节点，构造后修改原始列表，验证节点内容与枚举结果都不变。
    /// </summary>
    public class SyntaxNodeImmutabilityTests
    {
        private static SyntaxToken Token(SyntaxKind kind, string text) =>
            new SyntaxToken(kind, new TextSpan(0, text.Length), text, TokenValue.None);

        [Fact]
        public void DocumentSyntax_MutatingOriginalListsAfterConstruction_DoesNotAffectDocument()
        {
            var enables = new List<EnableDeclarationSyntax> { new EnableDeclarationSyntax(Token(SyntaxKind.EnableKeyword, "enable"), Token(SyntaxKind.CsKeyword, "cs")) };
            var statements = new List<StatementSyntax> { new EndStatementSyntax(Token(SyntaxKind.EndKeyword, "end")) };

            var document = new DocumentSyntax(null, enables, statements, Token(SyntaxKind.EndOfFileToken, ""));

            enables.Clear();
            statements.Clear();
            statements.Add(new EndStatementSyntax(Token(SyntaxKind.EndKeyword, "end")));

            Assert.Single(document.EnableDeclarations);
            Assert.Single(document.Statements);
        }

        [Fact]
        public void IfStatementSyntax_MutatingOriginalBodyAndElifListsAfterConstruction_DoesNotAffectNode()
        {
            var body = new List<StatementSyntax> { new EndStatementSyntax(Token(SyntaxKind.EndKeyword, "end")) };
            var elifClauses = new List<ElifClauseSyntax>
            {
                new ElifClauseSyntax(Token(SyntaxKind.ElifKeyword, "elif"), new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "b")), new List<StatementSyntax>()),
            };

            var ifStatement = new IfStatementSyntax(
                Token(SyntaxKind.IfKeyword, "if"),
                new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "a")),
                body,
                elifClauses,
                null,
                Token(SyntaxKind.EndIfKeyword, "endif"));

            body.Clear();
            elifClauses.Clear();

            Assert.Single(ifStatement.Body);
            Assert.Single(ifStatement.ElifClauses);
        }

        [Fact]
        public void ArgumentListSyntax_MutatingOriginalArgumentsAfterConstruction_DoesNotAffectNode()
        {
            var arguments = new List<ExpressionSyntax> { new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "a")) };
            var commas = new List<SyntaxToken>();

            var argumentList = new ArgumentListSyntax(Token(SyntaxKind.OpenParenToken, "("), arguments, commas, Token(SyntaxKind.CloseParenToken, ")"));

            arguments.Add(new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "b")));

            Assert.Single(argumentList.Arguments);
        }

        [Fact]
        public void ChainedBinaryExpressionSyntax_MutatingOriginalSegmentsAfterConstruction_DoesNotAffectNode()
        {
            var segments = new List<BinaryChainSegment>
            {
                new BinaryChainSegment(Token(SyntaxKind.PlusToken, "+"), new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "b"))),
            };

            var chain = new ChainedBinaryExpressionSyntax(new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "a")), segments);

            segments.Clear();

            Assert.Single(chain.Segments);
        }

        [Fact]
        public void IdentifierListSyntax_MutatingOriginalIdentifiersAfterConstruction_DoesNotAffectNode()
        {
            var identifiers = new List<SyntaxToken> { Token(SyntaxKind.IdentifierToken, "a") };
            var commas = new List<SyntaxToken>();

            var list = new IdentifierListSyntax(Token(SyntaxKind.OpenParenToken, "("), identifiers, commas, Token(SyntaxKind.CloseParenToken, ")"));

            identifiers.Add(Token(SyntaxKind.IdentifierToken, "b"));

            Assert.Single(list.Identifiers);
        }

        [Fact]
        public void SwitchStatementSyntax_MutatingOriginalArmsAfterConstruction_DoesNotAffectNode()
        {
            var arms = new List<SwitchArmSyntax>
            {
                new DefaultArmSyntax(Token(SyntaxKind.DefaultKeyword, "default"), new List<StatementSyntax> { new EndStatementSyntax(Token(SyntaxKind.EndKeyword, "end")) }),
            };

            var switchStatement = new SwitchStatementSyntax(
                Token(SyntaxKind.SwitchKeyword, "switch"),
                new NameExpressionSyntax(Token(SyntaxKind.IdentifierToken, "x")),
                arms,
                Token(SyntaxKind.EndSwitchKeyword, "endswitch"));

            arms.Clear();

            Assert.Single(switchStatement.Arms);
        }

        [Fact]
        public void ParameterListSyntax_MutatingOriginalParametersAfterConstruction_DoesNotAffectNode()
        {
            var parameters = new List<ParameterSyntax>
            {
                new ParameterSyntax(Token(SyntaxKind.IdentifierToken, "x"), Token(SyntaxKind.ColonToken, ":"), Token(SyntaxKind.IntKeyword, "int")),
            };
            var commas = new List<SyntaxToken>();

            var parameterList = new ParameterListSyntax(Token(SyntaxKind.OpenParenToken, "("), parameters, commas, Token(SyntaxKind.CloseParenToken, ")"));

            parameters.Clear();

            Assert.Single(parameterList.Parameters);
        }
    }
}
