using System;
using System.Collections.Generic;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 游戏侧时钟。帧号不取 Unity 的 <c>Time.frameCount</c> 而由宿主的更新点自己递增，因为 PEVT 的等待都以调度器推进次数为单位，
    /// 用引擎帧会让暂停、慢动作后处理和编辑器步进产生和调度不一致的计时；受管动作的聚合等待同理走
    /// <see cref="RegisterMotion"/> 登记的票据，而不是去问原版"现在有没有东西在动"。
    /// </summary>
    internal sealed class PevtGameClock : IPevtClock
    {
        private readonly List<Func<bool>> _motions = new List<Func<bool>>();

        public long Frame { get; private set; }

        /// <summary>由宿主更新点每帧调用一次。</summary>
        public void Advance()
        {
            Frame++;

            // 已完成的票据不必留着；否则一次长事件会把成百上千个闭包一直挂到事件结束。
            for (int i = _motions.Count - 1; i >= 0; i--)
            {
                if (PevtGameHost.Safe(_motions[i], true))
                    _motions.RemoveAt(i);
            }
        }

        /// <summary>登记一个受管动作票据；谓词返回 true 表示该动作已经结束。</summary>
        public void RegisterMotion(Func<bool> finished)
        {
            if (finished != null)
                _motions.Add(finished);
        }

        /// <summary>清空票据。事件结束、替换与异常都要走到，否则下一个事件会等上一个的动作。</summary>
        public void ClearMotions() => _motions.Clear();

        public PevtWait WaitFrames(int frames) => new PevtFrameWait(frames);

        public PevtWait<bool> WaitInput(int timeoutFrames) =>
            new PevtInputWait(PevtGameHost.AdvancePressed, timeoutFrames);

        public PevtWait<bool> WaitOwnedMotions(int timeoutFrames) =>
            new PevtMotionWait(AllMotionsFinished, timeoutFrames);

        private bool AllMotionsFinished()
        {
            for (int i = _motions.Count - 1; i >= 0; i--)
            {
                if (PevtGameHost.Safe(_motions[i], true))
                    _motions.RemoveAt(i);
                else
                    return false;
            }

            return true;
        }
    }
}
