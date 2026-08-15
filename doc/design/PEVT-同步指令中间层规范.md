# PEVT 同步指令中间层规范

## 1. 定位

本文规定 PEVT 解释器如何把公开 `@` 指令分派为一组有序 C# 原子方法调用。

```text
.pevt 中的 @ 调用
    ↓
解释器求值并快照全部实参
    ↓
按完整签名查找同步 @ 处理器
    ↓
处理器顺序调用受控 C# 原子方法
    ↓
全部步骤成功后才返回 PEVT 值并推进下一条语句
```

- 原子操作是 PolarisEvent 内部的强类型 C# 方法，不是第二套脚本语言。
- 中间层不生成原版 Ev 文本，不经过 `EV.readOneLine`，不创建 `.cmd`。
- 解释器只负责参数求值、签名选择、指令帧续跑和返回值提交；不知道角色、图层、音频或地图的底层类型。
- 严格来说，词法器和语法解析器不执行 C# 方法；它们只产生已绑定的 `@` 调用节点。真正顺序调用原子方法的是运行时解释器和同步指令帧。
- 本文只覆盖无 `_start` 后缀的同步 `@` API。
- `_start`、`handler`、`await`、`kill`、并行调度、取消和异步所有权见 `PEVT-异步协程与等待模型.md`。

## 2. “同步”的精确含义

- `同步` 表示当前 PEVT 流程在整条 `@` 指令完成之前不执行下一条语句。
- `同步` 不表示阻塞 Unity 主线程。
- 立绘移动、对话、音频渐变、地图切换等原子方法返回具体 `PevtWait` 对象。解释器保存当前组合协程及其当前等待，后续帧由 PolarisEvent 续跑。
- 跨帧同步等待不产生 `handler`，也不会让后续 PEVT 语句提前执行。

## 3. C# 调用契约

每个同步 `@` 组合实现为一个强类型 C# 协程：

```csharp
public interface IPevtCommandRoutine
{
    IEnumerator<PevtWait> Run(PevtRoutineContext context, PevtArguments arguments);
}
```

- 立即原子方法直接返回 `void`、`bool`、`int` 或其他受支持普通值。
- 跨帧原子方法返回 `PevtWait` 或 `PevtWait<T>`，组合协程使用 `yield return` 产出它。
- 一个协程同时只能持有一个尚未结束的 `PevtWait`。
- 有值 `@` 通过 `PevtRoutineContext.Result.Set(...)` 设置整条指令的普通返值。
- 返回 `bool` 的查找或操作可以在可预期条件不满足时提前设置 `false` 并结束协程；这只是组合处理器内部的早退，不是 PEVT 控制流。
- 原子方法、协程 `MoveNext()` 和等待 `Tick()` 中的 C# 异常均在 PolarisEvent 调度边界捕获，并转换为 `PEVTR4001` 或更具体的运行诊断。

## 4. 同步指令帧

解释器为每次同步 `@` 调用创建一个临时指令帧，至少保存：

| 字段 | 作用 |
| --- | --- |
| `Descriptor` | 已按名称和参数类型确定的 `@` 处理器。 |
| `Arguments` | 调用开始时已求值的实参快照。 |
| `Coroutine` | 当前 `IPevtCommandRoutine` 产生的 `IEnumerator<PevtWait>`。C# 迭代器自行保存组合步骤位置和局部值。 |
| `CurrentWait` | 协程当前产出且尚未处理完成的 `PevtWait`。 |
| `Result` | 整条 `@` 指令成功时提交给 PEVT 的普通返值。 |
| `Cleanup` | 为当前组合建立但尚未释放的临时资源或占用。 |

执行顺序：

1. 先完成参数类型、参数域、对象 ID 与资源 ID 校验；校验失败时不得产生任何副作用。
2. 创建协程并调用 `MoveNext()`，顺序执行其中的立即 C# 原子方法。
3. 协程产出 `PevtWait` 时暂停当前 PEVT 流程，后续帧由调度器推进该等待。
4. 等待成功后再次调用同一协程的 `MoveNext()`，从 `yield return` 之后续跑。
5. `MoveNext()` 返回 `false` 时验证并一次性提交普通返值，然后解释下一条 PEVT 语句。
6. 等待或协程任一步失败时，处置迭代器并按反向顺序执行临时清理，但不回滚已完成的持久状态修改。

