# Phase three review — portability and trusted execution

Nightshift 0.3.1 implements the approved local Linux-container profile, interchangeable
Codex/Claude coding-agent adapters, separate GitHub/ADO repository and CI services,
external coordinator-owned state, immutable approval snapshots and exact-candidate
verification. Hostile workers were actually exercised inside the supported runtime.
The [acceptance table](docs/phase-3-acceptance.md) links each property to its test.

The assurance is deliberately bounded: macOS 26.5 ARM64 host, local Linux ARM64
rootless Podman 6.1.4 (`nightshift-dev`), official immutable Alpine fixture image.
There were no model calls, work-system access, live hosted operations, remote pushes,
new accounts/credentials, machine changes or runtime startup/reconfiguration.

## Use the installed CLI

The current-user executable is `/Users/adamkimberley/.dotnet/tools/nightshift`.
If the default tools directory is absent from your Terminal PATH:

```sh
export PATH="$HOME/.dotnet/tools:$PATH"
nightshift --version
nightshift doctor
nightshift demo
```

Version is `nightshift 0.3.1`. The runtime must already be available. For a later
session, the operator starts it manually:

```sh
podman machine start nightshift-dev
```

The VM was already running when this task checked it and remains under operator
control. The demo uses real network-disabled containers and independent exact-byte
verification, with no model/account. For both complete offline hosted references:

```sh
nightshift reference --repository-service github --pipeline-service github-actions
nightshift reference --repository-service azure-devops --pipeline-service azure-pipelines
```

Each preserves a fixture project/run, uses a fake coding agent in a real container,
captures an exact local Git commit, exercises real HTTP translations through an
injected fake service, reconciles a lost dispatch response, and rejects stale evidence.
The JSON explicitly says actual isolation + fake HTTP; it is not live CI evidence.

For everyday fixture authoring, [follow the protected guide](docs/guides/protected-workflows.md):

```sh
nightshift init
# Review/edit nightshift-policy.json, including commands and pinned verifier files.
nightshift adopt --policy nightshift-policy.json
nightshift run
nightshift status --latest
nightshift result --latest --json
nightshift resume --latest
```

Default state is `~/Library/Application Support/Nightshift/state`, outside source and
VM sharing, with stable project-path hashes and unique run IDs. Only operator CLI
`--state-root` can select another fresh external root. Project config cannot redirect
an active run. Old runs remain readable and labeled untrusted; native execution now
requires explicit `--profile native-development`. Protected resume refuses legacy
checkpoints. Coordinator hash/runtime/backend/profile changes require a fresh run.

## Exact build, test and package commands

