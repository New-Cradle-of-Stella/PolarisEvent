# PEVT 人物目录与原版 CMD 别名规范

## 1. 目的

PEVT 对外只使用可读、稳定的人物 ID，不把原版 CMD 的 `n`、`a`、`so` 等短键传播到新事件中。人物目录同时负责对话资料、事件立绘、地图像素角色和可选 UI 头像，但不向 PEVT 暴露 `EvPerson`、`PxlCharacter`、`MImage` 或其他游戏类型。

原版固定人物来自游戏的 `StreamingAssets/evt/__vp_person.dat`。原版站位来自 `__vp_talker_pos.dat`。脚本内的 `TALKER_REPLACE` 只属于当次原版事件会话，不是可注册人物定义。

## 2. 身份模型

- 公开人物 ID 采用 `<namespace>:<local-id>`，比较方式固定为 `StringComparer.Ordinal`。
- 原版命名空间固定为 `aic`，例如 `aic:noel`、`aic:alice`、`aic:tigrina`。
- 自定义目录必须声明自己的 `Namespace`，人物的最终 ID 由 `Namespace + ":" + Actor.Id` 组成。
- `aic` 是封闭命名空间；外部程序集不能注册、覆盖或扩展它。
- 原版短键只记录在 `LegacyPerson` 字段中，不能作为 `@say` 或 `@actor_*` 的公开人物 ID。
- 一个故事人物可以拥有多套视觉资源。`n`、`nb`、`nb2` 因此映射为同一个 `aic:noel` 的 `default`、`bass`、`epbench` 三套 portrait，而不是三个不同人物。
- 人物 ID、视觉 ID、动作 ID 和站位 ID 都是参数域，不是 PEVT 新类型；语言的普通类型仍只有五种。

## 3. `.pactor` 文件

`.pactor` 是 UTF-8 XML 文件，不是可执行脚本。一个文件可以登记一个或多个人物，根元素固定为：

```xml
<ActorCatalog xmlns="urn:polaris:pevt:actors:v1"
              Version="1"
              Namespace="example.mod">
    ...
</ActorCatalog>
```

目录级属性：

| 属性 | 规则 |
| --- | --- |
| `Version` | 第一版只能为 `1`。 |
| `Namespace` | 非空、小写 ASCII 标识段，可包含 `.`；`aic` 仅允许 Polaris 内置目录使用。 |
| `BuiltIn` | 仅内置目录可写 `true`；模组文件不得声明。 |

人物元素：

```xml
<Actor Id="iris"
       DisplayName="Iris"
       DisplayKey="Talker_Iris"
       Voice="talk_iris"
       Color="#DCCAE7"
       Icon="MyMod.Resources.IrisIcon"
       DefaultPortrait="default">
    <WorldSprite Provider="polaris-res"
                 Resource="MyMod.Resources.IrisWorldPxls" />
    <Portrait Id="default"
              Provider="polaris-res"
              Resource="MyMod.Resources.IrisPortraitPxls" />
    <UiPortrait Id="default"
                Provider="polaris-res"
                Resource="MyMod.Resources.IrisUiImage" />
    <Appearance Id="neutral"
                Portrait="default"
                Pose="stand"
                Frame="neutral" />
</Actor>
```

- `Id` 是目录命名空间内唯一的局部 ID。
- `DisplayName` 与 `DisplayKey` 至少写一个；两者都有时优先查 `DisplayKey`，查不到再用 `DisplayName`。
- `Voice`、`Color`、`Icon` 可省略。颜色只接受 `#RRGGBB` 或 `#RRGGBBAA`。
- `DefaultPortrait` 必须引用同一人物下存在的 `Portrait.Id`；没有任何视觉资源的纯对话 profile 可以省略它。
- `WorldSprite` 最多一个；`Portrait`、`UiPortrait` 和 `Appearance` 的 `Id` 分别在人物内唯一。
- `Appearance` 是可读外观名到 PXLS pose/frame 的数据映射，不执行表达式，也不能调用 C#。
- 未登记 appearance 时，普通 PEVT 不得退回接受原版 `a_3/a0__...` 组合串；确需原串只能使用 `$raw cmd`。

### 3.1 视觉元素属性契约

`.pactor` 的全部数据都写在属性里；元素正文与 CDATA 一律拒绝，避免混入脚本或 CMD 文本。读取器只接受下表列出的属性，其余属性一律 `PEVT9101`。

