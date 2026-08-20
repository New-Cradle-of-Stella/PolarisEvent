using System;
using System.Collections.Generic;
using Polaris.Pevt.Runtime;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// PEVT-E01 的游戏侧只读查询表。
    ///
    /// 它包装的是原版自己的两张按名字查值的表：
    /// <list type="bullet">
    /// <item>文本序列表（<see cref="TX.getTX"/>）——按标题取一段已登记文本；</item>
    /// <item>数值查询表（<see cref="TX.evalLsnConvert"/>）——按键调用一个已登记的 <c>FnTxEval</c>，
    /// 结果写进 <see cref="TX.value_inputted"/>。</item>
    /// </list>
    ///
    /// 这里刻意**不**调用原版的表达式求值入口（<c>TX.Nm</c> / <c>&amp;{...}</c> 展开）：那条路会解析
    /// 运算符、条件与嵌套调用，等于把任意原版表达式文本重新交给原版解释器执行。本适配器只做
    /// "一个键 + 一串参数 → 一个值"，没有赋值、反射、方法调用或对象引用的通道。
    /// </summary>
    internal sealed class PevtGameQueryTable : IPevtGameQuery
    {
        /// <summary>
        /// 复用同一个参数列表，避免每次查询都分配。PEVT 与原版查询表都只在游戏主线程上跑，
        /// 一次查询在 <see cref="TryRead"/> 返回前就已经用完这份缓冲。
        /// </summary>
        private readonly List<string> _arguments = new List<string>(8);

        public PevtQueryStatus TryRead(string key, IReadOnlyList<string> arguments, out PevtQueryValue value)
        {
            value = default(PevtQueryValue);

            if (string.IsNullOrEmpty(key))
                return PevtQueryStatus.UnknownKey;

            // 文本序列优先：同名文本与同名数值键在原版里也是文本优先（见 TX.convertDefinedData）。
            string text = PevtGameHost.Safe(() => TX.getTX(key, no_make: true, no_error: true)?.Get(), null);
            if (text != null)
            {
                // 文本键不吃查询参数；给了参数说明脚本把它当成函数式键在用。
                if (arguments != null && arguments.Count > 0)
                    return PevtQueryStatus.InvalidArguments;

                value = PevtQueryValue.FromText(text);
                return PevtQueryStatus.Found;
            }

            _arguments.Clear();
            if (arguments != null)
            {
                for (int i = 0; i < arguments.Count; i++)
                    _arguments.Add(arguments[i]);
            }

            bool found;
            try
            {
                found = TX.evalLsnConvert(key, _arguments);
            }
            catch (Exception)
            {
                // 键存在（否则查表根本不会调到处理器），处理器在解析参数时炸了：这是参数问题，
                // 不是"键不存在"，所以按 PEVTR4502 回报而不是 PEVTR4501。
                return PevtQueryStatus.InvalidArguments;
            }

            if (!found)
                return PevtQueryStatus.UnknownKey;

            value = PevtQueryValue.FromNumber(TX.value_inputted);
            return PevtQueryStatus.Found;
        }
    }
}
