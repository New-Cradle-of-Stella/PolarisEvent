# CMD → PEVT 转换器计划

## 1. 范围与职责

本计划仅属于 `AicCmdToPevt`。转换器负责理解 AliceInCradle 原版 CMD，并把已能证明等价的内容降低为正式 PEVT；它不在 PolarisEvent 中注册原版命令，也不让 PEVT 运行时重新解析 MoveScript。

第一验收对象为 `s13_4a.cmd`，目标是在所依赖的 PEVT 语义扩展完成后生成零 `$raw cmd` 的结果。

转换器输出：

- 主 `.pevt` 文件；
- 必要时生成相邻的 `.pactor` appearance 扩展；
- 转换诊断与未支持能力报告；
- 批量模式下的事件调用图和参数签名推断结果。

## 2. 输入语法树

### 2.1 CMD AST

保留现有 CMD 文档 AST，并继续覆盖：

- 空行、注释和普通命令；
- 赋值、标签、GOTO；
- `IF/ELIF/ELSE`、`IFDEF/IFNDEF`；
- `TL` 中的嵌套命令；
- `CHANGE_EVENT/CHANGE_EVENT2` 的目标和参数；
- 原始行号、列号和原文范围。

不得通过逐行正则替换绕过 AST。

### 2.2 MoveScript AST

为 `#MS`、`#MS_`、`#MS_SOFT` 和 `#MS_SOFT_` 增加独立的 `MoveScriptLexer`、`MoveScriptParser` 与节点模型。

原版 `#MS_ <实体> '<脚本>'` 会选择地图 Mover 并向其动作队列追加 MoveScript。实体选择应标准化为：

| CMD 选择器 | 转换器内部目标 |
| --- | --- |
| 空值或 `#` | 当前基础事件实体 |
| `THIS` | 当前事件对象 |
| `%` | 玩家 |
| `%suffix` | 指定玩家 |
| 普通字符串 | 地图实体键 |

MoveScript 第一阶段节点：

- `Wait`：`w40`；
- `Pose`：`P[pose]`；
- `Facing`：`@L`、`@R`；
- `MoveByPixels`：`>+[x,y :frames]`；
- `MoveToOffset`：`>>[target x,y :frames]`；
- `CameraFocus`：`##`；
- `CameraRelease`：`#~`；
- `CameraRefresh`：`#;`；
- `Sound`、`Manpu`、`Particle`；
- `Gravity`、`Jump`、`Velocity`、`FootWait`；
- `QueueCancel`：`q`；
- `QueueSubmit`：`=`；
- 未知节点，必须保留原始范围。

出现未知子节点时，第一阶段整条 MoveScript 回退，不能删除未知节点后继续生成一个表面可运行但语义残缺的 PEVT。

### 2.3 CMD 表达式 AST

增加原版条件表达式解析，至少覆盖：

- 数字、字符串和布尔常量；
- 比较、算术和逻辑操作；
- 普通变量读取；
- 带参数的查询，如 `SF[...]`、`ItemHas[...]`；
- `$1`、`$2` 等事件参数。

表达式节点需要进行类型推断。优先降低到已有强类型 PEVT 查询；没有专用能力时使用 `PEVT-E01` 的 `@game_read_*`。

## 3. 降低规则

### 3.1 任意游戏值读取

依赖 `PEVT-E01`。

原版：

```cmd
IF 'sensitive_level>=2' {
    #MS_ ixia 'P[weakdown_sensitive]'
} ELSE {
    #MS_ ixia 'P[weakdown]'
}
```

目标：

```pevt
var sensitiveLevel : int = @game_read_int("sensitive_level")
if sensitiveLevel >= 2
    @entity_pose("ixia", "weakdown_sensitive")
else
    @entity_pose("ixia", "weakdown")
endif
```

选择原则：

1. `SF[...]`、任务、物品、flag、counter 等已有 PEVT 强类型查询时优先使用专用指令；
2. 其他原版只读键使用 `@game_read_int/float/bool/string`；
3. 无法推断目标类型时产生转换错误，不默认猜成 `int`。

### 3.2 `#MS_` 降低

依赖 `PEVT-E02`、`PEVT-E03` 和后续实体物理能力。

| MoveScript AST | 目标 PEVT |
| --- | --- |
| `Wait(40)` | `@wait(40)` |
| `Pose("weakdown")` | `@entity_pose(entity, "weakdown")` |
| `Facing(L/R)` | `@entity_face(entity, "left/right")` |
| `MoveByPixels` | `@entity_move_by_pixels(...)` |
| `MoveToOffset` | `@entity_move_to_offset(...)` |
| `CameraFocus` | `@camera_move("entity:" + entity, ...)` |
| `CameraRelease` | `@camera_reset(...)` |

`#MS_ cam_left '##'` 必须生成：

```pevt
@camera_move("entity:cam_left", 0.0, 0.0, 1.0, 0, "linear")
```

MoveScript 与主事件并行运行。动作序列应降低为异步 block 或 PEVT 受管实体动作通道，不得直接把所有节点同步插入主事件。

转换器按实体追踪动作通道：

- 普通脚本追加到当前通道；
- `q` 取消旧通道；
- `=` 提交当前动作；
- `WAIT_MOVE` 等待当前活跃动作；
- CMD 事件结束时由 PEVT ownership 清理，不生成原版清理文本。

