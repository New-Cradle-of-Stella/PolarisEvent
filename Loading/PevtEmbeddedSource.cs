using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Polaris.Pevt.Loading
{
    /// <summary>
    /// 一个 PEVT 嵌入包：与模组一起编译进程序集的压缩原始源文本，不是 PEVT 字节码，也不是原版命令文本。
    /// </summary>
    public sealed class PevtEmbeddedSource
    {
        /// <summary>第一版嵌入包格式版本。与 PEVT 语法版本分开。</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>第一版唯一受支持的压缩算法。</summary>
        public const string GzipBase64V1 = "gzip-base64-v1";

        public int FormatVersion { get; }

        public string Compression { get; }

        /// <summary>工具侧从源码 <c>id "..."</c> 读取的事件 ID。加载时必须与源码再次核对。</summary>
        public string DeclaredId { get; }

        /// <summary>项目相对 `.pevt` 路径，不嵌入开发机绝对路径。</summary>
        public string SourcePath { get; }

        /// <summary>未压缩 UTF-8 字节数，用于损坏与解压膨胀检查。</summary>
        public int UncompressedLength { get; }

        /// <summary>未压缩 UTF-8 字节的 SHA-256，十六进制小写。</summary>
        public string ContentHash { get; }

        /// <summary>GZip 后再 Base64 的源文本载荷。</summary>
        public string Payload { get; }

        public PevtEmbeddedSource(
            int formatVersion,
            string compression,
            string declaredId,
            string sourcePath,
            int uncompressedLength,
            string contentHash,
            string payload)
        {
            FormatVersion = formatVersion;
            Compression = compression ?? string.Empty;
            DeclaredId = declaredId ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            UncompressedLength = uncompressedLength;
            ContentHash = contentHash ?? string.Empty;
            Payload = payload ?? string.Empty;
        }

        /// <summary>
        /// 工具侧编码入口：把原始源文本打包。源文本按 UTF-8 无 BOM 编码，
        /// 保留其中现有的 CRLF 或 LF，不在生成阶段格式化或标准化换行。
        /// </summary>
        public static PevtEmbeddedSource Create(string declaredId, string sourcePath, string sourceText)
        {
            if (sourceText == null)
                throw new ArgumentNullException(nameof(sourceText));

            return CreateFromBytes(declaredId, sourcePath, new UTF8Encoding(false).GetBytes(sourceText));
        }

        /// <summary>直接从磁盘读到的原始字节打包，连字节都不改，最大限度保证"源文本即真相"。</summary>
        public static PevtEmbeddedSource CreateFromBytes(string declaredId, string sourcePath, byte[] utf8Bytes)
        {
            if (string.IsNullOrEmpty(declaredId))
                throw new ArgumentException("DeclaredId 不能为空。", nameof(declaredId));
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            string normalizedPath = NormalizeSourcePath(sourcePath);
            if (!IsProjectRelativePath(normalizedPath))
                throw new ArgumentException($"SourcePath `{sourcePath}` 不是项目相对路径。", nameof(sourcePath));

            return new PevtEmbeddedSource(
                CurrentFormatVersion,
                GzipBase64V1,
                declaredId,
                normalizedPath,
                utf8Bytes.Length,
                ComputeContentHash(utf8Bytes),
                Convert.ToBase64String(Compress(utf8Bytes)));
        }

        /// <summary>未压缩 UTF-8 字节的 SHA-256，十六进制小写。加载与生成两侧共用本方法。</summary>
        public static string ComputeContentHash(byte[] utf8Bytes)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(utf8Bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        /// <summary>把路径分隔符统一为 <c>/</c>，其余原样保留。</summary>
        public static string NormalizeSourcePath(string sourcePath) =>
            sourcePath == null ? string.Empty : sourcePath.Replace('\\', '/');

        /// <summary>
        /// 项目相对路径判定：非空、不是根路径、不含盘符、不含 <c>..</c> 段。
        /// 判定前先归一化分隔符，因此 <c>Events\Opening.pevt</c> 与 <c>Events/Opening.pevt</c> 等价。
        /// </summary>
        public static bool IsProjectRelativePath(string sourcePath)
        {
            string path = NormalizeSourcePath(sourcePath);
            if (path.Length == 0)
                return false;
            if (path[0] == '/')
                return false;
            if (path.IndexOf(':') >= 0)
                return false;

            int segmentStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i != path.Length && path[i] != '/')
                    continue;

                int length = i - segmentStart;
                if (length == 0)
                    return false;
                if (length == 2 && path[segmentStart] == '.' && path[segmentStart + 1] == '.')
                    return false;

                segmentStart = i + 1;
            }

            return true;
        }

        private static byte[] Compress(byte[] bytes)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }

        public override string ToString() => $"{DeclaredId} ({SourcePath}, {UncompressedLength} bytes)";
    }

    /// <summary>
    /// 加载嵌入包时的大小上限。解压前限制 Base64 载荷长度与包内声明的未压缩长度；
    /// 解压过程中另外使用 <see cref="MaxUncompressedBytes"/> 作为硬上限，不信任包内长度。
    /// </summary>
    public sealed class PevtEmbeddedSourceLimits
    {
        public int MaxPayloadCharacters { get; }

        public int MaxUncompressedBytes { get; }

        public PevtEmbeddedSourceLimits(int maxPayloadCharacters, int maxUncompressedBytes)
        {
            if (maxPayloadCharacters <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadCharacters), maxPayloadCharacters, "载荷长度上限必须为正数。");
            if (maxUncompressedBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUncompressedBytes), maxUncompressedBytes, "解压上限必须为正数。");

            MaxPayloadCharacters = maxPayloadCharacters;
            MaxUncompressedBytes = maxUncompressedBytes;
        }

        /// <summary>默认上限：单个事件源 4 MiB，Base64 载荷 8 MiB。远高于任何正常事件文件。</summary>
        public static PevtEmbeddedSourceLimits Default { get; } =
            new PevtEmbeddedSourceLimits(8 * 1024 * 1024, 4 * 1024 * 1024);
    }
}
