# PEVT 内置事件语句 API 表

## 签名格式

纯调用：

```pevt
@语句名(参数名 : 参数类型)
```

有返回值：

```pevt
@语句名(参数名 : 参数类型) : 返回值类型
```

异步定义：

```pevt
async @语句名(参数名 : 参数类型) : 返回值类型
```

定义中未书写 `async` 时为同步调用；定义中书写 `async` 时，调用固定返回异步句柄。
异步调用由 PolarisEvent 运行时调度，不直接降级为原始游戏 DSL 的并行执行。

每个公开 `@` API 都由 PolarisEvent 注册，并由 PEVT 解释器在运行时直接解析执行。处理器通常由 C# 实现。原版指令只作为能力语义参考，不作为 `@` 的必经执行通道；基础能力规范见：

```text
PEVT-内置能力规范.md
```

该能力规范不是强制中间 IR，`@` 不降低为另一组命令或原版 Ev 文本；事件作者实际能够调用的内容只以下方登记的 `@` API 为准。

人物 ID、visual/appearance 与站位的来源和原版短键固定映射见：

```text
PEVT-人物目录与原版别名规范.md
```

同步 `@` 如何组合并顺序调用 C# 原子方法，见：

```text
PEVT-同步指令中间层规范.md
```

## 命名与原版对应

| 原版家族 | PEVT 主要入口 | 改造方式 |
| --- | --- | --- |
| `MSG` / `TX_BOARD` | `@say`、`@narrate`、`@board` | 直接传说话人与文本，不暴露原版的 ID 前缀和复合参数。 |
| `TALKER` / `PIC` / `PIC_MP` | `@actor_enter`、`@actor_appearance`、`@actor_emote` | 把角色位置、立绘差分和表情拆成有稳定语义的参数。 |
| `PIC_*` | `@image_*`、`@screen_*` | 合并别名和正反指令，补充缩放、旋转、层级和透明度等直接能力。 |
| `WAIT*` | `@wait*` | 统一超时和布尔结果，不暴露原版反直觉的等待回调。 |
| `SELECT*` | `@choose`、`@choice_*` | 简单选择用一条命令；动态选择使用带键的构造器。 |
| `SND` / `*_BGM` | `@sound_*`、`@music_*` | 合并加载、播放、渐变和停止语义。 |
| `ALLOW_*` / `DENY_*` / `SHOW_*` / `HIDE_*` | `@input_enabled`、`@*_visible` | 用显式 `bool` 参数代替成对指令。 |
| `#MS` / `MOVE_ROUTE_*` / `%POSE` / `%AIM` | `@entity_*` | 直接传实体 ID、目标和速度，不再嵌入 MoveScript 文本。 |
| `GFB/GFC/SF` | `@flag_*`、`@counter_*` | 显式区分布尔与整数状态，增加直接查询能力。 |

## API

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@wait` | 同步等待 | 无 | `frames : int` | 等待指定帧数。 |
| `@wait_input` | 同步等待 | `bool` | `timeoutFrames : int` | 等待玩家确认；返回是否由输入结束。`0` 表示无超时。 |
| `@wait_motion` | 同步等待 | `bool` | `timeoutFrames : int` | 等待当前事件所有受管动作结束；返回是否正常完成。 |
| `@wait_resources` | 同步等待 | `bool` | `groupId : string, timeoutFrames : int` | 等待指定资源组就绪。 |

### 对话与选择

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@say` | 同步等待 | 无 | `actorId : string, text : string` | 以人物目录中的可读 ID 说话，例如 `aic:noel`；等待文本显示及玩家推进。 |
| `@narrate` | 同步等待 | 无 | `text : string` | 显示无说话人的旁白。 |
| `@board` | 同步等待 | 无 | `text : string, style : string` | 显示告示牌、信件或独立文本板。 |
| `@talker_bind` | 立即 | 无 | `actorId : string, displayName : string, voiceId : string` | 绑定角色显示名和默认对话音效。 |
| `@talker_reset` | 立即 | 无 | `actorId : string` | 恢复角色的默认对话资料。 |
| `@dialogue_visible` | 立即 | 无 | `visible : bool, immediate : bool` | 显示或隐藏对话框。 |
| `@dialogue_hold` | 立即 | 无 | `enabled : bool` | 设置对话框是否在下一句前保留。 |
| `@dialogue_auto` | 立即 | 无 | `enabled : bool` | 设置后续对话是否自动推进。 |
| `@dialogue_log` | 立即 | 无 | `enabled : bool` | 允许或禁止当前事件写入对话回看。 |
| `@skip_enabled` | 立即 | 无 | `enabled : bool` | 允许或禁止当前事件快进。 |
| `@choose` | 同步等待 | `int` | `prompt : string, option1 : string, option2 : string` | 显示两个简单选项，返回从 `1` 开始的序号。 |
| `@choose` | 同步等待 | `int` | `prompt : string, option1 : string, option2 : string, option3 : string` | 三选一便捷重载。 |
| `@choose` | 同步等待 | `int` | `prompt : string, option1 : string, option2 : string, option3 : string, option4 : string` | 四选一便捷重载。 |
| `@choice_clear` | 立即 | 无 | 无 | 清空当前事件的高级选项构造器。 |
| `@choice_add` | 立即 | 无 | `key : string, text : string, enabled : bool, cancel : bool` | 添加一个带稳定键的选项。 |
| `@choice_show` | 同步等待 | `string` | `prompt : string, initialKey : string` | 显示已构造的选项并返回选中的键。 |