### 3.3 复合立绘 `PIC`

依赖 `PEVT-E06`。

转换器拆分原版复合视觉串：

```text
i/a00d__F2__f2__m1__b5_u3
```

得到 `Pose="i/a00d"` 和 `Frame="F2__f2__m1__b5_u3"`，分配稳定 appearance ID，并写入相邻 `.pactor` sidecar。

主 PEVT 只生成：

```pevt
@actor_appearance("aic:ixia", "cmd-s134a-ixia-2")
```

appearance ID 必须由事件 ID、人物 ID 和视觉内容稳定计算；同一输入重复转换不得产生不同 ID。

### 3.4 `PIC_MOVE`

依赖 `PEVT-E04`。

`PIC_MOVE i D+0 D-200 40 ZPOW` 操作事件立绘而非地图实体。转换器解析 `D+0`、`D-200` 为相对默认锚点偏移：

```pevt
@actor_move_by_start("aic:ixia", 0.0, -200.0, 40, "zpow")
```

`ZPOW` 只能映射到经过验证的等价 easing。没有等价实现时保留诊断，不替换成近似的 `ease_out`。

### 3.5 `TL/DOTL/CLEARTL`

依赖 `PEVT-E05`。

原版：

```cmd
TL 50 PIC_FADEOUT i 40
TL 75 SND pr_down
DOTL
```

目标：

```pevt
async block _timeline1()
    @actor_exit_start("aic:ixia", 40)
endblock

async block _timeline2()
    @sound_play("pr_down", 1.0, 1.0)
endblock

schedule tl1 after 50 call _timeline1()
schedule tl2 after 75 call _timeline2()
flush schedules
```

`CLEARTL` 生成 `clear schedules`。调度 ID 使用事件内唯一、稳定的生成名称。

### 3.6 `CHANGE_EVENT2`

依赖 `PEVT-E07`。

批量转换器建立事件调用图，读取所有调用点和目标事件中的 `$1`、`$2` 使用方式，推断参数数量与类型。

```cmd
CHANGE_EVENT2 s13_4b 1
```

生成：

```pevt
callevt "s134b"(1)
```

目标事件生成对应参数头。动态目标或无法统一的参数类型必须诊断；不允许静默丢弃参数。

### 3.7 `<LOAD>`

依赖 `PEVT-E08`，但可以分两阶段降低：

1. 若检查点覆盖范围全部是受管 PEVT 资源指令，则删除 `<LOAD>` 并生成“使用按需加载”的信息诊断；
2. 资源预载组完成后，静态收集检查点之后使用的人物、appearance、图片和音频，生成 PEVT 预载声明。

检查点范围仍含 raw 时，不得把 `<LOAD>` 与对应 raw 拆成不共享原版上下文的两个块。

## 4. 输出管线

1. 解析 CMD 文档 AST；
2. 解析嵌套 MoveScript 和条件表达式 AST；
3. 执行跨节点状态分析；
4. 批量模式构建调用图和参数签名；
5. 收集人物 appearance 与资源依赖；
6. 按能力可用性降低为 PEVT；
7. 合并连续 raw，但不跨越依赖原版临时状态的边界；
8. 输出 `.pevt` 与可选 `.pactor`；
9. 调用 PolarisEvent 正式前端验证；
10. 输出转换覆盖率和未支持能力统计。

## 5. raw 与诊断规则

- 未知普通 CMD：保留整条原文；
- 未知 MoveScript 子节点：保留整条 `#MS/#MS_`；
- 依赖局部变量、标签或跳转且不能降低时：保持同一 raw 上下文；
- 不得用注释、空操作或近似动画伪装成成功转换；
- 每条回退诊断包含文件、行列、原命令、缺失的 `PEVT-E` 能力编号；
- `--strict` 下任何 raw 回退均使转换失败；
- 默认模式仍允许输出可人工测试的混合 PEVT。

## 6. 实施顺序

1. MoveScript Lexer/Parser 与 AST 测试；
2. CMD 表达式 AST 和类型推断；
3. 接入 `PEVT-E01/E02/E03`，转换 `sensitive_level`、姿态、朝向和摄像机跟随；
4. `.pactor` sidecar 收集与输出；
5. 接入立绘坐标动作和时间轴；
6. 批量调用图与参数化 `callevt`；
7. 资源依赖收集；
8. 扩展 MoveScript 粒子、表情、重力、跳跃和速度节点；
9. 对原版事件目录运行覆盖率统计与回归转换。

## 7. 验收标准

- `s13_4a.cmd` 在全部依赖能力完成后生成零 `$raw cmd`；
- 生成物通过 PolarisEvent 正式 parser、binder 和编译前端；
- `#MS_ cam_left '##'` 转成实体摄像机目标而非摇晃；
- 条件表达式使用任意键只读查询且分支一致；
- `TL` 在对话期间计时，`DOTL` 不重复触发已启动项；
- 未登记复合 PIC 通过 sidecar appearance 显示；
- `CHANGE_EVENT2` 参数不丢失；
- 相同输入重复转换产生字节稳定的 `.pevt` 和 `.pactor`；
- 任何不能证明等价的内容都有明确诊断，不静默近似。
