using System;
using System.Collections.Generic;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 一次事件会话持有的全部游戏侧适配器。
    ///
    /// 每个事件拿一套新的实例，因为几乎每个适配器都记着"本事件建了什么"——立绘、图层、遮罩、
    /// 环境声、UI 临时状态。共用一套实例会让上一个事件的清理表跟到下一个事件里去。
    /// <see cref="Release"/> 是唯一的清理入口，事件正常结束、被替换、异常终止和插件卸载
    /// 四条路径都必须走到它。
    /// </summary>
    internal sealed class PevtGameSessionServices
    {
        public PevtServices Services { get; }

        public PevtGameResources Resources { get; }

        public PevtGameDialogue Dialogue { get; }

        public PevtGamePortrait Portrait { get; }

        public PevtGameImage Image { get; }

        public PevtGameScreen Screen { get; }

        public PevtGameCamera Camera { get; }

        public PevtGameAudio Audio { get; }

        public PevtGameMusic Music { get; }

        public PevtGameUi Ui { get; }

        private readonly PevtGameClock _clock;
        private bool _released;

        public PevtGameSessionServices(PevtActorRegistry actors, PevtGameClock clock, string eventId)
        {
            if (actors == null)
                throw new ArgumentNullException(nameof(actors));

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            Resources = new PevtGameResources(actors);
            Dialogue = new PevtGameDialogue(actors, eventId);
            Portrait = new PevtGamePortrait(actors, Resources, clock);
            Image = new PevtGameImage(clock);
            Screen = new PevtGameScreen(clock);
            Camera = new PevtGameCamera(clock);
            Audio = new PevtGameAudio(clock);
            Music = new PevtGameMusic(clock, Resources);
            Ui = new PevtGameUi(clock);

            var choice = new PevtGameChoice { ShowPrompt = Dialogue.OpenText };

            Services = new PevtServices(
                clock,
                new PevtEventSession(eventId),
                new PevtGameActorCatalog(actors),
                Resources,
                Dialogue,
                choice,
                Portrait,
                Image,
                Screen,
                Camera,
                new PevtGameEffect(clock),
                Audio,
                Music,
                Ui,
                new PevtGameInput());

            Camera.CaptureSnapshot();
        }

        /// <summary>
        /// 逐层清理。先跑会话登记的临时状态恢复表（它记录的是脚本显式改过的东西），
        /// 再让各适配器收掉自己建出来的显示实例与资源租约。两步都不能因为前一步抛异常而跳过，
        /// 所以恢复表的失败只被收集、不向上抛。
        /// </summary>
        public IReadOnlyList<Exception> Release()
        {
            if (_released)
                return Array.AsReadOnly(Array.Empty<Exception>());

            _released = true;

            IReadOnlyList<Exception> failures = Services.Session.RestoreAll();

            Dialogue.RestoreProfiles();
            Portrait.ReleaseAll();
            Image.ReleaseAll();
            Screen.ReleaseAll();
            Camera.RestoreEventSnapshot(0);
            Audio.ReleaseAll();
            Music.ReleaseAll();
            Ui.ReleaseAll();
            Resources.ReleaseAll();
            _clock.ClearMotions();

            return failures;
        }
    }
}
