using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Loading
{
    /// <summary>
    /// 一份外部导入的 PEVT 源文本：没有经过 PolarisTools 生成器、没有压缩载荷、也没有内容哈希声明，直接以磁盘上（或热重载通道推来）的原始 UTF-8 字节为真相。
    /// 与 <see cref="PevtEmbeddedSource"/> 的差别只有"信封"：嵌入包要校验格式版本、长度与哈希声明，外部源没有这些字段，但解码后走的是完全相同的 <see cref="PevtSourceCompiler"/> 静态校验，不因"本机文件"而放宽检查。
    /// </summary>
    public sealed class PevtExternalSource
    {
        private readonly byte[] _utf8;

        private PevtExternalSource(string displayPath, byte[] utf8)
        {
            DisplayPath = displayPath ?? string.Empty;
            _utf8 = utf8;
            ContentHash = PevtEmbeddedSource.ComputeContentHash(utf8);
        }

        /// <summary>诊断与冲突报告里显示的路径。外部源允许是绝对路径——它本来就在作者的机器上。</summary>
        public string DisplayPath { get; }

        /// <summary>原始 UTF-8 字节的 SHA-256，十六进制小写。与嵌入包的 <c>ContentHash</c> 同一口径。</summary>
        public string ContentHash { get; }

        public int ByteLength => _utf8.Length;

        internal byte[] Utf8 => _utf8;

        /// <summary>以磁盘（或通道）上的原始字节建立外部源。BOM 按嵌入包的约定去掉。</summary>
        public static PevtExternalSource FromBytes(string displayPath, byte[] utf8Bytes)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            return new PevtExternalSource(displayPath, TrimBom(utf8Bytes));
        }

        /// <summary>
        /// 以已经解码好的文本建立外部源（热重载通道送来的就是 .NET 字符串）。
        /// 仍旧编码回 UTF-8 无 BOM 字节再走一遍严格解码，这样"PEVT 永远以 UTF-8 原始字节为真相"
        /// 对推送路径同样成立：孤立代理项这类字符串里合法、UTF-8 里不合法的内容照样被 PEVT9212 挡下。
        /// </summary>
        public static PevtExternalSource FromText(string displayPath, string text) =>
            new PevtExternalSource(displayPath, new UTF8Encoding(false).GetBytes(text ?? string.Empty));

        private static byte[] TrimBom(byte[] bytes)
        {
            if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
                return bytes;

            var trimmed = new byte[bytes.Length - 3];
            Array.Copy(bytes, 3, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        public override string ToString() => $"{DisplayPath} ({ByteLength} bytes)";
    }

    /// <summary>外部源加载失败的分类。<see cref="None"/> 之外的任何值都表示该事件不进入 `/event`。</summary>
    public enum PevtExternalLoadFailure
    {
        None,
        SourceTooLarge,
        InvalidSourceEncoding,
        StaticAnalysis,
    }

    /// <summary>加载一份外部源的结果。失败时保留来源路径与具体诊断，不尝试降级为原版 EV。</summary>
    public sealed class PevtExternalLoadResult
    {
        public PevtExternalSource Source { get; }

        /// <summary>通过全部静态门的不可变程序定义；失败时为 null。</summary>
        public PevtProgramDefinition Definition { get; }

        /// <summary>严格 UTF-8 解码后的源文本；解码之前失败时为 null。</summary>
        public SourceText Text { get; }

        public PevtExternalLoadFailure Failure { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Success => Failure == PevtExternalLoadFailure.None && Definition != null;

        /// <summary>成功时的事件 ID；失败时为空串。外部源没有"声明 ID"，源码 `id` 就是唯一答案。</summary>
        public string EventId => Definition != null ? Definition.EventId : string.Empty;

        internal PevtExternalLoadResult(
            PevtExternalSource source,
            PevtProgramDefinition definition,
            SourceText text,
            PevtExternalLoadFailure failure,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            Source = source;
            Definition = definition;
            Text = text;
            Failure = failure;
            Diagnostics = diagnostics;
        }

        public override string ToString() =>
            Success ? $"{EventId} <- {Source.DisplayPath}" : $"{Source.DisplayPath} -> {Failure}";
    }

    /// <summary>
    /// 外部源加载器：大小上限 → 严格 UTF-8 → 完整静态校验。
    /// 没有"声明 ID 一致性"这一步（外部源没有声明 ID，事件 ID 只来自源码里的 <c>id</c>），但静态校验走的是与嵌入源、与 PolarisTools 编辑器完全相同的 <see cref="PevtSourceCompiler"/>，因此同一份源码在三处得到同一批 PEVTxxxx。
    /// </summary>
    public static class PevtExternalSourceLoader
    {
        private const string SourceTooLarge = "PEVT9211";
        private const string InvalidSourceEncoding = "PEVT9212";

        /// <param name="rawCsAnalyzer">
        /// <c>$raw cs</c> 的 C# 分析器。接上它，外部源里的 C# 内容才会在加载期一并重校
        /// （PEVT8007–8010）；为 null 时那四个编号不产生，其余静态门不受影响。
        /// </param>
        public static PevtExternalLoadResult Load(
            PevtExternalSource source,
            PevtEmbeddedSourceLimits limits = null,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            limits = limits ?? PevtEmbeddedSourceLimits.Default;
            var diagnostics = new DiagnosticBag();

            if (source.ByteLength > limits.MaxUncompressedBytes)
            {
                diagnostics.Add(new Diagnostic(SourceTooLarge, DiagnosticSeverity.Error,
                    $"`{source.DisplayPath}` 的 {source.ByteLength} 字节超出单个事件源的上限 {limits.MaxUncompressedBytes}。",
                    null));
                return new PevtExternalLoadResult(source, null, null,
                    PevtExternalLoadFailure.SourceTooLarge, diagnostics.ToReadOnly());
            }

            SourceTextLoadResult decoded = SourceText.FromUtf8(source.Utf8, source.DisplayPath, cancellationToken);
            if (!decoded.Success)
            {
                diagnostics.Add(new Diagnostic(InvalidSourceEncoding, DiagnosticSeverity.Error,
                    $"`{source.DisplayPath}` 不是合法的 UTF-8 源文本。", null));
                return new PevtExternalLoadResult(source, null, null,
                    PevtExternalLoadFailure.InvalidSourceEncoding, diagnostics.ToReadOnly());
            }

            PevtCompilation compilation = PevtSourceCompiler.Compile(
                decoded.Text, diagnostics, builtinApi, cancellationToken, null, rawCsAnalyzer);
            if (!compilation.Success)
            {
                return new PevtExternalLoadResult(source, null, decoded.Text,
                    PevtExternalLoadFailure.StaticAnalysis, diagnostics.ToReadOnly());
            }

            return new PevtExternalLoadResult(source, compilation.Definition, decoded.Text,
                PevtExternalLoadFailure.None, diagnostics.ToReadOnly());
        }
    }
}
