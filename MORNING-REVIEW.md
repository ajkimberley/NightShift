# Morning review — Nightshift / Attractor

Historical phase-one handoff. For the installed CLI and current onboarding, see [getting started](docs/guides/getting-started.md) and [phase-two review](PHASE-2-REVIEW.md).

**Runnable partial implementation delivered. No implementation blocker.** C# / net10.0 with installed SDK **10.0.103**, pinned in `global.json`. Debug and Release builds have **0 warnings / 0 errors**; **91 tests pass in each configuration**. All deterministic CLI demos ran successfully. No real model worker ran, and no work systems, deployments, remote writes or account changes were used.

Repository started empty with no repository/ancestor AGENTS.md or visual guide present. Git initialized on **`implement/attractor`**; local commits retain the implementation. Existing visual content outside this checkout was not accessed or modified.

## Can I use this with real Codex?

The adapter is ready for a **controlled experimental trial**, but real Codex integration is still **unverified**. All worker tests use fake executables. It launches the installed `codex exec` CLI after explicit opt-in and requires existing CLI authentication. Each stage starts fresh with `truncate` fidelity; shared files can carry work between stages, while conversation/session reuse is unsupported. The example defaults to read-only inspection; editing workflows need `--sandbox workspace-write` and independent verifier stages.

Follow-up on 2026-10-29 found and fixed a real adapter defect: the shared snake-case serializer changed JSON Schema's `additionalProperties` keyword. A regression assertion first failed, then passed after preserving protocol-defined keyword casing. Both complete 91-test suites passed again; see [follow-up results](docs/evidence/codex-schema-followup.txt). No real model was invoked during this fix. A bounded read-only live smoke test is still needed before recommending unattended practical use.

## Run it

From `/Users/adamkimberley/Repos/nightshift`:

```sh
dotnet --version                         # 10.0.103
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build

dotnet run --no-build --project src/Nightshift.Cli -- help
dotnet run --no-build --project src/Nightshift.Cli -- validate examples/linear.dot
dotnet run --no-build --project src/Nightshift.Cli -- run examples/linear.dot --dir runs/morning-linear
dotnet run --no-build --project src/Nightshift.Cli -- run examples/branching.dot --dir runs/morning-branching
dotnet run --no-build --project src/Nightshift.Cli -- run examples/repair.dot --dir runs/morning-repair
dotnet run --no-build --project src/Nightshift.Cli -- run examples/human.dot --dir runs/morning-human --answers R,A

# Intentional pause: exit code 3 is expected.
dotnet run --no-build --project src/Nightshift.Cli -- run examples/resume.dot --dir runs/morning-resume --pause-after 2
dotnet run --no-build --project src/Nightshift.Cli -- resume runs/morning-resume
dotnet run --no-build --project src/Nightshift.Cli -- inspect runs/morning-resume
dotnet run --no-build --project src/Nightshift.Cli -- events runs/morning-resume
```

Those `morning-*` names are fresh suggested directories, not claimed as executed. The equivalent commands were actually executed under `runs/final-verification/`. To repeat the exact complete verification sequence with another fresh directory:

```sh
bash scripts/verify.sh runs/morning-verification
```

Existing initialized directories are deliberately refused by `run`; use `resume` or a new path. Default backend is scripted, with no credentials/network dependency. Human gates use console input unless `--answers` is supplied; queue exhaustion skips/fails, never auto-approves.

## What works

