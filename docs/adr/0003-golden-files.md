# 0003: Golden files for generated pipelines

**Status:** Accepted, October 2026

## Context

Most tests check selected lines of the generated YAML. An unintended change elsewhere in the file can pass them, and the planned refactoring (phases 2 and 3 in [ARCHITECTURE.md](../ARCHITECTURE.md#goals)) must not change the output at all.

## Decision

- `tests/PipelineBuilder.Tests/Golden/` holds the complete generated YAML for the wizard defaults and for each built-in template.
- `GoldenFileTests` compares the output byte for byte (line endings normalised) and reports the first differing line.
- The files are regenerated, never edited by hand: `PIPELINEBUILDER_UPDATE_GOLDEN=1 dotnet test`, or the **Update golden files** workflow, which commits them to the branch. The diff is reviewed in the pull request.
- A new built-in template gets a golden file automatically; a file without a test case fails `EveryGoldenFileHasACase`.

## Consequences

- Any change in output is visible in review, as YAML.
- A change that affects many pipelines touches many golden files. That is intended: it shows the reach of the change.
- Commits made by the workflow do not start CI by themselves; push again or re-run CI to test them.
