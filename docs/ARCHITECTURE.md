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

## Security rules

`SecurityTests` and `DeployFromAgentTests` fail the build when one of these is broken.

1. **A value in a script is quoted.** Use `YamlBuilder.PsLiteral`, `YamlBuilder.BashLiteral` or `ServerScript.PathLiteral`. Never put a setting into a script as it is.
2. **Text from the run is not pasted into a script.** A build can change its own number, and anyone can name a branch. Read such text from the environment (`$env:BUILD_BUILDNUMBER`), not with `$(Build.BuildNumber)`.
3. **A secret reaches a script through `env:`**, never as part of the script text.
4. **A pull request is not deployed.** Every deploy and notify stage has `DeploymentStageGenerator.NotPullRequest` in its condition.
5. **Nothing goes in a shared folder on a server** (`/tmp`, `C:\ProgramData`). Use `ServerScript.RemotePackageFolder`.
6. **No script is written into a page.** The Content-Security-Policy (`SecurityHeaders`) only runs files from `wwwroot/js`.

## Generated scripts

The scripts in the generated pipeline are checked in two ways:

- `GeneratedScriptSyntaxTests` parses every script: PowerShell with PowerShell's parser (and only what Windows PowerShell 5.1 can run), bash with `bash -n`. It runs with the other tests.
- The tests in `RealScripts/` run the scripts on a Linux and a Windows machine: a systemd service, Docker, SSH, a Windows service, WinRM and IIS, including a failed deployment that is rolled back. The **Real scripts** workflow runs them. In an ordinary test run they are skipped.

The **browser** job in CI opens the published app in Chrome and clicks through the wizard (`tests/browser/wizard.mjs`).

The **CodeQL** workflow scans the code for security mistakes on every pull request and once a week. Its findings are under Security → Code scanning on GitHub.

Azure DevOps itself is not part of the tests. Try a new kind of pipeline in a test project first.

## Add a check

Add one line and one method in `Validation/InputRules.cs` (blocks generation) or `Validation/PolicyRules.cs` (advice and governance).

## Golden files

`tests/PipelineBuilder.Tests/Golden/` holds the approved YAML for every template. A test fails when the output changes.

If the change is intended, push a commit with `[update-golden]` in its message. A workflow updates the files, and you review the diff in the pull request.