- Lexer and recursive-descent DOT parser: typed attributes, escaped/multiline strings, comments, declaration-order defaults, chained edges, flattened/scoped subgraphs and derived classes. Malformed/unsupported syntax produces source-located errors.
- Typed graph/context/outcome/diagnostics, error/warning/info handling, structural validation and execution refusal for unsupported handlers or invalid fidelity.
- Deterministic condition/preferred-label/suggestion/weight/ordinal routing, strict core condition grammar and explicit failure routes.
- Start/exit, codergen, conditional, local shell tool and human handlers; public registry/backend/transform/lint seams.
- Additional-attempt retries with injectable delay, transient/terminal classification, backoff/jitter, failure recovery, visited goal gates and bounded run visit/time limits.
- Atomic checkpoints, historical snapshots, hash/schema checks, exclusive run lease, durable chosen continuations, retained visit/attempt artifacts and resume after actual abrupt CLI termination.
- Console, queued, callback, recording and explicitly constructed test auto-approver interviewers; cancellation, timeout, default, revise, skip and invalid-answer handling.
- Stylesheet specificity/cascade and configuration provenance, independent fidelity/thread resolution, artifact storage primitive, typed event callbacks and JSONL audit.
- Opt-in Codex subprocess boundary with conservative capabilities and strict completion/status handling, tested with fake processes.

## Actual verification

| Check | Result / evidence |
|---|---|
| `bash scripts/verify.sh runs/final-verification > docs/evidence/verification.txt 2>&1` | Exit 0; locked restore, Debug build, **91/91 tests**, help, validate, five demos, resume, inspect and events. [Full output](docs/evidence/verification.txt). |
| `dotnet build -c Release --no-restore` | Exit 0; 0 warnings/errors. |
| `dotnet test -c Release --no-build --logger 'console;verbosity=minimal'` | Exit 0; **91/91 tests**, none skipped. [Release output](docs/evidence/release-verification.txt). |
| Abrupt process termination/recovery | Real CLI subprocess killed before tool-stage completion, resumed from persisted continuation; previous completed stage was not repeated and prior attempt evidence remained. `CliRecoveryTests`. |
| Subprocess timeout/cancellation | Attached child cleanup and bounded output waits tested on macOS; nonzero exits and incomplete/malformed worker output fail. `ProcessTests`. |
| `git diff --check` | Passed. |
| Reference provenance | Unmodified pinned spec + Apache-2.0 licence saved, SHA-256 values recorded in [PROVENANCE](docs/reference/PROVENANCE.md). |

Development failures are retained as a concise [findings ledger](docs/evidence/development-findings.md): boolean spelling, anonymous JSON serialization and an empty sentinel in the crash-test fixture. Implementation defects were repaired; the fixture was corrected to write real effect evidence. Assertions were not weakened/deleted.

These tests were authored during this implementation. They are **not independent verification** by another party. Not executed: real Codex/model sessions, real auth/sandbox enforcement, cross-platform tests, power-loss durability tests, parallel/fan-in/manager execution or live HTTP/UI tests.

## Architecture and five files to read first

The structure is intentionally small: `src/Nightshift.Core` owns parsing/preparation, domain types, handlers and persistence; `src/Nightshift.Cli` owns commands, console wiring and explicit worker opt-in; `tests/Nightshift.Tests` exercises behavior with scripted workers and fake/local processes. Production code uses the standard library; NuGet test dependencies are locked.

1. [Engine.cs](src/Nightshift.Core/Engine.cs) — traversal, retries, gates and durable continuation boundaries.
2. [Routing.cs](src/Nightshift.Core/Routing.cs) — exact edge order, failure routes and condition grammar.
3. [DotParser.cs](src/Nightshift.Core/DotParser.cs) — lexical boundaries and scoped defaults.
4. [Handlers.cs](src/Nightshift.Core/Handlers.cs) — worker seam, scripted backend, tool and human behavior.
5. [EngineTests.cs](tests/Nightshift.Tests/EngineTests.cs) — executable semantics and recovery cases.

Then read [spec decisions](docs/spec-decisions.md), [conformance](docs/conformance.md), [Processes.cs](src/Nightshift.Core/Processes.cs) and [CLI recovery tests](tests/Nightshift.Tests/CliRecoveryTests.cs).

## Sample execution and checkpoint

Actually executed repair demo:

