# PEVT / PolarisEvent 分阶段实现计划（Claude 执行版）

## 1. 使用方式

本文是新 PolarisEvent 的代码实施清单。后续工作按完整功能闭环推进；一个功能阶段可以连续完成其全部生产代码、测试和集成，不再为了控制单次行数拆成零散小阶段。当前功能阶段未通过验证时，不进入下一功能阶段。

需求真相按以下优先级读取：

1. PEVT-语法设计草案.md
2. PEVT-静态诊断表.md 与 PEVT-运行诊断表.md
3. PEVT-PolarisEvent实现总纲.md
4. PEVT-人物目录与原版别名规范.md
5. PEVT-嵌入注册与ID冲突规范.md
6. PEVT-同步指令中间层规范.md
7. PEVT-异步协程与等待模型.md
8. PEVT-内置事件语句表.md
9. PEVT-内置能力规范.md

规范冲突时不得静默选择。先在实现说明中指出冲突；若诊断表已经为某种行为分配编号，则以诊断表和语法规范为准。

## 2. 实施原则

- 以功能完整性为第一目标：先把当前功能阶段所列能力完整实现，再统一构建、测试和收口。
- 不设置单次或单阶段代码行数上下限，不要求统计或汇报有效变更行数，也不为满足预算填充抽象、注释或测试。
- 一个功能阶段可以跨多次工作会话或提交，但中间状态必须保持可构建，不交付半个公开模型或破坏已有入口。
- 当前功能阶段内允许同时修改生产代码、测试、项目文件和必要资源；不提前实现下一功能阶段的独立能力。
- 每个功能阶段结束时汇报改动范围、对应规范、验证结果和明确未完成项。
- 工作树可能含用户的其他未提交改动，只修改当前功能点所需路径，禁止还原、清理或格式化无关文件。

## 3. 已完成的清场基线

旧链路已经删除，不属于后续实现范围：

- Polaris/Event 下旧 IPolarisEventRegistrar、PolarisEventDefinition、注册表、冲突守卫、运行时、Start/Change 外观和引用类型。
- Polaris/Patch/Patch_EV_getEventContent.cs。
- PolarisTools/Event/PolarisEventGenerator.cs。
- PolarisTools/Event/Language 下全部 HPP/.phxx 语言服务。
- 旧 .phxx 与事件别名模板。
- Plugin、PathsAPI、项目引用、VSIX 清单和菜单中的旧入口。

后续任何阶段都不得恢复以下默认执行链：

    .phxx -> commandText -> 磁盘 .cmd -> EV.getEventContent -> EV.stack

原版 EV 文本解释入口只允许在“原始桥与扩展能力”功能阶段的 `$raw cmd` 独占桥中出现。`game-pxls` 资源桥可以复用原版资源加载类，但不能创建 EvReader 或提交 CMD 文本。

## 4. 目标代码布局

项目已经拆成并排的两个仓库，PEVT 运行时与 PEVT 工具/IDE 不放在同一个仓库里：

    E:\Projects\Polaris\          聚合仓库，组件为 submodule
    E:\Projects\PolarisTools\     Polaris 系列全部工具（VS 扩展 VSIX，net472）

### 4.1 PolarisEvent 组件仓库（`Polaris/PolarisEvent`）

`PolarisEvent.csproj` 双目标：

- `netstandard2.0`：纯 PEVT 前端，供 PolarisTools 引用；不得引用 Unity、BepInEx、游戏程序集或 Visual Studio SDK。
- `netstandard2.1`：游戏侧组件目标，引用同级 `PolarisCore`。

目录职责：

- `Text/`、`Syntax/`、`Binding/`、`Flow/`、`Diagnostics/`、`Actors/`：共享语言核心与人物目录，两个目标共用。
- `Content/`：Polaris 内置 `.pactor` 人物固定目录，作为 EmbeddedResource 随程序集分发。
- 游戏侧宿主、注册扫描、生命周期与游戏原子服务适配器：本仓库 `netstandard2.1` 目标。
- `tests/PolarisEvent.Tests`：无游戏依赖的语言、人物目录与便携运行时单元测试。
- `tests/Polaris.IntegrationTests`：宿主程序集边界与跨模块集成测试、游戏接口替身。

