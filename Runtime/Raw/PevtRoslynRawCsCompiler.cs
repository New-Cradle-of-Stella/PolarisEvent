using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Polaris.Pevt.Binding;
using PevtDiagnostic = Polaris.Pevt.Diagnostics.Diagnostic;
using PevtSeverity = Polaris.Pevt.Diagnostics.DiagnosticSeverity;

namespace Polaris.Pevt.Runtime.Raw
{
    /// <summary>
    /// 基于 Roslyn 的 <c>$raw cs</c> 编译器与静态分析器。
    /// </summary>
    public sealed class PevtRoslynRawCsCompiler : IPevtRawCsCompiler
    {
        /// <summary>
        /// 默认语言版本固定为 C# 7.3。
        /// </summary>
        public const LanguageVersion DefaultLanguageVersion = LanguageVersion.CSharp7_3;

        private static int _assemblySequence;

        private readonly IReadOnlyList<MetadataReference> _references;
        private readonly LanguageVersion _languageVersion;

        public PevtRoslynRawCsCompiler(IEnumerable<Assembly> references, LanguageVersion? languageVersion = null)
            : this(ResolvePaths(references), languageVersion)
        {
        }

        public PevtRoslynRawCsCompiler(IEnumerable<string> referencePaths, LanguageVersion? languageVersion = null)
        {
            _languageVersion = languageVersion ?? DefaultLanguageVersion;

            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (referencePaths != null)
            {
                foreach (string path in referencePaths)
                {
                    if (string.IsNullOrEmpty(path) || !seen.Add(path) || !File.Exists(path))
                        continue;
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);

            var references = new List<MetadataReference>(paths.Count);
            foreach (string path in paths)
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (Exception)
                {
                    // 引用集合里的单个坏文件不该让整个 `$raw cs` 能力失效；它只是不可用。
                }
            }

            _references = references;
            ReferencePaths = paths;
            CacheScope = "roslyn/" + _languageVersion.ToString() + "/" + HashPaths(paths);
        }

        /// <summary>实际参与编译的引用文件路径，按序排列。只读诊断与测试用。</summary>
        public IReadOnlyList<string> ReferencePaths { get; }

        public string CacheScope { get; }

        /// <summary>用当前 AppDomain 里已加载的程序集作为引用集合。游戏侧与测试侧都用这一条。</summary>
        public static PevtRoslynRawCsCompiler FromLoadedAssemblies(LanguageVersion? languageVersion = null) =>
            new PevtRoslynRawCsCompiler(AppDomain.CurrentDomain.GetAssemblies(), languageVersion);

        public PevtRawCsCompilation Compile(PevtRawCsRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var diagnostics = new List<PevtDiagnostic>();

            PevtType? returnType = DiscoverReturnType(request, diagnostics);
            if (HasError(diagnostics))
                return PevtRawCsCompilation.Failed(diagnostics);

            if (request.Usage == PevtRawCsUsage.Expression && !returnType.HasValue)
            {
                // 静态诊断表 PEVT8006 明确覆盖"无返回值的 `$raw cs` 被用于表达式"。
                diagnostics.Add(Report(request, "PEVT8006", 0, 0,
                    "`$raw cs` 没有任何有值 `return`，不能作为 PEVT 表达式使用。"));
                return PevtRawCsCompilation.Failed(diagnostics);
            }

            bool appendTrailingReturn = returnType.HasValue && request.Usage == PevtRawCsUsage.Statement;
            string source = PevtRawCsTypes.BuildWrapperSource(request, returnType, appendTrailingReturn, out int codeOffset);
            SyntaxTree tree = Parse(source);

            CSharpCompilation compilation = CSharpCompilation.Create(
                "PevtRawCs" + (++_assemblySequence).ToString(CultureInfo.InvariantCulture),
                new[] { tree },
                _references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false));

            using (var stream = new MemoryStream())
            {
                EmitResult emit = compilation.Emit(stream);
                if (!emit.Success)
                {
                    Translate(request, emit.Diagnostics, codeOffset, diagnostics, allowMissingReturnValue: false);
                    if (!HasError(diagnostics))
                    {
                        diagnostics.Add(Report(request, "PEVT8007", 0, request.Code.Length,
                            "`$raw cs` 的 C# 内容无法编译，但编译器没有给出可映射的错误。"));
                    }

                    return PevtRawCsCompilation.Failed(diagnostics);
                }

                MethodInfo method;
                try
                {
                    Assembly assembly = Assembly.Load(stream.ToArray());
                    Type wrapper = assembly.GetType(PevtRawCsTypes.WrapperTypeName, throwOnError: true);
                    method = wrapper.GetMethod(PevtRawCsTypes.WrapperMethodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Report(request, "PEVT8007", 0, request.Code.Length,
                        $"`$raw cs` 编译成功但无法加载：{ex.GetType().Name}: {ex.Message}"));
                    return PevtRawCsCompilation.Failed(diagnostics);
                }

                if (method == null)
                {
                    diagnostics.Add(Report(request, "PEVT8007", 0, request.Code.Length,
                        "`$raw cs` 编译成功但找不到包装方法。"));
                    return PevtRawCsCompilation.Failed(diagnostics);
                }

                return new PevtRawCsCompilation(returnType, args => InvokeWrapper(method, args), diagnostics);
            }
        }

