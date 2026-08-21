using System.Collections.Generic;
using Polaris.Pevt.Binding;

namespace Polaris.Pevt.Commands
{
    /// <summary>
    /// 第一版 <c>@</c> API 的权威登记数据，逐条对应 PEVT-内置事件语句表.md，<c>Capability</c> 列对应 PEVT-内置能力规范.md。
    /// 优先级按功能阶段划分（P0 演出，P1 地图实体与持久状态）；<c>_start</c> 异步变体由
    /// <see cref="CommandWaitKind.WaitParallel"/> 条目自动派生，不在本表出现。
    /// </summary>
    public static class BuiltinCommandDescriptors
    {
        private static CommandParameter P(string name, PevtType type, ParameterDomain domain = null) =>
            new CommandParameter(name, type, domain);

        private static CommandDescriptor Cmd(
            string name,
            CommandWaitKind wait,
            PevtType? returnType,
            CommandPriority priority,
            string capability,
            params CommandParameter[] parameters) =>
            new CommandDescriptor(name, parameters, returnType, wait, priority, capability,
                BuiltinCommandDescriptions.Get(name, capability, parameters?.Length ?? 0));

        private static ParameterDomain Actor => ParameterDomain.ActorId;
        private static ParameterDomain Appearance => ParameterDomain.ActorAppearance;
        private static ParameterDomain Anchor => ParameterDomain.ActorAnchor;
        private static ParameterDomain Emote => ParameterDomain.ActorEmote;
        private static ParameterDomain Easing => ParameterDomain.Easing;
        private static ParameterDomain Color => ParameterDomain.Color;
        private static ParameterDomain Layer => ParameterDomain.LayerId;
        private static ParameterDomain Asset => ParameterDomain.AssetId;

        /// <summary>直接显示给玩家看的文案。带这个域的实参在进入处理器前会过一遍本地化解析（`&` 开头 = 本地化键）。</summary>
        private static ParameterDomain Text => ParameterDomain.Text;
        private static ParameterDomain QueryKey => ParameterDomain.GameQueryKey;
        private static ParameterDomain CameraTarget => ParameterDomain.CameraTarget;

        private const CommandWaitKind Immediate = CommandWaitKind.Immediate;
        private const CommandWaitKind Query = CommandWaitKind.Query;
        private const CommandWaitKind Wait = CommandWaitKind.Wait;
        private const CommandWaitKind Parallel = CommandWaitKind.WaitParallel;

        private const CommandPriority P0 = CommandPriority.P0;
        private const CommandPriority P1 = CommandPriority.P1;

        public static IReadOnlyList<CommandDescriptor> Create()
        {
            var descriptors = new List<CommandDescriptor>(Fixed());
            AddGameQueries(descriptors);
            return descriptors;
        }

        /// <summary>
        /// PEVT-E01：任意已登记游戏值的只读查询。键名不是白名单，所以这一族只按"实参数量"分重载——
        /// <c>key</c> 之后是 0–<see cref="MaxQueryArguments"/> 个纯查询参数。重载仍然由参数数量与类型唯一确定，
        /// 因此这里登记的是一族固定签名，而不是一条可变参数签名。
        /// </summary>
        private static void AddGameQueries(List<CommandDescriptor> descriptors)
        {
            var returns = new[] { PevtType.Int, PevtType.Float, PevtType.Bool, PevtType.String };
            var names = new[] { "game_read_int", "game_read_float", "game_read_bool", "game_read_string" };

            for (int i = 0; i < names.Length; i++)
            {
                for (int argumentCount = 0; argumentCount <= MaxQueryArguments; argumentCount++)
                {
                    var parameters = new List<CommandParameter>(argumentCount + 1)
                    {
                        P("key", PevtType.String, QueryKey),
                    };

                    for (int a = 1; a <= argumentCount; a++)
                        parameters.Add(P("arg" + a, PevtType.String));

                    descriptors.Add(new CommandDescriptor(
                        names[i], parameters, returns[i], Query, P1, "game.query.read",
                        BuiltinCommandDescriptions.Get(names[i], "game.query.read", parameters.Count)));
                }
            }
        }