```text
start visit 1 / attempt 1: success
check visit 1 / attempt 1: fail (no handler retry)
  explicit outcome=fail edge -> repair
repair visit 1 / attempt 1: success -> check
check visit 2 / attempt 1: success -> exit
exit: visited goal gate check satisfied; pipeline success
```

Full working evidence remains at:

- `runs/final-verification/repair/checkpoint.json` (final revision 10)
- `runs/final-verification/repair/events.jsonl`
- `runs/final-verification/repair/check/visit-0001/attempt-0001/status.json`
- `runs/final-verification/repair/check/visit-0002/attempt-0001/status.json`

`runs/` is Git-ignored. Portable snapshots are committed: [sample checkpoint](docs/evidence/sample-repair-checkpoint.json), [sample events](docs/evidence/sample-repair-events.jsonl). The checkpoint includes completed order `start, check, repair, check, exit`, the latest outcome map and chosen transitions.

## Decisions needing review

The [decision record](docs/spec-decisions.md) cites sections and tests; the most consequential choices are:

1. **FAIL does not handler-retry** (§3.5 followed over §11.5); graph revisits are separate executions. Only RETRY/classified transient errors consume the additional-attempt budget.
2. **Failure routing is explicit** (§3.7): positive `outcome=fail` guards, node recovery targets, then fail. An unrelated success edge cannot swallow failure.
3. **Goal gates cover visited latest outcomes**, including PARTIAL_SUCCESS (§3.4). They are not artifact-versioned acceptance; skipped/unvisited work does not prove correctness.
4. **Resume may repeat external effects**. Completion and chosen continuation are saved together, but a process can die after an effect and before completion. Replay preserves prior evidence and does not use a permanent completion skip set.
5. **Worker text/status is a claim**. Missing outcome fails by default; nonzero exits override success; auto-status must be explicit. A separate verifier remains necessary for task correctness.
6. **Unknown/ambiguous human answers fail** rather than selecting the first option. Identities are recorded but unverified. Human choices use §4.6 suggested targets rather than §11.6 preferred-label wording.
7. **Configuration follows the detailed sections**: explicit node > stylesheet > node defaults > graph/system; fidelity edge > node > graph > compact; thread node > edge > graph > subgraph > previous node. No fake claim of full/summary fidelity.

Reversible assumptions: exact installed SDK pin; Unix `/bin/sh` tools; no extra architectural layers; original source/attempt evidence retained; local commits only. The project’s own-code distribution licence has not been selected; upstream reference licence/provenance is preserved.

## Limitations, deferrals and next experiment

Parallel/fan-in and manager supervision are **deferred**, with validation rejection, because real branch scheduling/join boundaries/context isolation/all-failed semantics need a coherent implementation and tests. Also deferred: loop_restart, per-tool hooks, full/summary/compact model handoff, persistent model sessions, HTTP/SSE/UI, integrations, provider client/inner-agent-loop work and sandbox platform work. There are no unconditional-success placeholders for these features.

Further limits: artifact-store catalogue is a host utility, not automatically indexed/restored; event payloads do not fully match spec duration/artifact-count fields; auto-status is restricted to codergen text results; in-process extensions must cooperate with cancellation; detached/reparented child daemons can escape process-tree cleanup; directory-metadata durability under power loss is unverified. Time bounds count checkpointed active runtime, not downtime or uncheckpointed time lost in a hard crash. See the full [conformance matrix](docs/conformance.md) before assuming a capability works.

**Codex adapter status: implemented and fake-process tested only. It was not genuinely exercised against a model.** It supports fresh truncate-fidelity executions and rejects other fidelity modes. See [capability/source documentation](docs/codex-adapter.md). Real invocation still needs a deliberate opt-in and is not part of verification.

No outstanding blocker prevents local use of the implemented subset. Recommended next experiment: build a tiny artifact-producing repair workflow with an independent shell check, then interrupt/resume it around the verifier boundary. This tests the separation between worker claims, durable state and actual artifact correctness before adding a real model. After that, choose and document an explicit join boundary for a small bounded parallel implementation.
