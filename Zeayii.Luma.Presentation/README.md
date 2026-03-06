# Zeayii.Luma.Presentation

简体中文 | [English](./README.en.md)

Presentation 模块负责终端可观测输出，建议外部项目按需依赖。

## 职责

1. 输出运行进度快照。
2. 输出运行日志快照。
3. 维护统一终端展示风格。

## 边界约束

1. 不参与抓取行为决策。
2. 不参与请求调度与下载执行。
3. 只消费 Engine 发布的运行态事件。

## 外部项目使用建议

1. 需要统一 TUI/控制台风格时依赖本模块。
2. 不需要终端仪表盘时可仅依赖 `Abstractions + Engine`。
