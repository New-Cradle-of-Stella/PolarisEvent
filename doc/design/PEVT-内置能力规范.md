# PolarisEvent 内置能力原子规范（草案）

## 1. 定位

本文件规定 PolarisEvent 应向 PEVT 提供的基础事件能力及其语义边界，用于设计和实现公开的 `@` 内置事件语句。

```text
.pevt 中的 @调用
    ↓
按已登记签名进行语法与类型检查
    ↓
PolarisEvent 直接解析并分派到已登记的 @处理器
    ↓
C# 处理器 / PolarisEvent 受控游戏服务 / 内部组合
```

- PEVT 不是自举语言，不能在 `.pevt` 文件中定义、实现或覆盖 `@` 指令。
- `@` 调用不会被转换或展开为另一套 PEVT 指令，也不存在必须实现的中间原子 IR。
- 每个 `@` 的名称、参数签名、返回值、同步性和语义由 PolarisEvent 的内置 API 注册信息决定。
- 本文的“原子能力”是一套设计规范和能力分解清单，不是运行时必须实例化的指令格式。
- 一个原子能力表示一个明确、稳定且不可再按业务语义拆分的状态变化、查询或等待契约。
- 公开 `@` 可以直接对应一个原子能力，也可以由单个 C# 处理器实现一个更高层的组合能力。
- PolarisEvent 使用 C# 处理器、受控游戏服务或其他内部服务实现 `@`；除 `$raw cmd` 外，不得把 `@` 重新提交给原版 Ev 指令读取器。
- 原版指令参考只说明语义来源；除 `$raw cmd` 外，PolarisEvent 不生成或提交对应的原版命令文本。
- PEVT 作者不能通过 `@` 直接访问任意 C# 或游戏 API，只能调用已经登记的有限内置 API。
- `@` 处理器执行失败时，由 PolarisEvent 转换为 PEVT 运行时诊断。

## 2. 来源与口径

分析来源：

```text
C:\Users\Administrator\Documents\polarisDocs\指令集.csv
C:\Users\Administrator\Documents\polarisDocs\事件技术文档-LLM 可读版.md
```

- CSV 使用 GBK/CP936 编码。
- CSV 共 647 条记录；前 335 条为主指令、重复的控制台入口及相邻资料，共约 297 个不同名称。
- 后续记录主要是 MoveScript 子指令、枚举值、表情/动作取值和版本变更记录，不能全部视为独立顶层事件指令。
- 原版 `PIC_FI/PIC_FADEIN`、`PIC_MV/PIC_MOVE`、`GFB_SET/GFB_PUT` 等别名在能力规范中只保留一个语义。

## 3. 注册契约

每个公开 `@` API 至少必须登记：

| 字段 | 含义 |
| --- | --- |
| `Name` | 不含 `@` 的调用名称。 |
| `Parameters` | 有序参数名称与 PEVT 类型。 |
| `ReturnType` | 可选普通返回类型；省略表示纯调用。 |
| `Async` | 是否产生 `handler` 并由 PolarisEvent 调度。 |
| `Capability` | 对应本文的一项原子能力或一个明确的组合能力。 |
| `SideEffects` | 可能修改的角色、画面、UI、音频、地图或持久状态。 |
| `Resources` | 可在执行前收集或预载的资源引用。 |
| `FailureContract` | 可能产生的运行时诊断及取消行为。 |

- PEVT 加载阶段只依赖上述公开签名完成调用检查，不依赖处理器的 C# 实现细节。
- 执行到 `@` 时，PolarisEvent 按名称和完整参数签名查找处理器并直接调用。
- 不允许脚本通过字符串反射调用未登记的 C# 方法。
- 同一名称是否允许重载由 API 表统一规定；存在重载时必须能由参数类型唯一确定处理器。
- 本文使用 `dialogue.show` 一类带点名称作为文档中的能力标识，不是合法的公开 `@` 名称，也不会出现在 `.pevt` 源码中。

## 4. 能力属性

每项基础能力除参数外还应记录以下元数据：

