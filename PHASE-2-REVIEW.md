# Phase-two review — installed Nightshift and everyday use

**Delivered and installed: Nightshift 0.2.1 for your user.** The package, installed commands, project configuration, saved worker identity, diagnostics, progress/results and onboarding are implemented. **116 tests pass in Debug and Release.** No model-backed invocation or login flow ran during this phase.

## Start using it

The installed executable is `/Users/adamkimberley/.dotnet/tools/nightshift`. The default tools folder is **missing from the current PATH**. No shell startup file was changed. In your Terminal tab, run this standard one-time path setup:

```sh
export PATH="$HOME/.dotnet/tools:$PATH"
nightshift --version
nightshift doctor
nightshift demo
```

Expected version: `nightshift 0.2.1`. The demo is offline, creates its own scratch paths, writes a tangible file and independently verifies its exact bytes. Completion prints its output, final response and evidence paths. No manually maintained repository/executable/run variables are needed.

For a real personal project:

```sh
cd /path/to/your/personal/project
nightshift init --backend codex --enable-codex
nightshift doctor
```

Review the new `nightshift.json` and edit the generated `workflow.dot` prompt, then:

```sh
nightshift validate
nightshift run
nightshift status --latest
nightshift result --latest
nightshift resume --latest
```

The last command is useful when a run is interrupted; when none is resumable it explains why. Init saves explicit permission for future real calls in this project and defaults to read-only. For writing, deliberately choose workspace-write and add an independent check. Real calls may consume usage. Those real-model commands are instructions for you, not claimed execution evidence from this phase.

## Install, update and uninstall

Local package: `artifacts/packages/Nightshift.Cli.0.2.1.nupkg`. Tool package ID: `Nightshift.Cli`; command: `nightshift`. Actual install/update evidence: [initial install](docs/evidence/phase-2/user-install.txt), [update to 0.2.1](docs/evidence/phase-2/user-update.txt). No registry publication occurred.

```sh
bash /Users/adamkimberley/Repos/nightshift/scripts/install.sh install
bash /Users/adamkimberley/Repos/nightshift/scripts/install.sh update
```

When you choose to remove the installed command:

```sh
bash /Users/adamkimberley/Repos/nightshift/scripts/install.sh uninstall
```

The helper builds with installed SDK 10.0.103 and manages only this package for the current user. The installed command needs .NET 10 runtime, not source/build paths; this machine has runtime 10.0.3. The helper checks command collisions, uses the local package source and does not change .NET, Codex, account permissions or shell startup files. It retains project configuration and run evidence on uninstall. Package lifecycle uninstall was tested in isolation; the actual user installation is intentionally retained.

## What changed

| Area | Delivered behavior |
|---|---|
| Installation | Framework-dependent .NET tool, local package, version/help and install/update/uninstall helper; all demo/template assets included. |
| Doctor | Resolved installation/project/workspace/output/backend/sandbox, Codex location/version/required flags and stored-auth status. Bounded probes, sanitized summaries, actionable failures; no model/login. |
| Project setup | Versioned `nightshift.json`; explicit init opt-in, editable truncate template, no silent config/workflow overwrite, scoped ignore append. |
| Paths and precedence | CLI > project > defaults. Config paths from config directory; CLI paths from invocation. Nearest config with Git/home boundary; unconfigured project is invocation directory. |
| Resume | Recorded backend, executable, sandbox, consent, limits, workspace, outputs and provenance. Current defaults cannot switch the worker. Audited same-backend executable and deliberate limit overrides persist. Legacy runs require explicit identity. |
| Visibility | Stage/visit/attempt/retry/elapsed-time messages, five-second quiet-stage heartbeat, concise final response/errors and exact output/log/checkpoint paths. |
| Results | Human `status`/`result`, project-scoped latest selection, declared output existence separate from worker claims/tool verdicts, JSON stdout isolated from progress stderr. |
| Demos | Installed offline demo; explicit bounded real read/write modes with independent local verifiers. Default demo stays offline even in a real-worker project. |
| Preservation | Old checkpoints/manifests and attempts retained; nonempty new run folders refused; numeric input overflow is an input error. |

The engine's feature scope remains sequential. No new parallel/supervisor/session/provider implementation was added. The only execution-level change beyond metadata is bounded cancellation log draining: kill attached children, drain already-emitted output for up to one second, then stop daemon-held pipe waits.

## Guides and visuals

- [Install and first demo](docs/guides/getting-started.md): first-use Mermaid visual, expected terminal output, runtime/PATH troubleshooting.
- [Configure a Codex project](docs/guides/codex-project.md): complete config, precedence/discovery, real writing/checking workflow.
- [Results, failures and resume](docs/guides/results-and-resume.md): directory tree, graphical artifact map and execution diagram.
- [Author a workflow](docs/guides/workflows.md): task/check/repair/human graph, explicit business facts and queued choices.
- [Implementation decisions](docs/phase-2-decisions.md) and [requirement audit](docs/phase-2-acceptance.md).

README now starts with installed usage. Phase-one walkthrough/review/evidence remain available and are marked as historical where behavior changed.

