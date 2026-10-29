# Nightshift

Nightshift runs personal workflows written as small DOT graphs. It can hand tasks to your Codex CLI, run independent local checks, pause for a choice, and save enough state to resume after interruption.

**Start here:** [install and run your first demo](docs/guides/getting-started.md). The demo needs no account or model. For real work, follow [configure a Codex project](docs/guides/codex-project.md).

## Install on this Mac

From Terminal:

```sh
bash /Users/adamkimberley/Repos/nightshift/scripts/install.sh install
```

This builds a local .NET tool package and installs `nightshift` for your user. It does not publish a package, install/upgrade .NET or Codex, or change shell startup files. Installation requires the pinned .NET SDK **10.0.103**; the installed command requires the **.NET 10 runtime** and can run outside this checkout. Shell tools/demos require Git and `/bin/sh`; macOS arm64 is the verified platform.

If installation reports the tools directory is absent from PATH, run this once in the current Terminal tab:

```sh
export PATH="$HOME/.dotnet/tools:$PATH"
```

Then:

```sh
nightshift --version
nightshift doctor
nightshift demo
```

Doctor checks setup without starting a model. In an unconfigured project, missing Codex/auth is a warning so the offline demo can still run. The demo prints progress, creates a tiny file, verifies its bytes with a local command, and prints exact output/response/log paths.

## Use a personal project with Codex

Change into your personal repository, then:

```sh
nightshift init --backend codex --enable-codex
nightshift doctor
```

Review `nightshift.json` and edit the generated `workflow.dot` task prompt. Initialization saves your explicit permission for later Codex calls in this project and defaults to read-only. It never starts a worker. Real runs may consume account usage or incur charges.

```sh
nightshift run
nightshift status --latest
nightshift result --latest
```

Codex is discovered automatically. Set an explicit path only if discovery cannot find your intended executable. Each Codex stage starts fresh with truncate fidelity; files can carry work across stages. To edit files, deliberately configure `sandbox` as `workspace-write` and add an independent check. See [a complete small writing workflow](docs/guides/codex-project.md).

## Understand, inspect and resume

```sh
nightshift resume --latest
nightshift validate
nightshift help
```

Resume selects the latest interrupted run **in this project**, restoring its recorded backend, permission, working directory and sandbox. It never silently changes a real run to simulation. Prior attempts remain on disk; an interrupted external effect may replay. Existing legacy runs without recorded worker identity require explicit backend selection.

- [First-use visual guide and installation](docs/guides/getting-started.md)
- [Project configuration, Codex discovery and real workflows](docs/guides/codex-project.md)
- [Results, directory map, failures and resume](docs/guides/results-and-resume.md)
- [Workflow authoring: tasks, checks, branches and human choices](docs/guides/workflows.md)
- [Phase-two handoff and actual verification](PHASE-2-REVIEW.md)

## Development and limits

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build
python3 scripts/verify-package.py
```

The package verifier installs, updates and uninstalls in an isolated directory. It exercises installed commands with fake Codex processes and preserves its transcript under `docs/evidence/phase-2/`. It never starts a model.

Implemented handlers: start/exit, codergen, conditional, tool and wait.human. Parallel/fan-in, manager supervision, loop_restart, per-tool hooks, persistent sessions, model summaries and HTTP/UI remain deferred. This is a personal learning implementation with partial conformance. Worker success is a claim; independent tool stages provide separate command evidence. A zero exit or an existing file is not proof of arbitrary correctness.

Technical references: [architecture/spec decisions](docs/spec-decisions.md), [conformance](docs/conformance.md), [Codex adapter contract](docs/codex-adapter.md), [original implementation review](MORNING-REVIEW.md), [pinned upstream source/licence](docs/reference/PROVENANCE.md). Historical phase-one source-based commands and evidence remain available; installed usage above is the primary user path.
