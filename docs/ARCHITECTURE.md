# How the code is organized

Two projects:

- **Core** builds and checks the pipeline. It has no web code.
- **Web** is the wizard (Blazor Server) and the Windows login.

```
Web  →  Core
        ├─ Generators/    the build, deploy and notify stages
        ├─ Deployment/    one class per deployment type (IIS, Linux service, Docker, ...)
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
3. Add its fields in `TargetStep.razor`, and a template in `templates.json`.

A script that runs on a server is created with `ServerScript.Step`. It becomes an ordinary step when an agent on the server deploys, and is sent over WinRM or SSH when the build agent deploys. Write the script once; do not handle the two cases yourself.

## Generated scripts

The scripts in the generated pipeline are checked in two ways:

- `GeneratedScriptSyntaxTests` parses every script: PowerShell with PowerShell's parser (and only what Windows PowerShell 5.1 can run), bash with `bash -n`. It runs with the other tests.
- The tests in `RealScripts/` run the scripts on a Linux and a Windows machine: a systemd service, Docker, SSH, a Windows service, WinRM and IIS, including a failed deployment that is rolled back. The **Real scripts** workflow runs them. In an ordinary test run they are skipped.

Azure DevOps itself is not part of the tests. Try a new kind of pipeline in a test project first.

## Add a check

Add one line and one method in `Validation/InputRules.cs` (blocks generation) or `Validation/PolicyRules.cs` (advice and governance).

## Golden files

`tests/PipelineBuilder.Tests/Golden/` holds the approved YAML for every template. A test fails when the output changes.

If the change is intended, push a commit with `[update-golden]` in its message. A workflow updates the files, and you review the diff in the pull request.
