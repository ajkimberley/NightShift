# Nightshift

Nightshift runs small DOT workflows with durable checkpoints, interchangeable
coding-agent backends and separate candidate verification. Version **0.3.0** defaults
to a protected local Linux-container profile. Worker success is a claim; approved
independent checks decide acceptance for an exact captured candidate.

[Protected workflow guide](docs/guides/protected-workflows.md) ·
[Phase-three results and limits](PHASE-3-REVIEW.md) ·
[Later work-porting guide](docs/work-porting-guide.md)

## Install and try offline

```sh
bash /Users/adamkimberley/Repos/nightshift/scripts/install.sh update
```

The helper installs/updates only your user's Nightshift .NET tool. It needs the
pinned SDK **10.0.103**; installed commands need the **.NET 10 runtime**. It does not
publish a package or change shell profiles. If the tools folder is absent from PATH,
add it for the current Terminal tab:

```sh
export PATH="$HOME/.dotnet/tools:$PATH"
```

Start the already configured local runtime yourself, then run:

```sh
podman machine start nightshift-dev
nightshift doctor
nightshift demo
nightshift reference --repository-service github --pipeline-service github-actions
nightshift reference --repository-service azure-devops --pipeline-service azure-pipelines
```

The demo/reference commands use no models/accounts. They exercise real containers
and offline HTTP fixtures. See [the installed local Podman runtime, start/stop commands
and prerequisite checks](docs/container-runtime.md). Nightshift verifies the named
local rootless connection and never installs, starts or reconfigures a runtime or
falls back to native execution. The initial image is pinned official Alpine and
contains fixtures, not live coding-agent binaries.

## Configure a protected workflow

In a small personal fixture project:

```sh
nightshift init
# Review/edit nightshift-policy.json: workflow, checks and pinned verification files.
nightshift adopt --policy nightshift-policy.json
nightshift run
nightshift result --latest
nightshift resume --latest
```

Adopted policies and new run state live outside the writable project and container
share. Resume retains backend/version, runtime/image/profile, coordinator hashes
and policy identity. Repository, pipeline and agent selections are independent;
real API translations are tested against fixtures. Live agent/network/auth, hosted
accounts, ADO Server and work-project integration remain separately authorized,
unverified steps. This is a bounded tested profile, not production certification or
a claim of arbitrary correctness or general sandbox-escape resistance.

## Develop and verify

With the approved runtime already running:

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build
python3 scripts/verify-phase3-package.py
python3 scripts/verify-package.py
```

The suite includes actual hostile containers; unavailable enforcement fails these
checks instead of skipping them. The second package script retains historical native
compatibility tests with explicit profile selection. Installed assets are checked
outside source and while source assets are hidden. Neither script starts a model.

Core retains the pinned Attractor sequential routing/retry/revisit behavior.
Start/exit, codergen, conditional, tool and human handlers remain supported.
Parallel/fan-in, supervision, persistent sessions, summaries, steering and per-tool
instrumentation remain unsupported. No internal model client or agent tool loop is
implemented. See [architecture](docs/phase-3-architecture.md),
[threat model](docs/phase-3-threat-model.md), [decisions](docs/phase-3-decisions.md) and
[acceptance evidence](docs/phase-3-acceptance.md).

Old runs remain inspectable and labeled untrusted. Legacy execution requires explicit
`--profile native-development` and lacks phase-three protection. Historical
[phase-two review](PHASE-2-REVIEW.md) and [guides](docs/guides/getting-started.md) are
preserved; their native commands require that explicit selection in 0.3.
