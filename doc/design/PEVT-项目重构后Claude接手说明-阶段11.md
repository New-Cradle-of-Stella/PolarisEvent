# PEVT 项目重构后 Claude 接手说明（阶段 10 补正门与阶段 11）

## 1. 当前结论

- 项目结构重构已经完成，PEVT 阶段 1–10 的代码已迁入新结构并可构建。
- 2026-08-15 只读验收确认：现有 649 项单元测试和 5 项集成测试全绿，但阶段 1–10 没有完整符合原计划，不能继续视为“已固化”。
- 当前下一阶段改为主计划中的阶段 10A“词法、语法与深不可变补正”，随后依次完成 10B、10C、10D。
- 阶段 11“人物目录模型、XML 读取与诊断”仍未开始，并暂停到 10A–10D 的语言闭环 A 全部通过。
- 10A–10D 可以按各自范围修改 lexer、parser、binder、控制流和程序定义；不得借补正重写项目宿主、人物目录或后续运行时。
- `doc/design/PEVT-Claude分阶段实现计划.md` 已加入复核归因、补正阶段和全阶段证据门。涉及路径、项目名和命令时仍以本文为准。
- `doc/design/PEVT-阶段验收与自动探针规范.md` 是阶段 10A 之后的强制验收协议；Claude 自检、人工复核和未来 MCP 探针都必须执行其中 G0–G10，任一失败不得推进游标。

## 2. 新结构及依赖边界

Polaris 已从单体项目拆成唯一插件核心与十个普通组件 DLL：

| 项目 | 职责 | 对阶段 11 的意义 |
| --- | --- | --- |
| `PolarisCore` | 唯一 `BepInPlugin` 入口、组件宿主和公共基础契约 | 阶段 11 不应修改，也不能让 Core 反向引用人物目录 |
| `PolarisEvent` | PEVT 前端、未来解释运行时和内置事件内容 | 阶段 11 的全部生产代码放在这里 |
| `tests/PolarisEvent.Tests` | PEVT 单元测试 | 阶段 11 的测试放在这里 |
| `tests/Polaris.IntegrationTests` | Core、Diagnostics、Event 的宿主集成测试 | 阶段 11 通常只需回归运行，不增加运行时人物注册测试 |
| `PolarisRes` | 资源挂载、缓存和 PXLS 资源 | 阶段 11 不引用；实际资源绑定留给阶段 18、29 |
| `PolarisTools` | 独立兄弟仓库中的 VSIX | 阶段 11 不修改，但必须验证其仍能引用 PEVT 前端 |

依赖方向必须保持：

```text
PolarisTools(net472)
        ↓ ProjectReference，选择 netstandard2.0
PolarisEvent(netstandard2.0 纯前端)

PolarisEvent(netstandard2.1 游戏组件)
        ↓
PolarisCore(netstandard2.1)
```

`PolarisCore` 不引用任何组件项目。`PolarisEvent` 是双目标项目：

- `netstandard2.0` 必须保持纯 PEVT 前端，可供 PolarisTools 使用；
- `netstandard2.1` 才引用 `PolarisCore` 并包含 `PolarisEventComponent`；
- 当前 `.csproj` 只在 `netstandard2.0` 排除了 `PolarisEventComponent.cs`，因此阶段 11 新增的所有 Actor 模型和 XML reader 必须同时兼容两个目标；
- 不得在这些共享文件中引用 Unity、BepInEx、游戏程序集、PolarisCore、PolarisRes 或 Visual Studio SDK。

现有公共 PEVT 命名空间仍是 `Polaris.Pevt.*`。不要因为项目的 `RootNamespace` 是 `Polaris.Event` 而批量改名，否则会破坏测试和 PolarisTools 的共享引用。

## 3. 重构前后路径对照

