using System;
using System.Collections.Generic;
using evt;
using XX;

namespace Polaris.Event.Game
{
    /// <summary>
    /// PEVT 根事件的进程级临时状态快照，只保存 PEVT/原版 EV 演出会修改、且在事件结束后必须回滚的静态标量与 BGM 身份。
    /// 不能反射深拷贝“所有 static 字段”：地图、Unity 对象等其他静态对象若被回写，既会撤销脚本有意产生的持久结果，也会把已销毁的 Unity 引用重新塞回游戏。
    /// </summary>
    internal sealed class PevtGameStaticStateSnapshot
    {
        private const int BgmRestoreTimeoutFrames = 600;

        private bool _evRestored;
        private bool _bgmRestored;
        private int _bgmRestoreAttempts;

        private bool _evCaptured;
        private bool _stopGame;
        private bool _stopGameDraw;
        private bool _handleGame;
        private KEY.SIMKEY _eventHandleKeys;
        private int _skipping;
        private bool _denySkip;
        private bool _runDrawer;
        private bool _valotileOverwriteMessage;

        private bool _bgmCaptured;
        private string _bgmTiming;
        private string _bgmCue;
        private bool _bgmPlaying;

        private PevtGameStaticStateSnapshot()
        {
        }

        public bool IsRestoreComplete => (!_evCaptured || _evRestored) && (!_bgmCaptured || _bgmRestored);

        public static PevtGameStaticStateSnapshot Capture()
        {
            var snapshot = new PevtGameStaticStateSnapshot();

            try
            {
                snapshot._stopGame = EV.stop_game;
                snapshot._stopGameDraw = EV.stop_gdraw;
                snapshot._handleGame = EV.handle_game;
                snapshot._eventHandleKeys = EV.alloc_evhandle_key;
                snapshot._skipping = EV.skipping;
                snapshot._denySkip = EV.deny_skip;
                snapshot._runDrawer = EV.run_drawer;
                snapshot._valotileOverwriteMessage = EV.valotile_overwrite_msg;
                snapshot._evCaptured = true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Game.StaticSnapshot.CaptureEV");
            }

            try
            {
                BGM.getFrontBgm(out snapshot._bgmTiming, out snapshot._bgmCue);
                snapshot._bgmPlaying = BGM.isFrontPlaying();
                snapshot._bgmCaptured = true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisEvent.Game.StaticSnapshot.CaptureBGM");
            }

            return snapshot;
        }

        /// <summary>
        /// 推进一步恢复。BGM sheet 可能异步准备，因此“尚未 prepared”不是错误，宿主会在后续帧重试；
        /// 每一组真正的异常单独收集，BGM 失败不会阻止输入和主循环恢复。
        /// </summary>
        public IReadOnlyList<Exception> RestoreStep()
        {
            if (IsRestoreComplete)
                return Array.AsReadOnly(Array.Empty<Exception>());

            var failures = new List<Exception>();

            if (_bgmCaptured && !_bgmRestored)
            {
                try
                {
                    _bgmRestored = TryRestoreBgm();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _bgmRestored = true;
                }

                if (!_bgmRestored && ++_bgmRestoreAttempts >= BgmRestoreTimeoutFrames)
                {
                    failures.Add(new TimeoutException(
                        $"恢复事件前 BGM `{_bgmTiming}/{_bgmCue}` 超过 {BgmRestoreTimeoutFrames} 帧。"));
                    _bgmRestored = true;
                }
            }

            if (_evCaptured && !_evRestored)
            {
                try
                {
                    EV.stopGMain(_stopGame);
                    EV.StopGMainDrawFlag(_stopGameDraw);
                    EV.setGHandleFlag(_handleGame);
                    EV.alloc_evhandle_key = _eventHandleKeys;
                    EV.skipping = _skipping;
                    EV.deny_skip = _denySkip;
                    EV.run_drawer = _runDrawer;
                    EV.valotile_overwrite_msg = _valotileOverwriteMessage;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
                finally
                {
                    // 静态输入/主循环标志没有异步阶段；失败已经上报，不能每帧反复写同一组状态。
                    _evRestored = true;
                }
            }

            return failures.AsReadOnly();
        }

        private bool TryRestoreBgm()
        {
            BGM.getFrontBgm(out string currentTiming, out string currentCue);

            if (string.IsNullOrEmpty(_bgmCue))
            {
                if (!string.IsNullOrEmpty(currentCue))
                    BGM.stop(false, true);
                return true;
            }

            if (!string.Equals(currentTiming, _bgmTiming, StringComparison.Ordinal)
                || !string.Equals(currentCue, _bgmCue, StringComparison.Ordinal))
            {
                BGM.load(_bgmTiming, _bgmCue, true);
                if (BGM.OthPl == null || !BGM.OthPl.isPrepared())
                    return false;

                // 当前事件曲目可以卸载；快照曲目切回前台后由地图继续管理。
                BGM.replace(0f, 0f, true, true);

                BGM.getFrontBgm(out currentTiming, out currentCue);
                if (!string.Equals(currentTiming, _bgmTiming, StringComparison.Ordinal)
                    || !string.Equals(currentCue, _bgmCue, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"恢复事件前 BGM 失败：期望 `{_bgmTiming}/{_bgmCue}`，实际 `{currentTiming}/{currentCue}`。");
            }

            if (_bgmPlaying)
                BGM.fadeout(100f, 0f, false);
            else
                BGM.stop(true, true);

            return true;
        }
    }
}