| 元素 | 允许属性 |
| --- | --- |
| `ActorCatalog` | `Version`、`Namespace`、`BuiltIn` |
| `Actor` | `Id`、`DisplayName`、`DisplayKey`、`Voice`、`Color`、`Icon`、`DefaultPortrait`、`LegacyPerson` |
| `WorldSprite` | `Provider`、`Asset`、`Resource`、`Lifetime` |
| `Portrait` | `Id`、`Provider`、`Asset`、`Resource`、`Lifetime`、`LegacyPerson` |
| `UiPortrait` | `Id`、`Provider`、`Asset`、`Resource`、`Lifetime` |
| `Appearance` | `Id`、`Portrait`、`Pose`、`Frame` |
| `Anchor` | `Id`、`X`、`Y`、`EnterX`、`EnterY` |

资源属性按 provider 二选一，两者同时出现或都不出现都是 `PEVT9110`：

- `game-pxls` 只写 `Asset`，值是第 5 节 `GamePxlsId` 的 Bundle 逻辑路径，例如 `EvImg/__ev_n.pxls`。只接受由 `/` 分隔的相对路径段，拒绝绝对路径、盘符和 `..`。
- `polaris-res` 只写 `Resource`，值是至少两段的点分静态字段路径，例如 `MyMod.Resources.IrisPortraitPxls`。拒绝方法调用、泛型和索引语法。

`Lifetime` 可省略，只能是 `event` 或 `static`，默认 `event`：

- `event`：随事件借用，事件结束、替换或异常时释放。
- `static`：常驻资源，事件结束时不撤销借用。原版把 `__ev_n` 一类常驻立绘一直保留在内存中，与随事件加载释放的配角立绘清理边界不同，因此在目录里显式记录，不由运行时猜测。

`Actor.Icon` 没有自己的 `Provider` 属性，其提供者由目录来源决定：内置目录借用原版资源名（如 `IconNoel0`），自定义目录引用 `MImage` 静态字段。

局部 ID（`Actor.Id` 与 portrait / ui-portrait / appearance / anchor 的 `Id`）统一规则：以小写 ASCII 字母开头，其余为小写字母、数字、`-` 或 `_`，不以 `-` 结尾；违反时 `PEVT9105`。

`WorldSprite` 每个人物最多一个且没有 `Id` 属性，在模型中固定使用局部 ID `default`；重复声明 `PEVT9112`。

`Anchor` 的 `X`、`Y` 必填，`EnterX`/`EnterY` 必须同时声明或同时省略；四个值都必须是有限十进制数，`NaN`、`Infinity` 与溢出为无穷的字面量都是 `PEVT9116`。

### 3.2 增量扩展 sidecar（PEVT-E06）

一个模组常常只是想给别人已经登记好的人物多加几套外观，而不是重新定义那个人物。为此有一种独立的
sidecar 根格式，与 `.pactor` 同一个 XML 命名空间：

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

- `Actor` 必须是**已登记的最终公开人物 ID**（`<namespace>:<local-id>`）。扩展不新建人物，也不声明命名空间。
- 第一版只能**追加** appearance：不允许覆盖人物元数据（`DisplayName`、`Voice`、`Color`、`DefaultPortrait`、
  `LegacyPerson` …），也不允许声明 `Portrait`、`UiPortrait`、`WorldSprite`、`Anchor` 或嵌套 `Actor`。
- `Portrait` 必须是目标人物**已登记**的 portrait；扩展不能顺带带进一份新视觉资源。
- 同一份 sidecar 里同一个人物只能出现一段 `ActorExtension`；一段里至少要有一个 `Appearance`。
- 其余约束与 `.pactor` 完全一致（禁止 DTD、外部实体、未知元素与属性、元素正文），因为两者共用同一个读取器。

为什么把"只能追加"写死成格式而不是运行时策略：扩展一旦能覆盖，加载顺序就会改变已有内容的含义，
于是"这个 appearance 现在是什么"取决于哪个模组先加载。只追加的话，扩展的唯一效果是多出几个可用的
appearance ID——加载顺序不影响任何已有内容，卸载也只是把那几个 ID 拿掉，不需要"扩展前的快照"。

