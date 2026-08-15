namespace Polaris.Pevt.Diagnostics
{
    /// <summary>诊断严重级。语法设计草案与静态诊断表约定：Error 阻止事件进入可执行状态，Warning 不阻止。</summary>
    public enum DiagnosticSeverity
    {
        Warning,
        Error,
    }
}