## 5. 原子服务边界

下表中的 `Service.Method(...)` 表示 PolarisEvent 内部应提供的强类型 C# 方法。这些名称是中间层接口名，不是 PEVT token。

| 服务 | 责任 |
| --- | --- |
| `Clock` | 帧等待、输入等待和受管动作聚合等待。 |
| `ActorCatalog` | 把可读人物 ID 解析为对话资料、视觉、appearance 与 anchor；隔离原版短键。 |
| `Resources` | 解析、预载并等待图像、音频、地图与角色资源，统一 PolarisRes 与只读 game-pxls 借用。 |
| `Dialogue` / `Choice` | 对话、旁白、文本板、回看和选择 UI。 |
| `Portrait` / `Image` | 角色立绘与通用演出图层。 |
| `Screen` / `Camera` / `Effect` | 遮罩、闪光、镜头和后处理。 |
| `Audio` / `Music` | 音效、语音、环境声和 BGM。 |
| `Ui` / `Input` | 全局 UI、标题、教程和输入策略。 |
| `World` / `Entity` | 地图、天气、地图元素和场景实体。 |
| `State` / `Inventory` / `Quest` | 持久状态、物品、能力、任务和存档。 |
| `Player` / `Battle` | Alice In Cradle 领域操作。 |

## 6. 调度与对话组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@wait` | `Clock.WaitFrames(frames)` | 无 |
| `@wait_input` | `Clock.WaitInput(timeoutFrames)` | 返回输入是否成功触发 |
| `@wait_motion` | `Clock.WaitOwnedMotions(timeoutFrames)` | 返回是否在超时前全部完成 |
| `@wait_resources` | `Resources.ResolveGroup(groupId)` → `Resources.WaitGroup(groupId, timeoutFrames)` | 返回资源组是否就绪 |
| `@say` | `ActorCatalog.Require(actorId)` → `Dialogue.SelectSpeaker(actor.Profile)` → `Dialogue.OpenText(text)` → `Dialogue.WaitAdvance()` → `Dialogue.CommitLog()` | 无 |
| `@narrate` | `Dialogue.ClearSpeaker()` → `Dialogue.OpenText(text)` → `Dialogue.WaitAdvance()` → `Dialogue.CommitLog()` | 无 |
| `@board` | `Dialogue.OpenBoard(text, style)` → `Dialogue.WaitBoardClose()` → `Dialogue.CloseBoard()` | 无 |
| `@talker_bind` | `Dialogue.BindProfile(actorId, displayName, voiceId)` | 无 |
| `@talker_reset` | `Dialogue.ResetProfile(actorId)` | 无 |
| `@dialogue_visible` | `Dialogue.SetVisible(visible, immediate)` | 无 |
| `@dialogue_hold` | `Dialogue.SetHold(enabled)` | 无 |
| `@dialogue_auto` | `Dialogue.SetAutoAdvance(enabled)` | 无 |
| `@dialogue_log` | `Dialogue.SetLogEnabled(enabled)` | 无 |
| `@skip_enabled` | `Input.SetCapability("skip", enabled)` | 无 |
| `@choose` | `Choice.Reset()` → `Choice.Begin(prompt)` → 按参数顺序 `Choice.AddIndex(text)` → `Choice.PresentIndex()` → `Choice.Reset()` | 返回从 `1` 开始的序号 |
| `@choice_clear` | `Choice.Reset()` | 无 |
| `@choice_add` | `Choice.Add(key, text, enabled, cancel)` | 无 |
| `@choice_show` | `Choice.Begin(prompt)` → `Choice.SetInitial(initialKey)` → `Choice.PresentKey()` → `Choice.Reset()` | 返回选中键 |

`@choose` 的两项、三项和四项签名共用同一个组合处理器，只有实参数量不同。