**应用时机与顺序。** 扩展可以指向别的程序集注册的人物，所以它不在提交时生效：全部基础目录登记完之后，
按提交顺序统一应用。因此

- "目标不存在"只有在全部目录都登记完之后才是确定的结论；
- 两个扩展抢同一个 appearance ID 时，先提交的留下、后来的被拒，同一次启动内结果稳定；
- 每条已应用的扩展带一个从 0 开始的 `#order`，卸载按逆序进行。

**来源追踪。** 每个被追加的 appearance 都记得自己来自哪个 owner、哪个 sidecar 路径与哪个内容哈希，
F8 的 Source 页面按人物列出"每个 appearance 来自基础目录还是某个扩展"，Ownership 页面按应用顺序列出
全部扩展与被拒内容。

**扩展目标处于跨来源冲突时。** 冲突的人物 ID 本来就查不到（见第 6 节），扩展它只会把"已经不可用"
变成"看起来可用"，因此这种情况按目标不存在处理（`PEVT9119`）。

## 4. 视觉提供者

`Provider` 第一版只有两类：

| Provider | 用途 | 可用位置 |
| --- | --- | --- |
| `game-pxls` | 借用游戏 `StreamingAssets` 内的原版 PXLS Bundle。 | 仅 Polaris 内置目录。 |
| `polaris-res` | 读取模组由 PolarisRes 自动绑定的资源字段。 | 自定义目录。 |

`polaris-res` 的 `Resource` 是 C# 静态字段引用：

- `WorldSprite` / `Portrait` 必须引用可访问的 `PxlsCharacterHandle` 静态字段；
- `UiPortrait` / `Icon` 必须引用可访问的 `MImage` 静态字段；
- 字段所属类必须有 `[PolarisResourceFolder]`，字段必须有 `[PolarisResource]`；
- 生成代码保存字段访问器，不在程序集扫描阶段读取字段值；首次演出时才解析，允许 PolarisRes 在游戏就绪后完成 PXLS 加载。

PolarisTools 的字段扫描复用 PUI `Image` 控件的项目定位、源码掩码、特性扫描、文件监视和失效引用保留规则，再扩展允许的资源类型。不得另写一套只靠文件名猜资源的扫描器。

## 5. 原版 PXLS 的 PolarisRes 桥

PolarisRes 增加只读的游戏资源入口，概念接口为：

```csharp
IGamePxlsLease BorrowGamePxls(GamePxlsId id);
```

- `GamePxlsId` 包含 Bundle 逻辑路径与 PXLS 名，不接受任意磁盘绝对路径。
- `EvImg/__ev_n.pxls`、`MapChars/sub_i.pxls`、`PxlNoel/noel.pxls` 等仍由原版 `MTI/MTRX/PxlsLoader` 链加载。
- PolarisRes 只把加载状态投影为统一的只读 PXLS handle；不复制 atlas，不把原版资源重新导出到模组目录。
- 借用句柄不拥有原版 Bundle、`PxlCharacter` 或 `MImage`，释放时只移除 PolarisEvent 自己的引用。
- 该桥可以复用资源加载类，但不能调用 `EV.readOneLine`、创建 `EvReader` 或提交 CMD 文本。
- 内置目录在启动时校验每个 `game-pxls` 目标可解析；缺失目标使对应人物视觉不可用，不影响其它人物登记。

## 6. 运行时注册

PolarisTools 为每个 `.pactor` 生成 `.g.cs`：

```csharp
[PevtActorAutoRegistration]
internal sealed class IrisActorRegistrar : IPevtActorRegistrar
{
    public void Register(PevtActorRegistrationContext context)
    {
        context.Register(/* 只含不可变数据和延迟资源访问器 */);
    }
}
```

- 生成器与运行时使用 Core 中同一个 XML reader 和验证器。
- `.g.cs` 不包含 XML 解析器、不加载文件、不执行任意方法名，也不生成 PEVT 源码。
- 注册上下文固定来源程序集、目录相对路径和内容哈希，注册器不能伪造所有者。
- 内置固定目录由 Polaris 自己嵌入并优先登记；外部目录随后登记。
- 同一程序集重复最终人物 ID 是构建错误；不同程序集重复是加载期致命冲突。
- 人物表与 `/event` 表分离。人物冲突不会覆盖事件，事件冲突也不会覆盖人物。

## 7. 原版短键分类

