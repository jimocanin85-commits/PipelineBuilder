# 0002: Build YAML as text and parse the result

**Status:** Accepted, October 2026

## Context

Azure DevOps pipelines mix YAML structure with template expressions (`${{ }}`), macros (`$(...)`) and multi-line PowerShell. Generating them through a YAML object model makes that awkward to control and harder to read than the YAML itself.

## Decision

- Generators write YAML as text, through the helpers in `YamlBuilder` (quoting, PowerShell steps).
- Every generated pipeline is parsed by `GeneratedYamlValidator` before it is returned. If it doesn't parse or lacks the structure Azure DevOps needs, generation throws: that is a bug, not a user error.
- We do not introduce a full YAML object model.

## Consequences

- Generated YAML reads like hand-written YAML, and the code that produces it is easy to compare with the output.
- Indentation is written by hand in many places, and mistakes are caught only when the whole file is parsed. Goal M11 moves indentation into one `YamlWriter`; golden files ([0003](0003-golden-files.md)) catch changes that still parse.
