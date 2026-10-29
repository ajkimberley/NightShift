# Opt-in Codex process boundary

No real model-backed worker was invoked during implementation. `CodexProcessBackend` was implemented and tested with controllable local shell executables. No spend, authentication, real model output or real sandbox behavior was exercised.

## Practical readiness

Ready for a controlled experimental trial; live end-to-end compatibility remains unverified. Existing CLI authentication is required. The read-only example is the intended first live test; editing workflows may opt into workspace-write. Stages share filesystem state, not a persistent conversation.

On 2026-10-29 a regression test exposed shared serializer rewriting of the JSON Schema keyword `additionalProperties`. Schema emission now preserves keyword casing explicitly. The updated full Debug and Release suites each passed 91 tests ([results](evidence/codex-schema-followup.txt)). No real Codex session was run.

## Interface sources

Checked local help without running a worker:

```sh
command -v codex
codex --version
codex exec --help
```

Installed CLI reported `codex-cli 0.164.0-alpha.11.1`. Its `exec` help established `--json`, `--ephemeral`, `--sandbox`, `-C`, `--output-schema`, `--output-last-message`, `--model`, `-c` and stdin prompt `-`.

Also consulted official [non-interactive mode](https://developers.openai.com/codex/noninteractive) (redirected to [ChatGPT Learn](https://learn.chatgpt.com/docs/non-interactive-mode)) and [the repair-loop example](https://developers.openai.com/cookbook/examples/codex/build_iterative_repair_loops_with_codex). The local installed help controls argument compatibility; no undocumented flags were inferred from examples.

## Boundary

`Processes.cs` separates process launch/cancellation (`ProcessRunner`), JSONL/structured final output (`CodexOutput`) and request/capability mapping (`CodexProcessBackend`). Arguments use `ProcessStartInfo.ArgumentList`, never shell interpolation. Prompt goes to stdin. Full stdout/stderr stream to per-attempt files, while in-memory response previews are limited to 1 MiB each.

The worker must exit zero, emit `turn.completed`, emit no `turn.failed`/`error` event, and write a valid final outcome. Missing status, malformed protocol, fluent text alone and incomplete runs fail. Unknown additive event types are ignored; malformed event envelopes fail. Structured worker outcomes remain worker claims; they do not establish independent correctness.

| Capability | State |
|---|---|
| Fresh `codex exec` with stdin / JSONL / structured final response | Fake-process tested |
| Model and reasoning effort mapping | Fake arguments and generated schema/config tested |
| `openai` provider | Accepted; live availability/auth untested |
| Other provider labels | Rejected; no provider abstraction |
| `truncate` | Implemented: only graph goal, run ID and stage prompt |
| compact / summary low, medium, high | Unsupported; rejected |
| full / persistent session reuse / resume | Unsupported; rejected |
| Per-tool hooks / compaction | Unsupported |
| Cancellation / timeout / process tree kill | Local process tests; real Codex untested |
| Sandboxing | Explicit read-only default or workspace-write; actual enforcement untested |
| Credential/authentication management | Delegated to installed CLI; not accessed or tested |

Adapter arguments never enable bypass/yolo/full-access modes. Installed CLI configuration may still influence a later opted-in real invocation. The engine does not implement a sandbox or inspect user configuration. Process-tree cleanup was verified for attached children on macOS; detached/reparented daemons can escape this boundary. Cancellation also interrupts pipe reads so escaped descendants cannot hold the runner's output wait indefinitely. Custom in-process handlers/interviewers must cooperate with cancellation.

A later manually authorized experiment could run this in an isolated personal scratch checkout:

```sh
dotnet run --project src/Nightshift.Cli -- run examples/codex-opt-in.dot --dir runs/codex-experiment --backend codex --enable-codex --max-seconds 180
```

That command was **not executed**. It can use paid services and may require prior CLI authentication. Prefer an independent shell verifier stage before using a model outcome as a goal gate.