## 7. 角色与图层组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@actor_enter` | `ActorCatalog.RequireVisual(actorId, appearanceId)` → `ActorCatalog.RequireAnchor(actorId, position)` → `Resources.RequirePortrait(visual)` → `Portrait.SetAppearance(actorId, visual)` → `Portrait.Place(actorId, anchor, frames)` | 无 |
| `@actor_exit` | `Portrait.Exit(actorId, frames)` | 无 |
| `@actor_move` | `ActorCatalog.RequireAnchor(actorId, position)` → `Portrait.Move(actorId, anchor, frames)` | 无 |
| `@actor_appearance` | `ActorCatalog.RequireVisual(actorId, appearanceId)` → `Resources.RequirePortrait(visual)` → `Portrait.SetAppearance(actorId, visual)` | 无 |
| `@actor_emote` | `Portrait.ValidateEmote(actorId, emoteId)` → `Portrait.PlayEmote(actorId, emoteId)` | 无 |
| `@actor_motion` | `Portrait.ValidateMotion(actorId, motionId)` → `Portrait.PlayMotion(actorId, motionId, frames)` | 无 |
| `@actor_motion_stop` | `Portrait.StopMotion(actorId)` | 无 |
| `@actor_flip` | `Portrait.SetFlip(actorId, horizontal)` | 无 |
| `@image_show` | `Resources.RequireImage(assetId)` → `Image.SetContent(layerId, assetId)` → `Image.SetVisible(layerId, true)` | 无 |
| `@image_show_at` | `Resources.RequireImage(assetId)` → `Image.SetContent(layerId, assetId)` → `Image.SetPosition(layerId, x, y)` → `Image.SetVisible(layerId, true)` | 无 |
| `@image_hide` | `Image.FadeTo(layerId, 0.0, frames, "linear")` → `Image.SetVisible(layerId, false)` | 无 |
| `@image_clear` | `Image.ClearGroup(groupId, frames)` | 无 |
| `@image_move` | `Image.MoveTo(layerId, x, y, frames, easing)` | 无 |
| `@image_move_by` | `Image.MoveBy(layerId, x, y, frames, easing)` | 无 |
| `@image_fade` | `Image.FadeTo(layerId, opacity, frames, easing)` | 无 |
| `@image_scale` | `Image.ScaleTo(layerId, x, y, frames, easing)` | 无 |
| `@image_rotate` | `Image.RotateTo(layerId, degrees, frames, easing)` | 无 |
| `@image_tint` | `Image.TintTo(layerId, color, frames)` | 无 |
| `@image_flip` | `Image.SetFlip(layerId, horizontal, vertical)` | 无 |
| `@image_order` | `Image.SetOrder(layerId, order)` | 无 |
| `@image_fill` | `Image.Fill(layerId, color)` | 无 |
| `@cg_show` | `Resources.RequireImage(assetId)` → `Image.OpenSingle(assetId, caption)` → `Image.WaitSingleClose()` → `Image.CloseSingle()` | 无 |
| `@silhouette_show` | `Resources.RequireImage(assetId)` → `Image.ShowSilhouette(layerId, assetId, position, frames)` | 无 |

## 8. 画面、镜头与后处理组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@screen_fade` | `Screen.EnsureFadeLayer()` → `Screen.SetFadeColor(color)` → `Screen.FadeTo(opacity, frames, easing)` → 透明度为零时 `Screen.ReleaseFadeLayer()` | 无 |
| `@screen_flash` | `Screen.Flash(color, delayFrames, holdFrames, fadeFrames)` | 无 |
| `@camera_shake` | `Camera.Shake(amplitude, durationFrames, frequency)` | 无 |
| `@camera_move` | `Camera.ResolveTarget(targetId)` → `Camera.MoveTo(targetId, x, y, zoom, frames, easing)` | 无 |
| `@camera_reset` | `Camera.RestoreEventSnapshot(frames)` | 无 |
| `@effect_set` | `Effect.Resolve(effectId)` → `Effect.SetEnabled(effectId, enabled, frames)` | 无 |
| `@effect_pulse` | `Effect.Resolve(effectId)` → `Effect.Pulse(effectId, fadeFrames, holdFrames)` | 无 |
| `@spotlight` | `Screen.ResolveTarget(targetId)` → `Screen.SetSpotlight(targetId, enabled, style)` | 无 |