### 角色立绘与演出层

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@actor_enter` | 等待／可并行 | 无 | `actorId : string, position : string, appearanceId : string, frames : int` | 从人物目录解析人物与 appearance，等待资源后进入指定语义位置。 |
| `@actor_exit` | 等待／可并行 | 无 | `actorId : string, frames : int` | 让角色退场。 |
| `@actor_move` | 等待／可并行 | 无 | `actorId : string, position : string, frames : int` | 将角色移到 `left`、`center`、`right` 等语义锚点。 |
| `@actor_appearance` | 立即 | 无 | `actorId : string, appearanceId : string` | 更换人物目录已登记的立绘、服装或差分组；不接受原版复合 PIC 串。 |
| `@actor_emote` | 立即 | 无 | `actorId : string, emoteId : string` | 播放单个表情符号，不暴露原版竖线拼接语法。 |
| `@actor_motion` | 等待／可并行 | 无 | `actorId : string, motionId : string, frames : int` | 播放点头、摇晃、受击等登记动作。 |
| `@actor_motion_stop` | 立即 | 无 | `actorId : string` | 停止角色当前立绘动作。 |
| `@actor_flip` | 立即 | 无 | `actorId : string, horizontal : bool` | 设置角色水平翻转。 |
| `@image_show` | 立即 | 无 | `layerId : string, assetId : string` | 在受管图层显示图像。 |
| `@image_show_at` | 立即 | 无 | `layerId : string, assetId : string, x : float, y : float` | 在指定坐标显示图像。 |
| `@image_hide` | 等待／可并行 | 无 | `layerId : string, frames : int` | 隐藏指定图层。 |
| `@image_clear` | 等待 | 无 | `groupId : string, frames : int` | 清理指定图层组，不依赖原版位标组合。 |
| `@image_move` | 等待／可并行 | 无 | `layerId : string, x : float, y : float, frames : int, easing : string` | 移动图层到绝对坐标。 |
| `@image_move_by` | 等待／可并行 | 无 | `layerId : string, x : float, y : float, frames : int, easing : string` | 按相对偏移移动图层。 |
| `@image_fade` | 等待／可并行 | 无 | `layerId : string, opacity : float, frames : int, easing : string` | 将图层透明度渐变到 `0.0`–`1.0`。 |
| `@image_scale` | 等待／可并行 | 无 | `layerId : string, x : float, y : float, frames : int, easing : string` | 渐变图层缩放。 |
| `@image_rotate` | 等待／可并行 | 无 | `layerId : string, degrees : float, frames : int, easing : string` | 渐变图层旋转角度。 |
| `@image_tint` | 等待／可并行 | 无 | `layerId : string, color : string, frames : int` | 设置或渐变图层颜色。 |
| `@image_flip` | 立即 | 无 | `layerId : string, horizontal : bool, vertical : bool` | 设置图层翻转。 |
| `@image_order` | 立即 | 无 | `layerId : string, order : int` | 直接设置图层顺序，比原版 `PIC_SWAP` 更完整。 |
| `@image_fill` | 立即 | 无 | `layerId : string, color : string` | 以纯色填充图层。 |
| `@cg_show` | 同步等待 | 无 | `assetId : string, caption : string` | 显示单张 CG，可附带说明。 |
| `@silhouette_show` | 等待 | 无 | `layerId : string, assetId : string, position : string, frames : int` | 显示剪影图层。 |

### 画面、镜头与后处理

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@screen_fade` | 等待／可并行 | 无 | `color : string, opacity : float, frames : int, easing : string` | 屏幕整体渐变；同一条指令覆盖渐入、渐出和遮罩保持。 |
| `@screen_flash` | 等待／可并行 | 无 | `color : string, delayFrames : int, holdFrames : int, fadeFrames : int` | 屏幕闪光。 |
| `@camera_shake` | 等待／可并行 | 无 | `amplitude : float, durationFrames : int, frequency : float` | 统一原版多种震动指令。 |
| `@camera_move` | 等待／可并行 | 无 | `targetId : string, x : float, y : float, zoom : float, frames : int, easing : string` | 把镜头移向对象或地图锚点，同时设置偏移和缩放。 |
| `@camera_reset` | 等待／可并行 | 无 | `frames : int` | 恢复事件开始前的镜头状态。 |
| `@effect_set` | 等待 | 无 | `effectId : string, enabled : bool, frames : int` | 启用或关闭已登记后处理。 |
| `@effect_pulse` | 等待／可并行 | 无 | `effectId : string, fadeFrames : int, holdFrames : int` | 播放一次后处理脉冲。 |
| `@spotlight` | 立即 | 无 | `targetId : string, enabled : bool, style : string` | 开启或关闭目标聚光，替代 `DARKSPOT*` 正反指令。 |

