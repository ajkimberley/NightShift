# Historical/manual source-based Codex walkthrough

For the installed CLI and shorter daily commands, start with [the current onboarding guide](guides/getting-started.md). This phase-one walkthrough is preserved for manual diagnostics and its original evidence checks.

This walkthrough starts with one read-only model invocation in a tiny personal scratch repository. It then offers a file-writing test and a checkpoint/resume test. Run the steps in the same Terminal tab so the shell variables remain available.

**These real-model commands are for you to run. They have not been executed by the implementation agent.** They use your Codex account/API configuration and may consume usage or incur charges. Timeouts bound runtime, not tokens or spend. Each example has zero automatic retries and no graph cycle. The installed CLI remains responsible for sandboxing, authentication and its configured policies; Nightshift does not supply a separate sandbox.

## 1. Build Nightshift and select the CLI

```sh
nightshift_repo=/Users/adamkimberley/Repos/nightshift
cd "$nightshift_repo"

dotnet --version
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build

nightshift_cli="$nightshift_repo/src/Nightshift.Cli/bin/Debug/net10.0/Nightshift.Cli.dll"
nightshift_codex="$(command -v codex)"
if [ ! -f "$nightshift_codex" ] || [ ! -x "$nightshift_codex" ]; then
  nightshift_codex=/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex
fi
```

Expected SDK: `10.0.103`; expected test result for this revision: **91 passed**. The walkthrough's JSON checks also use the already available `python3`; no Python packages are required.

```sh
python3 --version
```

If `command -v codex` found nothing in your Terminal, the bundled executable observed on this machine was:

```sh
nightshift_codex=/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex
```

Use your actual CLI location if different; no global installation or PATH change is needed. Confirm the selected executable:

```sh
printf 'Codex executable: <%s>\n' "$nightshift_codex"
if [ -f "$nightshift_codex" ] && [ -x "$nightshift_codex" ]; then
  "$nightshift_codex" --version
  "$nightshift_codex" exec --help
  "$nightshift_codex" login status
else
  printf 'Set nightshift_codex to an existing executable file before continuing.\n' >&2
fi
```

If zsh reports `permission denied:` with no path after the colon, check whether the variable is empty: `printf '<%s>\n' "$nightshift_codex"`. An empty quoted command reproduces that error. Reassign the executable path in the current Terminal tab; shell variables do not carry into a new tab. A directory path is also unsuitable, even if `test -x` alone succeeds. The guard above checks for an executable file and falls back to the observed bundled path; no permission change is required for that copy.

The version checked when writing this guide was `codex-cli 0.164.0-alpha.11.1`. Its help supports `exec`, `--json`, `--ephemeral`, `--sandbox`, `-C`, `--output-schema`, `--output-last-message` and stdin prompt `-`. The adapter uses those flags. If your version rejects one, keep the error evidence and resolve compatibility before retrying.

If login status reports you are signed out, complete the normal login flow yourself:

```sh
"$nightshift_codex" login
"$nightshift_codex" login status
```

