using System.Collections.Generic;
using Polaris.Pevt.Diagnostics;

namespace Polaris.Pevt.Text
{
    /// <summary>
    /// <see cref="SourceText.FromUtf8"/> 的结果。编码失败时 <see cref="Text"/> 为 null 且
    /// <see cref="Diagnostics"/> 至少包含一条 Error；成功时 <see cref="Text"/> 非 null，
    /// <see cref="Diagnostics"/> 通常为空（本阶段的解码检查只产生编码相关诊断）。
    /// </summary>
    public sealed class SourceTextLoadResult
    {
        public SourceText Text { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Success => Text != null;

        internal SourceTextLoadResult(SourceText text, IReadOnlyList<Diagnostic> diagnostics)
        {
            Text = text;
            Diagnostics = diagnostics;
        }
    }
}
