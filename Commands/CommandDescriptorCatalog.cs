using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Commands
{
    /// <summary>
    /// 唯一权威的 <c>@</c> API 描述目录。
    /// </summary>
    public sealed class CommandDescriptorCatalog
    {
        private readonly Dictionary<string, List<CommandDescriptor>> _byName;

        public IReadOnlyList<CommandDescriptor> Descriptors { get; }

        /// <summary>本目录登记的全部条目，含自动派生的 <c>_start</c> 变体。</summary>
        public IReadOnlyList<CommandDescriptor> DeclaredDescriptors { get; }

        public CommandDescriptorCatalog(IEnumerable<CommandDescriptor> descriptors)
        {
            if (descriptors == null)
                throw new ArgumentNullException(nameof(descriptors));

            var declared = new List<CommandDescriptor>();
            foreach (CommandDescriptor descriptor in descriptors)
            {
                if (descriptor == null)
                    throw new ArgumentException("描述列表中不能包含 null。", nameof(descriptors));
                if (descriptor.IsAsync)
                    throw new ArgumentException($"`{descriptor.Name}`：`_start` 变体由目录自动派生，不能作为登记条目传入。", nameof(descriptors));
                declared.Add(descriptor);
            }

            var all = new List<CommandDescriptor>(declared);
            foreach (CommandDescriptor descriptor in declared)
            {
                if (descriptor.CanRunInParallel)
                    all.Add(descriptor.CreateStartVariant());
            }

            var byOverload = new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);
            _byName = new Dictionary<string, List<CommandDescriptor>>(StringComparer.Ordinal);

            foreach (CommandDescriptor descriptor in all)
            {
                if (byOverload.TryGetValue(descriptor.OverloadKey, out CommandDescriptor existing))
                {
                    throw new ArgumentException(
                        $"`@{descriptor.Name}` 的重载无法由参数数量和类型唯一确定：`{existing}` 与 `{descriptor}` 冲突。",
                        nameof(descriptors));
                }

                byOverload[descriptor.OverloadKey] = descriptor;

                if (!_byName.TryGetValue(descriptor.Name, out List<CommandDescriptor> list))
                    _byName[descriptor.Name] = list = new List<CommandDescriptor>();
                list.Add(descriptor);
            }

            // 同一名称下不允许同步条目与派生的异步条目混在一起：`@x` 与 `@x_start` 是两个名称，
            // 但如果有人把 `@foo_start` 当成普通 API 登记，上面的重载检查未必撞上，这里再兜一层。
            foreach (KeyValuePair<string, List<CommandDescriptor>> entry in _byName)
            {
                bool hasAsync = false;
                bool hasSync = false;
                foreach (CommandDescriptor descriptor in entry.Value)
                {
                    hasAsync |= descriptor.IsAsync;
                    hasSync |= !descriptor.IsAsync;
                }

                if (hasAsync && hasSync)
                    throw new ArgumentException($"`@{entry.Key}` 同时存在同步条目与自动派生的 `_start` 变体。", nameof(descriptors));
            }

            DeclaredDescriptors = new ReadOnlyCollection<CommandDescriptor>(declared);
            Descriptors = new ReadOnlyCollection<CommandDescriptor>(all);
        }

        /// <summary>登记的第一版 P0/P1 全部 API。</summary>
        public static CommandDescriptorCatalog Builtin { get; } = new CommandDescriptorCatalog(BuiltinCommandDescriptors.Create());

        public IReadOnlyList<CommandDescriptor> Find(string name) =>
            _byName.TryGetValue(name ?? string.Empty, out List<CommandDescriptor> list)
                ? list
                : Array.Empty<CommandDescriptor>();

        /// <summary>按名称与完整实参类型序列唯一解析一个重载。</summary>
        public bool TryResolve(string name, IReadOnlyList<PevtType> argumentTypes, out CommandDescriptor descriptor)
        {
            descriptor = null;
            if (argumentTypes == null)
                throw new ArgumentNullException(nameof(argumentTypes));

            foreach (CommandDescriptor candidate in Find(name))
            {
                if (candidate.Parameters.Count != argumentTypes.Count)
                    continue;

                bool matches = true;
                for (int i = 0; i < argumentTypes.Count; i++)
                {
                    if (candidate.Parameters[i].Type != argumentTypes[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    descriptor = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>投影成绑定器用的 API 表。每次调用产生一份新表，调用方对它的登记不会污染本目录。</summary>
        public BuiltinApiTable ToBuiltinApiTable()
        {
            var table = new BuiltinApiTable();
            foreach (CommandDescriptor descriptor in Descriptors)
                table.Register(descriptor.ToBuiltinSignature());
            return table;
        }

        public override string ToString() => $"{Descriptors.Count} descriptors ({DeclaredDescriptors.Count} declared)";
    }
}