Run from `/Users/adamkimberley/Repos/nightshift` with the approved runtime available:

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --logger 'console;verbosity=normal'
dotnet test -c Release --no-restore --logger 'console;verbosity=normal'
python3 scripts/verify-package.py
python3 scripts/verify-phase3-package.py
bash scripts/install.sh update
```

`verify-package.py` retains existing installed native behavior/negative checks with
explicit legacy selection, installing 0.2.0 then updating to 0.3.1 in an isolated tool
directory. It does not rewrite phase-two evidence. `verify-phase3-package.py` installs
0.3.1 outside source, hides source assets during installed commands, checks adopted
state/resume despite hostile project defaults, exercises both hosted reference
fixtures, restores assets in finally, then uninstalls its isolated tool. The user
update occurs only after these isolated checks pass and changes only Nightshift.

Evidence is under [docs/evidence/phase-3](docs/evidence/phase-3/). Final observed
counts, package results and installation confirmation are recorded in the closing
verification record below. Runtime tests are mandatory tests with zero skips;
missing runtime infrastructure would fail, not downgrade the phase.

## What is enforced

Workers and checkout-supplied tools/verifiers receive only fresh disposable byte
copies mounted at `/workspace`, private tmpfs scratch and approved image assets.
No state, coordinator binaries, host .git/home, service credentials, privileged
handles or runtime sockets are mounted. The profile is nonroot, read-only rootfs,
network none, all capabilities dropped, no-new-privileges, separate PID/IPC and
bounded CPU/memory/pids/logs. Runtime identity is checked on every launch; only the
approved local rootless connection/share and immutable available image are allowed.
No runtime start, implicit pull, remote context or native fallback exists.

Container ownership is journaled before create. Cancellation force-removes it with
zero grace; abrupt coordinator death is recovered by recorded unit identity on
resume. Logs are coordinator-captured with bounded draining. Collection occurs
from a stopped container, removes the execution unit, and parses bounded tar bytes
without extracting links. Traversal/symlink/hardlink/rename/special/.git attacks
are refused. Fresh host copies eliminate pre-existing writable aliases.

Attractor outcome/routing/retry/revisit semantics remain. Appendix C statuses remain
audit records. Worker completion and command exit zero cannot write the separate
protected evidence ledger. Acceptance binds repository, exact immutable candidate,
approved policy, check ID, execution/native run and terminal result. Changing code
invalidates the old candidate's evidence. Modified pinned check definitions require
review; PATH-selected workspace tools cannot replace the image's selected tools.
Git plumbing captures ignored files and bypasses attribute/filter conversion.
Evidence ordering uses durable sequence rather than wall-clock timestamps.

## Capabilities, assumptions and limits

The [decision record](docs/phase-3-decisions.md), [protocol sources](docs/phase-3-sources.md)
and [porting guide](docs/work-porting-guide.md) contain full assumptions and API pins.
GitHub REST `2022-11-28` and ADO **Services** `7.1` implement PR create/read and check
trigger/reconcile/observe/cancel/log/artifact paths against fixtures. Agent, repository
and pipeline choices are independent; all four hosted combinations are tested.
Native IDs/repository/SHA/pipeline/correlation must match. A lost POST response leaves
an intent to reconcile; missing/ambiguous results never permit blind resubmission.
Searches are bounded to 100 results, with uncertainty rather than an exactly-once claim.
Call `AdvanceAsync` again to observe pending/running checks. Cancellation of waiting
is separate from deliberate hosted cancellation. No merge/delete/policy changes exist.

Fresh truncate tasks, bounded completion claims, cancellation and captured normalized
progress events are supported. Sessions, steering, compaction/summary fidelity,
per-tool instrumentation and usage reporting are not invented. Claude uses bare
mode; Codex ignores user/rule config. The official Alpine image contains shell
fixtures, not live agent/.NET installations. No live network/auth profile is tested.
Actual Claude CLI compatibility, real Codex tasks, live providers/accounts, ADO
Server version, Linux-host enforcement and the work port are separate experiments.

The trusted base is coordinator/approved policies, host OS and selected runtime.
Malicious host administration and kernel/runtime exploitation are excluded. Disk
write quotas, transitive pipeline supply-chain approval, power-loss directory fsync,
arbitrary secret discovery/redaction and general program correctness are not claimed.
Pinned verifier dependency closure is an operator responsibility. No production,
compliance or general sandbox-escape assurance is asserted.

## Review priorities and preserved work

Five files to review first:

1. [TrustedContracts.cs](src/Nightshift.Core/TrustedContracts.cs) — immutable provider-neutral contracts and exact-candidate acceptance.
2. [PodmanLauncher.cs](src/Nightshift.Adapters/PodmanLauncher.cs) — prerequisites, local identity, enforced process/mount profile and lifecycle.
3. [Snapshots.cs](src/Nightshift.Adapters/Snapshots.cs) — fresh copies, bounded stable collection and path/link rejection.
4. [ProtectedWorkflow.cs](src/Nightshift.Adapters/ProtectedWorkflow.cs) — approved identity, isolated handlers/verifiers, resume and evidence.
5. [IsolationTests.cs](tests/Nightshift.Tests/IsolationTests.cs) — hostile paths, useful edits, forgeries, links, children and fail-closed evidence.

Also inspect `HostedWorkflow.cs`, the GH/ADO adapters and `HostedAdapterTests.cs` for
side-effect correlation/reconciliation. Core has no provider DTOs, runtime SDK or
credentials. The pinned Attractor source/license is unchanged; no unified model
client, agent loop or multi-agent platform was implemented.

Initial goal/README/runtime local edits were inspected and their intended runtime
setup retained. Pre-existing goal/runtime files are left as supplied, without
including them in implementation commits. Historical run data and phase-two evidence
were not rewritten. Local commits and progress notes preserve the implementation.

Development failures are retained: nonexistent-path reparse detection, fixture
correlation offset, explicit legacy-profile expectations/schema audit import, DOT
fixture escaping and Podman default removal grace. Corrections passed the original
assertions. Two empty workspaces left by the initial cleanup timeout were removed
by their exact owned IDs after confirming no containers remained; evidence is kept.

## Next experiment

No work-system access is needed to review this result. Start with the installed
offline demo/reference commands. A later deliberately authorized work port should
first rerun the hostile suite on its chosen runtime, then validate one real Linux
agent image/auth/egress profile, followed by one exact-SHA hosted PR/check path.
Confirm the actual ADO Server version if applicable. Do not treat successful fixture
translations or root YAML hashing as live environment compatibility.

The closing timeout audit added a protected opt-in for resumable graph deadlines
and explicit per-check deadlines with durable pending/cancelled evidence. The
interim 0.3.0 user/package evidence is preserved under `interim-0.3.0`; the corrected
package is 0.3.1. Existing native deadline/routing/retry semantics remain unchanged.

## Closing verification record

Pending final transcript/installation collation.
