using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Loading;
using Xunit;

namespace Polaris.Pevt.Core.Tests.Loading
{
    /// <summary>
    /// 嵌入包的编码、加载顺序与完整性限制（PEVT-嵌入注册与ID冲突规范.md 第 2、5、8 节）。
    /// PEVT9201–PEVT9210 每条都有直接断言，并各配一个不会误报该编号的合法邻近样例。
    /// </summary>
    public class PevtEmbeddedSourceTests
    {
        private const string ValidSource = "id \"Opening\"\nvar greeting : string = \"hi\"\nend\n";

        private static PevtEmbeddedSource Pack(string source = ValidSource, string declaredId = "Opening") =>
            PevtEmbeddedSource.Create(declaredId, "Events/Opening.pevt", source);

        private static PevtEmbeddedSource With(
            PevtEmbeddedSource original,
            int? formatVersion = null,
            string compression = null,
            string declaredId = null,
            string sourcePath = null,
            int? uncompressedLength = null,
            string contentHash = null,
            string payload = null) =>
            new PevtEmbeddedSource(
                formatVersion ?? original.FormatVersion,
                compression ?? original.Compression,
                declaredId ?? original.DeclaredId,
                sourcePath ?? original.SourcePath,
                uncompressedLength ?? original.UncompressedLength,
                contentHash ?? original.ContentHash,
                payload ?? original.Payload);

        private static void AssertFails(PevtEmbeddedSource source, PevtEmbeddedLoadFailure failure, string diagnosticId, PevtEmbeddedSourceLimits limits = null)
        {
            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(source, limits);

            Assert.False(result.Success);
            Assert.Null(result.Definition);
            Assert.Equal(failure, result.Failure);
            Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
        }

        // ---- 编码 ----

        [Fact]
        public void Create_ProducesAPackageThatRoundTrips()
        {
            PevtEmbeddedSource package = Pack();

            Assert.Equal(PevtEmbeddedSource.CurrentFormatVersion, package.FormatVersion);
            Assert.Equal(PevtEmbeddedSource.GzipBase64V1, package.Compression);
            Assert.Equal("Opening", package.DeclaredId);
            Assert.Equal("Events/Opening.pevt", package.SourcePath);
            Assert.Equal(Encoding.UTF8.GetByteCount(ValidSource), package.UncompressedLength);
            Assert.Equal(64, package.ContentHash.Length);

            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(package);

            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            Assert.Equal("Opening", result.Definition.EventId);
            Assert.Equal(ValidSource, result.Text.Content);
        }

        [Theory]
        [InlineData("id \"A\"\r\nend\r\n")]
        [InlineData("id \"A\"\nend\n")]
        [InlineData("id \"A\"\r\nend\n")]
        public void Create_PreservesOriginalLineEndingsWithoutNormalizing(string source)
        {
            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(PevtEmbeddedSource.Create("A", "E/A.pevt", source));

            Assert.True(result.Success);
            Assert.Equal(source, result.Text.Content);
        }

        [Fact]
        public void Create_EncodesUtf8WithoutByteOrderMark()
        {
            PevtEmbeddedSource package = PevtEmbeddedSource.Create("A", "E/A.pevt", "id \"A\"\nend\n");
            byte[] bytes = Decompress(Convert.FromBase64String(package.Payload));

            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal(PevtEmbeddedSource.ComputeContentHash(bytes), package.ContentHash);
        }

        [Fact]
        public void Create_NormalizesBackslashSeparatorsButRejectsNonRelativePaths()
        {
            Assert.Equal("Events/Opening.pevt", PevtEmbeddedSource.Create("A", @"Events\Opening.pevt", "id \"A\"\nend\n").SourcePath);

            Assert.Throws<ArgumentException>(() => PevtEmbeddedSource.Create("A", @"C:\Dev\Mod\Events\A.pevt", "id \"A\"\nend\n"));
            Assert.Throws<ArgumentException>(() => PevtEmbeddedSource.Create("A", "/Events/A.pevt", "id \"A\"\nend\n"));
            Assert.Throws<ArgumentException>(() => PevtEmbeddedSource.Create("A", "../A.pevt", "id \"A\"\nend\n"));
            Assert.Throws<ArgumentException>(() => PevtEmbeddedSource.Create("A", "", "id \"A\"\nend\n"));
            Assert.Throws<ArgumentException>(() => PevtEmbeddedSource.Create("", "E/A.pevt", "id \"A\"\nend\n"));
        }

