using System.Collections.Generic;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// PEVT-E08：一条已编译的资源引用（<c>resources</c> 声明里的一个成员）。
    /// 静态形状（前缀、actor 引用的 "/" 切分）已经在解析阶段核对过，这里只是把
    /// <see cref="Syntax.ResourceReferenceSyntax"/> 的事实原样搬进运行时编译产物。
    /// </summary>
    public sealed class PevtResourceMember
    {
        public string Kind { get; }

        /// <summary><c>actor</c> 引用：最终人物 ID。其余种类为空串。</summary>
        public string ActorId { get; }

        /// <summary><c>actor</c> 引用：appearance ID。其余种类为空串。</summary>
        public string AppearanceId { get; }

        /// <summary>其余种类：冒号之后的资源 ID。<c>actor</c> 引用为空串。</summary>
        public string AssetId { get; }

        /// <summary>原始字面量文本，供 F8 与诊断展示。</summary>
        public string RawText { get; }

        internal PevtResourceMember(string kind, string actorId, string appearanceId, string assetId, string rawText)
        {
            Kind = kind ?? string.Empty;
            ActorId = actorId ?? string.Empty;
            AppearanceId = appearanceId ?? string.Empty;
            AssetId = assetId ?? string.Empty;
            RawText = rawText ?? string.Empty;
        }

        public override string ToString() => RawText;
    }

    /// <summary>PEVT-E08：一个已编译的资源预载组，对应源码里一段 <c>resources "id"(...)</c>。</summary>
    public sealed class PevtResourceGroupInfo
    {
        public string GroupId { get; }

        public IReadOnlyList<PevtResourceMember> Members { get; }

        internal PevtResourceGroupInfo(string groupId, IReadOnlyList<PevtResourceMember> members)
        {
            GroupId = groupId ?? string.Empty;
            Members = members;
        }

        public override string ToString() => $"{GroupId} ({Members.Count})";
    }
}