| 旧计划中的路径 | 当前真实路径 |
| --- | --- |
| `Polaris/Polaris.Pevt.Core` | `Polaris/PolarisEvent` |
| `Polaris/Polaris.Pevt.Core.Tests` | `Polaris/tests/PolarisEvent.Tests` |
| `Polaris/Polaris.Pevt.IntegrationTests` | `Polaris/tests/Polaris.IntegrationTests` |
| `Polaris/Event/Pevt` | `Polaris/PolarisEvent` |
| `Polaris/Event/Pevt/Content` | `Polaris/PolarisEvent/Content` |
| `Polaris.csproj` | `Polaris.slnx`，生产代码已拆为多个组件项目 |
| 根目录 `PEVT-*.md` | `Polaris/doc/design/PEVT-*.md` |

PolarisTools 仍位于兄弟目录 `E:/Projects/PolarisTools`，其项目引用已经改为：

```xml
<ProjectReference Include="$(PolarisDir)\PolarisEvent\PolarisEvent.csproj" />
```

## 4. 阶段 1–10 的现有产物与验收状态

`PolarisEvent` 中已有：

- `Text`：`SourceText`、严格 UTF-8、位置和行列映射；
- `Diagnostics`：`Diagnostic`、`DiagnosticBag`、`DiagnosticCatalog`；
- `Syntax`：词法器、AST、完整语法解析和恢复；
- `Binding`：类型、符号、环境、调用和能力绑定；
- `Flow`：已有控制流诊断遍历与 `PevtProgramDefinition` 雏形，但尚不满足原阶段 10 的显式控制流模型和绑定程序要求；
- `Content/AliceInCradle.BuiltinActors.pactor`：阶段 12 将读取的内置固定人物目录。

不要移动这些目录，也不要把它们重新拆成另一个所谓 Core 项目。新的“共享 Core”就是 `PolarisEvent` 的 `netstandard2.0` 目标，不是 `PolarisCore`。

当前验证基线：

- `tests/PolarisEvent.Tests`：补正前基线 649 项通过；10A–10D 必须增加测试，因此后续不得把 649 当成固定上限或唯一验收数量；
- `tests/Polaris.IntegrationTests`：5 项通过；
- `dotnet build Polaris.slnx --no-restore`：0 warning、0 error；
- PolarisTools：0 error，但当前已有大量 nullable、线程分析器和 VSIX 重复项 warning。阶段 11 以“不增加新的 PEVT/Actor warning”为要求，不要顺手清理工具项目历史 warning。

绿色基线只表示当前覆盖没有回归，不表示阶段要求已经完整实现。只读审计确认：

| 阶段 | 结论 | 必须补正的核心问题 |
| --- | --- | --- |
| 1 | 基本符合 | 当前重构树无法再独立证明历史阶段行数预算，不要求返工基础设施 |
| 2 | 符合 | 不返工；保持诊断表与目录快照一致 |
| 3–4 | 部分符合 | int32 延迟边界没有闭合；一行一语句只是部分关键字启发式 |
| 5 | 部分符合 | AST 的只读属性没有阻止外部可变集合别名修改 |
| 6–7 | 部分符合 | 文件 ID PEVT1110/1111 与若干精确语法诊断缺发射或直接测试 |
| 8 | 未验收 | 缺独立常量值、绑定节点、静态快照复制契约及 PEVT5/6 逐编号覆盖；真实运行快照留给阶段 19/20 |
| 9 | 未验收 | Binder 注释明确承认集合等待、PEVT7203、PEVT7403 等未完成；参数域接口缺失；Raw C# 编号还暴露了原计划阶段 9 与阶段 39 的归属冲突 |
| 10 | 未验收 | 没有语句索引、显式控制流边、持久化 switch/goto 目标和真正的 Bound 程序定义 |

当前测试源码按编号直接覆盖的缺口包括：

- PEVT1xxx：缺 PEVT1110、PEVT1111；
- PEVT5xxx：缺 PEVT5002、5003、5006、5010、5022、5023；
- PEVT6xxx：缺 PEVT6005、6011、6013；
- PEVT7xxx：缺 PEVT7001、7003、7119、7202、7203、7216、7218、7219、7221–7225、7403；
- PEVT8xxx：缺 PEVT8001、8007–8010。