| 属性 | 含义 |
| --- | --- |
| `Immediate` | 提交后立即完成。 |
| `Awaitable` | 可能跨帧执行，PolarisEvent 为其建立内部任务。 |
| `Returns` | 操作产生普通 PEVT 返回值。 |
| `Stateful` | 会修改游戏或事件的持续状态。 |
| `Reversible` | 具有明确的反向操作或恢复方式。 |
| `Cacheable` | 注册处理器能够声明资源依赖，供 PolarisEvent 提前收集并预载。 |
| `Contextual` | 依赖当前地图、角色、UI 或其他运行时对象。 |

## 5. 调度与等待能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `wait.frames` | `frames : int` | Awaitable | `WAIT` |
| `wait.input` | `timeoutFrames : int` | Awaitable, Returns `bool` | `WAIT_BUTTON`；无超时可使用约定值。 |
| `wait.motion` | `timeoutFrames : int` | Awaitable, Contextual | `WAIT_MOVE` |
| `wait.resources` | `resourceGroup : string` | Awaitable | `WAIT_LOAD`、`WAIT_BGM_LOAD` |
| `wait.function` | `key : string` | Awaitable, Contextual | `WAIT_FN` |
原版 `TL/MTL/DOTL/DOMTL/CLEARTL/CLEARMTL` 不定义为公开基础能力。需要定时或并行组合时，由 PolarisEvent 的 `async`、句柄和调度器直接实现，避免再暴露一套嵌套原版命令语法。

## 6. 对话能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `dialogue.show` | `messageId : string, recordId : string` | Awaitable | `MSG` |
| `dialogue.board.show` | `textId : string, style : int` | Awaitable | `TX_BOARD` |
| `dialogue.prefix.set` | `actorId : string, prefix : string` | Stateful | `MSG_PREFIX` |
| `dialogue.profile.bind` | `actorId : string, nameId : string, voiceId : string` | Stateful | `TALKER_REPLACE` |
| `dialogue.profile.reset` | `actorId : string` | Stateful | 无附加参数的 `TALKER_REPLACE` |
| `dialogue.style.set` | `actorId : string, position : string, pointer : string, style : string` | Stateful | `HKDS` |
| `dialogue.visible.set` | `visible : bool, immediate : bool` | Stateful, Reversible | `MSG_HIDE`、`AUTO_MSG_HIDE` 及恢复逻辑。 |
| `dialogue.hold.set` | `enabled : bool` | Stateful, Reversible | `MSG_HOLD`、`MSG_SKIPHOLD` 的保持部分。 |
| `dialogue.nextAutoAdvance.set` | `enabled : bool` | Stateful | `MSG_SKIP`、`MSG_SKIPHOLD` 的跳过等待部分。 |
| `dialogue.log.setEnabled` | `enabled : bool` | Stateful, Reversible | `ALLOW_MSGLOG`、`DENY_MSGLOG` |
| `dialogue.skip.setEnabled` | `enabled : bool` | Stateful, Reversible | `ALLOW_SKIP`、`DENY_SKIP` |

## 7. 选择能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `choice.reset` | 无 | Stateful | `SELECTARRAY_CLEAR` |
| `choice.add` | `key : string, textId : string, enabled : bool, cancel : bool, visited : bool` | Stateful | `SELECTARRAY` |
| `choice.focus.set` | `index : int` | Stateful | `SELECT_FOCUS` |
| `choice.focus.restorePrevious` | 无 | Stateful | `SELECT_FOCUS_RANDOM_TALK` |
| `choice.present` | 无 | Awaitable, Returns `int` | `SELECT` 或构造完成后的可变选择列表；PolarisEvent 处理器不向 PEVT 暴露原版结果变量。 |
| `choice.log.setEnabled` | `enabled : bool` | Stateful, Reversible | `START_LOG_RECORD_SELECTION`、`STOP_LOG_RECORD_SELECTION` |
| `choice.log.recordLast` | 无 | Immediate | `SELECT_RESULT_TO_LOG` |

