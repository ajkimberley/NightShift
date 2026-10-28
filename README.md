# Nightshift

A runnable, partial C# implementation of [StrongDM Attractor](docs/reference/PROVENANCE.md), pinned to `fb57a55ed97372a27ac90102f436947e29f48426`. It orchestrates DOT workflows with deterministic routing, bounded retries, human choices, external commands and durable checkpoints. The default worker is a deterministic simulator. No credentials or model service are required.

Requires the installed **.NET SDK 10.0.103**, pinned in `global.json`; target **net10.0**. NuGet lock files pin test dependencies. Verified on macOS arm64. Shell tool commands and fake-process tests use `/bin/sh`; Windows support is unverified.

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build

dotnet run --no-build --project src/Nightshift.Cli -- help
dotnet run --no-build --project src/Nightshift.Cli -- validate examples/linear.dot
dotnet run --no-build --project src/Nightshift.Cli -- run examples/linear.dot --dir runs/linear
dotnet run --no-build --project src/Nightshift.Cli -- run examples/branching.dot --dir runs/branching
dotnet run --no-build --project src/Nightshift.Cli -- run examples/repair.dot --dir runs/repair
dotnet run --no-build --project src/Nightshift.Cli -- run examples/human.dot --dir runs/human --answers R,A
```

Use fresh run directories; existing initialized runs require `resume`. Human gates use console input by default. `--answers` supplies a deterministic queue; an exhausted queue returns SKIPPED and fails the interaction. No implicit auto-approval. Answers are recorded with **unverified identity**, including console answers.

```sh
# Intentional pause exits 3, after start and first have completed.
dotnet run --no-build --project src/Nightshift.Cli -- run examples/resume.dot --dir runs/resume --pause-after 2
dotnet run --no-build --project src/Nightshift.Cli -- resume runs/resume
dotnet run --no-build --project src/Nightshift.Cli -- inspect runs/resume
dotnet run --no-build --project src/Nightshift.Cli -- events runs/resume
```

Run the complete deterministic verification and demos, including the expected pause exit code:

```sh
bash scripts/verify.sh runs/my-verification
```

`--max-visits` (default 1000) and `--max-seconds` (default 3600) bound a run. Reaching a bound is **incomplete**, never success. Exit codes: success 0, failed/incomplete 1, invalid input 2, interrupted/pause 3; cancellation before a run is initialized may return 130. Ctrl-C saves resumable state when possible. Resuming a final run returns its saved result.

## Contracts and evidence

- [Morning review](MORNING-REVIEW.md): actual results, architecture, next steps.
- [Conformance](docs/conformance.md): capability-by-capability coverage and gaps.
- [Spec decisions](docs/spec-decisions.md): ambiguities and protective divergences.
- [Verification transcript](docs/evidence/verification.txt): commands/results from `scripts/verify.sh`.
- [Codex boundary](docs/codex-adapter.md): source interface, capabilities and test scope.

Status files use external `outcome`, with `success`, `partial_success`, `fail`, `retry` (and internal-compatible `skipped`). Worker text alone is insufficient for success. A zero shell exit is command completion; a model's status is its claim. Independent test/acceptance stages are still needed to establish task correctness. `auto_status=true` is explicit synthetic success and emits a warning.

Node artifacts live under `<run>/<node>/visit-0001/attempt-0001/`. Every visit and retry retains evidence. Tool commands receive `ATTRACTOR_STAGE_DIR` and may write Appendix C `status.json`; the original is retained as `worker-status.json`. A nonzero process exit overrides claimed success. `status.json` records the handler result; retry-exhaustion conversion is retained separately as `effective-outcome.json`.

`checkpoint.json` is replaced atomically; history lives in `checkpoints/`. Checkpoints retain next node, incoming edge, node outcomes, visit/attempt/retry state and chosen transitions. A run-directory lease prevents concurrent runners. Resume can replay an interrupted effect. This is **not exactly-once execution**, and power-loss durability of directory metadata is unverified.

## Scope

Implemented handlers: start, exit, codergen, conditional, tool and wait.human. Public seams: handlers, backend, interviewer, transforms, lint rules, retry delay/backoff and typed `Engine.Event` callbacks. Context supports JSON values and deep snapshots/clones. File-backed artifact storage is available to hosts; it is not an automatically restored artifact catalogue.

Parallel/fan-in, manager supervision, loop_restart, per-tool hooks, HTTP/SSE and UI are deferred. Executable deferred handlers/hooks/restarts are rejected. This is a learning implementation, not production-ready or fully conformant.

The Codex process backend requires explicit `--backend codex --enable-codex` on every invocation and supports only **truncate** fidelity. It has been tested with fake executables only. Do not include it in unattended verification. Full session reuse, compaction and summary modes are unsupported there. Other fidelity strings on the simulator only describe simulated contracts.