“测试中没直接出现编号”不自动证明生产实现一定错误，但它已经不满足原计划要求的逐编号或正反测试，因此必须进入 10A–10C 的证据矩阵逐项定责，不能整体豁免。

### 4.1 为什么会出现偏差

- 阶段 8–10 的主体要求原计划已经明确。尤其阶段 10 明写“生成语句索引、分支边、循环边、switch case 目标、已解析 goto 目标”和“包含绑定节点”；用 AST 结构或临时分析字典替代属于实现偏离，不是计划遗漏。
- 阶段 9 明写参数域接口和调用绑定；Binder 又在注释中列出未完成项，因此主体上属于在已知未完成时提前封板。不过原计划一边要求阶段 9 覆盖全部 PEVT8xxx，一边又把 PEVT8007–8015 的 C# 分析交给阶段 39，这一处确实是计划矛盾。主计划现已明确：10C 只负责 raw 外围语法、能力、参数副本和源码保存，PEVT8007–8015 唯一归阶段 39。
- 阶段 8 的“实现快照语义”没有区分静态 Bound 复制契约与阶段 19/20 的真实运行复制，也属于计划归属不够清楚；补正后由 10B 固定绑定形状、19/20 验证运行结果。
- 阶段 3–7 的规则本身存在于语法和诊断规范，但阶段摘要的反例、边界表和逐编号验收不够具体，给启发式实现留下了空间；这里是计划细化不足与实现规范追踪不足共同造成。
- “不可变”“生成控制流”“快照语义”等词此前没有在通用门中定义可证伪标准，使实现者可以用只读属性、遍历 AST、类型检查等近似物声称完成。

主计划现已增加强制证据矩阵、反向审计和阶段 10A–10D。今后不能只凭测试总数、代码注释或近似结构宣布完成。

## 5. 阶段 11 的唯一交付目标

本节是 10A–10D 全部通过后的阶段 11 执行合同，不是当前立即开工授权。补正阶段不得提前实现本节内容。

在 `PolarisEvent` 内实现可同时供游戏侧和 PolarisTools 使用的纯数据人物目录前端：

1. 不可变模型：
   - `ActorCatalog`
   - `ActorDefinition`
   - `WorldSpriteDefinition`
   - `PortraitDefinition`
   - `UiPortraitDefinition`
   - `AppearanceDefinition`
   - `ActorAnchorDefinition`
   - 视觉 provider、生命周期等必要值对象
2. 严格 `.pactor` XML reader 和只读解析结果；
3. `PEVT9101–PEVT9118` 描述符，同步加入权威静态诊断文档；
4. 覆盖阶段 11 规则的单元测试。

建议物理布局：

```text
PolarisEvent/
  Actors/
    ActorCatalog.cs
    ActorDefinition.cs
    ActorVisualDefinitions.cs
    ActorCatalogReadOptions.cs
    ActorCatalogReadResult.cs
    ActorCatalogReader.cs

tests/PolarisEvent.Tests/
  Actors/
    ActorCatalogModelTests.cs
    ActorCatalogReaderTests.cs
    ActorCatalogSecurityTests.cs
```

文件可以按实际内聚性微调，但不要创建新项目，也不要修改 `PolarisEventComponent`。

## 6. XML reader 的强制边界

权威格式见 `PEVT-人物目录与原版别名规范.md`。实现时必须满足：

