using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Syntax
{
    /// <summary>
    /// 一条资源引用字面量的解析结果（PEVT-E08）。<see cref="Kind"/> 为 null 表示这条引用形状非法——
    /// 前缀不认识（PEVT1115）或前缀认得但剩余部分不对（PEVT1116），两种情况下其余字段都不可用。
    /// </summary>
    public sealed class ResourceReferenceSyntax : SyntaxNode
    {
        public SyntaxToken Literal { get; }

        /// <summary><c>actor</c>/<c>image</c>/<c>sound</c>/<c>voice</c>/<c>ambience</c>/<c>music</c> 之一；形状非法时为 null。</summary>
        public string Kind { get; }

        /// <summary>仅 <c>actor</c> 引用：最终人物 ID。</summary>
        public string ActorId { get; }

        /// <summary>仅 <c>actor</c> 引用：appearance ID。</summary>
        public string AppearanceId { get; }

        /// <summary>其余引用种类：冒号之后的资源 ID。</summary>
        public string AssetId { get; }

        public ResourceReferenceSyntax(SyntaxToken literal, string kind, string actorId, string appearanceId, string assetId)
        {
            Literal = literal;
            Kind = kind;
            ActorId = actorId;
            AppearanceId = appearanceId;
            AssetId = assetId;
        }

        public override TextSpan Span => Literal.Span;

        public override string ToString() => Literal.Text;
    }

    /// <summary>
    /// 文件级资源预载组声明 <c>resources "groupId"(引用...)</c>（PEVT-E08）。和 <c>id</c>/<c>enable</c>
    /// 一样是文件头声明，不是事件语句——预载在加载阶段就要开始，不等事件正文跑到对应语句。
    /// </summary>
    public sealed class ResourcesDeclarationSyntax : SyntaxNode
    {
        public SyntaxToken ResourcesKeyword { get; }
        public SyntaxToken GroupId { get; }
        public SyntaxToken OpenParen { get; }
        public IReadOnlyList<ResourceReferenceSyntax> Members { get; }
        public IReadOnlyList<SyntaxToken> Commas { get; }
        public SyntaxToken CloseParen { get; }

        public ResourcesDeclarationSyntax(
            SyntaxToken resourcesKeyword,
            SyntaxToken groupId,
            SyntaxToken openParen,
            IReadOnlyList<ResourceReferenceSyntax> members,
            IReadOnlyList<SyntaxToken> commas,
            SyntaxToken closeParen)
        {
            ResourcesKeyword = resourcesKeyword;
            GroupId = groupId;
            OpenParen = openParen;
            Members = SyntaxCollections.Freeze(members);
            Commas = SyntaxCollections.Freeze(commas);
            CloseParen = closeParen;
        }

        public override TextSpan Span => TextSpan.FromBounds(ResourcesKeyword.Span.Start, CloseParen.Span.End);

        public override string ToString() => $"Resources({GroupId.Text}, [{string.Join(", ", Members)}])";
    }

    /// <summary>
    /// 资源引用字面量 <c>"kind:..."</c> 的纯字符串解析（PEVT-E08）。不依赖任何解析器状态，
    /// 供词法/语法层与运行时编译共用，保证两处对"这条引用合不合法"得到完全一致的判断。
    /// </summary>
    public static class ResourceReferenceFacts
    {
        /// <summary><c>actor</c> 引用把 appearance 之外的部分再拆成 <c>&lt;namespace&gt;:&lt;local-id&gt;</c>，
        /// 因此按第一个冒号切出 kind 前缀不会和人物 ID 自己的冒号混淆。</summary>
        public static readonly IReadOnlyList<string> RecognizedKinds =
            new[] { "actor", "image", "sound", "voice", "ambience", "music" };

        public const string ActorKind = "actor";

        /// <summary>只判前缀（冒号之前的部分）是否是已知种类；不检查剩余部分的形状。</summary>
        public static string RecognizedKindOf(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            int colon = content.IndexOf(':');
            if (colon <= 0)
                return null;

            string candidate = content.Substring(0, colon);
            foreach (string kind in RecognizedKinds)
            {
                if (kind == candidate)
                    return kind;
            }

            return null;
        }

        /// <summary>
        /// 完整解析：<c>actor:&lt;namespace&gt;:&lt;local-id&gt;/&lt;appearanceId&gt;</c> 或
        /// <c>&lt;kind&gt;:&lt;assetId&gt;</c>（image/sound/voice/ambience/music）。
        /// 前缀不认识或剩余部分为空/形状不对都返回 false。
        /// </summary>
        public static bool TryParse(string content, out string kind, out string actorId, out string appearanceId, out string assetId)
        {
            kind = actorId = appearanceId = assetId = null;

            string recognizedKind = RecognizedKindOf(content);
            if (recognizedKind == null)
                return false;

            string remainder = content.Substring(recognizedKind.Length + 1);
            if (remainder.Length == 0)
                return false;

            if (recognizedKind == ActorKind)
            {
                int slash = remainder.IndexOf('/');
                if (slash <= 0 || slash == remainder.Length - 1)
                    return false;

                actorId = remainder.Substring(0, slash);
                appearanceId = remainder.Substring(slash + 1);
            }
            else
            {
                assetId = remainder;
            }

            kind = recognizedKind;
            return true;
        }
    }
}
