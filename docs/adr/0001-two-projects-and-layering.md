# 0001: Two projects, layered, Core without ASP.NET

**Status:** Accepted, October 2026

## Context

PipelineBuilder generates Azure DevOps YAML from a set of choices. The generation logic is the valuable part; the wizard is one way to drive it, and tests (or a future command-line tool) are others.

## Decision

- **PipelineBuilder.Core** holds the model, the generators, validation and templates. It depends only on .NET, the DI and logging abstractions, and YamlDotNet.
- **PipelineBuilder.Web** is the Blazor Server wizard and the hosting (IIS, Windows login). It uses Core through the interfaces in `PipelineBuilder.Core.Abstractions`.
- Inside Core, dependencies point down: application and services → foundation (`Models`, `Enums`, `Yaml`).
- It stays one deployable app. No separate API, no database.

## Consequences

- Core can be tested without a browser or web server, and most tests do.
- The rules are checked by `ArchitectureTests`, so a reference in the wrong direction fails the build instead of being found in review.
- Web still calls a few static helpers in Models directly (settings file, step validation). That is allowed; a single facade is a phase 2 goal.
