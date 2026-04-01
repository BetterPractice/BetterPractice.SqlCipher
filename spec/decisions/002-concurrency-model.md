# ADR 002 — Concurrency Model

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

BetterPractice.SqlCipher is intended for use in server components where multiple callers may interact with the same database concurrently (e.g., via MCP tool calls from a local AI model). SQLite connections and prepared statements are not thread-safe. The library must enforce thread safety by default — callers must not need to perform any external synchronization.

Two models were evaluated:

**Model A — Full serialization via Channel:** A single SQLite connection processes all operations (reads and writes) one at a time through an unbounded `Channel<Operation>`. Thread safety is absolute and trivially provable.

**Model B — WAL + read/write gate:** WAL mode allows multiple concurrent readers on separate connections, with writes gated by an exclusive lock. Provides higher read throughput but requires a connection pool and significantly more implementation complexity.

WAL mode is always enabled regardless of concurrency model choice, for its write performance and OS-level contention benefits.

---

## Decision

Use **Model A**: full serialization via an unbounded `Channel<Operation>`.

A single `DatabaseHandle` owns exactly one SQLite connection and one `Channel<Operation>`. A dedicated background task is the sole consumer of the channel and the only code that touches the SQLite connection. All callers submit an `Operation` to the channel and await its `TaskCompletionSource<T>` for the result.

The channel is **unbounded**. The anticipated workload (a small number of concurrent users on segmented local databases, with MCP-mediated AI tool calls as the heaviest load) does not warrant backpressure complexity.

---

## Consequences

**Enables:**
- Thread safety is guaranteed at the architectural level — no caller coordination required
- Simple to implement, audit, and reason about
- Single connection eliminates shared prepared-statement cache coherence issues
- Easy to test: sequential semantics, no races

**Constrains:**
- Reads are serialized behind writes and other reads — there is no read concurrency
- An unbounded channel can grow without limit if a producer enqueues faster than the consumer processes; this is accepted given the expected workload
- If read throughput becomes a bottleneck in the future, migrating to Model B would require a significant rewrite of the concurrency layer (this is not anticipated)

---

## Supersedes / Superseded by

None.
