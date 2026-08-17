using System;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 人物目录的命名规则集中处。人物 ID 与事件 ID 不共用字符规则（见 PEVT-嵌入注册与ID冲突规范.md 第 10 节），
    /// 因此这里独立定义，不复用 <c>SyntaxFacts</c> 的 PEVT 标识符判定。
    /// </summary>
    public static class ActorNaming
    {
        /// <summary>原版固定人物的封闭命名空间；外部程序集不能注册、覆盖或扩展它。</summary>
        public const string BuiltInNamespace = "aic";

        /// <summary>最终人物 ID 的分隔符：<c>&lt;namespace&gt;:&lt;local-id&gt;</c>。</summary>
        public const char NamespaceSeparator = ':';

        /// <summary>人物 ID 固定使用大小写敏感的序数比较。</summary>
        public static StringComparer IdComparer => StringComparer.Ordinal;

        /// <summary>
        /// 目录命名空间：一个或多个以 <c>.</c> 连接的小写 ASCII 标识段，每段以字母开头，
        /// 后续字符只能是小写字母、数字或下划线。
        /// </summary>
        public static bool IsValidNamespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            int segmentStart = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i == value.Length || value[i] == '.')
                {
                    if (!IsValidNamespaceSegment(value, segmentStart, i))
                        return false;
                    segmentStart = i + 1;
                }
            }

            return true;
        }

        private static bool IsValidNamespaceSegment(string value, int start, int end)
        {
            if (end <= start)
                return false;
            if (value[start] < 'a' || value[start] > 'z')
                return false;

            for (int i = start + 1; i < end; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 局部 ID（人物、portrait、ui-portrait、appearance、anchor）：以小写字母开头，其余为小写字母、数字、<c>-</c> 或 <c>_</c>，
        /// 且不以 <c>-</c> 结尾。内置目录里的 <c>noel-father</c>、<c>first-human</c> 就是这条规则的实例。
        /// </summary>
        public static bool IsValidLocalId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            if (value[0] < 'a' || value[0] > 'z')
                return false;
            if (value[value.Length - 1] == '-')
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 原版 CMD 短键：ASCII 可见字符且不含空白与 <c>:</c>。只用于内置目录的 <c>LegacyPerson</c> 记录，
        /// 不作为公开人物 ID 使用。
        /// </summary>
        public static bool IsValidLegacyPerson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char c in value)
            {
                if (c <= ' ' || c > '~' || c == NamespaceSeparator)
                    return false;
            }

            return true;
        }

        /// <summary>组合最终人物 ID。调用方负责先校验两段的合法性。</summary>
        public static string ComposeId(string catalogNamespace, string localId)
        {
            if (catalogNamespace == null)
                throw new ArgumentNullException(nameof(catalogNamespace));
            if (localId == null)
                throw new ArgumentNullException(nameof(localId));

            return catalogNamespace + NamespaceSeparator + localId;
        }

        /// <summary>把最终人物 ID 拆回命名空间与局部 ID；格式不符时返回 false 且不抛异常。</summary>
        public static bool TrySplitId(string actorId, out string catalogNamespace, out string localId)
        {
            catalogNamespace = null;
            localId = null;

            if (string.IsNullOrEmpty(actorId))
                return false;

            int separator = actorId.IndexOf(NamespaceSeparator);
            if (separator <= 0 || separator == actorId.Length - 1)
                return false;
            if (actorId.IndexOf(NamespaceSeparator, separator + 1) >= 0)
                return false;

            catalogNamespace = actorId.Substring(0, separator);
            localId = actorId.Substring(separator + 1);
            return true;
        }

        /// <summary>最终人物 ID 是否同时满足命名空间与局部 ID 规则。</summary>
        public static bool IsValidActorId(string actorId) =>
            TrySplitId(actorId, out string ns, out string local) && IsValidNamespace(ns) && IsValidLocalId(local);
    }
}
