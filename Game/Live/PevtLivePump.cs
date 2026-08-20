using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Polaris.Pevt.Live;
using Polaris.Pevt.Loading;
using UnityEngine;

namespace Polaris.Event.Game.Live
{
    /// <summary>
    /// 把命名管道线程收到的推送搬到 Unity 主线程应用，再把回执送回等待中的连接。
    /// <para>
    /// 必须过这一道：导入会改注册表、可能重启当前事件，而重启要碰 UI、摄像机和原版 EV——
    /// 那些只能在主线程动。管道线程直接调用的话，第一次热重载就会在 Unity 里炸出一个
    /// 与真正原因毫无关系的线程错误。
    /// </para>
    /// </summary>
    internal sealed class PevtLivePump : MonoBehaviour
    {
        private sealed class Request
        {
            internal Request(IReadOnlyList<PevtLiveWireFile> files, string focusPath)
            {
                Files = files;
                FocusPath = focusPath;
            }

            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

            internal IReadOnlyList<PevtLiveWireFile> Files { get; }

            internal string FocusPath { get; }

            internal bool Ok;

            internal string Message;
        }

        private static readonly ConcurrentQueue<Request> Queue = new ConcurrentQueue<Request>();
        private static PevtLivePump _instance;

        internal static void EnsureInstance(GameObject root)
        {
            if (_instance == null)
                _instance = root.AddComponent<PevtLivePump>();
        }

        /// <summary>由管道线程调用：排队，等主线程应用完这次推送，返回回执。</summary>
        internal static (bool ok, string message) EnqueueAndWait(
            IReadOnlyList<PevtLiveWireFile> files,
            string focusPath,
            TimeSpan timeout)
        {
            if (_instance == null)
                return (false, "PEVT 热重载通道尚未就绪。");

            var request = new Request(files, focusPath);
            Queue.Enqueue(request);

            if (!request.Done.Wait(timeout))
                return (false, "游戏主线程没能在超时之前应用这次推送。");

            return (request.Ok, request.Message);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out Request request))
            {
                try
                {
                    var sources = new List<PevtExternalSource>(request.Files.Count);
                    foreach (PevtLiveWireFile file in request.Files)
                        sources.Add(PevtExternalSource.FromText(file.SourcePath, file.Text));

                    PevtLiveApplyOutcome outcome = PevtLiveImport.Apply(
                        sources, "PolarisTools push", request.FocusPath);
                    request.Ok = outcome.Ok;
                    request.Message = outcome.Message;
                }
                catch (Exception ex)
                {
                    request.Ok = false;
                    request.Message = ex.Message;
                    PolarisAPI.Errors.Report(ex, "PolarisEvent.Live.Pump");
                }
                finally
                {
                    request.Done.Set();
                }
            }
        }

        /// <summary>
        /// 卸载时把队列里还在等的连接一并放掉。不做这一步，管道线程会一直阻塞到超时，
        /// 而 PolarisTools 那边只会看到"游戏没反应"。
        /// </summary>
        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            while (Queue.TryDequeue(out Request request))
            {
                request.Ok = false;
                request.Message = "PEVT 热重载通道正在关闭。";
                request.Done.Set();
            }
        }
    }
}