### 7.1 稳定人物键

`__vp_person.dat` 中的 18 个稳定说话人键全部写入内置目录；其中 `_` 是无 PXLS 的默认叙述者，其余 17 个键具有事件视觉资源：

| 原键 | 公开人物 ID | portrait | 原版 PXLS |
| --- | --- | --- | --- |
| `_` | `aic:narrator` | — | — |
| `n` | `aic:noel` | `default` | `__ev_n` |
| `nb` | `aic:noel` | `bass` | `__ev_n_bass` |
| `nb2` | `aic:noel` | `epbench` | `__ev_n_epbench` |
| `v` | `aic:laevi` | `default` | `__ev_v` |
| `p` | `aic:primula` | `default` | `__ev_p` |
| `i` | `aic:ixia` | `default` | `__ev_i` |
| `t` | `aic:nightingale` | `default` | `__ev_t` |
| `d` | `aic:tilde` | `default` | `__ev_d` |
| `l` | `aic:alma` | `default` | `__ev_l` |
| `f` | `aic:noel-father` | `default` | `__ev_f` |
| `g` | `aic:mepha` | `default` | `__ev_g` |
| `s` | `aic:ostrea` | `default` | `__ev_s` |
| `w` | `aic:walross` | `default` | `__ev_w` |
| `bt` | `aic:barten` | `default` | `__ev_bt` |
| `so` | `aic:tigrina` | `default` | `__ev_so` |
| `a` | `aic:alice` | `default` | `__ev_a` |
| `fh` | `aic:first-human` | `default` | `__ev_fh` |

### 7.2 临时键

原版 726 个 CMD 中的 `TALKER_REPLACE` 会创建或改写 `ann`、`b`、`bs*`、`cane`、`cm`、`cn`、`dev`、`dj*`、`fd`、`ff`、`fm`、`ixiacane`、`ma`、`mb*`、`mc`、`mob`、`noelcane`、`ow`、`pp`、`st`、`tc`、`x`、`xa`、`xb` 等临时键，也会临时改写部分稳定键。

- 同一个临时键在不同 CMD 中可以代表不同姓名和音效，因此不进入全局人物表。
- `$raw cmd` 桥为每次原版会话保存并恢复这些替换；PEVT 人物注册表不观察其变化。
- `.pactor` 的 Actor 或 Portrait `LegacyPerson` 仅允许内置固定目录使用，避免模组抢占原版键；Actor 级仅用于 `_` 这种无 portrait 的固定说话人。

## 8. 站位

- 普通 `@actor_enter` / `@actor_move` 使用 `left`、`center`、`right`、`near-left`、`near-right`、`far-left`、`far-right`、`off-left`、`off-right` 等语义锚点。
- 内置锚点由 PolarisEvent 直接保存坐标和入场方向，不在运行时把名字翻译回 `L/R/C/...` 后交给原版解释器。
- `.pactor` 可以用纯数值 `<Anchor Id="..." X="..." Y="..." EnterX="..." EnterY="..." />` 增加人物专用站位。
- 原版 58 个 `__vp_talker_pos.dat` 键保留在审计文档中供 `$raw cmd` 兼容；普通 PEVT 不追求逐个照搬晦涩组合键。

## 9. 指令解析

```text
@say("aic:noel", "早上好")
@actor_enter("aic:noel", "left", "neutral", 20)
@actor_appearance("aic:noel", "surprised")
```

运行顺序固定为：解析人物 ID、解析 appearance/anchor、等待资源、验证 pose/frame、创建或更新演出实例。任何一步失败都不得留下半创建的图层。

人物或视觉资源的存在性通常是运行期事实：跨模组人物可以晚于当前 `.pevt` 所在项目注册。因此普通 `.pevt` 静态分析只在能看到同项目 `.pactor` 时提供提示，不把跨项目未知人物 ID 当作编译错误。

## 10. 生命周期

- 原版 `game-pxls` 是借用资源，人物退场和事件结束只清 PolarisEvent 的显示实例。
- 自定义 `polaris-res` 资源由注册来源的 PolarisRes owner 持有；事件只持临时 lease/引用。
- 事件结束、替换、异常和插件卸载必须取消在途视觉加载并清除显示层。
- 人物目录卸载时，仍有事件引用该目录则先停止这些事件，再撤销人物注册。
- 任何来源的 PXLS 未 Ready 时都以 `PevtResourceWait` 等待，不阻塞 Unity 主线程。
- 增量扩展的卸载就是"重建时不再排入"：它只追加 appearance，撤销它不需要恢复任何被覆盖的内容。基础目录被卸载时，指向它的扩展自动退化成目标不存在。

