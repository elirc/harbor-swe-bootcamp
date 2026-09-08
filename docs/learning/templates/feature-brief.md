# Feature brief

## Problem

Who needs what behavior, and what currently prevents it? Give a concrete example.

## Contract and acceptance criteria

Describe input, output, permissions, validation, boundaries, and empty results.
List observable examples before naming implementation classes.

## Proposed approach

Which responsibilities change? What existing behavior must remain compatible?
Identify database reads, writes, side effects, and any schema migration.

## Alternative considered

Describe one reasonable alternative and the evidence supporting your choice.
State a condition that would cause you to reconsider.

## Failure and concurrency behavior

What happens on retry, cancellation, stale data, or a crash? What commits together?
Which outcomes require operator or client recovery?

## Validation

Name the highest-value tests and measurements. After implementation, record the
actual commands and results separately from what you merely planned to run.

## Adoption and operation

How do clients use the change? How is it deployed, observed, and reversed if
needed? For a local-only exercise, say which production questions remain open.