### 4.2 PolarisTools 仓库（`E:\Projects\PolarisTools`）

全部 PEVT IDE 内容只写在这里，不写进 PolarisEvent：

- `Event/Pevt`：`.pevt` 生成器与 Visual Studio 语言服务（ContentType、分类、tagger、补全、跳转、错误列表）。
- `Event/Actors`：`.pactor` 生成器、参数编辑器与 PolarisRes 资源选择器。
- `ItemTemplates/Polaris/*`：`.pevt` / `.pactor` 项目模板。

PolarisTools 通过 `ProjectReference` 引用 `$(PolarisDir)\PolarisEvent\PolarisEvent.csproj` 的 `netstandard2.0` 目标，`$(PolarisDir)` 默认取兄弟目录 `..\Polaris`。工具侧不允许复制解析器、载荷编码或人物目录规则；两侧对同一源文件必须得到一致的 AST、绑定结果和 PEVTxxxx。

VSIX 不能用 `dotnet build`，只能用 MSBuild 构建。

## 5. 全局不变量

- PEVT 永远以 UTF-8 原始源文本为真相，不生成 PEVT 字节码，不写回 .cmd。
- 游戏侧必须对嵌入源重新做完整静态校验，不能信任工具侧结果。
- 解释器只执行已绑定的不可变节点；除 exec 外不在每帧重新解析。
- 五种普通类型只有 int、float、bool、char、string；handler 是运行时专用包装，不进入普通类型系统。
- 表达式按规范的从左到右规则执行，括号形成强制子表达式；不得套用 C# 运算符优先级。
- 同步和异步 @ 调用复用同一个协程工厂，不能复制业务处理器。
- 全部跨帧等待必须表现为可取消、可诊断的 PevtWait。
- callevt 只在运行时查询全局 ID；目标不存在不是构建错误。
- exec、原始桥和子事件都必须进入同一所有权树、预算、诊断与清理边界。
- 静态和运行诊断使用规范中已有编号；禁止为同一错误临时另造编号。
- 普通 PEVT 代码不能获得 Unity 对象、游戏对象、反射入口或任意 C# 方法调用能力。
- 人物公开 ID 只能来自独立人物目录；原版 CMD 人物短键和 TALKER_REPLACE 临时键不得泄漏到普通 @ 参数。
- `.pactor` 是 XML 数据，不执行表达式、流程或任意 C#；自定义视觉只能引用允许的 PolarisRes 静态资源字段。
- 原版 game-pxls 资源是借用，自定义 PolarisRes 资源由其来源 owner 持有；事件只拥有显示实例和临时等待。

## 6. 通用验证命令

在 `Polaris/PolarisEvent` 仓库内，每个功能阶段至少运行：

    dotnet build PolarisEvent.csproj --nologo
    dotnet test tests/PolarisEvent.Tests/PolarisEvent.Tests.csproj --nologo

涉及游戏宿主、注册或适配器时再运行：

    dotnet test tests/Polaris.IntegrationTests/Polaris.IntegrationTests.csproj --nologo

改动涉及工具/IDE 时，在 `E:\Projects\PolarisTools` 用 MSBuild 构建（VSIX 不支持 `dotnet build`）：

    & "<VS 安装目录>\MSBuild\Current\Bin\MSBuild.exe" PolarisTools.csproj /t:Build /p:Configuration=Debug /p:DeployExtension=false

每次都运行旧链路回归扫描：

    rg -n -g "!*.md" "commandText|\.phxx|Polaris\.Event\.Compiler|HppCompiler|EventsDir|Patch_EV_getEventContent" .

命令应无命中；新增的原始 EV 桥必须用精确白名单路径复核，不能放宽为全项目允许。

## 7. 功能阶段门

