using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Commands
{
    /// <summary>
    /// 一条 <c>@</c> API 的等待类别，对应内置事件语句表「执行方式」列。
    /// </summary>
    public enum CommandWaitKind
    {
        /// <summary>立即：处理器在当前解释步完成。</summary>
        Immediate,

        /// <summary>查询：立即完成且必定产生普通返回值。</summary>
        Query,

        /// <summary>等待：可以跨帧，自动暂停当前 PEVT 流程直到结束，不返回 handler。</summary>
        Wait,

        /// <summary>等待／可并行：既有线性等待版本，也必须登记同参数的 <c>_start</c> 异步版本。</summary>
        WaitParallel,
    }

    /// <summary>第一版实现优先级（内置能力规范第 17 节）。</summary>
    public enum CommandPriority
    {
        /// <summary>足以完成普通 Galgame 演出。</summary>
        P0,

        /// <summary>地图内剧情与进度。</summary>
        P1,

        /// <summary>游戏专用演出（Alice In Cradle 领域扩展）。</summary>
        P2,
    }

    /// <summary>一条 <c>@</c> API 的形参：名称、普通类型与可选参数域。构造后不可变。</summary>
    public sealed class CommandParameter
    {
        public string Name { get; }

        public PevtType Type { get; }

        /// <summary>取值语义；null 表示除普通类型外没有额外约束。</summary>
        public ParameterDomain Domain { get; }

        public CommandParameter(string name, PevtType type, ParameterDomain domain = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("形参名不能为空。", nameof(name));
            if (!type.IsOrdinaryType())
                throw new ArgumentException($"形参 `{name}` 的类型必须是五种普通类型之一。", nameof(type));
            if (domain != null && domain.UnderlyingType != type)
                throw new ArgumentException($"参数域 `{domain.Name}` 依附于 {domain.UnderlyingType.DisplayName()}，与形参类型 {type.DisplayName()} 不符。", nameof(domain));

            Name = name;
            Type = type;
            Domain = domain;
        }

        public override string ToString() => $"{Name} : {Type.DisplayName()}";
    }

    /// <summary>
    /// 一条 <c>@</c> API 的权威描述。这是全项目唯一的 API 目录条目类型——绑定器用的
    /// <see cref="BuiltinApiTable"/> 由本目录投影产生，不是另一份平行注册表。
    /// </summary>
    public sealed class CommandDescriptor
    {
        /// <summary>可并行 API 的异步变体固定后缀。</summary>
        public const string StartSuffix = "_start";

        /// <summary>不含 <c>@</c> 的调用名称。</summary>
        public string Name { get; }

        public IReadOnlyList<CommandParameter> Parameters { get; }

        /// <summary>普通返回类型；null 表示纯调用。</summary>
        public PevtType? ReturnType { get; }

        public CommandWaitKind WaitKind { get; }

        public CommandPriority Priority { get; }

        /// <summary>内置能力规范里的能力标识，例如 <c>dialogue.show</c>。只作文档追溯用，不是可调用名称。</summary>
        public string Capability { get; }

        /// <summary>调用是否立即返回 <c>handler</c>。只有派生出来的 <c>_start</c> 变体为 true。</summary>
        public bool IsAsync { get; }

        /// <summary>本条目派生自哪个同步条目；非 <c>_start</c> 变体为 null。</summary>
        public CommandDescriptor ParallelSource { get; }

        public CommandDescriptor(
            string name,
            IEnumerable<CommandParameter> parameters,
            PevtType? returnType,
            CommandWaitKind waitKind,
            CommandPriority priority,
            string capability)
            : this(name, parameters, returnType, waitKind, priority, capability, isAsync: false, parallelSource: null)
        {
            if (name.EndsWith(StartSuffix, StringComparison.Ordinal))
                throw new ArgumentException($"`{name}`：`{StartSuffix}` 变体由可并行条目自动派生，不能单独登记。", nameof(name));
        }

        private CommandDescriptor(
            string name,
            IEnumerable<CommandParameter> parameters,
            PevtType? returnType,
            CommandWaitKind waitKind,
            CommandPriority priority,
            string capability,
            bool isAsync,
            CommandDescriptor parallelSource)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("API 名称不能为空。", nameof(name));
            if (returnType.HasValue && !returnType.Value.IsOrdinaryType())
                throw new ArgumentException($"`{name}` 的返回类型必须是五种普通类型之一。", nameof(returnType));
            if (waitKind == CommandWaitKind.Query && !returnType.HasValue)
                throw new ArgumentException($"`{name}` 标记为查询，但没有返回值。", nameof(waitKind));

            var copy = new List<CommandParameter>();
            if (parameters != null)
            {
                foreach (CommandParameter parameter in parameters)
                {
                    if (parameter == null)
                        throw new ArgumentException("形参列表中不能包含 null。", nameof(parameters));
                    copy.Add(parameter);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CommandParameter parameter in copy)
            {
                if (!seen.Add(parameter.Name))
                    throw new ArgumentException($"`{name}` 的形参名 `{parameter.Name}` 重复。", nameof(parameters));
            }

            Name = name;
            Parameters = new ReadOnlyCollection<CommandParameter>(copy);
            ReturnType = returnType;
            WaitKind = waitKind;
            Priority = priority;
            Capability = capability ?? string.Empty;
            IsAsync = isAsync;
            ParallelSource = parallelSource;
        }

        /// <summary>是否登记了并行能力。</summary>
        public bool CanRunInParallel => WaitKind == CommandWaitKind.WaitParallel;

        /// <summary>可并行条目对应的 <c>_start</c> 名称；不可并行时为 null。</summary>
        public string StartName => CanRunInParallel ? Name + StartSuffix : null;

        /// <summary>
        /// 派生 <c>_start</c> 异步变体：相同参数、相同普通返回值契约，只是调用时立即返回 handler。
        /// 参数列表直接复用同一批不可变 <see cref="CommandParameter"/> 实例，不复制描述数据。
        /// </summary>
        public CommandDescriptor CreateStartVariant()
        {
            if (!CanRunInParallel)
                throw new InvalidOperationException($"`{Name}` 不是「等待／可并行」API，没有 `{StartSuffix}` 变体。");

            return new CommandDescriptor(
                StartName,
                Parameters,
                ReturnType,
                WaitKind,
                Priority,
                Capability,
                isAsync: true,
                parallelSource: this);
        }

        /// <summary>重载键：名称 + 参数数量 + 完整参数类型序列。同名重载必须由它唯一确定。</summary>
        public string OverloadKey
        {
            get
            {
                var builder = new StringBuilder(Name).Append('/').Append(Parameters.Count);
                foreach (CommandParameter parameter in Parameters)
                    builder.Append(':').Append(parameter.Type.DisplayName());
                return builder.ToString();
            }
        }

        /// <summary>投影成绑定器使用的签名。绑定器只认普通类型，参数域随形参一起带过去供工具使用。</summary>
        public BuiltinSignature ToBuiltinSignature()
        {
            var parameters = new List<BuiltinParameter>(Parameters.Count);
            foreach (CommandParameter parameter in Parameters)
                parameters.Add(new BuiltinParameter(parameter.Name, parameter.Type, parameter.Domain));

            return new BuiltinSignature(Name, IsAsync, parameters, ReturnType);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            if (IsAsync)
                builder.Append("async ");
            builder.Append('@').Append(Name).Append('(');
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(Parameters[i]);
            }

            builder.Append(')');
            if (ReturnType.HasValue)
                builder.Append(" : ").Append(ReturnType.Value.DisplayName());
            return builder.ToString();
        }
    }
}
