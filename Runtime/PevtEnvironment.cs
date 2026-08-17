using System;
using System.Collections.Generic;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Runtime
{
    /// <summary>槽位种类。<c>handler</c> 与普通类型分开存放，不进入普通类型系统。</summary>
    public enum PevtSlotKind
    {
        Variable,
        Constant,
        Handler,
    }

    /// <summary>
    /// 一个变量/常量槽位。每个槽位分别记录声明类型和初始化状态（8.2 节）：
    /// 未初始化槽位仍然有类型，可以作为赋值目标，但不能被读取。
    /// </summary>
    public sealed class PevtSlot
    {
        public string Name { get; }

        public PevtType DeclaredType { get; }

        public PevtSlotKind Kind { get; }

        public bool IsInitialized { get; private set; }

        private PevtValue _value;

        internal PevtSlot(string name, PevtType declaredType, PevtSlotKind kind)
        {
            Name = name;
            DeclaredType = declaredType;
            Kind = kind;
        }

        /// <summary>读取值。调用方必须先检查 <see cref="IsInitialized"/>；未初始化读取由解释器转成 PEVTR3002。</summary>
        public PevtValue Value =>
            IsInitialized ? _value : throw new InvalidOperationException($"槽位 `{Name}` 尚未初始化。");

        /// <summary>写入一份值快照。<see cref="PevtValue"/> 是只读结构体，因此这里存的必然是副本。</summary>
        internal void Set(PevtValue value)
        {
            if (value.Type != DeclaredType)
                throw new InvalidOperationException($"槽位 `{Name}` 声明为 {DeclaredType.DisplayName()}，不能写入 {value.Type.DisplayName()}。");

            _value = value;
            IsInitialized = true;
        }

        public override string ToString() =>
            IsInitialized ? $"{Name} : {DeclaredType.DisplayName()} = {_value}" : $"{Name} : {DeclaredType.DisplayName()} (未初始化)";
    }

    /// <summary>
    /// 一次事件外层调用或一次自定义事件块调用的运行环境（9.4 节）。
    /// </summary>
    public sealed class PevtEnvironment
    {
        private readonly Dictionary<string, PevtSlot> _slots = new Dictionary<string, PevtSlot>(StringComparer.Ordinal);
        private readonly Dictionary<string, PevtHandlerValue> _handlers = new Dictionary<string, PevtHandlerValue>(StringComparer.Ordinal);
        private readonly HashSet<int> _executedDeclarations = new HashSet<int>();

        /// <summary>环境所属的事件 ID 或事件块名称，用于诊断展示。</summary>
        public string ScopeName { get; }

        /// <summary>
        /// 外层环境；只有 <c>exec</c> 的临时片段环境会有。
        /// </summary>
        public PevtEnvironment Parent { get; }

        public PevtEnvironment(string scopeName, PevtEnvironment parent = null)
        {
            ScopeName = scopeName ?? string.Empty;
            Parent = parent;
        }

        public IReadOnlyCollection<string> SlotNames => _slots.Keys;

        /// <summary>
        /// 标记一个声明语句"已经执行过"，声明用它在编译产物里的唯一序号标识。
        /// 同一个声明在同一环境里第二次执行返回 false，由调用方转成 PEVTR3001。
        /// </summary>
        public bool MarkDeclarationExecuted(int declarationId) => _executedDeclarations.Add(declarationId);

        public bool HasDeclarationExecuted(int declarationId) => _executedDeclarations.Contains(declarationId);

        public PevtSlot Declare(string name, PevtType declaredType, PevtSlotKind kind)
        {
            var slot = new PevtSlot(name, declaredType, kind);
            _slots[name] = slot;
            return slot;
        }

        /// <summary>本地找不到时沿父链继续；没有父链（普通事件与事件块）时行为与以前完全一致。</summary>
        public bool TryGetSlot(string name, out PevtSlot slot)
        {
            string key = name ?? string.Empty;
            for (PevtEnvironment env = this; env != null; env = env.Parent)
            {
                if (env._slots.TryGetValue(key, out slot))
                    return true;
            }

            slot = null;
            return false;
        }

        /// <summary>该名称是否声明在本环境自身（而不是继承自外层）。</summary>
        public bool DeclaresLocally(string name) => _slots.ContainsKey(name ?? string.Empty);

        /// <summary>句柄与普通值分开存放，因此同名的普通槽位与句柄槽位不会互相覆盖。</summary>
        public void SetHandler(string name, PevtHandlerValue handler) => _handlers[name] = handler;

        public bool TryGetHandler(string name, out PevtHandlerValue handler)
        {
            string key = name ?? string.Empty;
            for (PevtEnvironment env = this; env != null; env = env.Parent)
            {
                if (env._handlers.TryGetValue(key, out handler))
                    return true;
            }

            handler = null;
            return false;
        }

        public IReadOnlyCollection<string> HandlerNames => _handlers.Keys;

        public override string ToString() => $"{ScopeName} ({_slots.Count} slots, {_handlers.Count} handlers)";
    }

    /// <summary>
    /// <c>handler</c> 的运行期表示：只保存调度器里的协程 ID、拥有者与预期返回类型，
    /// 不保存运行状态副本——<c>status</c>、<c>await</c> 和 <c>kill</c> 始终按 ID 查调度器。
    /// </summary>
    public sealed class PevtHandlerValue
    {
        public long RoutineId { get; }

        public long OwnerExecutionId { get; }

        /// <summary>预期普通返回类型；null 表示无返回值。</summary>
        public PevtType? ExpectedResultType { get; }

        public PevtHandlerValue(long routineId, long ownerExecutionId, PevtType? expectedResultType)
        {
            RoutineId = routineId;
            OwnerExecutionId = ownerExecutionId;
            ExpectedResultType = expectedResultType;
        }

        public override string ToString() => $"handler#{RoutineId}";
    }
}
