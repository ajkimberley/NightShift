> Historical phase-two native-development guide. In 0.3, executing these examples requires explicit `--profile native-development` and lacks protected acceptance. For current defaults see [protected workflows](protected-workflows.md).

# Configure a personal project for Codex

Use a personal repository whose files and workflows you trust. `init` writes configuration and a template; it does not run Codex. Real runs may consume usage or incur charges.

## First setup

Change into the project you want to work on and run:

```sh
nightshift init --backend codex --enable-codex
nightshift doctor
```

This creates `nightshift.json`, creates `workflow.dot` only if absent, and appends one scoped run-output entry to `.gitignore`. Existing workflows/ignore content are preserved. If configuration already exists, init refuses to replace it. Edit it explicitly.

The flag pair saves explicit permission for later real Codex runs in this configured project. It defaults to read-only. Discovery alone never grants permission. The CLI uses existing Codex authentication; if doctor reports you are signed out, complete your normal `codex login` flow yourself. Doctor's stored-authentication check does not establish live service/model access. The official [CLI reference](https://learn.chatgpt.com/docs/cli/reference) describes these read-only diagnostic commands.

Review the generated task prompt, then:

```sh
nightshift validate
nightshift run
nightshift result --latest
```

The initial prompt inspects the README/structure and writes its answer into the run's response file. It does not promise to create a workspace report. `result` surfaces that response and exact evidence paths.

## Configuration and path rules

The generated configuration has these fields:

```json
{
  "schema_version": 1,
  "working_directory": ".",
  "default_workflow": "workflow.dot",
  "backend": "codex",
  "codex_executable": null,
  "sandbox": "read-only",
  "enable_codex": true,
  "run_output_root": ".nightshift/runs",
  "max_visits": 1000,
  "max_seconds": 3600,
  "outputs": []
}
```

| Setting | Meaning |
|---|---|
| `working_directory` | Workspace where stages run |
| `default_workflow` | DOT file used by `nightshift run` with no argument |
| `backend` | `scripted` simulation or `codex` |
| `codex_executable` | Optional explicit path; null means automatic discovery |
| `sandbox` | Codex worker's read-only or workspace-write mode |
| `enable_codex` | Saved explicit real-worker opt-in |
| `run_output_root` | Parent for unique run/evidence folders |
| `max_visits`, `max_seconds` | Finite cumulative active-run bounds; reaching a bound is incomplete |
| `outputs` | Declared user-facing files/directories; existence is reported, not inferred as correctness |

Precedence for new runs is explicit command-line options, then project config, then defaults. All **configuration paths** are relative to the directory containing `nightshift.json`. All **CLI paths** are relative to the directory where you invoke the command. `--workdir` explicitly overrides the workspace for a new run. New run summaries show the resolved paths, backend, sandbox, configuration source and CLI override names. Full provenance is saved in the manifest.

Without a config, the invocation directory defines the project. From a subdirectory of a configured project, Nightshift finds the nearest ancestor config, stopping at an inclusive Git root or before leaving for the user's home directory. A nested Git repository forms its own boundary. An explicit `--config /absolute/path/nightshift.json` selects a config directly; `--project /absolute/path/project` changes where discovery starts. This avoids silently taking settings from an unrelated enclosing repository.

For a one-off workflow, provide its path directly:

```sh
nightshift run workflow.dot --max-visits 10 --max-seconds 180
```

No user-created shell variables are required. New runs get unique timestamp/ID directories automatically. `--dir` selects an exact fresh run directory; an initialized directory is not overwritten.

## Codex executable selection

Nightshift tries explicit `--codex`, configured `codex_executable`, an external executable on PATH, then the bundled macOS app location. An invalid explicit/configured path is an error; it never silently falls back to another installation. Shell aliases/functions are not executables. Empty values, directories and nonexecutable files produce actionable errors.

If required, use a direct path, quoted when it contains spaces:

```sh
nightshift run --codex /Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex
```

For persistent selection, put that path into `codex_executable`. Do not put credentials in this file. The default model is the installed CLI's configured/default model; explicit node `llm_model` and `reasoning_effort` settings still apply. Only `truncate` fidelity is supported by this adapter, so the generated graph sets `graph [default_fidelity=truncate]`. Every stage starts fresh; shared files carry artifacts, not a shared conversation.

## A small writing workflow with an independent check

In a new personal scratch project, deliberately edit the generated configuration:

- Set `sandbox` to `workspace-write`.
- Set `outputs` to `["report.txt"]`.
- Use short limits, e.g. `max_visits: 4` and `max_seconds: 180`.

Replace the newly generated template with this workflow when you are ready (preserve any workflow you have already authored):

```dot
digraph WriteReport {
  graph [goal="Create a tiny report and independently verify it", default_fidelity=truncate]
  start [shape=Mdiamond]
  write [prompt="Write report.txt containing exactly Hello from Nightshift followed by one newline. Change no other workspace files. Return fail if you cannot do this.", timeout=120s, max_retries=0]
  verify [type=tool, tool_command="printf 'Hello from Nightshift\\n' | cmp -s - report.txt", timeout=5s, goal_gate=true]
  exit [shape=Msquare]
  start -> write -> verify -> exit
}
```

```sh
nightshift validate
nightshift run
nightshift result --latest
```

Expected trace: `start -> write -> verify -> exit`. Expected report content: `Hello from Nightshift` plus newline. Worker SUCCESS remains its claim; the `verify` command compares exact file bytes independently and fails the pipeline for wrong/missing output. The demo-style goal-gate warning about missing recovery is expected because this workflow deliberately stops rather than looping.

**Tool stages execute as local host commands.** The sandbox setting applies to the Codex worker, not those host commands. Review tool commands as you would any local script. This phase adds no new sandbox platform.

The installed real-demo paths and configured real-run/resume paths were exercised with fake executables during development. Historical user-run phase-one Codex read/write/resume trials worked, but no fresh model-backed call was made as part of this implementation. Keep real trial evidence distinct from automated fake-worker evidence.