| 功能阶段 | 必须证明的结果 |
| --- | --- |
| A：语言闭环（已完成基线） | 同一源文本能稳定产生 token、AST、绑定程序和规定诊断。 |
| B：目录与分发闭环 | `.pactor` 与 `.pevt` 使用共享规则生成强类型注册代码，游戏侧按独立全局空间登记。 |
| C：同步解释闭环 | 不接游戏 API 也能用替身完整运行流程、块、同步 @、人物目录解析与失败诊断。 |
| D：可演出 P0 | 对话、选择、原版/自定义人物立绘、图层、画面、音频和 UI 可由真实适配器执行并清理。 |
| E：异步与组合 | handler、await、all/any、callevt 与 exec 具有确定调度和所有权。 |
| F：原始桥与扩展能力 | raw 桥及 P1/P2 原子服务可控，普通代码仍不依赖原版 EV 文本解释器。 |
| G：工具与发布 | `.pevt/.pactor` 编辑器与运行时共用规则，全链路、性能、诊断和清理达到发布门槛。 |

## 8. 按功能闭环实施

### 功能阶段 B：人物目录、注册与生成分发

目标：一次完成 `.pactor`、`.pevt`、描述目录、注册表和工具生成器，使人物与事件都能从源文件可靠进入游戏侧只读注册表。

- 实现深不可变的 ActorCatalog、Actor、WorldSprite、Portrait、UiPortrait、Appearance、Anchor 与视觉提供者模型，以及严格 `.pactor` XML reader；校验命名空间、Version、目录 namespace、局部 ID、颜色、重复项、引用完整性和源码位置，拒绝 DTD、外部实体、未知执行性元素、任意类型名、方法名和条件。
- 将 `PEVT9101–9118` 同步加入静态诊断文档、DiagnosticCatalog 和逐项测试；恶意 XML、未知元素/属性、非有限 anchor、重复 visual 和无效默认 portrait 都必须有确定结果。
- 实现人物最终 ID `<namespace>:<local-id>`、目录合并、内置 namespace 封闭和同源/跨源冲突候选；读取 `AliceInCradle.BuiltinActors.pactor`，将 18 个稳定原版短键归并为 16 个公开 profile（含 `_` 叙述者），固定 Noel 三套 portrait，并禁止 TALKER_REPLACE 动态键和不受信任 LegacyPerson 进入公开目录。
- 实现 ActorId、AppearanceId、ActorAnchor 等参数域；未知跨模组人物只影响补全，不产生静态错误。
- 建立唯一 CommandDescriptor 目录，描述 P0/P1/P2 API 的参数、返回类型、参数域、等待类别和并行能力；同名重载必须按参数数量和完整类型唯一选择，可并行 API 自动校验对应 `_start` 签名且不复制描述数据。
- 实现 PevtEmbeddedSource、严格 UTF-8、GZip Base64 v1、SHA-256、长度上限和项目相对 SourcePath；保持原始换行，统一处理损坏 Base64、截断 GZip、膨胀、长度、哈希和格式版本失败。
- 建立事件与人物各自的 registrar、扫描器、只读注册表、来源上下文和冲突守卫。运行时必须解包并重新完整校验 `.pevt`，核对 DeclaredId；ID 使用 Ordinal 比较，事件与人物冲突分表收集，跨程序集人物冲突映射 PEVTR4404，Seal 后不可继续注册，卸载可撤销对应 owner。
- 内置 `.pactor` 作为 Polaris EmbeddedResource 优先登记；外部来源不能伪造 BuiltIn 或 `aic` namespace。人物注册只保存不可变数据和延迟视觉访问器，扫描期不得触发资源加载。
- 完成 PolarisTools 的 `.pevt` 与 `.pactor` 单文件生成器、项目模板、VSIX 资产和 GeneratorBinding。生成的 `.g.cs` 只包含载荷或强类型延迟资源访问器，不包含 AST、CMD 或复制的解析规则。
- 抽取并复用 PUI Image 的项目定位、注释/字符串掩码、特性字段扫描和 FileSystemWatcher；资源字段必须校验 static、可见性、特性与类型，跨文件重复事件/人物 ID 在生成期报告。
- 完成合法/恶意 XML、固定人物 golden、参数域、载荷损坏、注册冲突、卸载、生成 C# 快照与可编译性、资源扫描缓存失效等全链路测试后，再进入下一功能阶段。

