# PolarisEvent

Polaris 的 PEVT 语言前端与事件运行时组件，包含文本、语法、绑定、控制流、诊断和内置事件内容。

- `doc/design/`：正在进行的 PolarisEvent / PEVT 设计与阶段契约。
- `tests/PolarisEvent.Tests/`：无游戏依赖的语言前端测试。
- `tests/Polaris.IntegrationTests/`：PEVT 宿主程序集边界与跨模块集成测试。

PEVT 专属测试全部随 PolarisEvent 仓库维护；聚合仓库仅从解决方案引用这些测试项目。

运行时目标依赖同级 `PolarisCore`；该仓库由 [Polaris](https://github.com/New-Cradle-of-Stella/Polaris) 聚合仓库作为 Git submodule 引用。
