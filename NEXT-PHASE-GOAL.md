# Approved implementation brief: make Nightshift practical to install and use

**Approved by the user on 2026-10-29.** This document is ready to be linked or attached to a `/goal` request. Execute it when the user submits that request; the document-preparation turn does not start implementation. The scope and authorizations below apply during that execution. Real model runs remain excluded unless separately authorized.

Command examples describe the target interface to implement; they are not claims that those commands already exist.

---

Turn the existing Nightshift prototype into an installable, understandable CLI for everyday use on my Mac. A new user should be able to install it, configure a personal project, run a workflow with Codex, understand progress, find the result, and resume without rebuilding commands from source paths or maintaining shell variables.

Proceed autonomously through implementation and verification within the scope below. Make reasonable reversible choices, record assumptions and unresolved decisions, and leave `PHASE-2-REVIEW.md` with exact commands, evidence and limitations. Do not stop after a plan. Finish when the acceptance criteria are met, or preserve completed work and explain a genuine blocker.

## 1. Desired user experience

After installing Nightshift, the intended first-use sequence is:

```sh
nightshift doctor
nightshift demo
```

For a real personal project, the intended sequence is:

```sh
cd /path/to/my/project
nightshift init --backend codex --enable-codex
# Review the generated configuration and edit the workflow's task prompts.
nightshift run workflow.dot
nightshift status --latest
nightshift result --latest
```

Once a default workflow is configured, `nightshift run` should also work. Resume should be available as `nightshift resume --latest` and by explicit run path. Keep existing useful commands, including validate, inspect and events, unless a compatibility change is justified and documented.

These commands must work without user-created shell variables or paths into Nightshift's source/build directory. One-time installation/configuration steps are acceptable. Show the working project, selected backend and sandbox before executing a worker.

## 2. Working boundaries

- Work in `/Users/adamkimberley/Repos/nightshift`; preserve existing code, reference documents and run evidence. Use temporary personal fixtures for verification, never work repositories or systems.
- Keep the C# core library, CLI and meaningful tests. Use the installed stable .NET SDK; preserve reproducibility and avoid machine-wide installations/upgrades.
- Do not expand engine semantics into parallel execution, manager supervision, new providers, session reuse or other deferred spec coverage. Preserve existing routing, retry, status, cancellation and checkpoint contracts.
- Use fake Codex executables for development and automated integration tests. Do not invoke real models, paid APIs, login flows or account changes during implementation without a separate explicit instruction. Previously observed real smoke-test success may be recorded as historical evidence, not claimed as a new live test.
- Do not push, open PRs, publish packages to registries, deploy, send messages, change permissions or use sudo. Local package creation and local commits are allowed.
- When executing this goal, installing/updating **Nightshift itself for the current user** is authorized, after isolated package tests pass. Do not alter unrelated tools, the .NET SDK, Codex or shell startup files. If the command's installation directory is absent from PATH, explain the exact one-time setup step and report that limitation. Do not overwrite an unrelated package/command with a colliding name.

## 3. Implement in this order

### A. Package and install the CLI

Package the application as a .NET tool exposing `nightshift`. Build the package locally; no public registry publication is required. Include all demo/template assets in the installed package so execution does not depend on the checkout.

Provide clear local-source install, update and uninstall instructions or a small reviewed helper. Distinguish build-time SDK requirements from runtime requirements. Verify the package first using an isolated tool installation directory, then install/update this package for the current user as authorized above.

Prove the installed command runs from an unrelated directory and does not access source-folder assets. Include version/help output and actionable PATH/runtime troubleshooting. A self-contained native distribution, Homebrew formula and public release pipeline are deferred.

### B. Add Codex discovery and `doctor`

Resolve the Codex executable in this order:

1. Explicit command-line executable path.
2. Explicit project-configured executable path.
3. An actual external executable on PATH.
4. The known bundled macOS application location, when present.

Reject an invalid explicitly supplied path with a useful error rather than silently selecting another executable. Validate that the candidate is an executable file; handle spaces, missing paths, directories, aliases and empty values. Do not treat a shell alias/function as a process executable.

