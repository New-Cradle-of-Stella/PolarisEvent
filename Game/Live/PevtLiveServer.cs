using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Polaris.Pevt.Live;

namespace Polaris.Event.Game.Live
{
    /// <summary>
    /// PEVT 热重载的游戏侧命名管道服务端。只在设置项打开时启动——不开就一个管道都不建，
    /// 正常游玩的进程上没有任何可连的端点。
    /// </summary>
    internal static class PevtLiveServer
    {
        /// <summary>一次推送允许主线程花多久应用。重启事件要跑清理，给得比 PUI 那条通道宽一些。</summary>
        private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(10);

        private static Thread _thread;
        private static volatile bool _running;

        internal static bool IsRunning => _running;

        /// <summary>已经收到的推送次数，供调试页显示"通道到底通不通"。</summary>
        internal static int ReceivedCount { get; private set; }

        internal static string LastError { get; private set; } = string.Empty;

        internal static void Start()
        {
            if (_thread != null)
                return;

            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "Polaris.Event.PevtLiveServer",
            };
            _thread.Start();
        }

        internal static void Stop()
        {
            if (_thread == null)
                return;

            _running = false;

            try
            {
                // 用一次假连接顶开阻塞中的 WaitForConnection，让循环看到 _running 已经翻转。
                using (var wakeUp = new NamedPipeClientStream(".", PevtLiveProtocol.PipeName, PipeDirection.InOut))
                    wakeUp.Connect(200);
            }
            catch (Exception)
            {
                // 连不上说明线程本来就没卡在 WaitForConnection 上（例如正在处理另一个连接）。
            }

            _thread.Join(TimeSpan.FromSeconds(6));
            _thread = null;
        }

        private static void Loop()
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        PevtLiveProtocol.PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None))
                    {
                        pipe.WaitForConnection();
                        if (!_running)
                            break;

                        Handle(pipe);
                    }
                }
                catch (Exception ex)
                {
                    if (!_running)
                        break;

                    LastError = ex.Message;
                    PolarisAPI.Errors.Report(ex, "PolarisEvent.Live.Pipe");
                }
            }
        }

        private static void Handle(Stream pipe)
        {
            string focusPath;
            IReadOnlyList<PevtLiveWireFile> files = ReadRequest(pipe, out focusPath);
            ReceivedCount++;

            (bool ok, string message) = PevtLivePump.EnqueueAndWait(files, focusPath, ApplyTimeout);
            LastError = ok ? string.Empty : message;

            using (var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(ok);
                writer.Write(message ?? string.Empty);
                writer.Flush();
            }
        }

        /// <summary>
        /// 读一帧推送。上限在读的过程中就生效，不是读完再判断：一个坏掉（或恶意）的对端可以声明
        /// 十万个文件，先分配再检查等于把内存交给对端决定。
        /// </summary>
        private static IReadOnlyList<PevtLiveWireFile> ReadRequest(Stream pipe, out string focusPath)
        {
            using (var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true))
            {
                int version = reader.ReadInt32();
                if (version != PevtLiveProtocol.Version)
                {
                    throw new InvalidDataException(
                        $"不支持的 PEVT 热重载协议版本 {version}，本进程只认 {PevtLiveProtocol.Version}。");
                }

                focusPath = reader.ReadString();

                int count = reader.ReadInt32();
                if (count < 0 || count > PevtLiveProtocol.MaxFiles)
                    throw new InvalidDataException($"推送声明的 `.pevt` 数量不合法：{count}。");

                var files = new List<PevtLiveWireFile>(Math.Min(count, 64));
                int totalChars = 0;

                for (int i = 0; i < count; i++)
                {
                    string path = reader.ReadString();
                    string text = reader.ReadString();

                    if (text.Length > PevtLiveProtocol.MaxFileChars)
                        throw new InvalidDataException($"{path} 超过单文件上限 {PevtLiveProtocol.MaxFileChars} 个字符。");

                    totalChars += text.Length;
                    if (totalChars > PevtLiveProtocol.MaxTotalChars)
                        throw new InvalidDataException($"这次推送超过总量上限 {PevtLiveProtocol.MaxTotalChars} 个字符。");

                    files.Add(new PevtLiveWireFile(path, text));
                }

                return files;
            }
        }
    }
}
