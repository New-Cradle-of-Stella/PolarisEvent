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
    ///
    /// 词法、语法、绑定、控制流四道静态门在这里按固定顺序全部执行，调用方无法只跑其中一部分就拿到
    /// <see cref="PevtProgramDefinition"/>——工具侧和游戏侧都走这一条路径，因此对同一份源文本必然
    /// 得到相同的 AST、绑定结果和 PEVTxxxx。游戏侧对嵌入源仍然重新执行本入口，不信任工具侧结果。
    /// </summary>
    public static class PevtSourceCompiler
    {
        public static PevtCompilation Compile(
            SourceText source,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var diagnostics = new DiagnosticBag();
            return Compile(source, diagnostics, builtinApi, cancellationToken);
        }

        /// <summary>把诊断累加到调用方自己的包里的重载，供已经在收集诊断的宿主使用。</summary>
        public static PevtCompilation Compile(
            SourceText source,
            DiagnosticBag diagnostics,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(source, diagnostics, cancellationToken);
            DocumentSyntax document = new Parser(tokens, diagnostics, source).ParseDocument();
            new Binder(diagnostics, source, builtinApi).BindDocument(document);
            new ControlFlowAnalyzer(diagnostics, source).AnalyzeDocument(document);

            PevtProgramDefinition definition = PevtProgramDefinition.TryBuild(document, source, diagnostics);
            return new PevtCompilation(definition, document, source, diagnostics.ToReadOnly());
        }

        /// <summary>严格 UTF-8 解码后编译。解码失败时返回只含编码诊断的失败结果，不继续词法分析。</summary>
        public static PevtCompilation Compile(
            byte[] utf8Bytes,
            string filePath,
            BuiltinApiTable builtinApi = null,
            CancellationToken cancellationToken = default)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            SourceTextLoadResult loaded = SourceText.FromUtf8(utf8Bytes, filePath, cancellationToken);
            if (!loaded.Success)
                return new PevtCompilation(null, null, null, loaded.Diagnostics);

            return Compile(loaded.Text, builtinApi, cancellationToken);
        }
    }
}