        /// <summary><c>@game_read_*</c> 在 <c>key</c> 之后允许的查询参数数量上限。</summary>
        public const int MaxQueryArguments = 4;

        private static IReadOnlyList<CommandDescriptor> Fixed() => new[]
        {
            // ---- 调度与等待 ----
            Cmd("wait", Wait, null, P0, "wait.frames", P("frames", PevtType.Int)),
            Cmd("wait_input", Wait, PevtType.Bool, P0, "wait.input", P("timeoutFrames", PevtType.Int)),
            Cmd("wait_motion", Wait, PevtType.Bool, P0, "wait.motion", P("timeoutFrames", PevtType.Int)),
            Cmd("wait_resources", Wait, PevtType.Bool, P0, "wait.resources",
                P("groupId", PevtType.String), P("timeoutFrames", PevtType.Int)),

            // ---- 对话与选择 ----
            Cmd("say", Wait, null, P0, "dialogue.show", P("actorId", PevtType.String, Actor), P("text", PevtType.String, Text)),
            Cmd("say", Wait, null, P0, "dialogue.show.cmdLayout",
                P("talkerId", PevtType.String), P("text", PevtType.String, Text),
                P("position", PevtType.String), P("followTarget", PevtType.String), P("bounds", PevtType.String),
                P("displayName", PevtType.String, Text), P("voiceId", PevtType.String)),
            Cmd("narrate", Wait, null, P0, "dialogue.show", P("text", PevtType.String, Text)),
            Cmd("board", Wait, null, P0, "dialogue.board.show", P("text", PevtType.String, Text), P("style", PevtType.String)),
            Cmd("talker_bind", Immediate, null, P0, "dialogue.profile.bind",
                P("actorId", PevtType.String, Actor), P("displayName", PevtType.String, Text), P("voiceId", PevtType.String)),
            Cmd("talker_reset", Immediate, null, P0, "dialogue.profile.reset", P("actorId", PevtType.String, Actor)),
            Cmd("dialogue_visible", Immediate, null, P0, "dialogue.visible.set",
                P("visible", PevtType.Bool), P("immediate", PevtType.Bool)),
            Cmd("dialogue_hold", Immediate, null, P0, "dialogue.hold.set", P("enabled", PevtType.Bool)),
            Cmd("dialogue_auto", Immediate, null, P0, "dialogue.nextAutoAdvance.set", P("enabled", PevtType.Bool)),
            Cmd("dialogue_log", Immediate, null, P0, "dialogue.log.setEnabled", P("enabled", PevtType.Bool)),
            Cmd("skip_enabled", Immediate, null, P0, "dialogue.skip.setEnabled", P("enabled", PevtType.Bool)),
            Cmd("choose", Wait, PevtType.Int, P0, "choice.present",
                P("prompt", PevtType.String, Text), P("option1", PevtType.String, Text), P("option2", PevtType.String, Text)),
            Cmd("choose", Wait, PevtType.Int, P0, "choice.present",
                P("prompt", PevtType.String, Text), P("option1", PevtType.String, Text), P("option2", PevtType.String, Text),
                P("option3", PevtType.String, Text)),
            Cmd("choose", Wait, PevtType.Int, P0, "choice.present",
                P("prompt", PevtType.String, Text), P("option1", PevtType.String, Text), P("option2", PevtType.String, Text),
                P("option3", PevtType.String, Text), P("option4", PevtType.String, Text)),
            Cmd("choice_clear", Immediate, null, P0, "choice.reset"),
            Cmd("choice_add", Immediate, null, P0, "choice.add",
                P("key", PevtType.String), P("text", PevtType.String, Text), P("enabled", PevtType.Bool), P("cancel", PevtType.Bool)),
            Cmd("choice_show", Wait, PevtType.String, P0, "choice.present",
                P("prompt", PevtType.String, Text), P("initialKey", PevtType.String)),

            // ---- 角色立绘与演出层 ----
            Cmd("actor_enter", Parallel, null, P0, "portrait.place",
                P("actorId", PevtType.String, Actor), P("position", PevtType.String, Anchor),
                P("appearanceId", PevtType.String, Appearance), P("frames", PevtType.Int)),
            Cmd("actor_exit", Parallel, null, P0, "portrait.place",
                P("actorId", PevtType.String, Actor), P("frames", PevtType.Int)),
            Cmd("actor_hide_all", Wait, null, P0, "portrait.hideAll", P("frames", PevtType.Int)),
            Cmd("actor_move", Parallel, null, P0, "portrait.place",
                P("actorId", PevtType.String, Actor), P("position", PevtType.String, Anchor), P("frames", PevtType.Int)),
            // PEVT-E04：相对当前受管位置位移，不改变语义锚点身份；动作结束后的位置成为下一次相对移动的基准。
            Cmd("actor_move_by", Parallel, null, P0, "portrait.place",
                P("actorId", PevtType.String, Actor), P("x", PevtType.Float), P("y", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("actor_appearance", Immediate, null, P0, "portrait.appearance.set",
                P("actorId", PevtType.String, Actor), P("appearanceId", PevtType.String, Appearance)),
            Cmd("actor_emote", Immediate, null, P0, "portrait.emote.play",
                P("actorId", PevtType.String, Actor), P("emoteId", PevtType.String, Emote)),
            Cmd("actor_motion", Parallel, null, P0, "portrait.motion.set",
                P("actorId", PevtType.String, Actor), P("motionId", PevtType.String), P("frames", PevtType.Int)),
            Cmd("actor_motion_stop", Immediate, null, P0, "portrait.motion.stop", P("actorId", PevtType.String, Actor)),
            Cmd("actor_flip", Immediate, null, P0, "layer.flip.set",
                P("actorId", PevtType.String, Actor), P("horizontal", PevtType.Bool)),
            Cmd("image_show", Immediate, null, P0, "layer.content.set",
                P("layerId", PevtType.String, Layer), P("assetId", PevtType.String, Asset)),
            Cmd("image_show_at", Immediate, null, P0, "layer.content.set",
                P("layerId", PevtType.String, Layer), P("assetId", PevtType.String, Asset),
                P("x", PevtType.Float), P("y", PevtType.Float)),
            Cmd("image_hide", Parallel, null, P0, "layer.hide",
                P("layerId", PevtType.String, Layer), P("frames", PevtType.Int)),
            Cmd("image_clear", Wait, null, P0, "layer.group.hide",
                P("groupId", PevtType.String), P("frames", PevtType.Int)),
            Cmd("image_move", Parallel, null, P0, "layer.move",
                P("layerId", PevtType.String, Layer), P("x", PevtType.Float), P("y", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("image_move_by", Parallel, null, P0, "layer.move",
                P("layerId", PevtType.String, Layer), P("x", PevtType.Float), P("y", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("image_fade", Parallel, null, P0, "layer.fade",
                P("layerId", PevtType.String, Layer), P("opacity", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("image_scale", Parallel, null, P0, "layer.move",
                P("layerId", PevtType.String, Layer), P("x", PevtType.Float), P("y", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("image_rotate", Parallel, null, P0, "layer.move",
                P("layerId", PevtType.String, Layer), P("degrees", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("image_tint", Parallel, null, P0, "layer.fill",
                P("layerId", PevtType.String, Layer), P("color", PevtType.String, Color), P("frames", PevtType.Int)),
            Cmd("image_flip", Immediate, null, P0, "layer.flip.set",
                P("layerId", PevtType.String, Layer), P("horizontal", PevtType.Bool), P("vertical", PevtType.Bool)),
            Cmd("image_order", Immediate, null, P0, "layer.order.swap",
                P("layerId", PevtType.String, Layer), P("order", PevtType.Int)),
            Cmd("image_fill", Immediate, null, P0, "layer.fill",
                P("layerId", PevtType.String, Layer), P("color", PevtType.String, Color)),
            Cmd("cg_show", Wait, null, P0, "layer.singleImage.show",
                P("assetId", PevtType.String, Asset), P("caption", PevtType.String, Text)),
            Cmd("silhouette_show", Wait, null, P0, "layer.silhouette.show",
                P("layerId", PevtType.String, Layer), P("assetId", PevtType.String, Asset),
                P("position", PevtType.String, Anchor), P("frames", PevtType.Int)),

            // ---- 画面、镜头与后处理 ----
            Cmd("screen_fade", Parallel, null, P0, "layer.fade",
                P("color", PevtType.String, Color), P("opacity", PevtType.Float),
                P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("screen_flash", Parallel, null, P0, "screen.flash",
                P("color", PevtType.String, Color), P("delayFrames", PevtType.Int),
                P("holdFrames", PevtType.Int), P("fadeFrames", PevtType.Int)),
            Cmd("camera_shake", Parallel, null, P0, "camera.shake",
                P("amplitude", PevtType.Float), P("durationFrames", PevtType.Int), P("frequency", PevtType.Float)),
            Cmd("camera_move", Parallel, null, P0, "camera.move",
                P("targetId", PevtType.String, CameraTarget), P("x", PevtType.Float), P("y", PevtType.Float),
                P("zoom", PevtType.Float), P("frames", PevtType.Int), P("easing", PevtType.String, Easing)),
            Cmd("camera_reset", Parallel, null, P0, "camera.reset", P("frames", PevtType.Int)),
            Cmd("effect_set", Wait, null, P0, "postEffect.set",
                P("effectId", PevtType.String), P("enabled", PevtType.Bool), P("frames", PevtType.Int)),
            Cmd("effect_pulse", Parallel, null, P0, "postEffect.pulse",
                P("effectId", PevtType.String), P("fadeFrames", PevtType.Int), P("holdFrames", PevtType.Int)),
            Cmd("spotlight", Immediate, null, P0, "screen.spotlight.set",
                P("targetId", PevtType.String), P("enabled", PevtType.Bool), P("style", PevtType.String)),

            // ---- 音频 ----
            Cmd("audio_preload", Wait, PevtType.Bool, P0, "audio.preload",
                P("audioId", PevtType.String), P("kind", PevtType.String)),
            Cmd("sound_play", Immediate, null, P0, "sound.play",
                P("soundId", PevtType.String), P("volume", PevtType.Float), P("pitch", PevtType.Float)),
            Cmd("voice_play", Immediate, null, P0, "sound.play",
                P("actorId", PevtType.String, Actor), P("voiceId", PevtType.String), P("volume", PevtType.Float)),
            Cmd("ambience_play", Wait, null, P0, "audio.ambience.play",
                P("trackId", PevtType.String), P("volume", PevtType.Float), P("fadeFrames", PevtType.Int)),
            Cmd("ambience_stop", Wait, null, P0, "audio.ambience.stop", P("fadeFrames", PevtType.Int)),
            Cmd("music_play", Parallel, null, P0, "music.replace",
                P("musicId", PevtType.String), P("fadeFrames", PevtType.Int)),
            Cmd("music_stop", Parallel, null, P0, "music.stop", P("fadeFrames", PevtType.Int)),
            Cmd("music_pause", Wait, null, P0, "music.pause", P("fadeFrames", PevtType.Int)),
            Cmd("music_resume", Wait, null, P0, "music.resume", P("fadeFrames", PevtType.Int)),
            Cmd("music_volume", Wait, null, P0, "music.volumeScale.set",
                P("scale", PevtType.Float), P("frames", PevtType.Int)),
            Cmd("music_block", Immediate, null, P0, "music.block.goto", P("blockId", PevtType.String)),

            // ---- UI 与输入策略 ----
            Cmd("ui_visible", Immediate, null, P0, "ui.global.setVisible", P("visible", PevtType.Bool)),
            Cmd("status_visible", Immediate, null, P0, "ui.status.setVisible", P("visible", PevtType.Bool)),
            Cmd("ui_portrait_visible", Immediate, null, P0, "ui.portrait.setVisible", P("visible", PevtType.Bool)),
            Cmd("letterbox_visible", Wait, null, P0, "ui.letterbox.set",
                P("visible", PevtType.Bool), P("frames", PevtType.Int)),
            Cmd("blur_visible", Wait, null, P0, "ui.blur.setVisible",
                P("visible", PevtType.Bool), P("frames", PevtType.Int)),
            Cmd("alert", Wait, null, P0, "ui.alert.show", P("text", PevtType.String, Text), P("style", PevtType.String)),
            Cmd("title_show", Wait, null, P0, "ui.title.show",
                P("text", PevtType.String, Text), P("x", PevtType.Float), P("y", PevtType.Float)),
            Cmd("title_hide", Wait, null, P0, "ui.title.hide", P("frames", PevtType.Int)),
            Cmd("tutorial_show", Wait, null, P0, "ui.tutorial.show", P("tutorialId", PevtType.String)),
            Cmd("tutorial_hide", Immediate, null, P0, "ui.tutorial.remove", P("tutorialId", PevtType.String)),
            Cmd("tutorial_clear", Immediate, null, P0, "ui.tutorial.remove"),
            Cmd("input_enabled", Immediate, null, P0, "input.capability.set",
                P("capability", PevtType.String), P("enabled", PevtType.Bool)),
            Cmd("input_key_enabled", Immediate, null, P0, "input.key.setEnabled",
                P("key", PevtType.String), P("enabled", PevtType.Bool)),

            // ---- 地图与场景实体 ----
            Cmd("require_map", Immediate, null, P1, "map.current.require", P("mapId", PevtType.String)),
            Cmd("map_current", Query, PevtType.String, P1, "map.current.get"),
            Cmd("map_change", Wait, null, P1, "map.transfer",
                P("mapId", PevtType.String), P("anchorId", PevtType.String)),
            Cmd("map_refresh", Wait, null, P1, "map.refresh", P("mode", PevtType.String)),
            Cmd("map_layer_load", Wait, PevtType.Bool, P1, "map.layer.load", P("layerId", PevtType.String)),
            Cmd("map_element_active", Immediate, PevtType.Bool, P1, "map.element.setActive",
                P("kind", PevtType.String), P("elementId", PevtType.String), P("active", PevtType.Bool)),
            Cmd("weather_set", Immediate, null, P1, "map.weather.set",
                P("weatherId", PevtType.String), P("frames", PevtType.Int)),
            Cmd("danger_set", Wait, null, P1, "map.danger.set",
                P("level", PevtType.Int), P("immediate", PevtType.Bool)),
            Cmd("darkness_set", Immediate, null, P1, "map.darkness.set", P("enabled", PevtType.Bool)),
            Cmd("entity_pose", Immediate, PevtType.Bool, P1, "entity.pose.set",
                P("entityId", PevtType.String), P("poseId", PevtType.String)),
            Cmd("entity_face", Immediate, PevtType.Bool, P1, "entity.facing.set",
                P("entityId", PevtType.String), P("direction", PevtType.String)),
            Cmd("entity_move_to", Parallel, PevtType.Bool, P1, "entity.move",
                P("entityId", PevtType.String), P("targetId", PevtType.String), P("speed", PevtType.Float)),
            Cmd("entity_move_by", Parallel, PevtType.Bool, P1, "entity.move",
                P("entityId", PevtType.String), P("x", PevtType.Float), P("y", PevtType.Float), P("speed", PevtType.Float)),
            Cmd("entity_move_by_pixels", Parallel, PevtType.Bool, P1, "entity.move",
                P("entityId", PevtType.String), P("xPixels", PevtType.Float),
                P("yPixels", PevtType.Float), P("frames", PevtType.Int)),
            Cmd("entity_move_to_offset", Parallel, PevtType.Bool, P1, "entity.move",
                P("entityId", PevtType.String), P("targetId", PevtType.String),
                P("xPixels", PevtType.Float), P("yPixels", PevtType.Float), P("frames", PevtType.Int)),
            Cmd("entity_follow", Immediate, PevtType.Bool, P1, "entity.move",
                P("entityId", PevtType.String), P("targetId", PevtType.String),
                P("distance", PevtType.Float), P("speed", PevtType.Float)),
            Cmd("entity_unfollow", Immediate, PevtType.Bool, P1, "entity.move", P("entityId", PevtType.String)),
            Cmd("entity_action", Parallel, PevtType.Bool, P1, "entity.action.request",
                P("entityId", PevtType.String), P("actionId", PevtType.String)),

            // ---- 状态、物品与进度 ----
            Cmd("flag_get", Query, PevtType.Bool, P1, "state.flag.get",
                P("scope", PevtType.String), P("key", PevtType.String)),
            Cmd("flag_set", Immediate, null, P1, "state.flag.set",
                P("scope", PevtType.String), P("key", PevtType.String), P("value", PevtType.Bool)),
            Cmd("counter_get", Query, PevtType.Int, P1, "state.counter.get",
                P("scope", PevtType.String), P("key", PevtType.String)),
            Cmd("counter_set", Immediate, null, P1, "state.counter.set",
                P("scope", PevtType.String), P("key", PevtType.String), P("value", PevtType.Int)),
            Cmd("counter_add", Immediate, PevtType.Int, P1, "state.counter.set",
                P("scope", PevtType.String), P("key", PevtType.String), P("delta", PevtType.Int)),
            Cmd("counter_raise", Immediate, PevtType.Int, P1, "state.counter.raiseTo",
                P("scope", PevtType.String), P("key", PevtType.String), P("minimum", PevtType.Int)),
            Cmd("progress_set", Immediate, null, P1, "state.mainProgress.set", P("value", PevtType.Int)),
            Cmd("item_count", Query, PevtType.Int, P1, "inventory.item.count", P("itemId", PevtType.String)),
            Cmd("item_change", Immediate, PevtType.Int, P1, "inventory.item.change",
                P("itemId", PevtType.String), P("delta", PevtType.Int),
                P("grade", PevtType.Int), P("notify", PevtType.Bool)),
            Cmd("money_get", Query, PevtType.Int, P1, "inventory.currency.get"),
            Cmd("money_change", Immediate, PevtType.Int, P1, "inventory.currency.change",
                P("delta", PevtType.Int), P("notify", PevtType.Bool)),
            Cmd("skill_has", Query, PevtType.Bool, P1, "ability.skill.isOwned", P("skillId", PevtType.String)),
            Cmd("skill_owned", Immediate, null, P1, "ability.skill.setOwned",
                P("skillId", PevtType.String), P("owned", PevtType.Bool), P("notify", PevtType.Bool)),
            Cmd("skill_enabled", Immediate, null, P1, "ability.skill.setEnabled",
                P("skillId", PevtType.String), P("enabled", PevtType.Bool)),
            Cmd("magic_has", Query, PevtType.Bool, P1, "ability.magic.isOwned", P("magicId", PevtType.String)),
            Cmd("magic_owned", Immediate, null, P1, "ability.magic.setOwned",
                P("magicId", PevtType.String), P("owned", PevtType.Bool)),
            Cmd("quest_status", Query, PevtType.Int, P1, "quest.status.get", P("questId", PevtType.String)),
            Cmd("quest_set", Immediate, null, P1, "quest.progress.set",
                P("questId", PevtType.String), P("step", PevtType.Int)),
            Cmd("quest_finish", Immediate, null, P1, "quest.finish", P("questId", PevtType.String)),
            Cmd("quest_remove", Immediate, null, P1, "quest.remove", P("questId", PevtType.String)),
            Cmd("autosave", Wait, PevtType.Bool, P1, "save.auto.request", P("mode", PevtType.String)),
            Cmd("store_refresh", Immediate, null, P1, "store.refresh", P("storeId", PevtType.String)),
        };
    }
}
