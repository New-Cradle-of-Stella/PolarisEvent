using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 一个同步 <c>@</c> 组合的实现。同步与异步变体复用同一个工厂和同一组原子方法，
    /// <c>_start</c> 只改变由谁驱动协程，不改变业务步骤。
    /// </summary>
    public interface IPevtCommandRoutine
    {
        IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments arguments);
    }

    /// <summary>
    /// 调用开始时已经求值并快照的实参。参数表达式在进入中间层前全部求值，
    /// 前一个原子方法不可能改变后一个实参的值。
    /// </summary>
    public sealed class PevtArguments
    {
        private readonly IReadOnlyList<PevtValue> _values;

        public PevtArguments(IEnumerable<PevtValue> values)
        {
            var copy = new List<PevtValue>();
            if (values != null)
                copy.AddRange(values);
            _values = new ReadOnlyCollection<PevtValue>(copy);
        }

        public static PevtArguments Empty { get; } = new PevtArguments(null);

        public int Count => _values.Count;

        public PevtValue this[int index] => _values[index];

        public int Int(int index) => _values[index].AsInt;

        public float Float(int index) => _values[index].AsFloat;

        public bool Bool(int index) => _values[index].AsBool;

        public char Char(int index) => _values[index].AsChar;

        public string String(int index) => _values[index].AsString;

        public IReadOnlyList<PevtValue> Values => _values;
    }

    /// <summary>
    /// 整条 <c>@</c> 的普通返回值。C# 迭代器不能用 <c>return</c> 返回值，因此有值 <c>@</c>
    /// 必须在正常结束前调用 <see cref="Set"/>；结果只能提交一次。
    /// </summary>
    public sealed class PevtResultSink
    {
        public bool HasValue { get; private set; }

        public PevtValue Value { get; private set; }

        public void Set(PevtValue value)
        {
            if (HasValue)
                throw new InvalidOperationException("同一条 `@` 指令的返回值只能提交一次。");

            HasValue = true;
            Value = value;
        }

        public void SetInt(int value) => Set(PevtValue.FromInt(value));

        public void SetFloat(float value) => Set(PevtValue.FromFloat(value));

        public void SetBool(bool value) => Set(PevtValue.FromBool(value));

        public void SetChar(char value) => Set(PevtValue.FromChar(value));

        /// <summary>null 不是合法的 PEVT <c>string</c>；调用方传 null 会被上层转成 PEVTR3003。</summary>
        public void SetString(string value)
        {
            if (value == null)
                throw new PevtNullResultException();
            Set(PevtValue.FromString(value));
        }
    }

    /// <summary>处理器向不支持 null 的普通类型返回了空值。调度边界把它转成 PEVTR3003。</summary>
    public sealed class PevtNullResultException : Exception
    {
        public PevtNullResultException()
            : base("处理器向不支持 null 的 PEVT 普通类型返回了空值。")
        {
        }
    }

    /// <summary>
    /// 处理器主动报告一个具体的运行诊断。
    /// </summary>
    public sealed class PevtRoutineFailureException : Exception
    {
        public string DiagnosticId { get; }

        public PevtRoutineFailureException(string diagnosticId, string message, Exception innerException = null)
            : base(message, innerException)
        {
            DiagnosticId = Diagnostics.RuntimeDiagnosticCatalog.Require(diagnosticId).Id;
        }
    }

    /// <summary>
    /// 逆序执行的临时清理栈：为当前组合建立但尚未释放的临时资源或占用登记在这里，失败、取消或事件终止时按后进先出顺序执行。
    /// 持久状态修改不进入这里，也不参与回滚。
    /// </summary>
    public sealed class PevtCleanupStack
    {
        private readonly List<KeyValuePair<string, Action>> _actions = new List<KeyValuePair<string, Action>>();

        public int Count => _actions.Count;

        /// <summary><paramref name="description"/> 只用于诊断展示。</summary>
        public void Push(string description, Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            _actions.Add(new KeyValuePair<string, Action>(description ?? string.Empty, action));
        }

        /// <summary>撤销最近一次登记的清理——例如转场已经正常结束，不再需要 Abort。</summary>
        public bool Pop()
        {
            if (_actions.Count == 0)
                return false;
            _actions.RemoveAt(_actions.Count - 1);
            return true;
        }

        /// <summary>
        /// 逆序执行并清空。单个清理动作抛出异常不阻止其余清理继续执行，
        /// 全部异常收集后返回，由调用方转成 PEVTR1101 附加诊断。
        /// </summary>
        public IReadOnlyList<Exception> RunAll()
        {
            var failures = new List<Exception>();
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _actions[i].Value();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _actions.Clear();
            return new ReadOnlyCollection<Exception>(failures);
        }

        public IReadOnlyList<string> Descriptions
        {
            get
            {
                var result = new List<string>();
                foreach (KeyValuePair<string, Action> entry in _actions)
                    result.Add(entry.Key);
                return new ReadOnlyCollection<string>(result);
            }
        }
    }

    /// <summary>组合协程能看到的运行上下文。刻意不暴露解释器内部结构。</summary>
    public sealed class PevtRoutineContext
    {
        /// <summary>当前 PEVT 执行实例的 ID。</summary>
        public long ExecutionId { get; }

        /// <summary>当前事件 ID。</summary>
        public string EventId { get; }

        /// <summary>当前 <c>@</c> 调用的源码位置。</summary>
        public TextLocation Location { get; }

        /// <summary>受控原子服务。</summary>
        public PevtServices Services { get; }

        public PevtResultSink Result { get; } = new PevtResultSink();

        public PevtCleanupStack Cleanup { get; } = new PevtCleanupStack();

        /// <summary>当前协程是否已被请求取消。只读。</summary>
        public bool IsCancellationRequested { get; internal set; }

        public PevtRoutineContext(long executionId, string eventId, TextLocation location, PevtServices services)
        {
            ExecutionId = executionId;
            EventId = eventId ?? string.Empty;
            Location = location;
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }
    }

    /// <summary>
    /// 按名称与完整参数类型查找同步 <c>@</c> 处理器。描述目录负责"有哪些 API"，
    /// 本注册表负责"每个 API 由哪个协程实现"，两者一一对应，不各自维护一份签名。
    /// </summary>
    public sealed class PevtCommandRegistry
    {
        private readonly Dictionary<string, IPevtCommandRoutine> _routines = new Dictionary<string, IPevtCommandRoutine>(StringComparer.Ordinal);

        public CommandDescriptorCatalog Catalog { get; }

        public PevtCommandRegistry(CommandDescriptorCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>为一个已登记的描述条目挂上处理器。描述目录里没有的重载不能登记。</summary>
        public void Register(CommandDescriptor descriptor, IPevtCommandRoutine routine)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (routine == null)
                throw new ArgumentNullException(nameof(routine));

            if (!Catalog.TryResolve(descriptor.Name, ArgumentTypes(descriptor), out CommandDescriptor known) || !ReferenceEquals(known, descriptor))
                throw new ArgumentException($"`@{descriptor.Name}` 的这个重载不在描述目录中。", nameof(descriptor));

            _routines[descriptor.OverloadKey] = routine;
        }

        /// <summary>按名称与完整实参类型登记处理器的便捷重载。</summary>
        public void Register(string name, IReadOnlyList<PevtType> parameterTypes, IPevtCommandRoutine routine)
        {
            if (!Catalog.TryResolve(name, parameterTypes ?? Array.Empty<PevtType>(), out CommandDescriptor descriptor))
                throw new ArgumentException($"描述目录中没有匹配的 `@{name}` 重载。", nameof(name));
            Register(descriptor, routine);
        }

        public bool TryGetRoutine(CommandDescriptor descriptor, out IPevtCommandRoutine routine)
        {
            routine = null;
            return descriptor != null && _routines.TryGetValue(descriptor.OverloadKey, out routine);
        }

        public int Count => _routines.Count;

        private static IReadOnlyList<PevtType> ArgumentTypes(CommandDescriptor descriptor)
        {
            var types = new List<PevtType>(descriptor.Parameters.Count);
            foreach (CommandParameter parameter in descriptor.Parameters)
                types.Add(parameter.Type);
            return types;
        }
    }
}
