namespace Polaris.Pevt.Syntax
{
    /// <summary><see cref="TokenValue"/> 实际持有的字面量种类；对应 8.2 节允许的五种 PEVT 变量类型。</summary>
    public enum TokenValueKind
    {
        None,
        Integer,
        Float,
        Boolean,
        Char,
        String,
    }
}