## 11. 人物目录静态诊断

以下编号已经加入权威静态诊断表和 Core `DiagnosticCatalog`（功能阶段 B）。`PEVT9101`–`PEVT9116` 由共享的 `.pactor` 读取器发射；`PEVT9111`、`PEVT9117`、`PEVT9118` 由共享的资源字段绑定判定发射，取决于调用方能否解析到目标字段。

`PEVT9119`–`PEVT9121` 属于增量扩展（第 3.2 节）：`PEVT9121` 与形状相关，由扩展读取器发射；`PEVT9119` 与 `PEVT9120` 依赖"当前登记了哪些人物"，只能在目录合并时发射，因此它们随合并结果一起报告，而不是读取单个 sidecar 时就能得出：

| 编号 | 名称 | 含义 |
| --- | --- | --- |
| `PEVT9101` | `InvalidActorCatalogXml` | XML、根元素或命名空间非法。 |
| `PEVT9102` | `UnsupportedActorCatalogVersion` | Version 不支持。 |
| `PEVT9103` | `InvalidActorNamespace` | 目录 namespace 非法。 |
| `PEVT9104` | `ReservedActorNamespace` | 外部目录使用 `aic` 或 BuiltIn。 |
| `PEVT9105` | `InvalidActorId` | 人物局部 ID 非法。 |
| `PEVT9106` | `DuplicateActorId` | 同目录或同项目最终人物 ID 重复。 |
| `PEVT9107` | `MissingActorDisplayName` | DisplayName/DisplayKey 均缺失。 |
| `PEVT9108` | `InvalidActorColor` | 颜色格式非法。 |
| `PEVT9109` | `InvalidActorProvider` | provider 未登记或来源无权使用。 |
| `PEVT9110` | `InvalidActorResourceReference` | PolarisRes 字段引用非法。 |
| `PEVT9111` | `ActorResourceTypeMismatch` | 字段资源类型与视觉类型不符。 |
| `PEVT9112` | `DuplicateActorVisualId` | 同分类 visual/anchor ID 重复。 |
| `PEVT9113` | `MissingDefaultPortrait` | 默认 portrait 缺失或引用不存在。 |
| `PEVT9114` | `UnknownActorVisualReference` | appearance 引用不存在或 pose/frame 不完整。 |
| `PEVT9115` | `ForbiddenLegacyActorAlias` | 外部目录在 Actor 或 Portrait 声明 LegacyPerson。 |
| `PEVT9116` | `InvalidActorAnchor` | anchor 坐标非法或不完整。 |
| `PEVT9117` | `ActorResourceFieldNotBindable` | 字段特性、static 或可见性不满足自动绑定。 |
| `PEVT9118` | `ActorCatalogSourceUnavailable` | Warning；编辑器暂时无法读取字段或预览。 |
| `PEVT9119` | `UnknownActorExtensionTarget` | 扩展目标不是已登记的公开人物 ID，或该 ID 处于跨来源冲突状态。 |
| `PEVT9120` | `DuplicateActorExtensionAppearance` | 扩展追加的 appearance ID 与基础目录、另一个扩展或同段扩展内的条目重复。 |
| `PEVT9121` | `ForbiddenActorExtensionOverride` | 扩展声明了人物元数据或视觉元素；第一版只允许追加 appearance。 |

## 12. 非目标

- `.pactor` 不定义类、函数、条件、表达式或 C# 回调。
- 不允许配置任意 C# 类型名或方法名。
- 不把原版全部 pose/frame 自动猜成英文情绪；语义 appearance 必须显式登记。
- 增量扩展不是"第二种 `.pactor`"：它不能新建人物、不能声明命名空间、不能覆盖任何已有内容。
- 不允许普通人物指令退回拼接 `TALKER`、`PIC` 或其它 CMD 文本。
- 世界实体 AI、碰撞和战斗逻辑不属于人物目录；`WorldSprite` 只提供视觉资源，行为仍由受控 `@entity_*` 服务负责。
