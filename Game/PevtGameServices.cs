using System;
using System.Collections.Generic;
using Polaris.Pevt.Registration;
using Polaris.Pevt.Runtime;

namespace Polaris.Event.Game
{
    /// <summary>
    /// 一次事件会话持有的全部游戏侧适配器。
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

        /// <param name="rawCommands">
        /// 进程级原版会话通道。它**不是**每个事件一份：原版 EV 是单套静态执行器，通道必须在全部
        /// 事件之间共享才能保证"同一时间只有一个原版文本会话"。
        /// </param>
        /// <param name="rawCs">同样是进程级共享对象——编译缓存跨事件复用。</param>
        public PevtGameSessionServices(
            PevtActorRegistry actors,
            PevtGameClock clock,
            string eventId,
            IPevtRawCommands rawCommands = null,
            Polaris.Pevt.Runtime.Raw.PevtRawCsExecutor rawCs = null)
        {
            if (actors == null)
                throw new ArgumentNullException(nameof(actors));

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            Resources = new PevtGameResources(actors);
            Dialogue = new PevtGameDialogue(actors, eventId);
            Portrait = new PevtGamePortrait(actors, clock);
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
                new PevtGameInput(),
                // P1/P2 领域包接到 GameAPI 上能完整表达的部分：持久状态、物品货币技能魔法、任务。
                // World / Entity / Player / Battle 仍然留空，对应的 `@` 以 PEVTR4001 明确失败——GameAPI 没有地图锚点、
                // 图层、地图元素、实体姿态与动作、服装、召唤点和魔法特效的入口，硬接只能靠猜原版内部语义。
                new PevtDomainServices(
                    state: new PevtGameState(),
                    inventory: new PevtGameInventory(),
                    quest: new PevtGameQuest()),
                rawCommands,
                rawCs);

            Camera.CaptureSnapshot();
        }

        /// <summary>
        /// 逐层清理：先跑会话登记的临时状态恢复表（它记录的是脚本显式改过的东西），再让各适配器收掉自己建出来的显示实例与资源租约。
        /// 两步都不能因为前一步抛异常而跳过，所以恢复表的失败只被收集、不向上抛。
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