- 根元素固定为命名空间 `urn:polaris:pevt:actors:v1` 中的 `ActorCatalog`；
- `Version` 第一版只接受 `1`；
- 自定义 `Namespace` 是非空、小写 ASCII 标识段，可用 `.` 分段；最终人物 ID 的拼接和目录合并留给阶段 12；
- `Actor.Id` 及 visual/anchor ID 使用稳定的 ASCII 局部 ID 规则并按 `StringComparer.Ordinal` 比较；
- `DisplayName`、`DisplayKey` 至少存在一个；
- `Color` 只允许 `#RRGGBB` 或 `#RRGGBBAA`；
- `WorldSprite` 最多一个，各分类 ID 独立去重；
- 有 `DefaultPortrait` 时必须指向同一 Actor 已定义的 Portrait；无视觉资源的纯对话 profile 可以不写；
- `Appearance` 引用同人物的 portrait，且 pose/frame 必须满足规范完整性；
- Anchor 数值必须是有限值，拒绝 `NaN` 和正负无穷；
- 第一版 provider 只有 `game-pxls` 与 `polaris-res`，并校验 `Asset`/`Resource` 的结构组合；
- reader 不访问磁盘绝对路径、不加载程序集、不反射资源字段、不读取游戏 Bundle；
- 用 `XmlReaderSettings` 禁止 DTD，设置 `XmlResolver = null`，拒绝外部实体；
- 拒绝 PEVT 权威命名空间中的未知元素、未知属性和任何看似类型名、方法名、条件、表达式或回调的可执行配置；
- 保留至少文件级、最好元素级的 `TextLocation`，使用 `IXmlLineInfo` 和原始 `SourceText` 将 XML 错误映射到准确文件、行、列；
- 解析失败返回诊断结果，不把普通用户格式错误作为未处理异常抛出。

### 信任不能来自 XML 自己

`BuiltIn="true"` 只是被解析的数据，不能令外部文件自行升级为受信任目录。reader 必须接收宿主提供的读取选项或来源上下文，例如 `IsTrustedBuiltInSource`：

- 只有宿主明确标记为内置的来源才能使用 `Namespace="aic"`、`BuiltIn="true"`、`game-pxls` 和 `LegacyPerson`；
- 外部 XML 即便手写 `BuiltIn="true"`，仍报告 `PEVT9104` 或相应来源权限诊断；
- Actor/Portrait 的 `LegacyPerson` 只允许受信任内置来源；
- 不要在阶段 11 实现程序集所有者、目录合并或运行时注册，这些属于阶段 12、16。

## 7. 诊断落地规则

`PEVT9101–9118` 已预留在 `PEVT-人物目录与原版别名规范.md`，但尚未进入权威表和代码。阶段 11 必须在同一次修改中更新：

1. `doc/design/PEVT-静态诊断表.md`；
2. `PolarisEvent/Diagnostics/DiagnosticCatalog.cs`；
3. `tests/PolarisEvent.Tests/Diagnostics/DiagnosticCatalogTests.cs` 依赖的文档快照结果。

不要建立第二张 Actor 专用编号表。现有测试会逐条比较 Markdown 与 `DiagnosticCatalog` 的编号、名称、级别和默认消息。

部分描述符在阶段 11 只需要登记，真正产生诊断要等后续阶段：

- `PEVT9106` 的跨目录/同项目冲突部分属于阶段 12；阶段 11 只处理单目录重复 Actor；
- `PEVT9111` 的实际 PolarisRes 字段类型检查属于阶段 18；
- `PEVT9117` 的特性、static、可见性检查属于阶段 18；
- `PEVT9118` 的编辑器资源源不可用 warning 属于阶段 18/45。

不得为了“覆盖所有编号”在阶段 11 伪造反射或资源加载行为。

## 8. 明确禁止提前实现

阶段 11 不包含：

- 加载或合并 `AliceInCradle.BuiltinActors.pactor`；
- 18 个 Legacy 键到 16 个 profile 的 golden 映射逻辑；
- ActorId、AppearanceId、Anchor 参数域；
- 人物运行时注册表、程序集扫描或冲突守卫；
- `.pactor` 的 `.g.cs` 生成器或 PolarisTools 编辑器；
- PolarisRes 字段扫描、PXLS/MImage 类型验证或游戏资源加载；
- 任何 `@say`、`@actor_*` 处理器；
- 对 `PolarisCore`、`PolarisEventComponent` 或组件加载顺序的修改。

