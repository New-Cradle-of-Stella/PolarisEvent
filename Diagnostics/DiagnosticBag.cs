using System;
using System.Collections.Generic;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Diagnostics
{
    /// <summary>
    /// 诊断收集器。加载器、词法器、解析器与后续静态分析阶段在处理过程中把诊断累加到同一个包里，
    /// 结束后用 <see cref="ToReadOnly"/> 取一份不再随本包变化的快照。
    /// </summary>
    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        public int Count => _diagnostics.Count;

        public bool HasErrors { get; private set; }

        public void Add(Diagnostic diagnostic)
        {
            if (diagnostic == null)
                throw new ArgumentNullException(nameof(diagnostic));

            _diagnostics.Add(diagnostic);
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                HasErrors = true;
        }

        public void AddError(string id, string message, TextLocation location) =>
            Add(new Diagnostic(id, DiagnosticSeverity.Error, message, location));

        public void AddWarning(string id, string message, TextLocation location) =>
            Add(new Diagnostic(id, DiagnosticSeverity.Warning, message, location));

        /// <summary>返回当前已收集诊断的快照数组。之后对本包的修改不会影响已返回的快照。</summary>
        public IReadOnlyList<Diagnostic> ToReadOnly() => _diagnostics.ToArray();
    }
}
