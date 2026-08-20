using System;
using System.Collections.Generic;

namespace Polaris.Pevt.Commands
{
    /// <summary>
    /// 内置指令面向事件作者的用途说明。描述与运行时目录放在同一程序集，
    /// PolarisTools、补全和其它工具都只读取 <see cref="CommandDescriptor"/>，不维护平行文档表。
    /// </summary>
    internal static class BuiltinCommandDescriptions
    {
        private static readonly IReadOnlyDictionary<string, string> ByName =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "actor_appearance", "更换人物目录已登记的立绘、服装或差分组；不接受原版复合 PIC 串。" },
            { "actor_emote", "播放单个表情符号，不暴露原版竖线拼接语法。" },
            { "actor_enter", "从人物目录解析人物与 appearance，等待资源后进入指定语义位置。" },
            { "actor_exit", "让角色退场。" },
            { "actor_flip", "设置角色水平翻转。" },
            { "actor_hide_all", "隐藏本次 PEVT 事件创建的全部人物立绘。" },
            { "actor_motion", "播放点头、摇晃、受击等登记动作。" },
            { "actor_motion_stop", "停止角色当前立绘动作。" },
            { "actor_move", "将角色移到 left、center、right 等语义锚点。" },
            { "actor_move_by", "相对当前受管位置位移，不改变语义锚点身份；动作结束后的位置成为下一次相对移动的基准（PEVT-E04）。" },
            { "alert", "显示受管警告或提示。" },
            { "ambience_play", "播放循环环境声。" },
            { "ambience_stop", "停止当前环境声。" },
            { "audio_preload", "预载 sound、voice、music 或 ambience 资源。" },
            { "autosave", "请求自动存档并返回是否成功。" },
            { "battle_enemy_action", "请求敌人执行已登记行为。" },
            { "battle_magic_effect", "播放战斗魔法效果。" },
            { "battle_summoner_active", "开关战斗召唤点。" },
            { "blur_visible", "显示或隐藏 UI 模糊层。" },
            { "board", "显示告示牌、信件或独立文本板。" },
            { "camera_move", "把镜头移向玩家、显式坐标、地图实体或地图标签点，同时设置缩放。" },
            { "camera_reset", "恢复事件开始前的镜头状态。" },
            { "camera_shake", "统一原版多种震动指令。" },
            { "cg_show", "显示单张 CG，可附带说明。" },
            { "choice_add", "添加一个带稳定键的选项。" },
            { "choice_clear", "清空当前事件的高级选项构造器。" },
            { "choice_show", "显示已构造的选项并返回选中的键。" },
            { "choose", "显示两个简单选项，返回从 1 开始的序号。" },
            { "counter_add", "增减整数状态并返回新值。" },
            { "counter_get", "读取整数状态。" },
            { "counter_raise", "保证整数状态不低于指定值。" },
            { "counter_set", "设置整数状态。" },
            { "danger_set", "设置地图危险度。" },
            { "darkness_set", "开关地图黑暗状态。" },
            { "dialogue_auto", "设置后续对话是否自动推进。" },
            { "dialogue_hold", "设置对话框是否在下一句前保留。" },
            { "dialogue_log", "允许或禁止当前事件写入对话回看。" },
            { "dialogue_visible", "显示或隐藏对话框。" },
            { "effect_pulse", "播放一次后处理脉冲。" },
            { "effect_set", "启用或关闭已登记后处理。" },
            { "entity_action", "请求执行已登记的实体动作，不接受原始 MoveScript。" },
            { "entity_face", "设置场景实体朝向。" },
            { "entity_follow", "让实体开始跟随目标。" },
            { "entity_move_by", "让场景实体按相对坐标移动。" },
            { "entity_move_by_pixels", "按像素相对移动实体，frames 为目标持续帧数（0 表示立即到位）；返回是否走完整段位移。" },
            { "entity_move_to", "让场景实体移动到锚点或另一实体。" },
            { "entity_move_to_offset", "把实体移到「参照目标位置 + 像素偏移」处；返回是否走完整段位移。" },
            { "entity_pose", "设置场景实体姿态。" },
            { "entity_unfollow", "停止实体跟随。" },
            { "entity_visible", "设置场景实体可见性。" },
            { "flag_get", "读取已登记作用域的布尔状态。" },
            { "flag_set", "写入布尔状态。" },
            { "game_read_bool", "读取一个已登记只读游戏值并转换成 bool。" },
            { "game_read_float", "读取一个已登记只读游戏值并转换成 float。" },
            { "game_read_int", "读取一个已登记只读游戏值并转换成 int。" },
            { "game_read_string", "读取一个已登记只读游戏值并转换成 string。" },
            { "image_clear", "清理指定图层组，不依赖原版位标组合。" },
            { "image_fade", "将图层透明度渐变到 0.0–1.0。" },
            { "image_fill", "以纯色填充图层。" },
            { "image_flip", "设置图层翻转。" },
            { "image_hide", "隐藏指定图层。" },
            { "image_move", "移动图层到绝对坐标。" },
            { "image_move_by", "按相对偏移移动图层。" },
            { "image_order", "直接设置图层顺序，比原版 PIC_SWAP 更完整。" },
            { "image_rotate", "渐变图层旋转角度。" },
            { "image_scale", "渐变图层缩放。" },
            { "image_show", "在受管图层显示图像。" },
            { "image_show_at", "在指定坐标显示图像。" },
            { "image_tint", "设置或渐变图层颜色。" },
            { "input_enabled", "开关登记的 skip、log、fast_travel 等输入能力。" },
            { "input_key_enabled", "开关已登记的单个事件按键。" },
            { "item_change", "增减物品并返回变更后数量。" },
            { "item_count", "查询物品数量。" },
            { "letterbox_visible", "显示或隐藏电影黑边。" },
            { "magic_has", "查询魔法所有权。" },
            { "magic_owned", "授予或移除魔法。" },
            { "map_change", "切换地图并将玩家放到指定锚点。" },
            { "map_current", "返回当前已加载地图的 ID。" },
            { "map_element_active", "开关已登记的 LP、Chip 或其他地图元素。" },
            { "map_layer_load", "加载地图图层并返回是否成功。" },
            { "map_refresh", "以登记模式刷新当前地图。" },
            { "money_change", "增减货币并返回新数量。" },
            { "money_get", "查询当前货币。" },
            { "music_block", "跳到当前 BGM 的已登记段落。" },
            { "music_pause", "暂停 BGM，保留当前播放位置。" },
            { "music_play", "预备并切换 BGM。" },
            { "music_resume", "恢复暂停的 BGM。" },
            { "music_stop", "渐止并释放当前 BGM。" },
            { "music_volume", "将 BGM 音量缩放渐变到 0.0–1.0。" },
            { "narrate", "显示无说话人的旁白。" },
            { "player_magic_cancel", "取消玩家当前魔法。" },
            { "player_outfit", "切换玩家服装。" },
            { "player_status_apply", "施加已登记玩家状态。" },
            { "player_status_cure", "以登记模式治疗玩家。" },
            { "player_voice", "播放玩家语音。" },
            { "progress_set", "设置游戏主进度。" },
            { "quest_finish", "完成任务。" },
            { "quest_remove", "移除任务。" },
            { "quest_set", "设置任务步骤。" },
            { "quest_status", "查询规范化任务状态。" },
            { "require_map", "要求当前已加载指定地图，不满足时以运行诊断终止事件。" },
            { "say", "以人物目录中的可读 ID 说话，例如 aic:noel；等待文本显示及玩家推进。" },
            { "screen_fade", "屏幕整体渐变；同一条指令覆盖渐入、渐出和遮罩保持。" },
            { "screen_flash", "屏幕闪光。" },
            { "silhouette_show", "显示剪影图层。" },
            { "skill_enabled", "启用或禁用已有技能。" },
            { "skill_has", "查询技能所有权。" },
            { "skill_owned", "授予或移除技能。" },
            { "skip_enabled", "允许或禁止当前事件快进。" },
            { "sound_play", "播放一次性音效。" },
            { "spotlight", "开启或关闭目标聚光，替代 DARKSPOT* 正反指令。" },
            { "status_visible", "设置状态 UI 可见性。" },
            { "store_refresh", "刷新指定商店数据。" },
            { "talker_bind", "绑定角色显示名和默认对话音效。" },
            { "talker_reset", "恢复角色的默认对话资料。" },
            { "title_hide", "隐藏当前标题。" },
            { "title_show", "显示章节名或地点标题。" },
            { "tutorial_clear", "移除当前全部教程。" },
            { "tutorial_hide", "移除指定教程。" },
            { "tutorial_show", "显示已登记的教程内容。" },
            { "ui_portrait_visible", "显示或隐藏游戏 HUD 自带的动态人物立绘，不影响 PEVT 事件立绘。" },
            { "ui_visible", "设置游戏主 UI 可见性。" },
            { "voice_play", "播放角色语音。" },
            { "wait", "等待指定帧数。" },
            { "wait_input", "等待玩家确认；返回是否由输入结束。0 表示无超时。" },
            { "wait_motion", "等待当前事件所有受管动作结束；返回是否正常完成。" },
            { "wait_resources", "等待指定资源组就绪；groupId 若匹配文件头 resources 声明（PEVT-E08），等的就是那份预载票据，否则落回隐式的人物/图片匹配。" },
            { "weather_set", "切换天气；处理器负责平滑过渡。" },
        };

        public static string Get(string name, string capability, int parameterCount)
        {
            if (name == "say" && capability == "dialogue.show.cmdLayout")
                return "按原版 CMD 的布局信息显示对话，并保留位置、跟随目标、边界、显示名和语音设置。";

            if (name == "choose")
                return "显示 " + Math.Max(0, parameterCount - 1) + " 个简单选项，返回从 1 开始的序号。";

            if (ByName.TryGetValue(name, out string description))
                return description;

            return string.IsNullOrEmpty(capability)
                ? "执行内置事件指令。"
                : "执行内置事件能力 " + capability + "。";
        }
    }
}
