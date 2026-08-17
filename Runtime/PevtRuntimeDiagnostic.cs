using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>调用栈里一层的种类。</summary>
    public enum PevtCallFrameKind
    {
        /// <summary>文件外层事件。</summary>
        Event,

        /// <summary>自定义事件块。</summary>
        Block,

        /// <summary>同步 <c>@</c> 指令帧。</summary>
        Command,

        /// <summary>协程（同步子例程或异步子例程）。</summary>
        Routine,
    }

    /// <summary>诊断调用栈里的一层。保存到事件 ID、模组、相对路径与源码行列，便于回溯。</summary>
    public sealed class PevtCallFrame
    {
        public PevtCallFrameKind Kind { get; }

        /// <summary>事件 ID、事件块名或 <c>@</c> 名称。</summary>
        public string Name { get; }

        public TextLocation Location { get; }

        public PevtCallFrame(PevtCallFrameKind kind, string name, TextLocation location)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            Location = location;
        }

        public override string ToString() =>
            Location != null ? $"{Kind} {Name} ({Location})" : $"{Kind} {Name}";
    }

    /// <summary>
    /// 一条运行诊断，编号必须来自 <see cref="RuntimeDiagnosticCatalog"/>，运行时不允许临时另造编号。
    /// 保存源码调用栈和内部异常链，使运行错误能回溯到事件 ID、相对路径、源码行列和事件/块/协程调用栈。
    /// </summary>
    public sealed class PevtRuntimeDiagnostic
    {
        public string Id { get; }

        public string Name { get; }

        public DiagnosticSeverity Severity { get; }

        public string Message { get; }

        public TextLocation Location { get; }

        /// <summary>从最内层到最外层的调用栈快照。</summary>
        public IReadOnlyList<PevtCallFrame> CallStack { get; }

        /// <summary>更根本的原因：被包装的运行诊断，或调度边界捕获到的 C# 异常。</summary>
        public PevtRuntimeDiagnostic InnerDiagnostic { get; }

        public Exception InnerException { get; }

        public PevtRuntimeDiagnostic(
            string id,
            string message,
            TextLocation location = null,
            IEnumerable<PevtCallFrame> callStack = null,
            PevtRuntimeDiagnostic innerDiagnostic = null,
            Exception innerException = null)
        {
            DiagnosticDescriptor descriptor = RuntimeDiagnosticCatalog.Require(id);

            Id = descriptor.Id;
            Name = descriptor.Name;
            Severity = descriptor.Severity;
            Message = string.IsNullOrEmpty(message) ? descriptor.DefaultMessage : message;
            Location = location;
            InnerDiagnostic = innerDiagnostic;
            InnerException = innerException;

            var frames = new List<PevtCallFrame>();
            if (callStack != null)
                frames.AddRange(callStack);
            CallStack = new ReadOnlyCollection<PevtCallFrame>(frames);
        }

        /// <summary>把本诊断连同调用栈和原因链渲染成可读文本。</summary>
        public string Describe()
        {
            var builder = new StringBuilder();
            builder.Append(Severity).Append(' ').Append(Id).Append(' ').Append(Name).Append(": ").Append(Message);
            if (Location != null)
                builder.Append(" @ ").Append(Location);

            foreach (PevtCallFrame frame in CallStack)
                builder.AppendLine().Append("  at ").Append(frame);

            if (InnerDiagnostic != null)
                builder.AppendLine().Append("  <- ").Append(InnerDiagnostic.Describe());
            else if (InnerException != null)
                builder.AppendLine().Append("  <- ").Append(InnerException.GetType().Name).Append(": ").Append(InnerException.Message);

            return builder.ToString();
        }

        public override string ToString() => Describe();
    }
}