### 音频

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@audio_preload` | 同步等待 | `bool` | `audioId : string, kind : string` | 预载 `sound`、`voice`、`music` 或 `ambience` 资源。 |
| `@sound_play` | 立即 | 无 | `soundId : string, volume : float, pitch : float` | 播放一次性音效。 |
| `@voice_play` | 立即 | 无 | `actorId : string, voiceId : string, volume : float` | 播放角色语音。 |
| `@ambience_play` | 等待 | 无 | `trackId : string, volume : float, fadeFrames : int` | 播放循环环境声。 |
| `@ambience_stop` | 等待 | 无 | `fadeFrames : int` | 停止当前环境声。 |
| `@music_play` | 等待／可并行 | 无 | `musicId : string, fadeFrames : int` | 预备并切换 BGM。 |
| `@music_stop` | 等待／可并行 | 无 | `fadeFrames : int` | 渐止并释放当前 BGM。 |
| `@music_pause` | 等待 | 无 | `fadeFrames : int` | 暂停 BGM，保留当前播放位置。 |
| `@music_resume` | 等待 | 无 | `fadeFrames : int` | 恢复暂停的 BGM。 |
| `@music_volume` | 等待 | 无 | `scale : float, frames : int` | 将 BGM 音量缩放渐变到 `0.0`–`1.0`。 |
| `@music_block` | 立即 | 无 | `blockId : string` | 跳到当前 BGM 的已登记段落。 |

### UI 与输入策略

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@ui_visible` | 立即 | 无 | `visible : bool` | 设置游戏主 UI 可见性。 |
| `@status_visible` | 立即 | 无 | `visible : bool` | 设置状态 UI 可见性。 |
| `@letterbox_visible` | 等待 | 无 | `visible : bool, frames : int` | 显示或隐藏电影黑边。 |
| `@blur_visible` | 等待 | 无 | `visible : bool, frames : int` | 显示或隐藏 UI 模糊层。 |
| `@alert` | 同步等待 | 无 | `text : string, style : string` | 显示受管警告或提示。 |
| `@title_show` | 同步等待 | 无 | `text : string, x : float, y : float` | 显示章节名或地点标题。 |
| `@title_hide` | 等待 | 无 | `frames : int` | 隐藏当前标题。 |
| `@tutorial_show` | 同步等待 | 无 | `tutorialId : string` | 显示已登记的教程内容。 |
| `@tutorial_hide` | 立即 | 无 | `tutorialId : string` | 移除指定教程。 |
| `@tutorial_clear` | 立即 | 无 | 无 | 移除当前全部教程。 |
| `@input_enabled` | 立即 | 无 | `capability : string, enabled : bool` | 开关登记的 `skip`、`log`、`fast_travel` 等输入能力。 |
| `@input_key_enabled` | 立即 | 无 | `key : string, enabled : bool` | 开关已登记的单个事件按键。 |