这些内容分别属于阶段 12、16、18、27、29、30、45。

## 9. 当前工作树注意事项

整个项目结构重构尚未形成干净 Git 基线，`git status` 会显示大量旧文件删除和新目录未跟踪。这些是用户的结构重构，不是阶段 11 的工作量：

- 禁止恢复旧根目录文件；
- 禁止对整个仓库执行格式化、checkout、reset 或清理未跟踪文件；
- 只修改阶段 11 明确列出的 Actor、诊断文档和测试路径；
- 行数统计必须以阶段开始时的快照为基准，不能把项目搬迁算入阶段 11；
- `E:/Projects/Polaris - 副本` 只是备份，不是实现目标，禁止在那里写代码或把旧项目结构复制回来。

`tests/PolarisEvent.Tests/Debug` 等迁移遗留输出也不是阶段 11 内容，不要把其中二进制文件加入提交。

## 10. 阶段 10A–10D 与阶段 11 验证命令

在 `E:/Projects/Polaris` 运行：

```powershell
dotnet test tests/PolarisEvent.Tests/PolarisEvent.Tests.csproj --no-restore
dotnet test tests/Polaris.IntegrationTests/Polaris.IntegrationTests.csproj --no-restore
dotnet build PolarisEvent/PolarisEvent.csproj -f netstandard2.0 --no-restore
dotnet build PolarisEvent/PolarisEvent.csproj -f netstandard2.1 --no-restore
dotnet build Polaris.slnx --no-restore
```

在 `E:/Projects/PolarisTools` 运行：

```powershell
dotnet build PolarisTools.csproj --no-restore -p:DeployExtension=false
```

旧链路扫描改为在新仓库根执行：

```powershell
rg -n -g "!*.md" "commandText|\.phxx|Polaris\.Event\.Compiler|HppCompiler|EventsDir|Patch_EV_getEventContent" .
```

10A–10D 每个阶段还必须执行主计划 2.1、2.2 的证据矩阵和反向审计。不能只重复上述绿色命令。10D 结束时额外验证：

- 程序定义公开入口不能绕过 Binder 或 Flow；
- Bound 节点、控制流边、语句索引和已解析目标能从不可变结果中直接检查；
- 修改所有构造输入集合后，AST、Bound 树、Flow 模型和程序定义保持不变；
- 后续解释器无需重新扫描 token、标签或 switch case 来决定执行目标。

阶段 11 退出条件：

- 有效变更 850–1000 行；
- 两个 `PolarisEvent` 目标均构建；
- 10A–10D 补正测试、全部既有 PEVT 测试及新增 Actor 测试全绿；
- 5 项集成测试全绿；
- PolarisTools 仍可引用 `PolarisEvent` 的 `netstandard2.0` 目标；
- 诊断文档与 `DiagnosticCatalog` 完全一致；
- 没有阶段 12+ 的人物合并、注册或资源逻辑。

## 11. 自动交付探针状态

`CodexDynamicProbe.Mcp` 1.0.0 已完成新结构迁移，MCP 配置指向 Release 二进制和当前 `doc/design` 计划路径。内置验证现在执行：

- `Polaris.slnx`、PolarisEvent netstandard2.0/netstandard2.1、PolarisTools 构建；
- `tests/PolarisEvent.Tests` 与 `tests/Polaris.IntegrationTests`；
- 阶段合同、证据矩阵、诊断三联覆盖、公开产物形状、测试防弱化、延期说明、旧链和前端程序集依赖；
- G10 新会话独立反证复核；无 Claude CLI 时使用 nonce/source/evidence 哈希绑定的外部复核提交。

旧状态中的“阶段 10 已通过”已按验收标准 v2 自动失效，当前游标为 10A。任何仍把游标直接推进到 11 的交付都应拒绝；只有 10A、10B、10C、10D 四份合同和证据依次通过 G0–G10 后才能解除。