`nightshift doctor` should report installation/version, project/configuration discovery, resolved working/output directories, selected backend, Codex location/version, required CLI flag compatibility, and local authentication status where applicable. Use bounded read-only commands such as version/help/login-status; do not read credential files or print secrets. Distinguish stored authentication from verified live model access. Provide a clear next action for each failure and sensible nonzero status for blocking problems.

Discovery and doctor must never start a model, perform login, change account configuration or enable real workers automatically.

### C. Add small project configuration and `init`

Use one documented, versioned JSON project configuration file, provisionally `nightshift.json`. Avoid a general configuration framework or additional file formats.

Support a project working directory, default workflow, backend, optional Codex executable, sandbox, run-output root, finite run limits, and explicit persistent real-worker opt-in. Prefer a conventional project-relative run directory, such as `.nightshift/runs/`, with unique run names created automatically. Keep artifacts separate from source files.

`init` should create a minimal configuration and useful editable workflow template, explaining what was created. Codex templates must use supported truncate fidelity. Preserve existing files; never overwrite configuration, workflows or ignore rules silently. Add a narrowly scoped run-output ignore entry without replacing the project's existing ignore file. Initialization must not launch a workflow or model. In noninteractive use, missing required choices must fail clearly rather than hang.

Define and test precedence: explicit CLI options, then project configuration, then defaults. Search for the nearest project config from the current directory upward; stop at a documented boundary so unrelated parent projects are not selected accidentally. Resolve config-relative paths against the config's directory and CLI-relative paths against the invocation directory. Show the final working directory and path origins clearly. Support an explicit working-directory override.

Keep the current safe behavior for unconfigured use: never infer permission to run real Codex from executable discovery, a workflow's presence or model settings. `init --backend codex --enable-codex` is an explicit, visible choice to save permission for later real runs in that configured project. Document that future runs may consume usage. Default its sandbox to read-only; workspace-write must be deliberately selected. Do not store credentials.

### D. Preserve execution identity on resume

Persist the effective nonsecret run settings needed to restore the working directory, backend, executable selection, sandbox, limits, configuration provenance and real-worker opt-in. Resume should use the recorded settings even if project defaults later change.

**Never silently switch a real Codex run to the scripted backend on resume.** Reject an incompatible backend override. Allow a deliberate executable-path replacement for the same backend when appropriate, recording the override. Do not silently broaden the sandbox or obtain new real-worker permission from unrelated configuration.

Keep existing runs readable. If an older manifest lacks backend/consent metadata, require explicit selection instead of guessing. Preserve checkpoints, outcomes, transitions and all prior evidence. Resume continues engine state; it does not imply persistent Codex conversation reuse or exactly-once effects.

`--latest` must be scoped to the selected project. Status/result select its most recent run; resume selects its most recent resumable interrupted run and explains when none exists. Never select an arbitrary run from another project.

### E. Make progress and results understandable

Before worker execution, show project, workflow, backend, real-worker mode, sandbox and run directory. During execution, report stage name, visit, attempt, running/completed/failed state, retries and elapsed time. Provide periodic bounded progress while a long worker is quiet. Do not invent percentages, token usage or a fixed stage denominator for graphs with cycles.

At completion or failure, print a concise outcome, useful final response/error summary and exact paths to the run directory, checkpoint, final worker response and relevant logs. Distinguish worker claims from independent verifier results.

Make `status` and `result` useful without requiring the user to inspect raw checkpoint JSON. Add a minimal documented way to declare user-facing output paths and display which exist; never invent paths from model prose or label existence as proof of correctness. Always expose the workspace and response location even when no generated outputs are declared. Keep raw artifacts available.

Preserve useful exit statuses. Make redirected output readable without terminal escape noise; retain machine-readable commands/output for automation. An unsupported terminal should still receive useful plain text.

### F. Supply onboarding and visual guides

Add an installed `nightshift demo` that creates its scratch workspace and paths internally. The default demo is deterministic and requires no account, credentials or model. Label simulation clearly and show a tangible result, progress and evidence location. Include independent output verification.