### 地图与场景实体

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@map_change` | 同步等待 | 无 | `mapId : string, anchorId : string` | 切换地图并将玩家放到指定锚点。 |
| `@map_refresh` | 同步等待 | 无 | `mode : string` | 以登记模式刷新当前地图。 |
| `@map_layer_load` | 同步等待 | `bool` | `layerId : string` | 加载地图图层并返回是否成功。 |
| `@map_element_active` | 立即 | `bool` | `kind : string, elementId : string, active : bool` | 开关已登记的 LP、Chip 或其他地图元素。 |
| `@weather_set` | 立即 | 无 | `weatherId : string, frames : int` | 切换天气；处理器负责平滑过渡。 |
| `@danger_set` | 等待 | 无 | `level : int, immediate : bool` | 设置地图危险度。 |
| `@darkness_set` | 立即 | 无 | `enabled : bool` | 开关地图黑暗状态。 |
| `@entity_visible` | 立即 | `bool` | `entityId : string, visible : bool` | 设置场景实体可见性。 |
| `@entity_pose` | 立即 | `bool` | `entityId : string, poseId : string` | 设置场景实体姿态。 |
| `@entity_face` | 立即 | `bool` | `entityId : string, direction : string` | 设置场景实体朝向。 |
| `@entity_move_to` | 等待／可并行 | `bool` | `entityId : string, targetId : string, speed : float` | 让场景实体移动到锚点或另一实体。 |
| `@entity_move_by` | 等待／可并行 | `bool` | `entityId : string, x : float, y : float, speed : float` | 让场景实体按相对坐标移动。 |
| `@entity_follow` | 立即 | `bool` | `entityId : string, targetId : string, distance : float, speed : float` | 让实体开始跟随目标。 |
| `@entity_unfollow` | 立即 | `bool` | `entityId : string` | 停止实体跟随。 |
| `@entity_action` | 等待／可并行 | `bool` | `entityId : string, actionId : string` | 请求执行已登记的实体动作，不接受原始 MoveScript。 |

### 状态、物品与进度

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@flag_get` | 查询 | `bool` | `scope : string, key : string` | 读取已登记作用域的布尔状态。 |
| `@flag_set` | 立即 | 无 | `scope : string, key : string, value : bool` | 写入布尔状态。 |
| `@counter_get` | 查询 | `int` | `scope : string, key : string` | 读取整数状态。 |
| `@counter_set` | 立即 | 无 | `scope : string, key : string, value : int` | 设置整数状态。 |
| `@counter_add` | 立即 | `int` | `scope : string, key : string, delta : int` | 增减整数状态并返回新值。 |
| `@counter_raise` | 立即 | `int` | `scope : string, key : string, minimum : int` | 保证整数状态不低于指定值。 |
| `@progress_set` | 立即 | 无 | `value : int` | 设置游戏主进度。 |
| `@item_count` | 查询 | `int` | `itemId : string` | 查询物品数量。 |
| `@item_change` | 立即 | `int` | `itemId : string, delta : int, grade : int, notify : bool` | 增减物品并返回变更后数量。 |
| `@money_get` | 查询 | `int` | 无 | 查询当前货币。 |
| `@money_change` | 立即 | `int` | `delta : int, notify : bool` | 增减货币并返回新数量。 |
| `@skill_has` | 查询 | `bool` | `skillId : string` | 查询技能所有权。 |
| `@skill_owned` | 立即 | 无 | `skillId : string, owned : bool, notify : bool` | 授予或移除技能。 |
| `@skill_enabled` | 立即 | 无 | `skillId : string, enabled : bool` | 启用或禁用已有技能。 |
| `@magic_has` | 查询 | `bool` | `magicId : string` | 查询魔法所有权。 |
| `@magic_owned` | 立即 | 无 | `magicId : string, owned : bool` | 授予或移除魔法。 |
| `@quest_status` | 查询 | `int` | `questId : string` | 查询规范化任务状态。 |
| `@quest_set` | 立即 | 无 | `questId : string, step : int` | 设置任务步骤。 |
| `@quest_finish` | 立即 | 无 | `questId : string` | 完成任务。 |
| `@quest_remove` | 立即 | 无 | `questId : string` | 移除任务。 |
| `@autosave` | 同步等待 | `bool` | `mode : string` | 请求自动存档并返回是否成功。 |
| `@store_refresh` | 立即 | 无 | `storeId : string` | 刷新指定商店数据。 |

