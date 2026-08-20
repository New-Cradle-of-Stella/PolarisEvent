# PEVT 语义扩展计划

## 1. 范围与设计原则

本计划仅属于 PolarisEvent 语言、编译前端、运行时和游戏适配层。这里定义的能力必须是通用、强类型且可由手写 PEVT 使用的语义，不得把 `#MS_`、`TL`、`DOTL` 或复合 PIC 字符串注册为 PEVT 指令。

原则：

- PEVT 不重新解析原版 CMD 或 MoveScript；
- 新能力通过字符串 ID、基础类型和受管服务工作，不暴露 Unity/游戏对象；
- parser、binder、runtime、ownership、诊断、F8 调试和游戏集成同时完成；
- Stop、异常、重载和自然结束都必须恢复临时状态；
- 保持旧 PEVT 源码兼容。

## 2. 能力清单

### PEVT-E01：任意游戏值只读查询

新增四个固定返回类型的查询原子：

```pevt
@game_read_int(key : string, ...args : string) : int
@game_read_float(key : string, ...args : string) : float
@game_read_bool(key : string, ...args : string) : bool
@game_read_string(key : string, ...args : string) : string
```

要求：

1. 具体键名不使用固定白名单；任何已由游戏只读查询系统登记的键都可读取；
2. 只允许查询，不允许赋值、反射、任意方法调用、对象引用或 C#；
3. `args` 只作为查询参数，不拼接并执行完整表达式；
4. 不存在的键、参数错误和类型转换失败产生明确运行错误；
5. F8 展示键、参数、原始结果、目标类型和失败原因；
6. 已有 `@counter_get`、`@quest_status` 等领域查询继续保留，通用读取不取代更强的契约。

实现建议：在 AliceInCradle 游戏适配层包装现有 TxEval 查询注册表，提供只读调用入口；通用 PolarisEvent 核心只依赖新的 `IPevtGameQuery` 服务。

### PEVT-E02：摄像机实体目标

扩展现有 `@camera_move` 的 `targetId`：

```pevt
@camera_move("entity:cam_left", 0.0, 0.0, 1.0, 0, "linear")
@camera_move("anchor:forest_center", 0.0, 0.0, 1.0, 30, "ease_in_out")
@camera_move("player", 0.0, 0.0, 1.0, 0, "linear")
@camera_move("point", 320.0, 180.0, 1.0, 30, "linear")
```

- `entity:`：按地图实体键解析并通过 `assignBaseMover()` 跟随；
- `anchor:`：按地图标签点解析；
- `player`、`point`：保持现有语义；
- 事件 session 首次修改摄像机时登记一次快照恢复；
- 实体中途消失时产生受控失败或按规范恢复快照，不能留下无效引用。

现有不带前缀的标签点写法保持兼容，新代码推荐显式 `anchor:`。

### PEVT-E03：按像素和帧移动场景实体

新增：

```pevt
async @entity_move_by_pixels(
    entityId : string,
    xPixels : float,
    yPixels : float,
    frames : int) : bool

async @entity_move_to_offset(
    entityId : string,
    targetId : string,
    xPixels : float,
    yPixels : float,
    frames : int) : bool
```

要求：

- 游戏适配器使用当前地图 `CLEN` 转换单位；
- `frames` 表示目标持续帧数，0 表示立即到位；
- 每帧由 PEVT 时钟推进，不依赖暂停的地图主循环；
- 动作登记为当前事件 owned motion，`@wait_motion` 可等待；
- 碰撞、落脚和实体消失规则应明确测试；
- `_start` 异步别名由命令描述符系统自动提供。

不得增加接受 MoveScript 文本的 `@entity_action_raw`。

### PEVT-E04：立绘相对位移与 easing

新增：

```pevt
async @actor_move_by(
    actorId : string,
    x : float,
    y : float,
    frames : int,
    easing : string)
```

语义是相对当前受管立绘位置移动，不改变语义锚点身份。动作结束后当前位置成为后续相对移动的基准。

增加经过原版曲线验证的 `zpow` easing；在完成公式核对前不得把它声明成其他 easing 的别名。

位移动作登记 ownership，角色退出或事件结束时停止，session 释放时由立绘容器统一清理。

### PEVT-E05：类型化延迟调度

新增流程语法：

```pevt
schedule timelineId after frames call _asyncBlock()
flush schedules
clear schedules
```

静态规则：

- `timelineId` 是当前环境唯一标识符；
- `frames` 是非负 int 表达式；
- 目标必须是已经定义的无参数异步 block；
- `schedule` 不能接收动态源码、`$raw` 文本或普通字符串命令；
- block 参数支持可作为后续扩展，但第一版不支持。

运行规则：

- 调度项使用 PEVT 帧时钟；
- 到期时启动目标 block，并把它登记为当前事件子协程；
- `flush schedules` 立即启动所有尚未触发的项，不重复已经触发的项；
- `clear schedules` 丢弃尚未触发的项，不停止已经启动的动作；
- Stop、异常和自然结束清理尚未触发项；
- F8 Async/History 页面显示计划时间、剩余帧数、触发原因和目标 block。

调度器是 PEVT 通用时间轴，不引用 `EvTLCommand`。

### PEVT-E06：人物目录增量扩展

新增 sidecar 根格式：

