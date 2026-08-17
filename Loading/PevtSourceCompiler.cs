using System;
using System.Collections.Generic;
using System.Threading;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Flow;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Loading
{
    /// <summary>
    /// 一次完整前端编译的结果。<see cref="Definition"/> 只在零 Error 级诊断时非 null；
    /// 警告不阻止事件进入可执行状态。
    /// </summary>
    public sealed class PevtCompilation
    {
        public PevtProgramDefinition Definition { get; }

        public DocumentSyntax Document { get; }

        public SourceText Source { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Success => Definition != null;

        internal PevtCompilation(PevtProgramDefinition definition, DocumentSyntax document, SourceText source, IReadOnlyList<Diagnostic> diagnostics)
        {
            Definition = definition;
            Document = document;
            Source = source;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// PEVT 源文本到不可变程序定义的唯一公开编译入口。
    /// </summary>
    public static class PevtSourceCompiler
    {
        public static PevtCompilation Compile(
            SourceText source,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default,
            IEnumerable<Symbol> seedSymbols = null,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var diagnostics = new DiagnosticBag();
            return Compile(source, diagnostics, builtinApi, cancellationToken, seedSymbols, rawCsAnalyzer);
        }

        /// <summary>把诊断累加到调用方自己的包里的重载，供已经在收集诊断的宿主使用。</summary>
        public static PevtCompilation Compile(
            SourceText source,
            DiagnosticBag diagnostics,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default,
            IEnumerable<Symbol> seedSymbols = null,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(source, diagnostics, cancellationToken);
            DocumentSyntax document = new Parser(tokens, diagnostics, source).ParseDocument();
            new Binder(diagnostics, source, builtinApi, rawCsAnalyzer).BindDocument(document, seedSymbols);
            new ControlFlowAnalyzer(diagnostics, source).AnalyzeDocument(document);

            PevtProgramDefinition definition = PevtProgramDefinition.TryBuild(document, source, diagnostics);
            return new PevtCompilation(definition, document, source, diagnostics.ToReadOnly());
        }

        /// <summary>严格 UTF-8 解码后编译。解码失败时返回只含编码诊断的失败结果，不继续词法分析。</summary>
        public static PevtCompilation Compile(
            byte[] utf8Bytes,
            string filePath,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default,
            IEnumerable<Symbol> seedSymbols = null,
            Runtime.Raw.IPevtRawCsAnalyzer rawCsAnalyzer = null)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            SourceTextLoadResult loaded = SourceText.FromUtf8(utf8Bytes, filePath, cancellationToken);
            if (!loaded.Success)
                return new PevtCompilation(null, null, null, loaded.Diagnostics);

            return Compile(loaded.Text, builtinApi, cancellationToken, seedSymbols, rawCsAnalyzer);
        }
    }
}