## Verification actually executed

| Command/check | Result |
|---|---|
| `dotnet test --no-restore --logger 'console;verbosity=minimal'` | **116/116 passed**; [Debug transcript](docs/evidence/phase-2/tests-debug.txt). |
| `dotnet test -c Release --no-restore --logger 'console;verbosity=minimal'` | **116/116 passed**; [Release transcript](docs/evidence/phase-2/tests-release.txt). Builds clean with warnings treated as errors. |
| `python3 scripts/verify-package.py` | Passed: local pack/install 0.2.0/update 0.2.1/uninstall in isolated tool directory; invoked `nightshift` by name outside checkout. Source assets temporarily hidden during demo/init and restored in finally. [Full transcript](docs/evidence/phase-2/package-lifecycle.txt). |
| Installed onboarding | Doctor, offline demo, init, validate, default run, status/result, saved real identity on resume despite changed defaults; fake Codex executable path contains spaces. |
| Documented example workflows | Extracted DOT blocks validated and executed with fake Codex; exact report bytes checked; queued accept and revise/accept paths executed. |
| Negative installed verification | Paused demos after creation, deleted/corrupted output, resumed: verifier failed and pipeline stayed failed. |
| Actual user installation | Initial install 0.2.0, then helper update to **0.2.1**. Current user's installed version, doctor, offline demo and result executed from a separate directory with the documented PATH addition. Saved demo limits verified. [Transcript](docs/evidence/phase-2/user-installed-smoke.txt). |
| Actual Codex diagnostics | Read-only version/help/login-status probes recognize 0.164.0-alpha.11.1, required flags and stored authentication. Live model access remains unverified in this phase. |
| Shell/docs checks | Install helper and 25 current-user-guide shell blocks syntax checked; local documentation links checked; Mermaid diagrams reviewed in source; `git diff --check` clean. Real-model guide blocks were not executed. |

The package check runs public CLI commands, not just syntax inspection. Tests were authored during implementation, not independent assurance by another party. Historical user-run real read/write/resume smoke evidence is preserved at `runs/real-codex-2W4pNMHw/`; it is not presented as a new 0.2.1 live test.

Development failures were retained: duplicate asset packaging was corrected; an interim build overlapping new type creation failed before the coherent build; expanded tests exposed a subprocess fixture/output-draining race. The fixture now signals readiness after stdout emission and the implementation drains emitted logs with a bound. No assertions were weakened/deleted. The completion audit also found and fixed persistence of deliberate resume-limit overrides, and added nonempty-run-folder/input-overflow protection. See [progress](PHASE-2-PROGRESS.md) and the evidence directory.

## Architecture and review priorities

`Nightshift.Core` still owns engine semantics and durable runs. Optional `ExecutionSettings` extends manifests compatibly, with resume snapshots/history written under the existing run lease. `Nightshift.Cli` composes project config, discovery, diagnostics, handlers, views and progress; packaged Assets provide workflow/demo templates. No new production NuGet dependency was introduced.

Five files to read first:

1. [CliApplication.cs](src/Nightshift.Cli/CliApplication.cs) — installed commands, real-worker guards, run/resume composition.
2. [Projects.cs](src/Nightshift.Cli/Projects.cs) — configuration/discovery/path origins and init preservation.
3. [CodexDiscovery.cs](src/Nightshift.Cli/CodexDiscovery.cs) — executable selection and bounded/redacted probes.
4. [UsabilityTests.cs](tests/Nightshift.Tests/UsabilityTests.cs) — concrete acceptance behavior, negative cases and fake workers.
5. [verify-package.py](scripts/verify-package.py) — package lifecycle and installed onboarding evidence.

Most consequential choices: trusted project config retains paid-worker opt-in; resume rejects backend/sandbox/workspace changes rather than silently guessing; same-backend executable and explicit bound overrides are saved with history; `--latest` uses the selected project/output root; output existence and command/model verdicts are separate claims. Full details are in the [decision record](docs/phase-2-decisions.md).

## Remaining friction and next experiment

No implementation blocker remains. The permitted environment limitation is PATH: the user installation is complete and verified, but unqualified `nightshift` requires the one-line PATH addition in your Terminal. No shell profile was modified automatically.

Other limits: only macOS arm64/Unix shell usage was verified; package is framework-dependent; config/workflows must be trusted; host tool commands are not covered by the Codex sandbox; real-call usage is not capped by a timeout; full/compact/summary fidelity, persistent Codex conversation reuse, parallel/supervisor execution and HTTP/UI remain deferred. `--latest` searches the configured output root; use explicit paths after relocating it. Directory fsync/power-loss guarantees and cleanup of detached/reparented daemons remain unverified limitations inherited from phase one.

Recommended next experiment: after the PATH step, run `nightshift demo`, then deliberately run the explicit real read demo `nightshift demo --backend codex --enable-codex`. If that passes, try the explicit write mode and inspect `nightshift result --latest`. Those are one-model-stage trials with real verifier stages. No further implementation or model run is required to inspect the completed local usability work.
