# Zeayii.Luma.Abstractions

[简体中文](./README.md) | English

This module defines the stable contract layer of Luma and is a required dependency for private consumers.

## Responsibilities

1. Defines command-module contract `ILumaCommandModule`.
2. Defines crawl contracts `ISpider` and node base type `LumaNode`.
3. Defines shared request/response/parse-output/persistence models.

## Consumer Guidance

1. External projects should reference this module directly instead of duplicating interfaces.
2. Provider-specific implementations should stay in private projects.
3. Public model changes must remain backward compatible.

## What Must Not Be Added Here

1. Concrete downloader implementations.
2. Concrete scheduler implementations.
3. CLI parameter binding and host lifecycle logic.
4. Terminal rendering logic.