### 功能阶段 C：便携同步解释器、等待与调度

目标：在不引用 Unity 或游戏程序集的前提下，用内存替身完整执行已绑定 PEVT 的同步流程，并形成统一的等待、预算、诊断和所有权模型。

- 实现 PevtValue、未初始化槽、值复制、普通类型访问器、handler 分离存储、文件/块环境、声明执行标记、显式执行帧栈、ExecutionResult、暂停/终止原因和源码调用栈。
- 执行字面量、读取、转换、一元/二元运算、赋值、var、const；int 使用 checked，除零、非有限 float 和未初始化读取映射到 PEVTR2xxx，重复声明执行映射 PEVTR3001。表达式遵守 PEVT 从左到右语义。
- 执行 if/elif/else、while、switch/case/default、标签 goto、switch 表达式 goto、end、自定义块和 return。所有跳转使用预绑定目标，switch 值只求值一次，事件与块调用使用显式帧而不是 C# 递归。
- 实现每帧步数、总步数、调用深度、无进展检测、只读跟踪、诊断调用栈和内部异常链；while/goto 超限产生 PEVTR1001，无进展产生 PEVTR1002。
- 实现 IPevtCommandRoutine、PevtRoutineContext、Arguments、Result 和逆序 Cleanup 栈。同步 @ 按已绑定 Descriptor 创建指令帧，所有实参先求值并快照，结果只能提交一次，调用与结果契约失败映射 PEVTR4001/4002。
- 实现统一 PevtWait 状态机及 Frame、Predicate、Signal、Resource、Motion、Input、Composite 等待；Tick、Cancel 幂等，完成后不再推进，不向 Core 暴露 Unity yield 对象。
- 实现 PevtRoutineInstance、稳定递增 ID、确定调度顺序、同帧完成顺序和所有权树；根事件、同步例程、子例程、等待与资源占用统一登记，结束、替换、异常和卸载时级联取消并逆序清理。
- 定义 ActorCatalog、Clock、Resources、Dialogue、Choice、Portrait、Image、Screen、Camera、Effect、Audio、Music、Ui、Input 等中立服务接口、事件会话状态和完整内存替身；不得向 Core 暴露 EvPerson、PxlCharacter、MImage、Unity 或游戏内部对象。
- 用内存测试覆盖值快照、环境隔离、所有流程、深帧、预算差一、同步指令结果、等待成功/失败/超时/取消、多例程竞态、所有权清理和人物解析；全部通过后再接真实游戏服务。

### 功能阶段 D：P0 演出适配与公开宿主

目标：一次接通对话、选择、人物视觉、画面、音频、UI、输入和公开 PolarisEvent 生命周期，形成可实际运行的完整 Galgame P0。

- 实现 @say、@narrate、@board、talker、dialogue、skip、choose/choice 等组合；人物 ID 先经目录解析，未知人物产生 PEVTR4401，选择序号从 1 开始，高级选择返回稳定 key，失败路径始终 Reset。
- 在 PolarisRes 实现只读 `game-pxls` 提供者，按逻辑 Bundle 路径借用原版 MTI/MTRX/PxlsLoader 资源并投影为统一 handle；禁止绝对路径和任意 Bundle 类型，释放只撤销 PolarisEvent 自己的引用。
- 适配自定义 `polaris-res` 延迟字段：PXLS 使用 PxlsCharacterHandle，UI portrait 使用 MImage，并转换为中立 visual lease。Ready/Faulted、重复借用、取消、原版 loader 已存在和插件卸载统一接入 PevtResourceWait，缺失资源产生 PEVTR4403。
- 实现 actor、image、cg、silhouette 组合。先验证 actor、appearance、anchor、资源、动作和图层，再产生副作用；原版 LegacyPerson 仅选择内置视觉源，不把 TALKER/PIC 文本交回原版解释器。
- 实现 screen_fade、flash、camera、effect、spotlight，并保存与恢复镜头、遮罩和临时占用；frames、opacity、zoom、easing 等参数必须先做域验证。
- 实现 sound、voice、ambience、music、UI、title、tutorial 和 input；预载与等待使用 ResourceWait，BGM、环境声、UI 和输入临时修改进入会话清理，持久设置不在此层改变。
- 实现公开 Start、Change、Stop 和 PevtEventInstance；Plugin 初始化先登记内置人物，再扫描人物与事件 registrar，更新点按固定顺序推进调度器。Change 必须完成旧根事件清理后再启动新事件。
- 提供事件状态、诊断和所有权树只读查询；覆盖最小对话事件、选择、`EvImg/__ev_n`、`PxlNoel/noel`、自定义人物 enter/appearance/move/exit、镜头取消恢复、音频/UI/输入清理、事件替换、停止与插件卸载。