        /// <summary>
        /// 第一遍：只为语义模型服务。这里报 PEVT8008（返回类型不在 12.3 节表里）与 PEVT8009
        /// （同一代码块的有值 <c>return</c> 返回了不同 PEVT 类型），并顺手把真正的 C# 错误报成 PEVT8007。
        /// </summary>
        private PevtType? DiscoverReturnType(PevtRawCsRequest request, List<PevtDiagnostic> diagnostics)
        {
            string source = PevtRawCsTypes.BuildWrapperSource(request, null, appendTrailingReturn: false, out int codeOffset);
            SyntaxTree tree = Parse(source);

            CSharpCompilation compilation = CSharpCompilation.Create(
                "PevtRawCsProbe",
                new[] { tree },
                _references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));

            SemanticModel model = compilation.GetSemanticModel(tree);

            // allowMissingReturnValue：`void` 包装里的 `return 值;` 是 CS0127，那是本遍包装的产物。
            Translate(request, model.GetDiagnostics(), codeOffset, diagnostics, allowMissingReturnValue: true);

            PevtType? resolved = null;
            bool reportedMismatch = false;

            foreach (ReturnStatementSyntax statement in tree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                if (statement.Expression == null)
                    continue;

                ITypeSymbol type = model.GetTypeInfo(statement.Expression).Type;
                int start = statement.Expression.SpanStart - codeOffset;
                int length = statement.Expression.Span.Length;

                if (type == null || type.TypeKind == TypeKind.Error)
                {
                    // 表达式根本没绑定成功——那是 C# 内容本身的问题（PEVT8007），
                    // 上面的诊断翻译已经报过；这里不再重复扣一个类型错误。
                    if (!HasError(diagnostics))
                    {
                        diagnostics.Add(Report(request, "PEVT8007", start, length,
                            "`$raw cs` 的 `return` 表达式无法确定 C# 类型。"));
                    }

                    continue;
                }

                if (!TryMapSymbol(type, out PevtType mapped))
                {
                    diagnostics.Add(Report(request, "PEVT8008", start, length,
                        $"`$raw cs` 返回了 `{type.ToDisplayString()}`，只允许 int、float、bool、char、string。"));
                    continue;
                }

                if (resolved == null)
                {
                    resolved = mapped;
                    continue;
                }

                if (resolved.Value != mapped && !reportedMismatch)
                {
                    reportedMismatch = true;
                    diagnostics.Add(Report(request, "PEVT8009", start, length,
                        $"同一个 `$raw cs` 代码块的有值 `return` 返回了 {resolved.Value.DisplayName()} 和 {mapped.DisplayName()} 两种 PEVT 类型。"));
                }
            }

            return resolved;
        }

        /// <summary>
        /// 类型符号 → PEVT 普通类型。
        /// </summary>
        private static bool TryMapSymbol(ITypeSymbol type, out PevtType mapped)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Int32: mapped = PevtType.Int; return true;
                case SpecialType.System_Single: mapped = PevtType.Float; return true;
                case SpecialType.System_Boolean: mapped = PevtType.Bool; return true;
                case SpecialType.System_Char: mapped = PevtType.Char; return true;
                case SpecialType.System_String: mapped = PevtType.String; return true;
            }

            string containing = type.ContainingNamespace != null && !type.ContainingNamespace.IsGlobalNamespace
                ? type.ContainingNamespace.ToDisplayString() + "."
                : string.Empty;
            return PevtRawCsTypes.TryMapReturnType(containing + type.MetadataName, out mapped);
        }

        private SyntaxTree Parse(string source) =>
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(_languageVersion, DocumentationMode.None, SourceCodeKind.Regular));

        /// <summary>
        /// 把 C# 诊断翻成 PEVT 静态编号。
        /// </summary>
        private void Translate(
            PevtRawCsRequest request,
            IEnumerable<Diagnostic> csharpDiagnostics,
            int codeOffset,
            List<PevtDiagnostic> diagnostics,
            bool allowMissingReturnValue)
        {
            foreach (Diagnostic diagnostic in csharpDiagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;

                // CS0127: `void` 方法里不能返回值——第一遍包装的必然产物，不是作者的错。
                if (allowMissingReturnValue && diagnostic.Id == "CS0127")
                    continue;

                string id = diagnostic.Id == "CS0161" || diagnostic.Id == "CS0126" ? "PEVT8010" : "PEVT8007";

                int start = diagnostic.Location.SourceSpan.Start - codeOffset;
                int length = diagnostic.Location.SourceSpan.Length;
                diagnostics.Add(Report(request, id, start, length, $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
            }
        }

        private static PevtDiagnostic Report(PevtRawCsRequest request, string id, int start, int length, string message) =>
            new PevtDiagnostic(id, PevtSeverity.Error, message, request.SourceMap?.Locate(start, length));

        private static bool HasError(List<PevtDiagnostic> diagnostics)
        {
            foreach (PevtDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == PevtSeverity.Error)
                    return true;
            }

            return false;
        }

        /// <summary>反射调用包装方法，并把 <see cref="TargetInvocationException"/> 拆回真实异常。</summary>
        private static object InvokeWrapper(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new PevtRawCsException(
                    $"`$raw cs` 抛出了未处理的 {ex.InnerException.GetType().Name}: {ex.InnerException.Message}",
                    ex.InnerException);
            }
        }

        private static IEnumerable<string> ResolvePaths(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                yield break;

            foreach (Assembly assembly in assemblies)
            {
                string location = null;
                try
                {
                    // 动态程序集没有磁盘位置，不能作为 MetadataReference。
                    if (!assembly.IsDynamic)
                        location = assembly.Location;
                }
                catch (Exception)
                {
                }

                if (!string.IsNullOrEmpty(location))
                    yield return location;
            }
        }

        private static string HashPaths(IReadOnlyList<string> paths)
        {
            var builder = new StringBuilder();
            foreach (string path in paths)
                builder.Append(path).Append('\n');

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(builder.ToString()));
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }
    }
}