面向作者的 `@choose(...) : int` 可以由一个 PolarisEvent C# 处理器直接实现完整选择流程；`reset → add* → focus → present` 只描述它的语义组成，不要求生成四条中间指令。

## 8. 立绘与图层能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `portrait.place` | `actorId : string, position : string, immediate : bool` | Awaitable, Stateful | `TALKER`；显式区分首次出现、移动和退出。 |
| `portrait.appearance.set` | `actorId : string, appearanceId : string` | Stateful, Cacheable | 角色形态的 `PIC` |
| `portrait.emote.play` | `actorId : string, emoteId : string` | Immediate/ Awaitable | `PIC_MP`；不暴露 `|` 拼接。 |
| `portrait.motion.set` | `actorId : string, motionId : string, frames : int` | Awaitable, Stateful | `PIC_MOVEA`、`PIC_MVA` |
| `portrait.motion.stop` | `actorId : string` | Immediate | `PIC_MOVEA ... NONE` |
| `portrait.replaceRule.add` | `actorId : string, pattern : string, replacement : string, condition : string` | Stateful | `PIC_TEMP_REPLACE_TERM` |
| `portrait.replaceRule.clear` | `actorId : string` | Stateful | `PIC_CLEAR_TERM_CACHE` |
| `layer.content.set` | `layerId : string, assetId : string` | Stateful, Cacheable | 非角色用途的 `PIC` |
| `layer.imageWithBackground.show` | `layerId : string, assetId : string, color : string` | Stateful, Cacheable | `PIC_B` |
| `layer.silhouette.show` | `layerId : string, assetId : string, position : string, immediate : bool` | Awaitable, Cacheable | `PIC_SILHOUETTE` |
| `layer.singleImage.show` | `assetId : string, captionId : string` | Awaitable, Cacheable | `PIC_SWIN` |
| `layer.hide` | `layerId : string, immediate : bool` | Awaitable, Stateful | `PIC_HIDE` |
| `layer.group.hide` | `group : string, immediate : bool` | Awaitable, Stateful | `PIC_HIDE_ALL` |
| `layer.move` | `layerId : string, x : float, y : float, frames : int, easing : string` | Awaitable, Stateful | `PIC_MOVE`、`PIC_MV` |
| `layer.moveFrom` | `layerId : string, fromX : float, fromY : float, toX : float, toY : float, frames : int, easing : string` | Awaitable, Stateful | `PIC_MOVE2`、`PIC_MV2` |
| `layer.flip.set` | `layerId : string, horizontal : bool, vertical : bool` | Stateful | `PIC_FLIP`、`PIC_FX`、`PIC_FY` |
| `layer.order.swap` | `firstLayer : string, secondLayer : string` | Stateful | `PIC_SWAP` |
| `layer.fill` | `layerId : string, color : string` | Stateful | `PIC_FILL` |
| `layer.fade` | `layerId : string, visible : bool, frames : int, transition : string` | Awaitable, Stateful | 语义参考 `PIC_FADEIN/PIC_FI`、`PIC_FADEOUT/PIC_FO` 和 `PIC_TFADE`；C# 实现无需生成这些文本。 |
| `screen.flash` | `delay : int, hold : int, fade : int, color : string` | Awaitable | `PIC_FLASH` |
| `screen.radiation` | `layerId : string, color : string` | Awaitable | `PIC_RADIATION` |

## 9. 镜头与后处理能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `camera.shake` | `amplitude : float, duration : int, frequency : float` | Awaitable | `QU_HANDSHAKE`、`QU_VIB` 的规范化形式。 |
| `camera.oscillate` | `axis : string, amplitude : float, duration : int, frequency : float` | Awaitable | `QU_SINH`、`QU_SINV` |
| `postEffect.set` | `effectId : string, enabled : bool, frames : int` | Awaitable, Stateful | `PE` |
| `postEffect.pulse` | `effectId : string, fadeFrames : int, holdFrames : int` | Awaitable | `PE_FADEINOUT` |
| `screen.spotlight.set` | `targetId : string, enabled : bool, style : string` | Stateful, Contextual | `DARKSPOT`、`DARKSPOT_DEACTIVATE` |

