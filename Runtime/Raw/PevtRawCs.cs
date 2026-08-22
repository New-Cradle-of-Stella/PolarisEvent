using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime.Raw
{
    /// <summary>一个 <c>$raw cs</c> 代码块出现的位置。它决定是否要求"每条可达退出路径都返回值"。</summary>
    public enum PevtRawCsUsage
    {
        /// <summary>纯调用语句，返回值（如果有）被丢弃。</summary>
        Statement,

        /// <summary>PEVT 表达式，必须产出一个值。</summary>
        Expression,
    }

    /// <summary>一个传入 <c>$raw cs</c> 的 PEVT 变量副本：同名、同类型的 C# 形参。</summary>
    public sealed class PevtRawCsParameter
    {
        public string Name { get; }

        public PevtType Type { get; }

        public PevtRawCsParameter(string name, PevtType type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("传入变量名不能为空。", nameof(name));
            if (!type.IsOrdinaryType())
                throw new ArgumentException($"传入变量 `{name}` 的类型必须是五种普通类型之一。", nameof(type));

            Name = name;
            Type = type;
        }

        public override string ToString() => $"{Name} : {Type.DisplayName()}";
    }

    /// <summary>
    /// 把 <c>$raw cs</c> 内容里的位置映射回 <c>.pevt</c> 源码。
    /// </summary>
    public sealed class PevtRawCsSourceMap
    {
        private readonly SourceText _source;
        private readonly int[] _offsets;

        private PevtRawCsSourceMap(SourceText source, int[] offsets)
        {
            _source = source;
            _offsets = offsets;
        }

        /// <summary>解码后文本的长度。</summary>
        public int DecodedLength => _offsets.Length - 1;

        /// <summary>
        /// 由原始文本块在源文件中的原文与起点建表。<paramref name="rawText"/> 是未解码的原文
        /// （<c>SyntaxToken.Text</c>），<paramref name="rawStart"/> 是它在源文件中的偏移。
        /// </summary>
        public static PevtRawCsSourceMap Create(SourceText source, string rawText, int rawStart)
        {
            if (source == null)
                return null;

            rawText = rawText ?? string.Empty;
            var offsets = new List<int>(rawText.Length + 1);

            int i = 0;
            while (i < rawText.Length)
            {
                // 词法器认的唯一转义：`\'''` 四个字符解码成 `'''` 三个字符。
                if (rawText[i] == '\\' && i + 3 < rawText.Length
                    && rawText[i + 1] == '\'' && rawText[i + 2] == '\'' && rawText[i + 3] == '\'')
                {
                    offsets.Add(rawStart + i + 1);
                    offsets.Add(rawStart + i + 2);
                    offsets.Add(rawStart + i + 3);
                    i += 4;
                    continue;
                }

                offsets.Add(rawStart + i);
                i++;
            }

            offsets.Add(rawStart + rawText.Length);
            return new PevtRawCsSourceMap(source, offsets.ToArray());
        }

        /// <summary>把解码文本上的一段区间映射成源文件位置。越界值被夹到区间端点。</summary>
        public TextLocation Locate(int decodedStart, int decodedLength)
        {
            int start = Clamp(decodedStart);
            int end = Clamp(decodedStart + Math.Max(decodedLength, 0));
            if (end < start)
                end = start;

            return _source.GetLocation(TextSpan.FromBounds(_offsets[start], _offsets[end]));
        }

        private int Clamp(int index) => index < 0 ? 0 : index > DecodedLength ? DecodedLength : index;
    }

    /// <summary>
    /// 一次 <c>$raw cs</c> 的编译请求：代码、形参契约和使用位置。
    /// 它同时是缓存键的来源（阶段 F 要求"缓存键包含代码、参数名/类型、引用集合与语言版本"）——
    /// 前三项在这里，后两项由编译器的 <see cref="IPevtRawCsCompiler.CacheScope"/> 提供。
    /// </summary>
    public sealed class PevtRawCsRequest
    {
        public string Code { get; }

        public IReadOnlyList<PevtRawCsParameter> Parameters { get; }

        public PevtRawCsUsage Usage { get; }

        /// <summary>诊断回映；宿主没有源文本时为 null，此时诊断不带位置。</summary>
        public PevtRawCsSourceMap SourceMap { get; }

        public PevtRawCsRequest(
            string code,
            IEnumerable<PevtRawCsParameter> parameters = null,
            PevtRawCsUsage usage = PevtRawCsUsage.Statement,
            PevtRawCsSourceMap sourceMap = null)
        {
            Code = code ?? string.Empty;
            Usage = usage;
            SourceMap = sourceMap;

            var copy = new List<PevtRawCsParameter>();
            if (parameters != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (PevtRawCsParameter parameter in parameters)
                {
                    if (parameter == null)
                        throw new ArgumentException("传入变量列表中不能包含 null。", nameof(parameters));
                    if (!seen.Add(parameter.Name))
                        throw new ArgumentException($"传入变量 `{parameter.Name}` 在同一个列表中重复出现。", nameof(parameters));
                    copy.Add(parameter);
                }
            }

            Parameters = new ReadOnlyCollection<PevtRawCsParameter>(copy);
        }

        /// <summary>代码 + 参数名/类型 + 使用位置组成的签名。不含引用集合与语言版本。</summary>
        public string Signature
        {
            get
            {
                var builder = new StringBuilder();
                builder.Append(Usage == PevtRawCsUsage.Expression ? "expr" : "stmt");
                foreach (PevtRawCsParameter parameter in Parameters)
                    builder.Append('|').Append(parameter.Name).Append(':').Append(parameter.Type.DisplayName());
                builder.Append("\n").Append(Code);
                return builder.ToString();
            }
        }
    }

    /// <summary>一次 <c>$raw cs</c> 的编译结果。失败时 <see cref="Diagnostics"/> 里是 PEVT8007–8010。</summary>
    public sealed class PevtRawCsCompilation
    {
        /// <summary>PEVT 返回类型；null 表示纯调用（C# 侧 <c>void</c>）。</summary>
        public PevtType? ReturnType { get; }

        /// <summary>已编译好的调用入口，失败时为 null。实参按 <see cref="PevtRawCsRequest.Parameters"/> 顺序传入。</summary>
        public Func<object[], object> Invoke { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Success => Invoke != null;

        public PevtRawCsCompilation(PevtType? returnType, Func<object[], object> invoke, IEnumerable<Diagnostic> diagnostics = null)
        {
            ReturnType = returnType;
            Invoke = invoke;

            var copy = new List<Diagnostic>();
            if (diagnostics != null)
                copy.AddRange(diagnostics);
            Diagnostics = new ReadOnlyCollection<Diagnostic>(copy);
        }

        /// <summary>失败结果的便捷构造。</summary>
        public static PevtRawCsCompilation Failed(IEnumerable<Diagnostic> diagnostics) =>
            new PevtRawCsCompilation(null, null, diagnostics);
    }

    /// <summary>
    /// <c>$raw cs</c> 的 C# 编译端口。
    /// </summary>
    public interface IPevtRawCsCompiler
    {
        /// <summary>引用集合与语言版本的稳定标识。它是缓存键的一部分。</summary>
        string CacheScope { get; }

        PevtRawCsCompilation Compile(PevtRawCsRequest request);
    }

    /// <summary>
    /// 加载阶段的 <c>$raw cs</c> 分析器。绑定器用它得到 PEVT8007–8010 与代码块的返回类型。
    /// </summary>
    public interface IPevtRawCsAnalyzer
    {
        /// <summary>分析一个代码块，向 <paramref name="diagnostics"/> 报告静态诊断并返回 PEVT 返回类型。</summary>
        PevtType? Analyze(PevtRawCsRequest request, DiagnosticBag diagnostics);
    }

    /// <summary>
    /// 受信任 <c>$raw cs</c> 执行器：编译缓存 + 值副本转换 + 失败契约。
    /// </summary>
    public sealed class PevtRawCsExecutor : IPevtRawCsAnalyzer
    {
        private readonly IPevtRawCsCompiler _compiler;
        private readonly Dictionary<string, PevtRawCsCompilation> _cache = new Dictionary<string, PevtRawCsCompilation>(StringComparer.Ordinal);

        public PevtRawCsExecutor(IPevtRawCsCompiler compiler)
        {
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        }

        /// <summary>缓存里的编译结果数量。</summary>
        public int CacheCount => _cache.Count;

        /// <summary>实际调用底层编译器的次数。缓存有效性的直接证据。</summary>
        public int CompileCount { get; private set; }

        public string CacheScope => _compiler.CacheScope;

        public PevtRawCsCompilation GetOrCompile(PevtRawCsRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string key = _compiler.CacheScope + "\n" + request.Signature;
            if (_cache.TryGetValue(key, out PevtRawCsCompilation cached))
                return cached;

            CompileCount++;
            PevtRawCsCompilation compilation = _compiler.Compile(request)
                ?? PevtRawCsCompilation.Failed(new[]
                {
                    new Diagnostic("PEVT8007", DiagnosticSeverity.Error, "C# 编译器没有返回结果。", null),
                });

            // 只缓存成功的编译：失败结果里的诊断带着第一次请求的源码位置，缓存它会让另一个文件里相同的 `$raw cs`
            // 拿到指向别人文件的 PEVT8007。失败的代码块本来就跑不起来，重算一次没有代价。
            if (compilation.Success)
                _cache[key] = compilation;

            return compilation;
        }

        /// <summary>加载阶段分析：把编译诊断原样交给绑定器，并返回代码块的 PEVT 返回类型。</summary>
        public PevtType? Analyze(PevtRawCsRequest request, DiagnosticBag diagnostics)
        {
            PevtRawCsCompilation compilation = GetOrCompile(request);

            if (diagnostics != null)
            {
                foreach (Diagnostic diagnostic in compilation.Diagnostics)
                    diagnostics.Add(diagnostic);
            }

            return compilation.ReturnType;
        }

        /// <summary>
        /// 执行一次代码块。
        /// 失败契约：编译失败或代码块抛出异常时抛 <see cref="PevtRawCsException"/>（PEVTR4102）；
        /// 向不支持 <c>null</c> 的 <c>string</c> 返回空值时抛 <see cref="PevtNullResultException"/>
        /// （由调用方翻成 PEVTR3003）。
        /// </summary>
        public PevtRawCsResult Execute(PevtRawCsRequest request, IReadOnlyList<PevtValue> arguments)
        {
            PevtRawCsCompilation compilation = GetOrCompile(request);
            if (!compilation.Success)
                throw new PevtRawCsException(DescribeFailure(compilation));

            var args = new object[request.Parameters.Count];
            for (int i = 0; i < args.Length; i++)
            {
                PevtValue value = arguments != null && i < arguments.Count ? arguments[i] : default;
                args[i] = PevtRawCsTypes.ToClrValue(value, request.Parameters[i].Type);
            }

            object result;
            try
            {
                result = compilation.Invoke(args);
            }
            catch (PevtRawCsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PevtRawCsException($"`$raw cs` 抛出了未处理的 {ex.GetType().Name}: {ex.Message}", ex);
            }

            if (!compilation.ReturnType.HasValue)
                return PevtRawCsResult.Void;

            PevtType returnType = compilation.ReturnType.Value;
            if (result == null)
            {
                // 12.3 节的返回类型表里没有可空类型；null 由调用方翻成 PEVTR3003。
                throw new PevtNullResultException();
            }

            return new PevtRawCsResult(PevtRawCsTypes.ToPevtValue(result, returnType), true);
        }

        private static string DescribeFailure(PevtRawCsCompilation compilation)
        {
            var builder = new StringBuilder("`$raw cs` 的 C# 内容无法编译");
            foreach (Diagnostic diagnostic in compilation.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;
                builder.Append('\n').Append("  ").Append(diagnostic.Id).Append(": ").Append(diagnostic.Message);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 延迟创建底层编译器的包装。
    /// </summary>
    public sealed class PevtLazyRawCsCompiler : IPevtRawCsCompiler
    {
        private readonly Func<IPevtRawCsCompiler> _factory;
        private IPevtRawCsCompiler _inner;

        public PevtLazyRawCsCompiler(Func<IPevtRawCsCompiler> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>底层编译器是否已经建出来，供只读诊断使用。</summary>
        public bool IsCreated => _inner != null;

        public string CacheScope => Inner.CacheScope;

        public PevtRawCsCompilation Compile(PevtRawCsRequest request) => Inner.Compile(request);

        private IPevtRawCsCompiler Inner => _inner ?? (_inner = _factory()
            ?? throw new InvalidOperationException("`$raw cs` 编译器工厂返回了 null。"));
    }

    /// <summary>一次 <c>$raw cs</c> 执行的结果。</summary>
    public struct PevtRawCsResult
    {
        public PevtValue Value { get; }

        public bool HasValue { get; }

        public PevtRawCsResult(PevtValue value, bool hasValue)
        {
            Value = value;
            HasValue = hasValue;
        }

        public static PevtRawCsResult Void => new PevtRawCsResult(default, false);
    }

    /// <summary><c>$raw cs</c> 的编译或执行失败。调度边界把它翻成 PEVTR4102。</summary>
    public sealed class PevtRawCsException : Exception
    {
        public PevtRawCsException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// 12.3 节的 C# ↔ PEVT 类型映射，以及 <c>$raw cs</c> 的 C# 包装文本。
    /// </summary>
    public static class PevtRawCsTypes
    {
        /// <summary>包装类型名。</summary>
        public const string WrapperTypeName = "PevtRawCsBlock";

        /// <summary>包装方法名。</summary>
        public const string WrapperMethodName = "Run";

        /// <summary>把 C# 类型全名映射成 PEVT 普通类型；不在 12.3 节表里时返回 false。</summary>
        public static bool TryMapReturnType(string clrFullName, out PevtType type)
        {
            switch (clrFullName)
            {
                case "System.Int32": type = PevtType.Int; return true;
                case "System.Single": type = PevtType.Float; return true;
                case "System.Boolean": type = PevtType.Bool; return true;
                case "System.Char": type = PevtType.Char; return true;
                case "System.String": type = PevtType.String; return true;
                default: type = PevtType.Error; return false;
            }
        }

        /// <summary>PEVT 普通类型对应的 C# 关键字。</summary>
        public static string CSharpKeyword(PevtType type)
        {
            switch (type)
            {
                case PevtType.Int: return "int";
                case PevtType.Float: return "float";
                case PevtType.Bool: return "bool";
                case PevtType.Char: return "char";
                case PevtType.String: return "string";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "只有五种普通类型能出现在 `$raw cs` 契约里。");
            }
        }

        public static object ToClrValue(PevtValue value, PevtType declaredType)
        {
            switch (declaredType)
            {
                case PevtType.Int: return value.Type == PevtType.Int ? value.AsInt : default(int);
                case PevtType.Float: return value.Type == PevtType.Float ? value.AsFloat : default(float);
                case PevtType.Bool: return value.Type == PevtType.Bool && value.AsBool;
                case PevtType.Char: return value.Type == PevtType.Char ? value.AsChar : default(char);
                case PevtType.String: return value.Type == PevtType.String ? value.AsString ?? string.Empty : string.Empty;
                default: throw new ArgumentOutOfRangeException(nameof(declaredType), declaredType, "只有五种普通类型能传入 `$raw cs`。");
            }
        }

        public static PevtValue ToPevtValue(object result, PevtType type)
        {
            switch (type)
            {
                case PevtType.Int: return PevtValue.FromInt(Convert.ToInt32(result, CultureInfo.InvariantCulture));
                case PevtType.Float: return PevtValue.FromFloat(Convert.ToSingle(result, CultureInfo.InvariantCulture));
                case PevtType.Bool: return PevtValue.FromBool(Convert.ToBoolean(result, CultureInfo.InvariantCulture));
                case PevtType.Char: return PevtValue.FromChar(Convert.ToChar(result, CultureInfo.InvariantCulture));
                case PevtType.String: return PevtValue.FromString(Convert.ToString(result, CultureInfo.InvariantCulture));
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "只有五种普通类型能从 `$raw cs` 返回。");
            }
        }

        /// <summary>
        /// 生成 C# 包装源码：<paramref name="returnType"/> 为 null 时包装成 <c>void</c>，非 null 且
        /// <paramref name="appendTrailingReturn"/> 为 true 时在正文末尾补 <c>return default(T);</c>
        /// （表达式位置"每条路径都返回值"交给 C# 自己的 CS0161 判定）。<paramref name="codeOffset"/> 返回正文在包装源码里的起点，
        /// 用来把 C# 诊断位置换算回原始文本块内的偏移。
        /// </summary>
        public static string BuildWrapperSource(
            PevtRawCsRequest request,
            PevtType? returnType,
            bool appendTrailingReturn,
            out int codeOffset)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var builder = new StringBuilder();

            // 唯一的隐式 using。`System` 之外不引入任何命名空间——原始 C# 要访问游戏类型时写全名，
            // 这样"能看见什么"不随 PolarisEvent 的内部 using 变化。
            builder.Append("using System;\n");
            builder.Append("internal static class ").Append(WrapperTypeName).Append("\n{\n");
            builder.Append("internal static ")
                .Append(returnType.HasValue ? CSharpKeyword(returnType.Value) : "void")
                .Append(' ').Append(WrapperMethodName).Append('(');

            for (int i = 0; i < request.Parameters.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(CSharpKeyword(request.Parameters[i].Type)).Append(' ').Append(request.Parameters[i].Name);
            }

            builder.Append(")\n{\n");

            codeOffset = builder.Length;
            builder.Append(request.Code);
            if (!request.Code.EndsWith("\n", StringComparison.Ordinal))
                builder.Append('\n');

            if (returnType.HasValue && appendTrailingReturn)
                builder.Append("return default(").Append(CSharpKeyword(returnType.Value)).Append(");\n");

            builder.Append("}\n}\n");
            return builder.ToString();
        }
    }
}
