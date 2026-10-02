# How the code is organised

Two projects:

- **Core** builds and checks the pipeline. It has no web code.
- **Web** is the wizard (Blazor Server) and the Windows login.

```
Web  →  Core
        ├─ Generators/    the build, deploy and notify stages
        ├─ Deployment/    one class per deployment type (IIS, Docker, ...)
        ├─ Validation/    the checks
        ├─ Services/      smaller YAML pieces (artifacts, health checks, ...)
        └─ Models/, Yaml/ the settings and YAML helpers
```

## Rules

1. Core never uses Web or ASP.NET.
2. `Models/` and `Yaml/` don't use the other Core folders.
3. The wizard gets Core services through the interfaces in `Abstractions/`.
4. A class takes at most 5 other services.

`ArchitectureTests` fails the build when a rule is broken.

## Add a deployment type

1. Add the value to the `DeploymentKind` enum.
2. Add a class in `Deployment/` and list it in `DeploymentKindRegistry`.
3. Add its fields in `DeploymentTargetStep.razor`.

## Add a check

Add one line and one method in `Validation/InputRules.cs` (blocks generation) or `Validation/PolicyRules.cs` (advice and governance).

## Golden files

`tests/PipelineBuilder.Tests/Golden/` holds the approved YAML for every template. A test fails when the output changes.

If the change is intended, push a commit with `[update-golden]` in its message. A workflow updates the files, and you review the diff in the pull request.
