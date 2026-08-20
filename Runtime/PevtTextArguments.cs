using System.Collections.Generic;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;

namespace Polaris.Pevt.Runtime
{
    /// <summary>
    /// 实参进入处理器之前的显示文案解析。
    ///
    /// 放在这里而不是各处理器里，是因为"哪些形参是给玩家看的文案"已经在
    /// <see cref="BuiltinCommandDescriptors"/> 里用 <see cref="ParameterDomain.Text"/> 声明过一次了；
    /// 让 <c>@say</c>、<c>@narrate</c>、<c>@choose</c>…各自记得调一次解析，等于把同一份清单抄第二遍，
    /// 而漏抄的那一条不会报错，只会在游戏里显示成一串 <c>&amp;key</c>。
    ///
    /// <para>
    /// 解析发生在参数求值之后、组合协程开始之前，因此对处理器完全透明：它们看到的 <c>args.String(i)</c>
    /// 一律是最终文案。<c>_start</c> 异步变体与同步条目共用同一批 <see cref="CommandParameter"/> 实例，
    /// 两条调用路径因此不可能得到不同的判定。
    /// </para>
    /// </summary>
    internal static class PevtTextArguments
    {
        /// <summary>
        /// 按 <paramref name="descriptor"/> 的参数域解析 <paramref name="values"/> 中的显示文案。
        /// 没有文案形参、或宿主没有接本地化服务时原样返回，不做任何多余的数组拷贝。
        /// </summary>
        public static IReadOnlyList<PevtValue> Localize(
            CommandDescriptor descriptor, PevtValue[] values, IPevtLocalization localization)
        {
            if (localization == null || descriptor == null || values == null)
                return values;

            PevtValue[] result = null;
            IReadOnlyList<CommandParameter> parameters = descriptor.Parameters;
            int count = values.Length < parameters.Count ? values.Length : parameters.Count;

            for (int i = 0; i < count; i++)
            {
                // 类型不符只可能出现在绑定器被绕过的路径上（例如宿主直接构造实参）；那时不解析，
                // 让后面的处理器按它自己的方式失败，别在这里多抛一个 InvalidOperationException。
                if (!ReferenceEquals(parameters[i].Domain, ParameterDomain.Text) || values[i].Type != PevtType.String)
                    continue;

                string raw = values[i].AsString;
                string resolved = localization.Text(raw);

                // 解析器返回 null 视为"没有意见"：宁可显示原文，也不要让处理器拿到 null 字符串。
                if (resolved == null || ReferenceEquals(resolved, raw) || resolved == raw)
                    continue;

                if (result == null)
                {
                    result = new PevtValue[values.Length];
                    values.CopyTo(result, 0);
                }

                result[i] = PevtValue.FromString(resolved);
            }

            return result ?? (IReadOnlyList<PevtValue>)values;
        }
    }
}