## 10. 音频能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `audio.preload` | `audioId : string, kind : string` | Awaitable, Cacheable | `LOAD_SND`、`LOAD_BGM` |
| `sound.play` | `soundId : string, volume : float, pitch : float` | Immediate, Cacheable | 语义参考 `SND`；实现不支持的参数应在 API 签名中省略，不应静默忽略。 |
| `music.replace` | `musicId : string, fadeFrames : int, releasePrevious : bool` | Awaitable, Stateful | `REPLACE_BGM` |
| `music.pause` | `fadeFrames : int` | Awaitable, Stateful | `STOP_BGM` |
| `music.resume` | `fadeFrames : int` | Awaitable, Stateful | `START_BGM` |
| `music.volumeScale.set` | `scale : float` | Stateful, Reversible | 语义参考 `HALF_BGM`；PolarisEvent 的 C# 实现可以提供完整的受控范围。 |
| `music.block.goto` | `blockId : string` | Stateful | `BGM_GOTO_BLOCK` |
| `music.override.set` | `key : string` | Stateful | `BGM_OVERRIDE_KEY` |
| `music.battleTransition.set` | `mode : string` | Stateful, Contextual | `BATTLE_TRANSITION_BGM` |

高层 `@music_play(id, fade)` 可以由单个 C# 处理器完成预载、等待和切换；`audio.preload → wait.resources → music.replace` 只是规范化的行为顺序。

## 11. UI 与输入策略能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `ui.global.setVisible` | `visible : bool` | Stateful, Reversible | `UI_ENABLE`、`UI_DISABLE` |
| `ui.status.setVisible` | `visible : bool` | Stateful, Reversible | `SHOW_STATUS`、`HIDE_STATUS` |
| `ui.letterbox.set` | `visible : bool, animated : bool, preserveOverlay : bool` | Awaitable, Stateful | `START/STOP/HIDE/SHOW_LETTERBOX` 及 `_A` 变体。 |
| `ui.blur.setVisible` | `visible : bool` | Awaitable, Stateful | `SHOW_BLURSC`、`HIDE_BLURSC` |
| `ui.money.setVisible` | `visible : bool` | Stateful | `UIBOX_MONEY_ACTIVATE`、`UIBOX_MONEY_DEACTIVATE` |
| `ui.alert.show` | `textId : string, style : string` | Awaitable | `UIALERT`、专用警告可映射到预设。 |
| `ui.title.show` | `textId : string, x : float, y : float` | Awaitable, Stateful | `TITLECALL`、`TITLE_POS_SHIFT` |
| `ui.title.hide` | `immediate : bool` | Awaitable, Stateful | `TITLECALL_HIDE` |
| `ui.tutorial.remove` | `tutorialId : string` | Stateful | `TUTO_REMOVE`、`TUTO_REMOVE_ALL` |
| `input.capability.set` | `capability : string, enabled : bool` | Stateful, Reversible | `ALLOW/DENY_SKIP`、`FASTTRAVEL`、`MSGLOG`、`EVENTHANDLE`。 |
| `input.key.setEnabled` | `key : string, enabled : bool` | Stateful, Reversible | `ALLOW_EVENTHANDLE_KEY`、`DENY_EVENTHANDLE_KEY` |

商店、炼金、烹饪、图鉴、盥洗室和小游戏 UI 不合并成一个接受任意字符串的 `ui.open` 原子；它们应由各自带固定参数契约的领域扩展定义。