## 9. 音频组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@audio_preload` | `Resources.PreloadAudio(audioId, kind)` → `Resources.WaitAudio(audioId, kind)` | 返回是否就绪 |
| `@sound_play` | `Resources.RequireAudio(soundId, "sound")` → `Audio.PlaySound(soundId, volume, pitch)` | 无 |
| `@voice_play` | `Resources.RequireAudio(voiceId, "voice")` → `Audio.PlayVoice(actorId, voiceId, volume)` | 无 |
| `@ambience_play` | `Resources.RequireAudio(trackId, "ambience")` → `Audio.PlayAmbience(trackId, volume, fadeFrames)` | 无 |
| `@ambience_stop` | `Audio.StopAmbience(fadeFrames)` | 无 |
| `@music_play` | `Resources.PreloadAudio(musicId, "music")` → `Resources.WaitAudio(musicId, "music")` → `Music.Replace(musicId, fadeFrames)` | 无 |
| `@music_stop` | `Music.Stop(fadeFrames)` → `Music.ReleaseCurrent()` | 无 |
| `@music_pause` | `Music.Pause(fadeFrames)` | 无 |
| `@music_resume` | `Music.Resume(fadeFrames)` | 无 |
| `@music_volume` | `Music.FadeVolume(scale, frames)` | 无 |
| `@music_block` | `Music.GoToBlock(blockId)` | 无 |

## 10. UI 与输入组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@ui_visible` | `Ui.SetGlobalVisible(visible)` | 无 |
| `@status_visible` | `Ui.SetStatusVisible(visible)` | 无 |
| `@letterbox_visible` | `Ui.SetLetterboxVisible(visible, frames)` | 无 |
| `@blur_visible` | `Ui.SetBlurVisible(visible, frames)` | 无 |
| `@alert` | `Ui.ShowAlert(text, style)` → `Ui.WaitAlertClose()` | 无 |
| `@title_show` | `Ui.ShowTitle(text, x, y)` → `Ui.WaitTitleShown()` | 无 |
| `@title_hide` | `Ui.HideTitle(frames)` | 无 |
| `@tutorial_show` | `Ui.ResolveTutorial(tutorialId)` → `Ui.ShowTutorial(tutorialId)` → `Ui.WaitTutorialClose()` | 无 |
| `@tutorial_hide` | `Ui.HideTutorial(tutorialId)` | 无 |
| `@tutorial_clear` | `Ui.ClearTutorials()` | 无 |
| `@input_enabled` | `Input.ValidateCapability(capability)` → `Input.SetCapability(capability, enabled)` | 无 |
| `@input_key_enabled` | `Input.ValidateKey(key)` → `Input.SetKeyEnabled(key, enabled)` | 无 |

