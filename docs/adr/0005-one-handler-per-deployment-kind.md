# 0005: One handler per deployment kind

**Status:** Accepted, October 2026

## Context

Supporting a deployment kind (IIS, Windows service, file share, App Service, Docker, Kubernetes, custom script) meant branching on `DeploymentKind` in seven files: the deploy steps, the backup and rollback steps, the server-resource decision, three governance checks and the wizard. Adding a kind meant finding all of them, and a missed one gave half support that only tests or users would notice.

## Decision

- Each kind is one class implementing `IDeploymentKindHandler` (via `DeploymentKindHandler`, which supplies the defaults): deploy steps, backup and rollback steps, the properties the pipeline needs (`RunsOnServers`, `DeploysFromAgentOnly`, `NeedsRepositoryCheckout`, `SupportsSlotSwap`) and its own validation.
- `DeploymentKindRegistry` (`IDeploymentKinds`) holds the handlers, picks one for a kind, and owns the decisions that combine a kind with other settings: whether to use registered servers, and which backup and rollback apply (Custom uses the handler of the chosen rollback target; a custom rollback script replaces the built-in rollback).
- Handlers are registered in dependency injection; one registered later for the same kind replaces the built-in one.
- The wizard's per-kind fields stay in `DeploymentTargetStep.razor`: they are UI, and one list there is easier to read than one component per kind.

## Consequences

- A new kind is one handler, one line in the registry, and its wizard fields. `ArchitectureBaselineTests` counts the files that branch on `DeploymentKind` (now 1).
- `IDeploymentStepService` and `IRollbackYamlService` are gone; the deployment stage generator has one dependency fewer.
- The output is unchanged; the golden files prove it.