### 功能阶段 E：异步、事件组合与动态片段

目标：在既有调度器和所有权树上完整实现 handler、组合等待、子事件与 exec，不复制同步处理器。

- async @ 与 async _ 启动为事件拥有的独立子例程；handler 只保存调度器 ID 和预期返回类型。同步与异步调用必须复用同一 CommandRoutine 或块定义。
- 实现 status、单句柄 await、kill、await all/any。all 返回正常结束数量；any 返回首个正常完成序号，全部异常返回 0，并按稳定调度 ID 解决同帧竞争后取消未完成项。
- 严格执行 all/any 的参数、返回变量和初始化规则；异常完成不赋值，未观察异常产生 PEVTR5005，事件结束自动 kill 所有子例程。
- 同步 callevt 在运行时查询 `/event` 并压入子事件帧；`enable async` 目标可作为子事件返回 handler。缺失、冲突、非异步目标和启动失败映射 PEVTR4301–4304，并受调用深度和父级所有权限制。
- exec 在运行时通过 Core 解析和绑定字符串片段，禁止 id、enable、块定义、end、标签和 goto；允许读写授权的外层变量，新增变量只存在临时环境，并继承而不能扩大宿主 cs/async 能力。
- 为 exec 设置独立深度与步数预算，将内部静态诊断包装为 PEVTR1201；覆盖并行完成、kill、异常混合、递归、晚注册、环境读写、临时变量销毁和禁止语句。

### 功能阶段 F：原始桥与 P1/P2 扩展能力

目标：集中完成两个受控逃生口和全部扩展领域服务，确保普通 PEVT 始终不回退到原版解释链或任意 C# 能力。

- `$raw cmd` 只允许存在于 Polaris/Event/Pevt/RawCmd；同一时间只运行一个原版 EV 文本会话，支持排队、等待、取消、错误翻译和清理，解析失败产生 PEVTR4101，不写永久 `.cmd` 缓存。
- 普通 @ 和普通 PEVT 事件不得进入 EV.readOneLine。TALKER_REPLACE 及 `mb/x/a` 等临时别名只存在于当前 raw 会话，结束或取消时恢复原版人物资料，绝不写入 PevtActorRegistry。
- 实现受信任 `$raw cs` 执行器、Roslyn 语句包装、普通值副本、返回类型与全路径 return 校验、参数名校验和编译缓存；缓存键包含代码、参数名/类型、引用集合与语言版本。
- raw cs 只返回 int、float、bool、char、string 或 void，拒绝 null；运行时要求 `enable cs`，exec 复用同一执行器且不得扩大引用集合。编译与运行诊断映射回 raw 源码，异常产生 PEVTR4102。
- 实现 World 与 Entity 适配器及地图、天气、实体移动/动作 API；地图切换登记 AbortTransition，动作统一使用受管 Wait，不接受 MoveScript 字符串。
- 实现 State、Inventory、Quest、autosave 适配器；所有 ID、范围和资源校验在首个持久写入前完成，持久写入不随事件失败回滚，counter_add 使用 checked。
- 实现 Alice In Cradle 的 Player、Battle 适配器与 P2 API；可预期查找失败按签名返回 bool，未知游戏异常进入 PEVTR4001，领域类型不得进入通用 Core。
- 验证 raw 会话互斥与恢复、PEVT8007–8015、C# 缓存与能力拒绝、地图取消、实体消失、持久状态原子性、P2 对象销毁和所有 P1/P2 Descriptor 处理器覆盖。