        [Fact]
        public void Payload_MaySpanMultipleConstantFragmentsAsLongAsTheConcatenationMatches()
        {
            PevtEmbeddedSource package = Pack();
            string first = package.Payload.Substring(0, package.Payload.Length / 2);
            string second = package.Payload.Substring(package.Payload.Length / 2);

            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(With(package, payload: first + second));

            Assert.True(result.Success);
        }

        // ---- 加载顺序与完整性 ----

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void Pevt9201_RejectsUnsupportedFormatVersionWithoutGuessing(int formatVersion) =>
            AssertFails(With(Pack(), formatVersion: formatVersion), PevtEmbeddedLoadFailure.UnsupportedFormatVersion, "PEVT9201");

        [Theory]
        [InlineData("")]
        [InlineData("gzip-base64-v2")]
        [InlineData("GZIP-BASE64-V1")] // 序数比较，大小写不同即不受支持
        [InlineData("deflate")]
        public void Pevt9202_RejectsUnsupportedCompression(string compression) =>
            AssertFails(With(Pack(), compression: compression), PevtEmbeddedLoadFailure.UnsupportedCompression, "PEVT9202");

        [Theory]
        [InlineData("")]
        [InlineData("/Events/A.pevt")]
        [InlineData(@"C:\Dev\Events\A.pevt")]
        [InlineData("../../secret.pevt")]
        [InlineData("Events//A.pevt")]
        public void Pevt9210_RejectsSourcePathThatIsNotProjectRelative(string sourcePath) =>
            AssertFails(With(Pack(), sourcePath: sourcePath), PevtEmbeddedLoadFailure.InvalidSourcePath, "PEVT9210");

        [Theory]
        [InlineData("A.pevt")]
        [InlineData("Events/A.pevt")]
        [InlineData(@"Events\A.pevt")]
        [InlineData("a/b/c/A.pevt")]
        public void Pevt9210_AcceptsProjectRelativePaths(string sourcePath)
        {
            Assert.True(PevtEmbeddedSourceLoader.Load(With(Pack(), sourcePath: sourcePath)).Success);
        }

        [Fact]
        public void Pevt9203_RejectsDeclaredLengthOverTheLimitBeforeDecompressing()
        {
            var limits = new PevtEmbeddedSourceLimits(1024, 16);

            AssertFails(Pack(), PevtEmbeddedLoadFailure.PayloadTooLarge, "PEVT9203", limits);
            AssertFails(With(Pack(), uncompressedLength: -1), PevtEmbeddedLoadFailure.PayloadTooLarge, "PEVT9203");
        }

        [Fact]
        public void Pevt9203_RejectsPayloadLongerThanTheConfiguredLimit()
        {
            var limits = new PevtEmbeddedSourceLimits(4, 4 * 1024 * 1024);

            AssertFails(Pack(), PevtEmbeddedLoadFailure.PayloadTooLarge, "PEVT9203", limits);
        }

        [Fact]
        public void Pevt9203_LimitsAreExactBoundaries()
        {
            PevtEmbeddedSource package = Pack();

            Assert.True(PevtEmbeddedSourceLoader.Load(package,
                new PevtEmbeddedSourceLimits(package.Payload.Length, package.UncompressedLength)).Success);

            AssertFails(package, PevtEmbeddedLoadFailure.PayloadTooLarge, "PEVT9203",
                new PevtEmbeddedSourceLimits(package.Payload.Length, package.UncompressedLength - 1));
            AssertFails(package, PevtEmbeddedLoadFailure.PayloadTooLarge, "PEVT9203",
                new PevtEmbeddedSourceLimits(package.Payload.Length - 1, package.UncompressedLength));
        }