## 12. 地图、天气与实体能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `map.transfer` | `mapId : string` | Awaitable, Stateful, Contextual | `MAP` |
| `map.actor.teleport` | `actorId : string, anchorId : string` | Immediate, Stateful, Contextual | 语义参考 `PRPOS` 及其目标选择行为。 |
| `map.refresh` | `mode : string` | Awaitable, Stateful, Contextual | `PRE_FLUSH_MAP`、`FLUSHED_MAP`、`REMAKE_MAP` |
| `map.layer.load` | `layerId : string` | Awaitable, Cacheable, Contextual | `LOAD_LAYER` |
| `map.element.setActive` | `kind : string, elementId : string, active : bool` | Stateful, Contextual | `LP_ACTIVATE/DEACTIVATE`、`CHIP_ACTIVATE/DEACTIVATE` |
| `map.weather.set` | `weatherId : string` | Stateful, Contextual | `WEATHER` |
| `map.danger.set` | `level : int, immediate : bool` | Awaitable, Stateful, Contextual | `DANGER`、`DANGER_MANUAL` |
| `map.darkness.set` | `enabled : bool` | Stateful, Contextual | `DARK_ACTIVATE`、`DARK_DEACTIVATE` |
| `entity.visible.set` | `entityId : string, visible : bool` | Stateful, Contextual | `#VANISH`、对象隐藏指令。 |
| `entity.pose.set` | `entityId : string, poseId : string` | Stateful, Contextual | `%POSE`、`MOVE_ROUTE_POSE` |
| `entity.facing.set` | `entityId : string, direction : string` | Stateful, Contextual | `%AIM`、`MOVE_ROUTE_TURN` |
| `entity.move` | `entityId : string, destination : string, speed : float, mode : string` | Awaitable, Stateful, Contextual | `#MS/#MS_`、`MOVE_ROUTE_TO/REL/FOLLOW` |
| `entity.action.request` | `entityId : string, actionId : string` | Awaitable, Contextual | `ENGINE`、`ENGINE_NNEA`、`ENEMY_ADD_TICKET` 的已登记动作。 |

MoveScript 的当前目标选择、`#<npc>`、引号和路线文本不应暴露到 `@` 参数。PolarisEvent 应通过受控 C# 服务直接控制实体，而不是生成并重新解析 MoveScript 文本。

## 13. 持久状态与进度能力

| 能力标识 | 建议参数 | 属性 | 原版语义参考 |
| --- | --- | --- | --- |
| `state.flag.set` | `scope : string, key : string, value : bool` | Immediate, Stateful | 语义参考 `GFB_SET/GFB_PUT` 和布尔型 SF。 |
| `state.counter.set` | `scope : string, key : string, value : int` | Immediate, Stateful | `GFC_SET/GFC_PUT`、`SF_SET` |
| `state.counter.raiseTo` | `scope : string, key : string, minimum : int` | Immediate, Stateful | `GFC_SET_MX/GFC_PUT_MX` |
| `state.mainProgress.set` | `value : int` | Immediate, Stateful | `PVV` |
| `inventory.item.change` | `itemId : string, delta : int, grade : int, notify : bool` | Immediate, Stateful | `GETITEM`、`REMITEM` 及 `_BOX/_NOANNOUNCE` 变体。 |
| `inventory.currency.change` | `delta : int, notify : bool` | Immediate, Stateful | `GETMONEY`、`GETMONEY_BOX` |
| `ability.skill.setOwned` | `skillId : string, owned : bool, notify : bool` | Immediate, Stateful | `GETSKILL`、`GETSKILL_NOANNOUNCE`、`REMSKILL` |
| `ability.skill.setEnabled` | `skillId : string, enabled : bool` | Immediate, Stateful | `ENABLESKILL`、`DISABLESKILL` |
| `ability.magic.setOwned` | `magicId : string, owned : bool` | Immediate, Stateful | `GETMAGIC`、`REMOVEMAGIC` |
| `quest.progress.set` | `questId : string, step : int, mode : string` | Immediate, Stateful | `QUEST_PROGRESS` |
| `quest.finish` | `questId : string` | Immediate, Stateful | `QUEST_FINISH` |
| `quest.remove` | `questId : string` | Immediate, Stateful | `QUEST_REMOVE` |
| `save.auto.request` | `checkpointMode : string` | Awaitable, Stateful | `AUTO_SAVE/AUTOSAVE`、`AUTO_SAVE_BENCH` |
| `store.refresh` | `storeId : string` | Immediate, Stateful | `STOREFLUSH` |
| `item.setActive` | `itemId : string, active : bool` | Immediate, Stateful | `ITEM_ACTIVATE`、`ITEM_DEACTIVATE` |

