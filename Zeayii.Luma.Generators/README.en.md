# Zeayii.Luma.Generators

[简体中文](./README.md) | English

The Generators module is an official sample generator that demonstrates automatic command-module wiring.

## Module Positioning

1. Discovers `ILumaCommandModule` implementations at compile time.
2. Emits subcommand wiring extension methods.
3. Emits execution forwarding code to `LumaCommandExecutor`.

## Boundary Notes

1. This module is not shipped as a public NuGet package.
2. Private projects may wire commands manually and do not need to depend on the generator.
3. The generator only removes boilerplate; it does not own provider business logic.