```xml
<ActorCatalogExtension xmlns="urn:polaris:pevt:actors:v1" Version="1">
  <ActorExtension Actor="aic:ixia">
    <Appearance Id="cmd-s134a-ixia-2"
                Portrait="default"
                Pose="i/a00d"
                Frame="F2__f2__m1__b5_u3" />
  </ActorExtension>
</ActorCatalogExtension>
```

规则：

- `Actor` 必须引用已经登记的公开 Actor ID；
- 只能追加 appearance，第一版不允许覆盖 Actor 元数据或已有 appearance；
- `Portrait` 必须是目标 Actor 已登记的 portrait；
- 重复 ID、未知 Actor、未知 portrait 和非法视觉数据产生静态目录诊断；
- 扩展来源、mod 所有权和卸载顺序必须可追踪；
- F8 Source/Ownership 页面显示 appearance 的基础目录与扩展来源。

### PEVT-E07：参数化事件与 `callevt`

扩展事件头：

```pevt
id "s134b"(mode : int, source : string)
```

扩展同步与异步调用：

```pevt
callevt "s134b"(1, "forest")
handler child = callevt "asyncChild"(1)
```

规则：

- 参数类型与自定义 block 一致：`int/float/bool/char/string`；
- 实参数量、顺序和类型由 binder 静态检查；
- 参数在子事件环境中是调用时值的快照；
- 子事件不能修改调用方变量；
- 同步调用结束后继续调用方下一条语句；
- 异步调用仍要求目标 `enable async`；
- 无参数旧语法完全兼容；
- 运行时目标晚绑定时也必须校验签名，不匹配产生专用 `PEVTR43xx`。

注册表元数据需要保存事件参数签名，F8 Stack 页面显示实参快照。

### PEVT-E08：显式资源预载组

第一版继续允许高层人物、图片和音频能力自行按需加载。第二版增加静态资源组声明和等待：

```pevt
resources "opening"(
    "actor:aic:ixia/cmd-s134a-ixia-2",
    "actor:aic:noel/cmd-s134a-noel-3",
    "sound:pr_down",
    "music:BGM_morinokioku")

@wait_resources("opening", 0)
```

要求：

- 资源声明在加载阶段解析并验证类型前缀；
- 不存在的资源产生静态或启动前诊断；
- 预载票据归事件/模组所有；
- `timeoutFrames=0` 的准确含义需与当前 `@wait_resources` 契约统一；
- F8 展示组内资源的 Ready/Loading/Failed 状态。

## 3. 编译器与运行时改动面

### 3.1 Syntax

- 解析事件参数列表；
- 解析带参数 `callevt`；
- 解析 `schedule/flush schedules/clear schedules`；
- 解析可选 `resources` 声明；
- 保持现有错误恢复和行范围准确性。

### 3.2 Binding

- 绑定事件参数环境；
- 校验直接可见目标的 `callevt` 参数；
- 校验 schedule ID、帧数和异步 block 目标；
- 校验资源组声明；
- 为所有新增 `@` 指令登记固定签名和 capability。

### 3.3 Runtime

- `PevtCompiledProgram` 保存事件参数签名和资源组；
- `PevtExecution` 接收实参快照；
- 子事件解析器返回目标程序与签名；
- 增加 owned schedule 队列；
- 新增游戏查询、实体像素动作、立绘相对动作服务；
- 所有新动作接入 clock、motion、ownership 和 restore。

### 3.4 Diagnostics/F8

新增诊断类别：

- 游戏查询键不存在、参数不匹配、结果类型转换失败；
- 摄像机 entity/anchor 目标不存在；
- 实体移动目标失效；
- schedule 重名、非法目标和运行时触发失败；
- ActorExtension 引用或冲突错误；
- `callevt` 目标签名不匹配；
- 资源组成员解析或加载失败。

F8 页面必须能够追踪调用参数、查询、调度项、实体动作和人物目录扩展来源。

## 4. 实施顺序

1. [x] `PEVT-E01` 任意游戏值只读查询；
2. [x] `PEVT-E02` 摄像机实体目标；
3. [x] `PEVT-E03` 实体像素/目标偏移移动；
4. [x] `PEVT-E06` ActorCatalogExtension；
5. [x] `PEVT-E04` 立绘相对位移与 `zpow`；
6. [x] `PEVT-E05` 类型化延迟调度；
7. [x] `PEVT-E07` 参数化事件调用；
8. [x] `PEVT-E08` 显式资源预载。

每项独立完成 parser/binder/runtime/diagnostic/test 后再让转换器依赖，避免转换器生成尚未正式支持的 PEVT。

## 5. 验收标准

- 新语法和能力完全不接受 CMD/MoveScript 原文；
- 手写 PEVT 可以独立使用所有新增能力；
- 所有新增异步动作都由 PEVT 时钟推进并受 ownership 管理；
- `@game_read_*` 可读取任意已登记查询键，但不能写入或获得对象；
- 摄像机能跟随地图实体并在事件结束时恢复；
- `flush schedules` 只启动尚未触发的项；
- ActorExtension 只能安全追加 appearance；
- 参数化 `callevt` 在静态已知和运行时晚绑定两种情况下都检查签名；
- Stop、异常、热重载和自然结束不遗留调度、镜头、实体动作、立绘或资源票据；
- 新增能力具有单元测试、绑定测试、运行时测试、游戏适配测试和 F8 可观察性测试；
- 旧 PEVT 测试全部保持通过。