        [Theory]
        [InlineData("not base64!!")]
        [InlineData("QQ")] // 长度不是 4 的倍数
        [InlineData("=AAA")]
        public void Pevt9204_RejectsCorruptedBase64(string payload) =>
            AssertFails(With(Pack(), payload: payload), PevtEmbeddedLoadFailure.InvalidPayloadEncoding, "PEVT9204");

        [Fact]
        public void Pevt9205_RejectsTruncatedGzipStream()
        {
            PevtEmbeddedSource package = Pack();
            byte[] compressed = Convert.FromBase64String(package.Payload);
            byte[] truncated = compressed.Take(compressed.Length / 2).ToArray();

            AssertFails(With(package, payload: Convert.ToBase64String(truncated)), PevtEmbeddedLoadFailure.CorruptedPayload, "PEVT9205");
        }

        [Fact]
        public void Pevt9205_RejectsBitRotInTheMiddleOfTheGzipStream()
        {
            // 中段翻转一个字节：分类必须是"载荷损坏"，而不是被后面的哈希检查笼统吃掉。
            PevtEmbeddedSource package = Pack();
            byte[] compressed = Convert.FromBase64String(package.Payload);
            compressed[compressed.Length / 2] ^= 0xFF;

            AssertFails(With(package, payload: Convert.ToBase64String(compressed)), PevtEmbeddedLoadFailure.CorruptedPayload, "PEVT9205");
        }

        [Theory]
        [InlineData(0)] // 空载荷
        [InlineData(1)]
        [InlineData(17)] // 差一：最小合法 GZip 成员为 18 字节
        public void Pevt9205_RejectsPayloadShorterThanAMinimalGzipMember(int length) =>
            AssertFails(With(Pack(), payload: Convert.ToBase64String(new byte[length])),
                PevtEmbeddedLoadFailure.CorruptedPayload, "PEVT9205");

        [Fact]
        public void Pevt9205_RejectsPayloadThatIsNotGzipAtAll() =>
            AssertFails(With(Pack(), payload: Convert.ToBase64String(Encoding.UTF8.GetBytes("id \"Opening\"\nend\n"))),
                PevtEmbeddedLoadFailure.CorruptedPayload, "PEVT9205");

        [Fact]
        public void Pevt9205_StopsADecompressionBombThatLiesAboutItsLength()
        {
            // 声明 16 字节，实际解压出 1 MiB 的零字节。上限来自配置而不是包内声明，因此必须中止。
            byte[] bomb = new byte[1024 * 1024];
            string payload = Convert.ToBase64String(Compress(bomb));
            var package = new PevtEmbeddedSource(
                PevtEmbeddedSource.CurrentFormatVersion,
                PevtEmbeddedSource.GzipBase64V1,
                "Opening",
                "Events/Opening.pevt",
                16,
                PevtEmbeddedSource.ComputeContentHash(bomb),
                payload);

            AssertFails(package, PevtEmbeddedLoadFailure.CorruptedPayload, "PEVT9205",
                new PevtEmbeddedSourceLimits(8 * 1024 * 1024, 64 * 1024));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(4096)]
        public void Pevt9206_RejectsLengthThatDisagreesWithTheDecompressedBytes(int declaredLength)
        {
            PevtEmbeddedSource package = Pack();
            Assert.NotEqual(package.UncompressedLength, declaredLength);

            AssertFails(With(package, uncompressedLength: declaredLength), PevtEmbeddedLoadFailure.LengthMismatch, "PEVT9206");
        }

        [Fact]
        public void Pevt9207_RejectsContentHashMismatch()
        {
            string wrongHash = new string('0', 64);

            AssertFails(With(Pack(), contentHash: wrongHash), PevtEmbeddedLoadFailure.ContentHashMismatch, "PEVT9207");
            AssertFails(With(Pack(), contentHash: ""), PevtEmbeddedLoadFailure.ContentHashMismatch, "PEVT9207");
        }

        [Fact]
        public void Pevt9207_AcceptsUppercaseHexHash()
        {
            PevtEmbeddedSource package = Pack();

            Assert.True(PevtEmbeddedSourceLoader.Load(With(package, contentHash: package.ContentHash.ToUpperInvariant())).Success);
        }

