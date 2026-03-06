# Zeayii.Luma.Generators

简体中文 | [English](./README.en.md)

Generators 模块是官方示范生成器，用于展示命令模块自动挂载模式。

## 模块定位

1. 编译期发现 `ILumaCommandModule` 类型。
2. 生成子命令挂载扩展函数。
3. 生成执行委托代码并转发到 `LumaCommandExecutor`。

## 边界说明

1. 本模块不参与 NuGet 对外交付。
2. 外部私有项目可以选择手写命令挂载，不强制依赖生成器。
3. 生成器仅解决样板代码问题，不承担业务解析职责。
