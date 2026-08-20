namespace Polaris.Pevt.Live
{
    /// <summary>
    /// PEVT 热重载线协议：PolarisTools 与游戏进程共用的唯一契约。
    /// <para>
    /// 与 PUI 的线协议不同，这里没有操作码——载荷就是一批 `.pevt` 源文本。游戏侧收到之后走的是
    /// 与嵌入源相同的静态校验，因此工具侧不需要（也不允许）替它做任何解析或降级。
    /// </para>
    /// 帧格式（<c>BinaryWriter</c>/<c>BinaryReader</c>，UTF-8 长度前缀字符串）：
    /// <code>
    /// int    Version
    /// string FocusPath      // 触发本次推送的文件，空串表示"没有特定焦点"
    /// int    Count
    /// Count × { string SourcePath; string Text }
    /// ---- 回执 ----
    /// bool   Ok
    /// string Message
    /// </code>
    /// 改动字节序列必须同时 +1 <see cref="Version"/>；两侧版本不一致时明确报错，不按错误的序列硬解。
    /// </summary>
    public static class PevtLiveProtocol
    {
        public const int Version = 1;

        /// <summary>命名管道名。与 PolarisTools 的 <c>PevtLiveClient.PipeName</c> 是同一个常量。</summary>
        public const string PipeName = "Polaris.Event.Pevt.Live";

        /// <summary>一次推送最多携带的 `.pevt` 数量。</summary>
        public const int MaxFiles = 512;

        /// <summary>单个文件的字符上限；与 <c>PevtEmbeddedSourceLimits.Default</c> 的 4 MiB 同量级。</summary>
        public const int MaxFileChars = 4 * 1024 * 1024;

        /// <summary>一次推送的总字符上限。</summary>
        public const int MaxTotalChars = 16 * 1024 * 1024;
    }

    /// <summary>线上的一份 `.pevt`：只有路径与源文本，没有任何工具侧结论。</summary>
    public sealed class PevtLiveWireFile
    {
        public PevtLiveWireFile(string sourcePath, string text)
        {
            SourcePath = sourcePath ?? string.Empty;
            Text = text ?? string.Empty;
        }

        /// <summary>展示用路径，通常是项目相对路径。</summary>
        public string SourcePath { get; }

        public string Text { get; }

        public override string ToString() => SourcePath;
    }
}
