# PEVT 嵌入注册与 ID 冲突规范

## 1. 分发模型

`.pevt` 不作为模组外置文件交给游戏，也不预编译为 `.cmd` 或 PEVT 字节码。PolarisTools 在保存/构建时完成静态校验，然后把原始 PEVT 源文本压缩为文本载荷，写入与模组一起编译的生成 `.g.cs` 文件。

```text
Foo.pevt
    ↓ PolarisTools：静态校验
保留原始换行的 UTF-8 源文本
    ↓ GZip 压缩
Base64 文本串
    ↓ 生成 Foo.pevt.g.cs
与模组 C# 一起编译进程序集
    ↓ 游戏加载模组
生成注册器提交压缩载荷
    ↓ PolarisEvent 解压、校验、解析
写入运行时虚拟 `/event/<id>.pevt`
```

压缩只是包装方式，不改变 PEVT 的解释型语义。游戏侧仍使用语言核心对解压后的原始源文本执行完整加载校验。

## 2. 嵌入包结构

每个生成的 PEVT 嵌入包至少包含：

| 字段 | 含义 |
| --- | --- |
| `FormatVersion` | 嵌入包格式版本，与 PEVT 语法版本分开。 |
| `Compression` | 压缩算法；第一版固定为 `gzip-base64-v1`。 |
| `DeclaredId` | 工具侧从 `id "..."` 读取的事件 ID。 |
| `SourcePath` | 项目相对 `.pevt` 路径，不嵌入开发机绝对路径。 |
| `UncompressedLength` | 未压缩 UTF-8 字节数，用于损坏与解压膨胀检查。 |
| `ContentHash` | 未压缩 UTF-8 字节的 SHA-256。 |
| `Payload` | GZip 后再 Base64 的源文本载荷。 |

源文本使用 UTF-8 无 BOM 字节压缩，保留源文本中现有的 CRLF 或 LF，不在生成阶段格式化或标准化换行。

Base64 可以在生成 C# 中分成多个常量片段，但拼接后必须与单一载荷字节一致。

## 3. 生成注册器

每个生成类只负责提交嵌入包，不在模组程序集中嵌入语法树、中间指令或可执行协程。

概念形状：

```csharp
[PevtAutoRegistration]
internal sealed class OpeningPevtRegistrar : IPevtRegistrar
{
    public void Register(PevtRegistrationContext context)
    {
        context.Register(new PevtEmbeddedSource(
            formatVersion: 1,
            compression: "gzip-base64-v1",
            declaredId: "Opening",
            sourcePath: "Events/Opening.pevt",
            uncompressedLength: 1234,
            contentHash: "...",
            payload: Payload0 + Payload1));
    }
}
```

- 生成类不能伪造来源程序集；`PevtRegistrationContext` 由扫描器创建并固定所有者。
- 一个注册器可以提交一个或多个嵌入包，但每个 `.pevt` 仍只声明一个事件。
- 调用方无法从 `DeclaredId` 绕过源码 `id`；运行时必须二次验证两者完全一致。

## 4. `/event` 运行时空间

`/event` 是 PolarisEvent 管理的内存虚拟事件空间，不是原版 `StreamingAssets/evt`、不是 `.cmd` 目录，也不是运行时的权威磁盘缓存。

事件存在时的虚拟路径：

```text
/event/<id>.pevt
```

- `<id>` 就是源文件的 `id` 值，不再额外拼接程序集名或模组命名空间。
- ID 区分大小写，虚拟索引使用 `StringComparer.Ordinal`。
- 每个入口保存解压后源信息、程序定义、所有者程序集、内容哈希和加载诊断。
- `callevt "ID"` 在真正执行时查询该空间。
- 可以提供把解压源文本导出到磁盘的调试功能，但导出文件不参与事件解析或冲突判定。

## 5. 加载流程

每个嵌入包按以下顺序处理：

1. 验证包格式版本和压缩算法。
2. Base64 解码并在配置上限内 GZip 解压。
3. 验证未压缩长度和 SHA-256。
4. 以严格 UTF-8 解码为 PEVT 源文本。
5. 使用共享语言核心执行完整静态校验。
6. 验证源码 `id` 与嵌入包 `DeclaredId` 完全一致。
7. 将候选定义交给 ID 冲突守卫。
8. 无冲突时写入 `/event/<id>.pevt`。

任一校验失败时，该事件不进入可执行表；运行时保留来源程序集、相对源路径和具体诊断，但不尝试降级为原版 EV。

## 6. ID 冲突规则

PEVT ID 是 `/event` 空间的全局身份，不使用现有 `namespace + logicalId` 合成运行键。

### 6.1 不同程序集的重复 ID

