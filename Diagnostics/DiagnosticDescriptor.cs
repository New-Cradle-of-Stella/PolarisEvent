using System;

namespace Polaris.Pevt.Diagnostics
{
    /// <summary>
    /// PEVTxxxx 诊断目录中一条固定元数据：编号、名称、级别与默认消息，来源为 PEVT-静态诊断表.md。
    /// 与运行时生成、带位置信息的 <see cref="Diagnostic"/> 实例不同，本类型只持有规范已经分配好的静态含义。
    /// </summary>
    public sealed class DiagnosticDescriptor
    {
        public string Id { get; }

        public string Name { get; }

        public DiagnosticSeverity Severity { get; }

        public string DefaultMessage { get; }

        public DiagnosticDescriptor(string id, string name, DiagnosticSeverity severity, string defaultMessage)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("诊断编号不能为空。", nameof(id));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("诊断名称不能为空。", nameof(name));

            Id = id;
            Name = name;
            Severity = severity;
            DefaultMessage = defaultMessage ?? throw new ArgumentNullException(nameof(defaultMessage));
        }
    }
}