### 功能阶段 G：PolarisTools 编辑体验与发布硬化

目标：一次完成 `.pevt/.pactor` 编辑体验、导航调试和端到端发布门，使工具侧与运行时共享全部语义。

- 实现 `.pevt` ContentType、分类、格式、tagger 和错误列表；高亮可使用轻量 token 快照，但实时诊断必须调用共享 Core，并支持版本取消、防抖和过期结果丢弃。
- 实现 `.pactor` XML 编辑器，只编辑人物资料、visual、appearance 和 anchor，不提供脚本或任意 C# 输入；复用 PolarisRes 目录与缩略图逻辑，失效引用保持可见可修复，BuiltIn 目录只读。
- `.pactor` 写回稳定可读的 UTF-8 XML，保留未知非执行性扩展节点；支持资源 watcher、撤销/重做、XML 转义和实时 PEVT91xx 错误。
- 基于绑定上下文实现关键字、变量、块、@ 重载、参数名和参数域补全；人物补全合并内置目录与当前项目 `.pactor`。快速信息展示签名、返回值、等待方式和能力要求。
- 实现变量/块跳转与引用查找，actor/appearance 跳到 `.pactor`；callevt 和跨模组 actor 不假定目标在当前项目。所有结果绑定文档版本并支持取消。
- 实现非强制格式化，嵌套四空格，保持 raw 内容和多行字符串列对齐；更新合法最小事件、P0 演出和自定义人物资源模板。
- 提供只读运行事件、调用栈、handler、Wait、预算、所有权树、人物解析和视觉来源查看；调试开关不得改变 Tick 顺序或暴露可变游戏对象。
- 建立从源文件生成 C#、编译模拟模组、扫描注册、解释执行到清理的端到端测试，覆盖 P0、P1、P2、async、callevt、exec、两个 raw 桥、固定人物映射、自定义人物和两类视觉资源。
- 加入 lexer/parser 模糊测试、深度/大小/解压上限、长事件分帧预算、调度确定性回放，以及全部静态/运行诊断、API Descriptor、人物 provider 和生成 registrar 覆盖检查。
- 最终 Core、Polaris、PolarisTools 与集成测试全部通过；旧链扫描除 RawCmd 精确白名单外为零，并输出人物冲突、性能和资源清理结果。

## 9. 功能阶段交付模板

每个功能阶段完整实现并验证后，按以下格式汇报：

1. 功能阶段与已完成目标。
2. 新增、修改、删除文件及关键公开入口。
3. 对应规范条目和诊断编号。
4. 执行的构建、测试、扫描命令及结果。
5. 本功能阶段退出条件逐项结论。
6. 仍未完成或明确留给下一功能阶段的内容。

不统计或汇报代码行数。功能阶段验证失败时继续修复当前功能点，不得通过跳过测试、删除诊断、降低断言或恢复旧 `.cmd` 执行链来获得绿色结果。

## 10. 最终验收

- 普通 Galgame 对话、选择、立绘、图层、画面、音频和 UI 完全由可读 @ 指令执行。
- 原版人物使用 `aic:noel` 等固定可读 ID；模组人物只需 `.pactor` 与 PolarisRes 资源字段即可参与同一套对话和演出。
- 除 `$raw cmd` 会话兼容外，普通代码和公开参数中不出现 `n/a/so` 等人物短键或 TALKER_REPLACE 临时键。
- PEVT 角色演出、地图实体、状态进度和 C# 回调均经过受控服务，不暴露任意游戏 API。
- 工具侧和游戏侧对同一源码得到一致 AST、绑定结果和 PEVTxxxx。
- 同步、异步、事件调用、替换、异常和卸载都能证明所有权与清理结果。
- 运行错误能回溯到事件 ID、模组、相对路径、源码行列和事件/块/协程调用栈。
- 除显式 $raw cmd 外，代码和测试中不存在 .cmd 生成、磁盘 events 缓存、EvReader 或 EV 默认分派依赖。
- 新系统保持解释型：嵌入的是压缩原始文本，不是 PEVT 字节码或原版命令文本。
