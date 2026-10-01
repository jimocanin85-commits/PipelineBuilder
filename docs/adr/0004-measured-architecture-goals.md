# 0004: Architecture goals are measured and ratcheted in tests

**Status:** Accepted, October 2026

## Context

Architecture goals written only in a document drift: nobody notices when the code moves away from them, or when it reaches them.

## Decision

- Each goal in [ARCHITECTURE.md](../ARCHITECTURE.md#goals) has a measurement.
- Rules that already hold are tests that must pass (`ArchitectureTests`).
- Goals not met yet are ratchets in `ArchitectureBaselineTests`: the test holds today's number and fails if the number grows, and also if it shrinks without the baseline being lowered. Baselines are only ever lowered.
- Coverage minimums live in CI and are only ever raised.

## Consequences

- Progress on a goal is a one-line change to a baseline in the same pull request that makes it.
- Measurements are deliberately simple (file counts, reflection) so they are cheap and stable. They indicate the goal; they do not replace review.
