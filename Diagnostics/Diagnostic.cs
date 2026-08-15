using System;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Diagnostics
{
    /// <summary>
    /// 一条不可变诊断记录。<see cref="Id"/> 使用规范中已分配的编号（例如 <c>PEVT1009</c>）；本类型本身
    /// 不维护编号目录——编号、名称与默认消息的集中目录由后续阶段的 DiagnosticCatalog 实现。
    /// </summary>
    public sealed class Diagnostic : IEquatable<Diagnostic>
    {
        public string Id { get; }

        public DiagnosticSeverity Severity { get; }

        public string Message { get; }

        /// <summary>诊断位置。加载器内部错误等极少数情况下允许为 null。</summary>
        public TextLocation Location { get; }

        public Diagnostic(string id, DiagnosticSeverity severity, string message, TextLocation location)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("诊断编号不能为空。", nameof(id));

            Id = id;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Location = location;
        }

        public bool Equals(Diagnostic other)
        {
            if (other is null)
                return false;
            return Id == other.Id
                && Severity == other.Severity
                && Message == other.Message
                && Equals(Location, other.Location);
        }

        public override bool Equals(object obj) => Equals(obj as Diagnostic);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id.GetHashCode();
                hash = (hash * 397) ^ Severity.GetHashCode();
                hash = (hash * 397) ^ Message.GetHashCode();
                hash = (hash * 397) ^ (Location?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString() =>
            Location != null
                ? $"{Location}: {Severity} {Id}: {Message}"
                : $"{Severity} {Id}: {Message}";
    }
}
