# Zeayii.Luma.Abstractions

简体中文 | [English](./README.en.md)

本模块定义 Luma 的稳定契约层。

## 职责

1. 定义站点入口契约：`ISpider`。
2. 定义节点生命周期基类：`LumaNode`。
3. 定义共享模型：
- `LumaRequest`（持有 `HttpRequestMessage`） / `HttpResponseMessage`
- `NodeResult` / `NodeExecutionOptions`
- `LumaNodeContext` / `LumaNodeResources`（Context 对外暴露解析与 Cookie 能力函数）
- `PersistResult` / `PersistContext`

## 设计原则

1. 契约稳定优先。
2. 不放入具体下载、调度实现。
3. 不放入 provider 专属业务模型。
4. 让业务只面向 Node 编程。

## 不应放入本模块的内容

1. 下载器实现。
2. 调度器实现。
3. 命令行宿主流程。
4. 终端展示逻辑。