## 11. 地图与实体组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@map_change` | `World.ResolveMap(mapId)` → `World.ResolveAnchor(mapId, anchorId)` → `World.BeginTransition()` → `World.ChangeMap(mapId)` → `World.WaitMapReady()` → `World.PlacePlayer(anchorId)` → `World.EndTransition()` | 无 |
| `@map_refresh` | `World.ValidateRefreshMode(mode)` → `World.Refresh(mode)` → `World.WaitRefresh()` | 无 |
| `@map_layer_load` | `World.TryResolveLayer(layerId)`；找不到时提前返回 `false`，否则 `World.LoadLayer(layerId)` → `World.WaitLayer(layerId)` | 返回是否成功 |
| `@map_element_active` | `World.TryResolveElement(kind, elementId)`；找不到时提前返回 `false`，否则 `World.SetElementActive(kind, elementId, active)` | 返回是否找到并更改元素 |
| `@weather_set` | `World.ResolveWeather(weatherId)` → `World.SetWeather(weatherId, frames)` | 无 |
| `@danger_set` | `World.SetDanger(level, immediate)` | 无 |
| `@darkness_set` | `World.SetDarkness(enabled)` | 无 |
| `@entity_visible` | `Entity.TryResolve(entityId)`；找不到时提前返回 `false`，否则 `Entity.SetVisible(entityId, visible)` | 返回是否找到并更改实体 |
| `@entity_pose` | `Entity.TryResolve(entityId)` → `Entity.TryResolvePose(entityId, poseId)`；任一失败时提前返回 `false`，否则 `Entity.SetPose(entityId, poseId)` | 返回是否成功 |
| `@entity_face` | `Entity.TryResolve(entityId)` → `Entity.TryResolveDirection(direction)`；任一失败时提前返回 `false`，否则 `Entity.SetFacing(entityId, direction)` | 返回是否成功 |
| `@entity_move_to` | `Entity.TryResolve(entityId)` → `Entity.TryResolveTarget(targetId)`；任一失败时提前返回 `false`，否则 `Entity.MoveTo(entityId, targetId, speed)` | 返回是否到达 |
| `@entity_move_by` | `Entity.TryResolve(entityId)`；找不到时提前返回 `false`，否则 `Entity.MoveBy(entityId, x, y, speed)` | 返回是否到达 |
| `@entity_follow` | `Entity.TryResolve(entityId)` → `Entity.TryResolveTarget(targetId)`；任一失败时提前返回 `false`，否则 `Entity.StartFollow(entityId, targetId, distance, speed)` | 返回是否成功开始跟随 |
| `@entity_unfollow` | `Entity.TryResolve(entityId)`；找不到时提前返回 `false`，否则 `Entity.StopFollow(entityId)` | 返回是否停止了跟随 |
| `@entity_action` | `Entity.TryResolve(entityId)` → `Entity.TryResolveAction(entityId, actionId)`；任一失败时提前返回 `false`，否则 `Entity.RunAction(entityId, actionId)` | 返回动作是否正常完成 |

`World.BeginTransition()` 成功后必须立即向指令帧登记 `World.AbortTransition()` 临时清理；只有 `World.EndTransition()` 成功时才撤销该清理。

## 12. 状态、物品与进度组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@flag_get` | `State.GetFlag(scope, key)` | 返回布尔值 |
| `@flag_set` | `State.SetFlag(scope, key, value)` | 无 |
| `@counter_get` | `State.GetCounter(scope, key)` | 返回整数值 |
| `@counter_set` | `State.SetCounter(scope, key, value)` | 无 |
| `@counter_add` | `State.GetCounter(scope, key)` →检查加法溢出 → `State.SetCounter(scope, key, old + delta)` | 返回新值 |
| `@counter_raise` | `State.GetCounter(scope, key)` →取较大值 → `State.SetCounter(scope, key, result)` | 返回最终值 |
| `@progress_set` | `State.SetMainProgress(value)` | 无 |
| `@item_count` | `Inventory.GetItemCount(itemId)` | 返回物品数量 |
| `@item_change` | `Inventory.ResolveItem(itemId)` → `Inventory.ChangeItem(itemId, delta, grade)` →按 `notify` 调用 `Ui.NotifyItemChange(...)` | 返回变更后数量 |
| `@money_get` | `Inventory.GetMoney()` | 返回货币数量 |
| `@money_change` | `Inventory.ChangeMoney(delta)` →按 `notify` 调用 `Ui.NotifyMoneyChange(...)` | 返回新数量 |
| `@skill_has` | `Inventory.HasSkill(skillId)` | 返回是否拥有 |
| `@skill_owned` | `Inventory.ResolveSkill(skillId)` → `Inventory.SetSkillOwned(skillId, owned)` →按 `notify` 调用 `Ui.NotifySkillChange(...)` | 无 |
| `@skill_enabled` | `Inventory.ResolveSkill(skillId)` → `Inventory.SetSkillEnabled(skillId, enabled)` | 无 |
| `@magic_has` | `Inventory.HasMagic(magicId)` | 返回是否拥有 |
| `@magic_owned` | `Inventory.ResolveMagic(magicId)` → `Inventory.SetMagicOwned(magicId, owned)` | 无 |
| `@quest_status` | `Quest.Resolve(questId)` → `Quest.GetStatus(questId)` | 返回规范化状态整数 |
| `@quest_set` | `Quest.Resolve(questId)` → `Quest.SetStep(questId, step)` | 无 |
| `@quest_finish` | `Quest.Resolve(questId)` → `Quest.Finish(questId)` | 无 |
| `@quest_remove` | `Quest.Resolve(questId)` → `Quest.Remove(questId)` | 无 |
| `@autosave` | `State.ValidateSaveMode(mode)` → `State.RequestAutosave(mode)` → `State.WaitAutosave()` | 返回是否保存成功 |
| `@store_refresh` | `Inventory.ResolveStore(storeId)` → `Inventory.RefreshStore(storeId)` | 无 |

