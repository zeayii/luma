# Zeayii.Luma.Presentation

[简体中文](./README.md) | English

The Presentation module handles terminal observability and is an optional dependency for private consumers.

## Responsibilities

1. Render runtime progress snapshots.
2. Render runtime log snapshots.
3. Keep a unified terminal output style.

## Boundary Constraints

1. Does not make crawling decisions.
2. Does not schedule requests or run downloads.
3. Only consumes runtime events published by Engine.

## Consumer Guidance

1. Reference this module when unified TUI/console experience is needed.
2. If dashboard output is not needed, `Abstractions + Engine` is sufficient.
