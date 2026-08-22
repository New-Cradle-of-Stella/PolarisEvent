using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Loading
{
    /// <summary>嵌入包加载失败的分类。<see cref="None"/> 之外的任何值都表示该事件不进入 `/event`。</summary>
    public enum PevtEmbeddedLoadFailure
    {
        None,
        UnsupportedFormatVersion,
        UnsupportedCompression,
        InvalidSourcePath,
        PayloadTooLarge,
        InvalidPayloadEncoding,
        CorruptedPayload,
        LengthMismatch,
        ContentHashMismatch,
        InvalidSourceEncoding,
        StaticAnalysis,
        DeclaredIdMismatch,
    }

    /// <summary>
    /// 加载一个嵌入包的结果。失败时保留来源信息与具体诊断，但不尝试降级为原版 EV。
    /// </summary>
    public sealed class PevtEmbeddedLoadResult
    {
        public PevtEmbeddedSource Source { get; }

        /// <summary>通过全部静态门的不可变程序定义；失败时为 null。</summary>
        public PevtProgramDefinition Definition { get; }

        /// <summary>解压并解码后的源文本；在解码成功之前失败时为 null。</summary>
        public SourceText Text { get; }

        public PevtEmbeddedLoadFailure Failure { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Success => Failure == PevtEmbeddedLoadFailure.None && Definition != null;

        internal PevtEmbeddedLoadResult(
            PevtEmbeddedSource source,
            PevtProgramDefinition definition,
            SourceText text,
            PevtEmbeddedLoadFailure failure,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            Source = source;
            Definition = definition;
            Text = text;
            Failure = failure;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// 嵌入包加载器，按固定顺序执行：格式版本 → 压缩算法 → 载荷上限 → Base64 解码 →
    /// 受限 GZip 解压 → 长度 → SHA-256 → 严格 UTF-8 → 完整静态校验 → <c>id</c> 与 <c>DeclaredId</c> 一致性。
    /// 任一步失败即停止并返回具体诊断；静态校验始终重新执行共享语言核心，不因构建时校验过而略过。
    /// </summary>
    public static class PevtEmbeddedSourceLoader
    {
        private const string UnsupportedFormatVersion = "PEVT9201";
        private const string UnsupportedCompression = "PEVT9202";
        private const string PayloadTooLarge = "PEVT9203";
        private const string InvalidPayloadEncoding = "PEVT9204";
        private const string CorruptedPayload = "PEVT9205";
        private const string LengthMismatch = "PEVT9206";
        private const string ContentHashMismatch = "PEVT9207";
        private const string InvalidSourceEncoding = "PEVT9208";
        private const string DeclaredIdMismatch = "PEVT9209";
        private const string InvalidSourcePath = "PEVT9210";

        /// <param name="rawCsAnalyzer">
        /// <c>$raw cs</c> 的 C# 分析器。游戏侧接上它，嵌入源里的 <c>$raw cs</c> 才会在加载期
        /// 重做 PEVT8007–8010——"游戏侧必须对嵌入源重新做完整静态校验"包括这一段 C#。
        /// </param>
        public static PevtEmbeddedLoadResult Load(
            PevtEmbeddedSource source,
            PevtEmbeddedSourceLimits limits = null,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            limits = limits ?? PevtEmbeddedSourceLimits.Default;
            var diagnostics = new DiagnosticBag();
            string origin = $"`{source.DeclaredId}` ({source.SourcePath})";

            if (source.FormatVersion != PevtEmbeddedSource.CurrentFormatVersion)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.UnsupportedFormatVersion, UnsupportedFormatVersion,
                    $"{origin} 的嵌入包格式版本为 {source.FormatVersion}，当前只支持 {PevtEmbeddedSource.CurrentFormatVersion}。");
            }

            if (!string.Equals(source.Compression, PevtEmbeddedSource.GzipBase64V1, StringComparison.Ordinal))
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.UnsupportedCompression, UnsupportedCompression,
                    $"{origin} 声明的压缩算法为 `{source.Compression}`，当前只支持 `{PevtEmbeddedSource.GzipBase64V1}`。");
            }

            if (!PevtEmbeddedSource.IsProjectRelativePath(source.SourcePath))
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.InvalidSourcePath, InvalidSourcePath,
                    $"`{source.DeclaredId}` 的 SourcePath `{source.SourcePath}` 不是项目相对路径。");
            }

            if (source.UncompressedLength < 0 || source.UncompressedLength > limits.MaxUncompressedBytes)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.PayloadTooLarge, PayloadTooLarge,
                    $"{origin} 声明的未压缩长度 {source.UncompressedLength} 超出上限 {limits.MaxUncompressedBytes}。");
            }

            if (source.Payload.Length > limits.MaxPayloadCharacters)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.PayloadTooLarge, PayloadTooLarge,
                    $"{origin} 的 Base64 载荷长度 {source.Payload.Length} 超出上限 {limits.MaxPayloadCharacters}。");
            }

            byte[] compressed;
            try
            {
                compressed = Convert.FromBase64String(source.Payload);
            }
            catch (FormatException ex)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.InvalidPayloadEncoding, InvalidPayloadEncoding,
                    $"{origin} 的载荷不是合法 Base64：{ex.Message}");
            }

            byte[] bytes;
            try
            {
                bytes = Decompress(compressed, limits.MaxUncompressedBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DecompressionLimitExceededException)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.CorruptedPayload, CorruptedPayload,
                    $"{origin} 解压后超过硬上限 {limits.MaxUncompressedBytes} 字节。");
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is EndOfStreamException)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.CorruptedPayload, CorruptedPayload,
                    $"{origin} 的 GZip 数据损坏或被截断：{ex.Message}");
            }

            if (bytes.Length != source.UncompressedLength)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.LengthMismatch, LengthMismatch,
                    $"{origin} 解压得到 {bytes.Length} 字节，声明为 {source.UncompressedLength} 字节。");
            }

            string actualHash = PevtEmbeddedSource.ComputeContentHash(bytes);
            if (!string.Equals(actualHash, source.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.ContentHashMismatch, ContentHashMismatch,
                    $"{origin} 的内容哈希不匹配：声明 `{source.ContentHash}`，实际 `{actualHash}`。");
            }

            SourceTextLoadResult decoded = SourceText.FromUtf8(bytes, source.SourcePath, cancellationToken);
            if (!decoded.Success)
            {
                return Fail(source, diagnostics, PevtEmbeddedLoadFailure.InvalidSourceEncoding, InvalidSourceEncoding,
                    $"{origin} 解压后的字节不是合法 UTF-8 源文本。");
            }

            // 第 5 步：语法错误不因构建时已经校验过而略过，这里始终重新执行完整静态校验。
            PevtCompilation compilation = PevtSourceCompiler.Compile(
                decoded.Text, diagnostics, builtinApi, cancellationToken, null, rawCsAnalyzer);
            if (!compilation.Success)
            {
                return new PevtEmbeddedLoadResult(source, null, decoded.Text,
                    PevtEmbeddedLoadFailure.StaticAnalysis, diagnostics.ToReadOnly());
            }

            // 第 6 步：调用方无法从 DeclaredId 绕过源码 id，两者必须完全一致。
            if (!string.Equals(compilation.Definition.EventId, source.DeclaredId, StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(DeclaredIdMismatch, DiagnosticSeverity.Error,
                    $"{origin} 的源码 `id` 为 `{compilation.Definition.EventId}`，与嵌入包 DeclaredId `{source.DeclaredId}` 不一致。",
                    decoded.Text.GetLocation(new TextSpan(0, 0))));
                return new PevtEmbeddedLoadResult(source, null, decoded.Text,
                    PevtEmbeddedLoadFailure.DeclaredIdMismatch, diagnostics.ToReadOnly());
            }

            return new PevtEmbeddedLoadResult(source, compilation.Definition, decoded.Text,
                PevtEmbeddedLoadFailure.None, diagnostics.ToReadOnly());
        }

        private static PevtEmbeddedLoadResult Fail(
            PevtEmbeddedSource source,
            DiagnosticBag diagnostics,
            PevtEmbeddedLoadFailure failure,
            string diagnosticId,
            string message)
        {
            diagnostics.Add(new Diagnostic(diagnosticId, DiagnosticSeverity.Error, message, null));
            return new PevtEmbeddedLoadResult(source, null, null, failure, diagnostics.ToReadOnly());
        }

        /// <summary>解压结果超过硬上限。单独一个异常类型，避免与真正的数据损坏混在一起报告。</summary>
        private sealed class DecompressionLimitExceededException : Exception
        {
        }

        /// <summary>
        /// 受限 GZip 解压。上限来自调用方配置而不是包内声明的 <c>UncompressedLength</c>——
        /// 一个恶意包完全可以声明 100 字节却解压出 1 GB，因此这里边读边计数，超过上限立刻中止。
        /// </summary>
        private static byte[] Decompress(byte[] compressed, int maxBytes, CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            byte[] result;

            using (var input = new MemoryStream(compressed, writable: false))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int read = gzip.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    if (output.Length + read > maxBytes)
                        throw new DecompressionLimitExceededException();

                    output.Write(buffer, 0, read);
                }

                result = output.ToArray();
            }

            ValidateGzipTrailer(compressed, result);
            return result;
        }

        /// <summary>
        /// 校验 GZip 尾部的 CRC32 与 ISIZE。
        /// </summary>
        private static void ValidateGzipTrailer(byte[] compressed, byte[] decompressed)
        {
            // 最小合法 GZip 成员 = 10 字节头部 + 8 字节尾部。
            if (compressed.Length < 18)
                throw new InvalidDataException("GZip 数据长度不足以同时包含头部与尾部。");

            uint declaredCrc = ReadUInt32LittleEndian(compressed, compressed.Length - 8);
            uint declaredSize = ReadUInt32LittleEndian(compressed, compressed.Length - 4);

            if (declaredSize != (uint)decompressed.Length)
                throw new InvalidDataException("GZip 尾部声明的长度与实际解压结果不一致，数据被截断或损坏。");

            uint actualCrc = Crc32(decompressed);
            if (actualCrc != declaredCrc)
                throw new InvalidDataException("GZip 尾部 CRC32 校验失败，数据已损坏。");
        }

        private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
            (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }

            return table;
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
                crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