持久状态修改不参与组合失败时的自动回滚。因此所有可失败的 ID、范围和资源校验必须位于第一个写操作之前。

## 13. Alice In Cradle 领域组合

| `@` API | 有序原子方法 | 最终结果 |
| --- | --- | --- |
| `@player_outfit` | `Player.TryResolveOutfit(outfitId)`；找不到时提前返回 `false`，否则 `Player.SetOutfit(outfitId)` | 返回是否成功 |
| `@player_voice` | `Resources.RequireAudio(voiceId, "voice")` → `Player.PlayVoice(voiceId)` | 无 |
| `@player_status_apply` | `Player.TryResolveStatus(statusId)`；找不到时提前返回 `false`，否则 `Player.ApplyStatus(statusId, strength, durationFrames)` | 返回是否成功 |
| `@player_status_cure` | `Player.ValidateCureMode(mode)` → `Player.Cure(mode)` | 无 |
| `@player_magic_cancel` | `Player.CancelMagic(force)` | 无 |
| `@battle_summoner_active` | `Battle.TryResolveSummoner(summonerId)`；找不到时提前返回 `false`，否则 `Battle.SetSummonerActive(summonerId, active, effect)` → `Battle.WaitSummonerState(summonerId, active)` | 返回是否成功 |
| `@battle_enemy_action` | `Battle.TryResolveEnemy(enemyId)` → `Battle.TryResolveEnemyAction(enemyId, actionId)`；任一失败时提前返回 `false`，否则 `Battle.RunEnemyAction(enemyId, actionId)` | 返回动作是否正常完成 |
| `@battle_magic_effect` | `Battle.ResolveMagicEffect(effectId)` → `Battle.PlayMagicEffect(effectId, produceMana)` | 无 |

## 14. 结果、错误与副作用

- 参数表达式在进入中间层前全部求值；前一个原子方法不能改变后一个实参的值。
- 有返回值的 `@` 只在整条组合成功后向接收变量赋值。
- 中间步骤产生的值仅保存在指令帧 `Locals` 中，不进入 PEVT 变量环境。
- 一个业务状态未满足但属于脚本可预期情况时，优先返回 `bool` 或数量，不抛出运行异常。
- 签名不匹配在 PEVT 加载阶段就是静态错误；执行时的资源丢失、游戏对象失效和底层调用失败使用 `PEVTR4001`。
- 原子方法返回的类型与 `@` API 表不符时使用 `PEVTR4002`。
- 事件结束时，由事件会话统一恢复所有被标记为“事件临时状态”的 UI、输入、镜头、遮罩和图层状态。
- 物品、货币、任务、技能、魔法和进度属于持久状态，不随事件结束自动恢复。

## 15. 非目标

- 本中间层不允许通过字符串指定任意 C# 方法。
- 不提供通用 `Invoke(string method, object[] args)` 给 PEVT 脚本调用。
- 不使用原版监听器的“首个返回 `true` 的处理器胜出”链式分派。
- 不在本层处理 PEVT 流程语句、表达式、自定义事件块或 `callevt`。
- 不在本层规定异步句柄模型；后续异步层应复用同一批 C# 原子方法和组合定义，而不复制另一套业务实现。
