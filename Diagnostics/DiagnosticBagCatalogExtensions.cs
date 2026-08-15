using System;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Diagnostics
{
    /// <summary>
    /// 让词法/语法阶段按目录编号直接上报诊断，不必在每个调用点重复拼接级别与默认消息。
    /// 独立成新文件而不是改 <see cref="DiagnosticBag"/> 本体，是为了不触碰阶段 1 已跟踪的文件
    /// （见交付记录里 stage-whitespace 对已跟踪文件的已知误报）。
    /// </summary>
    public static class DiagnosticBagCatalogExtensions
    {
        public static void AddFromCatalog(this DiagnosticBag bag, string diagnosticId, TextLocation location, string message = null)
        {
            DiagnosticDescriptor descriptor = DiagnosticCatalog.Find(diagnosticId)
                ?? throw new ArgumentException($"未知诊断编号: {diagnosticId}", nameof(diagnosticId));
            bag.Add(new Diagnostic(descriptor.Id, descriptor.Severity, message ?? descriptor.DefaultMessage, location));
        }
    }
}
