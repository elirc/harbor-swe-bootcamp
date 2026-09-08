# 0010: Share conversation membership across list and overview

## Context

The conversation list owned all filtering inside its action and called a static
helper on `SegmentsController` for segment membership. A filtered queue overview
needs the same population but a different result shape. Copying the conditions
would make future changes easy to apply to only one endpoint.

## Decision

Extract membership into the concrete `ConversationQueries` component in the API
project. It consumes the API filter contract, returns a no-tracking `IQueryable`,
accepts one evaluation time and cancellation token, and resolves segments within
the workspace. A null result means an unavailable segment; controllers retain
the existing 422 response. Move segment rule/query helpers to Infrastructure.

The list applies a named ordering with an ID tie-breaker, then pagination. The
overview performs one SQL aggregate over all matches and returns zeros for an
empty population. Creation windows are inclusive/exclusive. Both actions use
the same membership logic and existing authorization conventions.

## Consequences

Filter semantics have one implementation and HTTP tests cover both consumers
against independent expected sets. The new component depends on EF and API
contracts; it is intentionally not a provider-independent repository abstraction.
No persistence model change or migration is required.

Offset pagination can still shift under concurrent writes, and list count/page
are separate statements. Counts can scan large populations. The older reports
have a separate contract and remain outside this extraction. No new index is
added without workload evidence. See the [worked lesson](../learning/worked-feature.md).
