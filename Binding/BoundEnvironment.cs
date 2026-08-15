using System.Collections.Generic;

namespace Polaris.Pevt.Binding
{
    /// <summary>
    /// 9.4 节的一个独立词法环境（外层事件一个，每个自定义事件块定义各自独立一个——彼此不共享，
    /// 也不构成父子链；块不会隐式捕获外层变量）。<c>if</c>/<c>elif</c>/<c>else</c>/<c>while</c>/
    /// <c>switch</c>/<c>case</c> 不创建新环境，但它们的正文各自是一条独立的执行路径，所以本类型把
    /// "这个名称永久存在于当前环境"（<see cref="_symbols"/>，只用于 PEVT6007 重复声明检查和类型查找，
    /// 一旦声明就不会因为分支而撤销）和"这个名称在当前路径上是否已经声明/初始化"
    /// （<see cref="_initialized"/>，随分支克隆、合并）分成两套独立状态维护。
    /// </summary>
    public sealed class BoundEnvironment
    {
        private readonly Dictionary<string, Symbol> _symbols = new Dictionary<string, Symbol>();

        /// <summary>只包含"在当前路径上至少已经声明"的名称；值为 true 表示同时已经完成初始化。
        /// 缺席等价于"在当前路径上从未声明过"（PEVT6001 的判定依据）。</summary>
        private readonly Dictionary<string, bool> _initialized = new Dictionary<string, bool>();

        public bool TryGetSymbol(string name, out Symbol symbol) => _symbols.TryGetValue(name, out symbol);

        public bool IsDeclaredEver(string name) => _symbols.ContainsKey(name);

        /// <summary>当前路径上的声明/初始化状态；<paramref name="declared"/> 为 false 时 <paramref name="initialized"/> 必然也是 false。</summary>
        public void GetFlowState(string name, out bool declared, out bool initialized)
        {
            declared = _initialized.TryGetValue(name, out initialized);
        }

        /// <summary>登记一个新符号（PEVT6007 的调用方负责先自行判断是否已经存在）并把它标记为
        /// 当前路径上已声明；<paramref name="initialized"/> 对应常量/形参"声明即初始化"，
        /// 变量则按是否带初始化器传入。</summary>
        public void Declare(Symbol symbol, bool initialized)
        {
            _symbols[symbol.Name] = symbol;
            _initialized[symbol.Name] = initialized;
        }

        /// <summary>把已经存在的名称在当前路径上标记为已初始化（赋值语句用）。</summary>
        public void MarkInitialized(string name) => _initialized[name] = true;

        /// <summary>为分支体（if/elif/else 的一支、while 正文、一个 case/default）拍一份当前流状态快照，
        /// 分支体内的声明/初始化只影响这份克隆，不影响本环境，直到调用方显式 <see cref="Restore"/> 或
        /// 通过 <see cref="Merge"/> 合并回来。</summary>
        public Dictionary<string, bool> SnapshotFlowState() => new Dictionary<string, bool>(_initialized);

        public void Restore(Dictionary<string, bool> snapshot)
        {
            _initialized.Clear();
            foreach (KeyValuePair<string, bool> entry in snapshot)
                _initialized[entry.Key] = entry.Value;
        }

        /// <summary>
        /// 9.4 节"每条可达路径都……才视为已定义/已初始化"的合并规则：一个名称合并后仍然存在，
        /// 当且仅当它在全部分支里都存在；仍然是"已初始化"，当且仅当它在全部分支里都已初始化。
        /// <paramref name="isExhaustive"/> 为 false（没有 <c>else</c>/<c>default</c>）时，"什么都不做"
        /// 本身也是一条合法路径，等价于再拿 <paramref name="preState"/> 参与一次合并。
        /// </summary>
        public static Dictionary<string, bool> Merge(IReadOnlyList<Dictionary<string, bool>> branchStates, bool isExhaustive, Dictionary<string, bool> preState)
        {
            var merged = new Dictionary<string, bool>(branchStates[0]);
            for (int i = 1; i < branchStates.Count; i++)
                IntersectInto(merged, branchStates[i]);
            if (!isExhaustive)
                IntersectInto(merged, preState);
            return merged;
        }

        private static void IntersectInto(Dictionary<string, bool> merged, Dictionary<string, bool> other)
        {
            var toRemove = new List<string>();
            foreach (KeyValuePair<string, bool> entry in merged)
            {
                if (!other.TryGetValue(entry.Key, out bool otherInitialized))
                {
                    toRemove.Add(entry.Key);
                    continue;
                }

                if (!otherInitialized)
                    merged[entry.Key] = false;
            }

            foreach (string key in toRemove)
                merged.Remove(key);
        }
    }
}