读取状态应通过单独的查询原子提供，不应让高层 `@` 直接依赖原版临时变量：

| 查询原子 | 返回类型 | 说明 |
| --- | --- | --- |
| `state.flag.get` | `bool` | 读取已登记作用域中的布尔状态。 |
| `state.counter.get` | `int` | 读取已登记作用域中的整数状态。 |
| `inventory.item.count` | `int` | 查询指定物品数量。 |
| `ability.skill.isOwned` | `bool` | 查询技能所有权。 |
| `ability.magic.isOwned` | `bool` | 查询魔法所有权。 |
| `quest.status.get` | `int` | 返回规范化任务状态。 |

如果原版 DSL 没有对应查询命令，PolarisEvent 可以直接通过受控 C# 处理器实现，但不得向 `.pevt` 暴露任意游戏 API。

## 14. 玩家与战斗领域能力

这些操作具有明显的 Alice In Cradle 业务语义，应放入可选领域模块，而不是所有 Polaris 项目的通用核心。

| 能力标识 | 建议参数 | 原版语义参考 |
| --- | --- | --- |
| `player.outfit.set` | `outfitId : string` | `PR_OUTFIT`、`NOEL_OUTFIT_TURNBACK` |
| `player.voice.play` | `voiceId : string` | `PR_VOICE` |
| `player.status.apply` | `statusId : string, strength : int, duration : int` | `SER_APPLY`、`SER_APPLY_NEAR_PEE` |
| `player.status.cure` | `mode : string` | `PR_CURE`、`MV_CURE` |
| `player.magic.cancel` | `force : bool` | `ABORT_PR_MAGIC`、`KILL_PR_MAGIC` |
| `player.key.simulate` | `key : string, pressed : bool` | `PR_KEY_SIMULATE` |
| `battle.summoner.setActive` | `summonerId : string, active : bool, effect : bool` | `SUMMONER_ACTIVATE`、`SUMMONER_ACTIVATE_EFFECT` 及对应停用逻辑。 |
| `battle.enemy.action` | `enemyId : string, actionId : string` | `ENEMY_ADD_TICKET` |
| `battle.danger.set` | `level : int, immediate : bool` | `DANGER` |
| `battle.magicEffect.play` | `effectId : string, produceMana : bool` | `SETMAGIC`、`SETMAGIC_NOMANA` |

## 15. 不进入通用能力集的原版指令

### 15.1 由 PEVT 语法取代

以下内容不应再包装成 `@` 或通用基础能力：

```text
IF / IFLANG / IFSTR / IFDEF / IFNDEF / ELSIF / ELSE
LABEL / GOTO
变量赋值 / 数学赋值
SEEK_END
CHANGE_EVENT / CHANGE_EVENT2 / MODULE / @event
```

- 条件、变量、标签和跳转由 PEVT 语法与控制流图负责。
- 事件结束由 `end` 负责。
- 事件间调用由 `callevt` 负责。
- 可复用局部逻辑由自定义事件块负责。

### 15.2 别名与正反命令

以下名称只作为原版语义参考，不形成不同的 PolarisEvent 能力：

```text
PIC_FADEIN / PIC_FI
PIC_FADEOUT / PIC_FO
PIC_MOVE / PIC_MV
PIC_MOVE2 / PIC_MV2
PIC_MOVEA / PIC_MVA
PIC_FLIP X / PIC_FX
PIC_FLIP Y / PIC_FY
GFB_SET / GFB_PUT
GFC_SET / GFC_PUT
GFC_SET_MX / GFC_PUT_MX
AUTO_SAVE / AUTOSAVE
ALLOW_* / DENY_*
SHOW_* / HIDE_*
START_* / STOP_*
```