        [Fact]
        public void Pevt9208_RejectsPayloadThatIsNotValidUtf8()
        {
            byte[] invalid = { 0x69, 0x64, 0xC0, 0x80 };
            var package = new PevtEmbeddedSource(
                PevtEmbeddedSource.CurrentFormatVersion,
                PevtEmbeddedSource.GzipBase64V1,
                "Opening",
                "Events/Opening.pevt",
                invalid.Length,
                PevtEmbeddedSource.ComputeContentHash(invalid),
                Convert.ToBase64String(Compress(invalid)));

            AssertFails(package, PevtEmbeddedLoadFailure.InvalidSourceEncoding, "PEVT9208");
        }

        [Fact]
        public void Pevt9209_RejectsDeclaredIdThatDisagreesWithTheSourceId()
        {
            AssertFails(With(Pack(), declaredId: "Something Else"), PevtEmbeddedLoadFailure.DeclaredIdMismatch, "PEVT9209");

            // 比较是序数的：大小写不同就是两个 ID。
            AssertFails(With(Pack(), declaredId: "opening"), PevtEmbeddedLoadFailure.DeclaredIdMismatch, "PEVT9209");
        }

        [Fact]
        public void StaticAnalysis_IsAlwaysRerunEvenThoughTheToolAlreadyValidated()
        {
            // 一份能正确打包、哈希与长度都对，但源码本身有语法错误的包。
            PevtEmbeddedSource package = PevtEmbeddedSource.Create("Broken", "Events/Broken.pevt", "id \"Broken\"\nif\nend\n");

            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(package);

            Assert.False(result.Success);
            Assert.Equal(PevtEmbeddedLoadFailure.StaticAnalysis, result.Failure);
            Assert.NotNull(result.Text); // 解码成功，失败发生在静态校验
            Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("PEVT92", StringComparison.Ordinal));
        }

        [Fact]
        public void FailureBeforeDecoding_KeepsOriginInformationButHasNoSourceText()
        {
            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(With(Pack(), compression: "deflate"));

            Assert.Null(result.Text);
            Assert.Same(result.Source, result.Source);
            Assert.Equal("Events/Opening.pevt", result.Source.SourcePath);
            Assert.Contains("Events/Opening.pevt", Assert.Single(result.Diagnostics).Message);
        }

        [Fact]
        public void Loader_ChecksFormatBeforeAnythingElse()
        {
            // 同时坏掉格式版本、压缩算法和载荷：只报第一步的 PEVT9201，不产生连锁误导报告。
            PevtEmbeddedLoadResult result = PevtEmbeddedSourceLoader.Load(
                With(Pack(), formatVersion: 99, compression: "deflate", payload: "!!!"));

            Assert.Equal("PEVT9201", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void Loader_AcceptsABuiltinApiTableSoCallsBindAgainstTheSameRegistry()
        {
            var table = new BuiltinApiTable();
            table.Register(new BuiltinSignature("say", false,
                new[] { new BuiltinParameter("text", PevtType.String) }, null));

            PevtEmbeddedSource package = PevtEmbeddedSource.Create("Talk", "Events/Talk.pevt", "id \"Talk\"\n@say(\"hi\")\nend\n");

            Assert.True(PevtEmbeddedSourceLoader.Load(package, builtinApi: table).Success);
            Assert.False(PevtEmbeddedSourceLoader.Load(package).Success); // 空 API 表：@say 未登记
        }

        [Fact]
        public void EveryEmbeddedDiagnosticId_IsRegisteredInTheCatalog()
        {
            for (int number = 9201; number <= 9210; number++)
            {
                string id = "PEVT" + number.ToString();
                Assert.True(DiagnosticCatalog.TryFind(id, out DiagnosticDescriptor descriptor), $"{id} 未登记。");
                Assert.Equal(DiagnosticSeverity.Error, descriptor.Severity);
            }
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

        private static byte[] Decompress(byte[] compressed)
        {
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