A successful status check establishes stored authentication, not a successful model call. Official command semantics: [Codex CLI reference](https://learn.chatgpt.com/docs/cli/reference). Do not paste credentials into graphs or run logs.

## 2. Create a scratch workspace

Everything created here stays under the checkout's Git-ignored `runs/` folder. The scratch repository contains one input file; it does not point at work repositories.

```sh
mkdir -p "$nightshift_repo/runs"
nightshift_session="$(mktemp -d "$nightshift_repo/runs/real-codex-XXXXXXXX")"
nightshift_scratch="$nightshift_session/workspace"
mkdir -p "$nightshift_scratch"
git init -q "$nightshift_scratch"

python3 -c 'import uuid; print("NIGHTSHIFT_SMOKE_" + str(uuid.uuid4()))' > "$nightshift_scratch/smoke-input.txt"

git -C "$nightshift_scratch" status --porcelain=v1 --untracked-files=all > "$nightshift_session/before-status.txt"
shasum -a 256 "$nightshift_scratch/smoke-input.txt" > "$nightshift_session/before-input.sha256"

cd "$nightshift_scratch"
pwd
cat smoke-input.txt
```

Keep `nightshift_session` for locating your evidence; each setup creates a fresh directory.

**Working directory matters:** Nightshift makes the current directory the worker's workspace. Supplying an absolute graph path does not make the graph's directory the workspace. Stay inside the scratch directory for all `run` commands below. The compiled DLL path lets you execute Nightshift here without copying or rebuilding the project.

## 3. Validate, then make one read-only Codex call

Validation parses the graph; it does not launch Codex:

```sh
dotnet "$nightshift_cli" validate "$nightshift_repo/examples/codex-read-smoke.dot"
```

Expected: `Valid: 3 nodes, 2 edges`. The graph uses `truncate`, the only real fidelity mode this adapter implements. It leaves `llm_model` unset so the installed CLI selects its configured/default model.

Now run the real worker:

```sh
dotnet "$nightshift_cli" run "$nightshift_repo/examples/codex-read-smoke.dot" \
  --dir "$nightshift_session/read" \
  --backend codex \
  --enable-codex \
  --codex "$nightshift_codex" \
  --sandbox read-only \
  --max-visits 3 \
  --max-seconds 180
nightshift_read_exit=$?
printf 'Nightshift exit code: %s\n' "$nightshift_read_exit"
```

Expected output includes:

```text
Backend: codex-process. Run directory: .../read
success: Pipeline reached exit; visited goal gates satisfied
Trace: start -> inspect -> exit
Nightshift exit code: 0
```

These are expected results, not a transcript of a live run. The model stage has a 120-second timeout inside the 180-second run limit. Normal worker progress is captured in files rather than continuously forwarded to the Terminal, so the command can appear quiet. Press Ctrl-C once to cancel; allow the runner to save its checkpoint and clean up attached processes.

If the backend line says `scripted`, the test did not use real Codex. Both `--backend codex` and `--enable-codex` are required. If the command fails, inspect its evidence in step 5 before running another model call.

## 4. Check the actual answer and unchanged input

The final model response is `last-message.json`. `response.md` contains a preview of the raw JSONL process output, not just the model's answer.

```sh
nightshift_stage="$nightshift_session/read/inspect/visit-0001/attempt-0001"
python3 -m json.tool "$nightshift_stage/last-message.json"
python3 -m json.tool "$nightshift_stage/status.json"
```

Check the unique value independently against the input file and require a successful final checkpoint:

```sh
python3 - "$nightshift_scratch/smoke-input.txt" "$nightshift_stage/last-message.json" "$nightshift_session/read/checkpoint.json" <<'PY'
import json
import pathlib
import sys

expected = pathlib.Path(sys.argv[1]).read_text().strip()
answer = json.loads(pathlib.Path(sys.argv[2]).read_text())
checkpoint = json.loads(pathlib.Path(sys.argv[3]).read_text())
assert answer["outcome"] == "success", answer
assert expected in answer["notes"], "Codex did not report the unique file contents"
assert checkpoint["state"] == "success", checkpoint["state"]
assert checkpoint["final_outcome"]["outcome"] == "success"
assert checkpoint["completed_nodes"] == ["start", "inspect", "exit"]
print("PASS: Codex reported the unique file contents and Nightshift completed.")
PY

git -C "$nightshift_scratch" status --porcelain=v1 --untracked-files=all > "$nightshift_session/after-status.txt"
shasum -a 256 "$nightshift_scratch/smoke-input.txt" > "$nightshift_session/after-input.sha256"
diff -u "$nightshift_session/before-status.txt" "$nightshift_session/after-status.txt"
diff -u "$nightshift_session/before-input.sha256" "$nightshift_session/after-input.sha256"
```

Both `diff` commands should print nothing and exit 0. Together these checks establish that the structured answer contains the unique input value and that the scratch file content/inventory stayed unchanged. They do not prove general model correctness or complete sandbox isolation.

If this passes, you have exercised real CLI launch, authentication/model access, read-only file access, structured output, event parsing, outcome mapping, routing, artifacts and checkpoint completion in this environment.

## 5. Locate evidence and diagnose failures

```sh
dotnet "$nightshift_cli" inspect "$nightshift_session/read"
dotnet "$nightshift_cli" events "$nightshift_session/read"
printf 'Evidence directory: %s\n' "$nightshift_session"
```

| File under `read/inspect/visit-0001/attempt-0001/` | What it tells you |
|---|---|
| `stderr.txt` | CLI/authentication/model/sandbox errors |
| `stdout.txt` | Raw Codex JSONL; successful completion must include `turn.completed` |
| `last-message.json` | Final model-supplied structured outcome and notes |
| `status.json` | Engine-facing handler outcome, including failure reason |
| `effective-outcome.json` (when present) | Retry-exhaustion conversion, e.g. timeout RETRY to FAIL |
| `configuration.json` | Resolved model/fidelity and backend capability map |
| `outcome-schema.json` | Expected response schema; key spelling is `additionalProperties` |
| `prompt.md` | Stage prompt, before the adapter adds goal/run identity and response instructions |

| Symptom | Next action |
|---|---|
| Cannot launch executable | Check `test -x "$nightshift_codex"`; keep `--codex` pointed at that exact path. |
| Authentication/access/model error | Read `stderr.txt`; check CLI login/account/model access. A login-status success alone does not guarantee a call will succeed. |
| Unsupported compact/full/summary fidelity | Use `graph [default_fidelity=truncate]`; node/edge fidelity overrides must also be truncate. |
| Unknown CLI option or invalid schema | Compare installed `exec --help`; retain schema, stderr and stdout. This is a compatibility failure, not verification success. |
| Missing `turn.completed`, missing final file, or malformed JSON | Inspect stdout/stderr. The adapter correctly fails an incomplete or unexpected protocol. |
| Stage timeout / max retries exceeded | First read logs for a stalled request. A fresh run with a deliberately longer stage timeout is a separate model call. |
| Run state `incomplete` | The overall run bound was reached; this is final, not success. |
| Run directory already initialized | Use a new directory for another trial; preserve earlier evidence. |
| Permission/approval policy blocks an action | Respect the active policy; adjust the test or resolve the specific policy requirement normally. No bypass flags are part of this walkthrough. |

Exit codes: 0 success; 1 failed/incomplete; 2 invalid input/configuration; 3 interrupted/pause. Cancellation before initialization can return 130. A failed or incomplete **final** checkpoint will just return its saved result on `resume`; after fixing the cause, use a new run directory.

For another read trial in the same scratch repo, use a fresh name such as `--dir "$nightshift_session/read-2"`. Keep all other worker flags. Do not repeatedly retry an unchanged failure.

## 6. Optional: verify a real file-writing task

This makes one additional model call. Use it only after the read-only test passes. The model may edit the scratch workspace under `workspace-write`; inherited CLI policies still apply.

```sh
cd "$nightshift_scratch"
dotnet "$nightshift_cli" validate "$nightshift_repo/examples/codex-write-smoke.dot"
dotnet "$nightshift_cli" run "$nightshift_repo/examples/codex-write-smoke.dot" \
  --dir "$nightshift_session/write" \
  --backend codex \
  --enable-codex \
  --codex "$nightshift_codex" \
  --sandbox workspace-write \
  --max-visits 4 \
  --max-seconds 180
nightshift_write_exit=$?
printf 'Nightshift exit code: %s\n' "$nightshift_write_exit"

printf 'NIGHTSHIFT_WRITE_OK\n' | cmp -s - codex-output.txt
printf 'Independent file comparison exit code: %s\n' "$?"
dotnet "$nightshift_cli" inspect "$nightshift_session/write"
```

Expected: both exit codes are 0 and trace `start -> write -> verify -> exit`. A `goal_gate_has_retry` warning for `verify` is expected: this smoke test intentionally stops on a failed check rather than scheduling a repair. The graph's `verify` tool stage compares exact file bytes independently of the model's claimed success. A missing/wrong file fails that stage and the pipeline. Tool stages run locally under the host's permissions; the `--sandbox` option is passed to the Codex worker only. This verifier command just reads/compares the scratch file.

Inspect `write/verify/visit-0001/attempt-0001/status.json` as well as the `write` worker's files. If the worker fails, the verifier is not executed. No automatic repair loop or retries occur in this example.

## 7. Optional: exercise durable pause/resume with real Codex

This is a separate read trial. Its first command pauses after the no-op start, before any model invocation; resuming makes one additional real model call.

```sh
cd "$nightshift_scratch"
dotnet "$nightshift_cli" run "$nightshift_repo/examples/codex-read-smoke.dot" \
  --dir "$nightshift_session/resume" \
  --backend codex --enable-codex --codex "$nightshift_codex" \
  --sandbox read-only --max-visits 3 --max-seconds 180 --pause-after 1
nightshift_pause_exit=$?
printf 'Pause exit code (expected 3): %s\n' "$nightshift_pause_exit"

dotnet "$nightshift_cli" inspect "$nightshift_session/resume"
```

Expected saved state: `interrupted`, completed nodes `["start"]`, next node `inspect`.

```sh
dotnet "$nightshift_cli" resume "$nightshift_session/resume" \
  --backend codex --enable-codex --codex "$nightshift_codex" \
  --sandbox read-only
nightshift_resume_exit=$?
printf 'Resume exit code (expected 0): %s\n' "$nightshift_resume_exit"
```

**Phase-two update:** new runs record worker identity and real-worker consent, so resume restores them without repeated worker flags and rejects incompatible backend changes. Legacy manifests that lack identity require explicit backend/permission selection. The explicit flags above remain valid when they match the saved backend/sandbox. Run bounds and working directory are restored from the manifest unless deliberately overridden within the supported rules.

Check the `resume/inspect/.../last-message.json` value against the input as in step 4. This tests the engine's durable continuation. It does not resume a Codex conversation: every worker stage is fresh. Ctrl-C during a model stage can leave an uncertain external effect that is repeated on engine resume; it may also consume additional model usage.

## What to keep after your trial

Retain the session directory and note which of the read/write/resume trials you actually ran, the selected CLI version, exit codes and any failure reason. Evidence from the real run is what lets us promote the adapter from fake-process tested to live-tested for that specific configuration. Avoid sharing whole raw logs without reviewing them for private content.

Further background: [adapter capabilities](codex-adapter.md), [specification decisions](spec-decisions.md), and official [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode) for JSONL and structured-response behavior. The examples were checked against repository code and the installed CLI help on 2026-10-29; their live outcomes remain unverified until you run them.
