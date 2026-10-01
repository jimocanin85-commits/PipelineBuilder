# Architecture

PipelineBuilder is one Blazor Server app with two projects: **Core** generates and validates pipelines, **Web** is the wizard and the hosting. Core knows nothing about Web, and the tests enforce that.

This page describes the architecture as it is, the rules that hold it together, and the goals it is moving towards. Larger decisions are recorded as [architecture decision records](adr/README.md).

## Layers

```
Web (Blazor Server)        Components/Steps, State (WizardState), Security (Windows login)
  │  injects only PipelineBuilder.Core.Abstractions
  ▼
Core · application         PipelineGeneratorService: validate → build YAML → parse YAML → governance
  │
  ▼
Core · stage generators    Generators/ (build, deployment, notification, governance)
and services               Services/ (artifact, rollback, health check, IaC, Key Vault, ...)
  │
  ▼
Core · foundation          Models/, Enums/, Yaml/ (YamlBuilder, GeneratedYamlValidator, PipelineTaskInventory)
                           Templates/templates.json
```

Dependencies point down only.

| Layer | Namespaces | May use |
|---|---|---|
| Web | `PipelineBuilder.Web.*` | Core through `PipelineBuilder.Core.Abstractions` (injected), plus the static helpers in Models (settings file, validation) |
| Application, generators, services | `PipelineBuilder.Core.Services`, `.Generators`, `.Abstractions`, `.DependencyInjection` | Each other and the foundation |
| Foundation | `PipelineBuilder.Core.Models`, `.Enums`, `.Yaml` | Only each other and .NET |

## How a pipeline is generated

1. The wizard edits a `PipelineDefinition` held by `WizardState`, and validates each step with `PipelineDefinitionValidator.ValidateDetailed`.
2. `IPipelineGeneratorService.Generate` validates the definition again and assembles the YAML from the stage generators.
3. `GeneratedYamlValidator` parses the result. YAML that Azure DevOps would reject is never handed out ([ADR 0002](adr/0002-yaml-as-text-with-a-parse-check.md)).
4. Governance and Key Vault checks add findings, and the result is returned with explanations and the optional agent diagnostics script.

## Rules and where they are enforced

| Rule | Enforced by |
|---|---|
| Core does not reference ASP.NET Core or Web | `ArchitectureTests.CoreDoesNotReferenceAspNetCoreOrWeb`, `CoreSourceDoesNotUseWeb` |
| The foundation does not depend on higher layers | `ArchitectureTests.FoundationDependsOnlyOnFoundation`, `FoundationSourceDoesNotImportHigherLayers` |
| Components inject Core only through `Core.Abstractions` | `ArchitectureTests.WebComponentsInjectOnlyCoreAbstractions` |
| A class has at most 5 constructor dependencies (two known exceptions are listed in the test) | `ArchitectureTests.ClassesHaveAtMostFiveConstructorDependencies` |
| The generated pipelines change only on purpose | `GoldenFileTests` ([ADR 0003](adr/0003-golden-files.md)) |
| Coverage does not drop | CI: `MIN_LINE_COVERAGE` and `MIN_BRANCH_COVERAGE` in `.github/workflows/ci.yml` |
| No new warnings | `TreatWarningsAsErrors` in `Directory.Build.props` |

## Golden files

`tests/PipelineBuilder.Tests/Golden/` holds the approved YAML for the wizard defaults and for every built-in template. When you change the generated output on purpose:

- locally: `PIPELINEBUILDER_UPDATE_GOLDEN=1 dotnet test --filter GoldenFileTests` (PowerShell: `$env:PIPELINEBUILDER_UPDATE_GOLDEN=1`), or
- on GitHub: push a commit with `[update-golden]` in its message, or run **Update golden files** from the Actions tab on your branch. The workflow commits the new files to the branch.

Then review the YAML diff in the pull request like any other code.

## Goals

The architecture is moving towards one entry point into Core and one class per deployment kind, so that a new kind no longer means changes across generators, validation and the wizard. The goals are measured; the ones not met yet are ratchets in `ArchitectureBaselineTests`, which fail both when a number grows and when it improves without the baseline being lowered.

| # | Goal | Measured by | Baseline (Oct 2026) | Target | Phase |
|---|---|---|---|---|---|
| M1 | Layering is enforced automatically | `ArchitectureTests` | Done | CI fails on a violation | 1 |
| M2 | Whole pipelines are kept as golden files | `GoldenFileTests` | Done | Defaults and all 8 templates | 1 |
| M3 | Coverage cannot drop | CI thresholds | Done | Line ≥ 90 %, branch ≥ 80 % | 1 |
| M4 | The architecture is written down | This page and [ADRs](adr/README.md) | Done | | 1 |
| M5 | A new deployment kind is one class | Files that branch on `DeploymentKind` | 8 | 2 | 2 |
| M6 | Validation has one entry point | Places that produce validation findings | 6 | 1 chain of rules | 2 |
| M7 | No class is a hub | Constructor dependencies | Up to 9 | At most 5 | 2 |
| M8 | Errors are logged once | catch-log-rethrow blocks in Core | 1 | 0, Web logs at the boundary | 2 |
| M9 | The domain model is immutable | Public setters on `PipelineDefinition` | 25 | 0 | 3 |
| M10 | Wizard state is separate from the forms | Lines in `WizardState`, text proxies | 246, 8 | Under 150, 0 | 3 |
| M11 | Indentation is handled in one place | Hand-written indentation in generators | 80 `Append` calls | 0, via a `YamlWriter` | 3 |
| M12 | Interfaces only where there is variation | Core interfaces with one implementation | 17 | Facade, handlers and rules only | 3 |

Phase 1 adds the safety net and changes no production code. Phases 2 and 3 change structure, not output; the golden files prove it.

**Not goals:** splitting into several services, a database, or a full YAML object model.