### Alice In Cradle 领域扩展

| 语句 | 执行方式 | 返回值 | 参数签名 | 作用 |
| --- | --- | --- | --- | --- |
| `@player_outfit` | 立即 | `bool` | `outfitId : string` | 切换玩家服装。 |
| `@player_voice` | 立即 | 无 | `voiceId : string` | 播放玩家语音。 |
| `@player_status_apply` | 立即 | `bool` | `statusId : string, strength : int, durationFrames : int` | 施加已登记玩家状态。 |
| `@player_status_cure` | 立即 | 无 | `mode : string` | 以登记模式治疗玩家。 |
| `@player_magic_cancel` | 立即 | 无 | `force : bool` | 取消玩家当前魔法。 |
| `@battle_summoner_active` | 等待 | `bool` | `summonerId : string, active : bool, effect : bool` | 开关战斗召唤点。 |
| `@battle_enemy_action` | 等待／可并行 | `bool` | `enemyId : string, actionId : string` | 请求敌人执行已登记行为。 |
| `@battle_magic_effect` | 立即 | 无 | `effectId : string, produceMana : bool` | 播放战斗魔法效果。 |

## 等待、并行与异步名称

- `立即` 表示处理器在当前解释步完成。
- `同步等待` 或 `等待` 可以跨多帧，但会自动暂停当前 PEVT 流程，直到操作结束；它不会返回 `handler`。
- `等待／可并行` 的每条 API 必须登记两个公开签名：

```pevt
@actor_move(actorId : string, position : string, frames : int)
async @actor_move_start(actorId : string, position : string, frames : int)
```

- 无 `_start` 后缀的版本按线性演出语义等待完成。
- `_start` 版本使用相同参数和普通返回值契约，但调用时立即返回 `handler`。
- `_start` 不是语法自动生成的任意后缀；只有本表标记为 `等待／可并行` 且由 PolarisEvent 实际登记的名称才能调用。
- 对话、选择、地图切换和存档默认不提供 `_start` 版本，因为它们占用全局交互或场景切换通道，并行语义不稳定。

协程、句柄和自定义等待类型见：

```text
PEVT-异步协程与等待模型.md
```

## 参数域

- `actorId` 使用人物目录的 `<namespace>:<local-id>`；内置人物使用 `aic:noel`、`aic:alice` 等固定 ID。
- `appearanceId`、`emoteId` 和人物专用 `position` 必须来自对应 `.pactor`，或来自 PolarisEvent 的通用登记表。
- `position` 使用语义锚点，第一版至少包含 `left`、`center`、`right`、`near-left`、`near-right`、`far-left`、`far-right`、`off-left`、`off-right`。
- `easing` 第一版只接受 `linear`、`ease_in`、`ease_out`、`ease_in_out`。
- `color` 使用 `#RRGGBB` 或 `#RRGGBBAA`；同时可以接受 PolarisEvent 登记的颜色名。
- `opacity`、`volume`、`scale` 的常规范围为 `0.0`–`1.0`；越界值是运行时参数错误，不静默截断。
- `frames`、`durationFrames`、`fadeFrames` 不能为负数；只有显式写明为超时的参数才允许使用 `0` 表示无超时。
- `scope`、`kind`、`style`、`mode`、`capability` 不是任意游戏 API 名称；每个处理器必须登记可接受值集，未登记值必须报错。
- 工具侧能看到同项目 `.pactor` 时可以补全和提示人物参数，但未知跨模组人物 ID 不是 `.pevt` 静态错误；真正缺失在执行时使用 `PEVTR4401`。

## 设计边界

- 常用线性剧情只需 `@actor_enter`、`@say`、`@actor_motion`、`@music_play`、`@choose` 等直观指令。
- 复杂演出使用图层、镜头、状态和 `_start` 异步变体，不需要回到原版 `PIC_*`、`TL/MTL` 或 MoveScript 文本。
- 表中实体、地图、玩家和战斗操作只能通过受控服务查找 ID，不向 PEVT 暴露对象引用或任意游戏 API。
- 商店、炼金、烹饪、长椅、小游戏和特定 NPC 不进入通用表；稳定能力以领域扩展注册，其余保留 `$raw cmd`。
- 不提供 `@game`、`@command`、`@api`、`@ui_open` 这类接受任意字符串的通用入口。