- 两个不同模组程序集注册相同 ID 时是致命冲突。
- 先注册的定义临时保留，后注册的定义忽略，使同一次启动内的结果稳定。
- 冲突不因两份源文本或哈希目前相同而豁免。
- 扫描期间收集全部冲突，扫描结束时汇总为一条致命报告，同时列出所有 ID 和涉及程序集。
- 扫描封闭后出现的新冲突立即单独上报。

### 6.2 同一程序集内的重复 ID

- PolarisTools 在能看到同项目全部 `.pevt` 文件时，应把重复 ID 作为构建期静态错误。
- 若单文件生成、手工注册或旧生成物导致运行时仍发生同程序集重复，行为与 Polaris Lang 一致：记录警告，后注册的定义覆盖先前定义。
- 警告必须同时列出两个项目相对源路径，避免只知道程序集名而无法找到重复文件。

### 6.3 比较口径

- ID 比较使用大小写敏感的序数比较。
- `Opening` 与 `opening` 是两个不同 ID。
- 中文字符使用源码中的 Unicode 序列直接比较，不执行隐式 Unicode 标准化。

## 7. 冲突守卫

PEVT 冲突守卫沿用 Polaris Lang 的运行结构：

```text
PevtRegistryScanner 逐个扫描生成注册器
    ↓ 设置 CurrentSourceAssembly
PevtRuntime.Register(...)
    ↓ 发现跨程序集重复
PevtConflictGuard.Record(...)
    ↓ 扫描结束
PevtConflictGuard.Seal()
```

每条冲突记录至少包含：

- 冲突事件 ID；
- 保留方程序集与相对源路径；
- 忽略方程序集与相对源路径；
- 两份内容哈希；
- 两个模组的可识别名称。

冲突报告必须把两个程序集都列为责任方，并告知作者修改其中一个 `.pevt` 的 `id`。

## 8. 完整性与安全限制

- Base64 解码、GZip 解压、UTF-8 解码、长度或哈希失败时，嵌入包视为损坏，不进入 `/event`。
- 解压前同时限制 Base64 载荷长度和声明的未压缩长度；解压过程中仍必须使用硬上限，不信任包内长度。
- 语法错误不因构建时已经校验过而略过运行时校验。
- 嵌入包格式版本不支持时必须明确报错，不根据载荷内容猜测格式。
- 嵌入源文本视为模组的受信任内容，但仍受 PEVT 语法、`enable cs`、`@` 注册表和运行预算限制。

## 9. 与现有实现的关系

- 现有 `IPolarisEventRegistrar` 可重构为 `IPevtRegistrar`，但注册参数从 `commandText` 改为 `PevtEmbeddedSource`。
- 现有 `PolarisEventRegistryScanner` 可保留“扫描带特性生成类”的总体形状。
- 现有 `PolarisEventConflictGuard` 与 Lang 冲突守卫相似，可改为直接以全局 PEVT ID 判定，不再以 `%polaris/<namespace>/<logicalId>` 为冲突键。
- 现有 `PolarisEventRuntime.EnsureInstalled` 的磁盘 `.cmd` 写入逻辑应删除，替换为嵌入源的解压、校验和 `/event` 内存登记。
- `Patch_EV_getEventContent` 不参与 PEVT 嵌入源加载；它只能在 `$raw cmd` 桥确实需要时作为原版 EV 兼容实现的一部分。

## 10. 人物目录注册空间

`.pactor` 使用独立于 `/event` 的全局人物空间。最终键为 `<Namespace>:<Actor.Id>`，例如 `aic:noel`、`example.mod:iris`。

- 人物比较同样使用 `StringComparer.Ordinal`，但人物 ID 不受 `.pevt` 事件 ID 的字符规则约束。
- Polaris 内置 `AliceInCradle.BuiltinActors.pactor` 先于外部程序集登记，并封闭 `aic` 命名空间。
- 同一项目的重复最终人物 ID 由 PolarisTools 报 `PEVT9106` 并拒绝生成有效注册器。
- 不同程序集重复人物 ID 是加载期致命冲突，记录为 `PEVTR4404`；先注册项仅为稳定报告而临时保留，冲突人物不能用于新事件。
- 人物冲突报告包含最终人物 ID、两个程序集、两个 `.pactor` 相对路径和目录哈希。
- 人物冲突守卫与事件冲突守卫分开 Seal 和查询，避免相同字符串在两个空间中互相影响。

`.pactor` 的 `.g.cs` 只提交已验证不可变数据与强类型延迟资源访问器，不提交可执行 XML、任意 C# 方法名或原版 CMD 文本。详细格式见 `PEVT-人物目录与原版别名规范.md`。
