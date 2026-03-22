# Luma Engine 并发与取消语义

## 1. 结构化并发

- 每个节点运行时由 `LumaNodeRuntime` 承载。
- 父节点通过 `AddChild` 形成子树，并发上限由父节点 `NodeExecutionOptions` 控制。
- 子节点并发槽在“子节点子树完成”后释放，不在“子节点注册完成”时释放。

## 2. 取消传播（严格单向）

- 根节点：`Token = RunToken`
- 子节点：`Token = Linked(ParentToken)`
- 语义保证：
  - 父节点取消 => 子树全部可取消
  - 子节点取消 => 不反向取消父节点

## 3. 子树完成定义

节点子树完成需要同时满足：

- 当前节点无排队请求
- 当前节点无活跃请求
- 无子节点注册进行中
- 无节点初始化进行中
- 直接子节点子树全部完成

完成后会设置 `SubtreeCompletionTask`，引擎以根节点子树完成作为主结束条件之一。

## 4. 运行结束条件

运行结束需满足：

- 根节点 `SubtreeCompletionTask` 完成
- 请求/下载队列为空
- 活跃网络计数为 0

持久化管线随后进入收尾阶段并完成批次刷新。

## 5. 流控策略可插拔

- 节点流控由 `INodeRequestFlowControlStrategy` 描述。
- 引擎支持通过 `LumaEngineOptions.NodeFlowControlStrategyResolver` 注入策略解析器。
- 未注入时使用框架默认策略解析逻辑。