A bounded real-Codex demo must require an explicit real-worker choice, default to read-only, make no automatic retries and show its runtime limit. A separate explicitly selected workspace-write example may produce a small file followed by a genuine local verifier. Never run either real mode automatically during tests or installation.

Rewrite README around the installed user journey. Provide separate short guides for:

- Install/update/uninstall and diagnose setup.
- First offline demo and what its result means.
- Configure and run a real personal workflow with Codex.
- Find output, inspect a failure, and resume.
- Edit a small workflow: task, explicit business fact/branch, independent check and optional queued human choice.

Keep advanced architecture/conformance material discoverable. Use self-contained copyable commands and expected results; avoid persistent shell variables in the basic user path.

Add static visual guides for first-use flow, graph-to-worker-to-verifier execution, and the run/output directory layout. Use repository-friendly Mermaid or SVG with readable text and links. No dashboard, HTTP host or separate website is required.

## 4. Acceptance evidence

Run the existing behavior suite and add focused tests for new behavior, including:

- Package build/install/update/uninstall in an isolated directory; invoke the installed tool outside the source checkout with packaged assets.
- Configuration precedence, relative paths, nested project discovery, malformed config, existing-file preservation and executable paths containing spaces.
- Missing/empty/nonexecutable/directory/alias Codex candidates; explicit invalid path must not fall through silently. Doctor subprocesses must time out cleanly and never start a model or leak credentials.
- Persistent explicit real-worker opt-in, unsupported fidelity errors, manifest settings, legacy-run handling and resume without backend switching. Changed project defaults must not redirect an existing run.
- Project-scoped latest-run selection, interrupted runs, successful runs and final failures.
- Readable progress/results and accurate output paths for success, verifier failure, cancellation and retry; scripts must still have usable exit codes and machine output.
- Offline installed demo from a fresh directory, without shell variables or prior project setup. Independent verification must fail when the artifact is wrong or absent.
- Real-mode command paths exercised with controllable fake Codex processes; no real model execution in the automated checks.

Execute the published onboarding commands against the installed package in clean temporary projects where feasible. Syntax inspection alone is not execution evidence. Record exact commands, observed results, failures and fixes. Keep the existing engine semantics intact; explain any necessary compatibility change rather than weakening tests.

The completion standard is: **someone unfamiliar with the implementation can install Nightshift, run its offline demo, configure a real Codex project, understand what is running, locate the result, and resume using short commands without source paths or shell-variable troubleshooting.** Clearly separate automatically verified behavior from any real-Codex steps left for me to run.

## 5. Handoff and stopping conditions

Keep a short progress note and useful local commits. Before finishing, review the complete user journey, repair concrete defects and write `PHASE-2-REVIEW.md` containing:

- What was installed, package version/location, PATH status, and exact install/update/uninstall instructions.
- The actual first-use and daily-use commands, with example configuration and visuals.
- Configuration precedence, persistent real-worker consent and resume decisions needing my review.
- Tests/onboarding checks actually executed, evidence locations, and any unverified claims.
- Compatibility changes, remaining friction, limitations and a recommended next experiment.

Stop when the scope and acceptance evidence are complete. Do not broaden into new orchestration features or keep working to fill time. If blocked, preserve the work, document the specific blocker and leave the handoff. Do not claim a real model run occurred when only fake-process tests ran.

---

## Approved design decisions

The user approved these choices as part of this brief:

1. **Distribution:** a locally built .NET tool, including a user-level Nightshift installation after isolated verification; no public package release or SDK/Codex upgrade.
2. **Daily operation:** project JSON configuration can retain an explicit real-worker opt-in, enabling short later run/resume commands. Initialization is explicit and defaults to read-only.
3. **Scope:** improve installation, configuration, diagnostics, progress/results and onboarding while keeping the existing engine feature set. Real-model calls remain outside autonomous implementation verification.

The installation approach follows Microsoft's [.NET tool installation documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) and [local-package/global-tool tutorial](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-use). The current checkout is an ordinary `net10.0` console project and has not yet been packaged as a tool.
