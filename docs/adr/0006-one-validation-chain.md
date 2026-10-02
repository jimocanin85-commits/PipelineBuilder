# 0006: One validation chain of rules with ids

**Status:** Accepted, October 2026

## Context

Validation findings came from six places: a static validator for the settings, a governance service, a class that combined governance with the secrets scan, the Key Vault service, the variable group service and the parse check of the generated YAML. It was unclear where a new check belonged, two classes had almost the same name, and a finding could not be traced back to the check that produced it.

The generator also caught failures only to log and rethrow them, so the same error was logged twice.

## Decision

- All findings come from `PipelineValidator` (`IPipelineValidator`), an ordered chain of `ValidationRule`s.
- A rule has a stable id (`area.rule-name`), a stage and a check. Every finding carries its rule's id in `ValidationResult.RuleId`.
- Two stages: **input** rules find problems in the settings and block generation; **generated** rules judge the finished pipeline (governance policy, deployment advice, variable groups, plain-text secrets, Key Vault) and never block it.
- The built-in rules are listed, in the order their findings are shown, in `InputRules` and `PolicyRules`. Checks for one deployment kind stay in that kind's handler and run as the rule `deployment.kind` ([0005](0005-one-handler-per-deployment-kind.md)).
- Hosts add rules by registering a `ValidationRule` in dependency injection; they run after the built-in ones. Duplicate ids are rejected at start-up.
- The parse check (`GeneratedYamlValidator`) is not a rule: invalid YAML is a bug in PipelineBuilder, so it throws instead of producing a finding ([0002](0002-yaml-as-text-with-a-parse-check.md)).
- Core does not catch, log and rethrow. Failures surface as exceptions and the host logs them once.

## Consequences

- One place to call and one place to add a check. `PipelineDefinitionValidator`, `GovernanceValidator`, `GovernanceValidationService` and `IGovernanceValidationService` are gone.
- `WizardState` gets the validator injected instead of calling a static class.
- Rule ids make findings traceable in tests and logs, and open the door to documenting or switching off individual rules later.
- The messages, their order and the generated YAML are unchanged.
