using System;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 显示文案解析的游戏侧适配：直通 <see cref="PolarisAPI.Localization"/>。
    ///
    /// 刻意不在这里重写一遍"<c>&amp;</c> 开头 = 本地化键"的判定，也不自己去查 <c>.plang</c>：
    /// 那条链路（内置表 → resolver 链（<c>PlangRuntime</c> 就挂在上面）→ 原版 <c>TX.Get</c> → 显示 key 本身）
    /// 已经服务着设置项文案与 <c>.pui</c>，PEVT 再造一条只会出现"同一个 key 在对话框里和界面上解析成两个结果"。
    /// </summary>
    internal sealed class PevtGameLocalization : IPevtLocalization
    {
        public static PevtGameLocalization Instance { get; } = new PevtGameLocalization();

        private PevtGameLocalization() { }

        public string Text(string raw)
        {
            if (raw == null)
            {
                return null;
            }

            try
            {
                return PolarisAPI.Localization.Text(raw);
            }
            catch (Exception ex)
            {
                // 查表失败不该让整条演出中断：显示原文（很可能就是 `&key` 本身）比事件直接报 PEVTR 好。
                PolarisAPI.Errors.Report(ex, "PEVT display text localization");
                return raw;
            }
        }
    }
}
