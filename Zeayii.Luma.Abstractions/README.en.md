# Zeayii.Luma.Abstractions

[简体中文](./README.md) | English

This module defines the stable public contracts of Luma.

## Responsibilities

1. Defines provider entry contract: `ISpider<TState>`.
2. Defines node lifecycle base type: `LumaNode<TState>`.
3. Defines shared models:

- `LumaRequest` (wrapping `HttpRequestMessage`) / `HttpResponseMessage`
- `NodeDispatchBatch` / `NodeExecutionOptions`
- `LumaContext<TState>` (Context exposes parsing and Cookie capability functions)
- `PersistResult` / `PersistContext`

## Design Principles

1. Contract stability first.
2. No concrete downloader/scheduler implementations.
3. No provider-specific business models.
4. Provider code should program against Node lifecycle only.

## What Must Not Be Added Here

1. Downloader implementations.
2. Scheduler implementations.
3. CLI host workflow.
4. Terminal rendering logic.
