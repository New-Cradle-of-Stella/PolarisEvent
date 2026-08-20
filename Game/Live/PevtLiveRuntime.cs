using System;
using System.IO;
using System.Threading;
using Polaris.Event.Game.Debugging;
using UnityEngine;

namespace Polaris.Event.Game.Live
{
    /// <summary>
    /// 外部导入与热重载的开关与生命周期。
    /// <para>
    /// 状态不靠设置项的 <c>OnChanged</c> 回调驱动，而是每帧和设置项对一次账：设置项的扫描注册与
    /// 组件的 Start 都在插件 Start 阶段，谁先谁后没有保证，靠回调启动就会依赖那个顺序。
    /// 对账只比较几个字段，代价可以忽略，而且顺带把"作者在设置界面里改了导入目录"这件事也覆盖了。
    /// </para>
    /// </summary>
    internal static class PevtLiveRuntime
    {
        /// <summary>文件改动到重新导入之间的静默期。编辑器保存一个文件往往触发多次事件，攒一攒再扫。</summary>
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

        /// <summary>
        /// 监视器装不上（目录还不存在、句柄用光、缓冲区溢出）之后的重试间隔。
        /// 有这个节流才不至于每帧都去 stat 一次目录——作者可能过五分钟才把那个目录建出来。
        /// </summary>
        private static readonly TimeSpan WatchRetry = TimeSpan.FromSeconds(2);

        private static GameObject _host;
        private static FileSystemWatcher _watcher;
        private static string _watchedDirectory = string.Empty;
        private static bool _enabled;
        private static DateTime _nextWatchAttempt = DateTime.MinValue;

        /// <summary>监视器自己报错（目录被删、缓冲区溢出）后置起，由对账点重装。</summary>
        private static volatile bool _watcherFaulted;

        /// <summary>监视线程写、主线程读，因此走 Interlocked 而不是普通字段。0 表示没有待处理的改动。</summary>
        private static long _dirtyTicks;

        internal static bool IsEnabled => _enabled;

        /// <summary>当前生效的导入目录；未启用时为空串。</summary>
        internal static string Directory => _watchedDirectory;

        /// <summary>组件每帧调一次：把实际状态推到设置项要求的状态上。</summary>
        internal static void Sync()
        {
            bool wanted = PevtDebugSettings.LiveImportEnabled;

            if (wanted != _enabled)
            {
                if (wanted)
                    Enable();
                else
                    Disable();
                return;
            }

            if (!_enabled)
                return;

            string directory = PevtLiveDirectory.Resolve(PevtDebugSettings.LiveDirectory);
            if (!string.Equals(directory, _watchedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                // 目录换了：换一份监视器，并把新目录的内容整批换上去。
                _watchedDirectory = directory;
                _nextWatchAttempt = DateTime.MinValue;
                _watcherFaulted = false;
                SyncWatcher(directory);
                Reload();
                return;
            }

            SyncWatcher(directory);
            ConsumeDirty();
        }

        /// <summary>重新扫描导入目录。调试页的按钮与文件监视都走这里。</summary>
        internal static PevtLiveApplyOutcome Reload()
        {
            Interlocked.Exchange(ref _dirtyTicks, 0L);
            return PevtLiveImport.ApplyFromDisk(PevtDebugSettings.LiveDirectory);
        }

        internal static void Shutdown()
        {
            Disable();
        }

        // ---- 内部 ----

        private static void Enable()
        {
            _enabled = true;
            _watchedDirectory = PevtLiveDirectory.Resolve(PevtDebugSettings.LiveDirectory);

            // 只有用默认目录时才替作者建目录。他自己填的路径不存在，多半是打错了或者盘没挂上，
            // 那种情况下建出一个空目录只会掩盖问题。
            if (string.IsNullOrWhiteSpace(PevtDebugSettings.LiveDirectory))
                PevtLiveDirectory.EnsureDefaultDirectory();

            PevtGameHost.Guard("Live.Enable", () =>
            {
                _host = new GameObject("PolarisEvent Live Import");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                PevtLivePump.EnsureInstance(_host);
            });

            PevtLiveServer.Start();

            _nextWatchAttempt = DateTime.MinValue;
            if (PevtDebugSettings.LiveWatchEnabled)
                InstallWatcher(_watchedDirectory);

            Reload();
            Debug.Log("[PolarisEvent] PEVT external import enabled; directory: " + _watchedDirectory);
        }

        private static void Disable()
        {
            bool wasEnabled = _enabled;
            _enabled = false;

            DisposeWatcher();
            PevtLiveServer.Stop();

            if (wasEnabled)
                PevtLiveImport.Clear();

            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }

            _watchedDirectory = string.Empty;
            _nextWatchAttempt = DateTime.MinValue;
            _watcherFaulted = false;
            Interlocked.Exchange(ref _dirtyTicks, 0L);
        }

        /// <summary>把监视器推到设置项要求的状态上，装不上时按 <see cref="WatchRetry"/> 节流重试。</summary>
        private static void SyncWatcher(string directory)
        {
            if (!PevtDebugSettings.LiveWatchEnabled)
            {
                DisposeWatcher();
                return;
            }

            if (_watcherFaulted)
            {
                DisposeWatcher();
                _watcherFaulted = false;
            }

            if (_watcher != null || DateTime.UtcNow < _nextWatchAttempt)
                return;

            _nextWatchAttempt = DateTime.UtcNow + WatchRetry;
            InstallWatcher(directory);
        }

        private static void InstallWatcher(string directory)
        {
            DisposeWatcher();

            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
                return;

            PevtGameHost.Guard("Live.InstallWatcher", () =>
            {
                var watcher = new FileSystemWatcher(directory, "*" + PevtLiveDirectory.Extension)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };

                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            });
        }

        private static void DisposeWatcher()
        {
            if (_watcher == null)
                return;

            PevtGameHost.Guard("Live.DisposeWatcher", () =>
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileChanged;
                _watcher.Created -= OnFileChanged;
                _watcher.Deleted -= OnFileChanged;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
            });

            _watcher = null;
        }

        /// <summary>监视器回调跑在线程池上，这里只记一个时间戳，真正的重扫留给主线程。</summary>
        private static void OnFileChanged(object sender, FileSystemEventArgs e) => MarkDirty();

        private static void OnFileRenamed(object sender, RenamedEventArgs e) => MarkDirty();

        /// <summary>
        /// 目录被删掉或改动太密导致内部缓冲区溢出时会走到这里。溢出意味着漏掉了一些改动，
        /// 因此除了重装监视器还要主动置脏，重扫一次把漏掉的那些补上。
        /// </summary>
        private static void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _watcherFaulted = true;
            MarkDirty();
        }

        private static void MarkDirty() => Interlocked.Exchange(ref _dirtyTicks, DateTime.UtcNow.Ticks);

        /// <summary>
        /// 静默期过了才真正重扫。编辑器保存一次文件会连着触发好几个 Changed，
        /// 每一个都重扫一遍等于把整个目录解析好几遍，还可能读到只写了一半的文件。
        /// </summary>
        private static void ConsumeDirty()
        {
            long ticks = Interlocked.Read(ref _dirtyTicks);
            if (ticks == 0L)
                return;

            if (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < Debounce)
                return;

            Reload();
        }
    }
}