### 15.3 领域 UI、小游戏和一次性剧情入口

以下家族不应进入第一版通用能力集：

```text
ALCHEMY* / COOKING* / ITEMSTORE / LUNCHSTORE / LUNCHTIME
BENCH* / COFFEEMAKER* / NIGHTINGALE* / PUPPETNPC / TILDENPC
MG_* / MGFARM / MGM_* / FISHPOND / SNEAKINGMG
UI_GAMEOVER / UICOOKING / UIGM / SMNCREATOR
牧场、道场、潜行、转轮、特定 NPC 和特定任务专用命令
```

处理顺序：

1. 若存在稳定且可复用的业务契约，为对应功能建立独立领域能力模块和专用 `@` 处理器。
2. 若只是单个剧情或单个小游戏的局部入口，保留 `$raw cmd`。
3. 作用仍标记为“不明”的 `UIP_*`、`PTCST`、`BENCHMARK*` 等指令，在完成运行验证前不得进入安全能力集。

### 15.4 开发、调试和资源工具

以下命令不应提供给普通剧情事件：

```text
EXPORT_IMAGE / MAXFPS / PXL_PROGRESS / TXCHECK
资源重载、地图素材导出、基准测试和调试入口
```

如确有需要，应放入仅开发环境启用的工具能力，不属于剧情运行时能力集。

## 16. `@` 处理器实现示例

### 15.1 角色入场

```pevt
handler enter = @actor_enter_start("alice", "left", "smile", 30)
```

建议行为顺序（可在同一个 C# 处理器内直接完成）：

```text
dialogue.profile.bind
portrait.appearance.set
portrait.place
wait.motion
```

### 15.2 对话

```pevt
@say("alice", "ev001_line03")
```

建议行为顺序（可在同一个 C# 处理器内直接完成）：

```text
dialogue.style.set（仅在需要改变时）
dialogue.show
```

### 15.3 画面转场

```pevt
handler fade = @screen_fade_start("black", 1.0, 45, "linear")
```

建议行为顺序（可在同一个 C# 处理器内直接完成）：

```text
layer.fill
layer.fade
```

### 15.4 播放音乐

```pevt
handler music = @music_play_start("BGM_school", 60)
```

建议行为顺序（可在同一个 C# 处理器内直接完成）：

```text
audio.preload
wait.resources
music.replace
```

### 15.5 选择

```pevt
var selected : int = @choose("选择", "留下", "离开")
```

建议行为顺序（可在同一个 C# 处理器内直接完成）：

```text
choice.reset
choice.add × 2
choice.focus.set
choice.present → int
```

这些顺序是语义说明，不是必须生成的 IR。处理器只要满足相同的可观察行为、等待语义、取消行为和错误契约，即可采用其他内部实现。

## 17. 第一版实现优先级

### P0：足以完成普通 Galgame 演出

```text
wait.*
dialogue.*
choice.*
portrait.place / portrait.appearance.set / portrait.emote.play
layer.hide / layer.move / layer.fill / layer.fade
screen.flash
camera.shake
audio.preload / sound.play / music.replace / music.pause / music.resume
ui.global.setVisible / ui.status.setVisible / ui.letterbox.set
input.capability.set
```

### P1：地图内剧情与进度

```text
map.transfer / map.actor.teleport / map.element.setActive
map.weather.set / map.danger.set
entity.pose.set / entity.facing.set / entity.move
state.flag.* / state.counter.*
inventory.* / ability.* / quest.*
save.auto.request
```

### P2：游戏专用演出

```text
玩家状态、服装、魔法
战斗点与敌人动作
商店、炼金、烹饪、长椅和小游戏领域模块
原版特殊后处理和特定 NPC 入口
```

第一版不要求消灭 `$raw cmd`。PolarisEvent 的 `@` 处理器覆盖高频、稳定、可验证的演出能力后，低频或高度上下文化的原版指令继续通过原始调用保留兼容性。
