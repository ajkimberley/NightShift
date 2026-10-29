# Phase three decisions and assumptions

1. **Pinned orchestration baseline unchanged:** StrongDM Attractor
   `fb57a55ed97372a27ac90102f436947e29f48426`; original SHA/license remain in
   [provenance](reference/PROVENANCE.md). No unified model client, tool loop,
   multi-agent platform, parallel handler or new routing engine was implemented.
2. **One assembly boundary:** Core holds traversal, Appendix C outcomes and neutral
   immutable contracts. Adapters owns provider protocols, native compatibility,
   hosted HTTP, local Git, enforcement and protected composition. CLI composes them.
   Provider DTOs, SDKs and credentials never enter Core. No new production NuGet dependency.
3. **Protection is an execution profile:** `linux-container-v1` is the default.
   Native development requires explicit `--profile native-development` on execution;
   it has no phase-three assurance. Old data remains inspectable and labeled untrusted.
   No JSON-based conversion to protected evidence or protected legacy resume exists.
4. **Operator adoption:** `init` creates a reviewable policy; `adopt --policy FILE`
   stores immutable workflow/check/runtime/backend snapshots outside the project.
   Project `nightshift.json`, DOT edits and worker context cannot redirect active
   state or weaken selection. `--state-root` is an explicit operator argument only.
   A store marker rejects unrelated nonempty directories. Binaries are host-only;
   their hashes, runtime version/image, agent version/capabilities, policy and service
   selections are pinned per run. Changed binaries require a fresh run, not an
   unreviewed resume override. Deliberate tighter bounds persist; overrides cannot
   exceed the adopted policy.
5. **Fresh byte copies and stable collection:** no host .git is mounted. Runtime
   ownership is journaled before create. Cancellation removes the container with
   zero grace; crash resume recovers known units. Collection reads a bounded tar
   from the stopped container, removes it, then parses bytes without extraction.
   Traversal, symlinks, hardlinks, special files, .git, duplicate/case aliases and
   oversized content are refused. Directory fsync and malicious concurrent host
   writers remain outside the tested guarantee; worker races are stopped by unit removal.
6. **Protected acceptance extension:** Attractor SUCCESS/retry/revisit/goal_gate
   behavior remains traversal behavior. An external policy lists approved checks.
   After pipeline success, separate isolated verifiers run against fresh copies of
   the captured candidate. A coordinator-only evidence ledger binds repository,
   exact snapshot/commit, policy digest, check ID, owned unit/native run and result.
   Worker JSON/context/labels/status files cannot write this ledger. Human/worker
   provenance cannot satisfy code checks. Missing, pending, stale, unknown or
   cancelled evidence fails acceptance. A zero exit proves only the specified check.
   Pin every verification-definition dependency whose bytes must be authoritative;
   repository code executed by the check is still hostile input. Modified pinned
   definitions produce `definition-review-required` rather than running a new check.
7. **Coding-agent subset:** fresh truncate tasks, explicit immutable workspace/limits,
   bounded stdout/stderr, validated structured claims and cancellation. Codex consumes
   JSONL agent-message + terminal events; Claude consumes stream-json structured
   result. Invalid/partial streams and zero-exit error payloads fail. Explicit typed
   auth/config errors differ from worker/transport failure. Unknown errors remain
   unclassified failures; no invented usage or summaries. Sessions, steering,
   fidelity compaction and per-tool hooks are explicitly unsupported. Codex ignores
   user/rule configuration; Claude uses bare mode. Both use their existing loops.
   Image tools, not CLI permission flags, establish the process boundary.
8. **Retries:** adapter transport errors never replay an entire task. Explicit
   worker `retry` claims and graph timeouts retain existing bounded Attractor retry
   semantics; the network-disabled worker's partial workspace is discarded on
   cancellation. Hosted POSTs use a distinct durable intent and correlation ledger.
   One bounded list page (100 items) is searched; no unique match means uncertainty,
   never permission to resend. Authentication failure after a recorded intent may
   need explicit reconciliation/fresh intent. Exactly-once effects are not claimed.
9. **Hosted definition approval:** GitHub policy revision is `workflow_native_id:SHA256`
   of workflow bytes at the exact candidate. Azure is `build_definition_revision:SHA256`
   of YAML at the exact candidate. GH run-name must be
   `nightshift-${{ inputs.nightshift_correlation }}` and dispatch declares both
   correlation and candidate inputs. Azure queue uses `sourceVersion`, sourceBranch
   and `nightshift.correlation` properties. Actual native identity/repository/SHA/
   pipeline/correlation is checked on every observation. Referenced templates,
   actions, verifier scripts and service-side settings need separate review/pinning
   in a later live adoption; root YAML hashing alone is not transitive supply-chain assurance.
10. **HTTP:** GitHub REST `2022-11-28`; Azure DevOps Services `7.1`. Authentication
    is injected only on the host, requests are origin-bound, redirects disabled.
    GH log redirects may go once to explicitly permitted HTTPS Actions/blob storage
    using an anonymous client. Artifact URLs are metadata, never fetched with auth.
    Diagnostics omit response bodies/credentials; log text redacts the injected
    auth parameter. Arbitrary secret discovery/redaction is not claimed.
11. **Runtime assumption:** operator had already started the named VM. It was verified
    running/rootless/local before use; no startup/install/reconfiguration performed.
    All test containers use the already present official Alpine digest with
    `--pull=never`, network none, read-only root, UID 501, keep-id, zero capabilities,
    no-new-privileges, private PID/IPC, 64 pids, 256 MiB, one CPU, bounded tmpfs/logs.
    Snapshot/archive imports cap 4096 files/16 MiB, tar transport 32 MiB,
    worker stdout/stderr 2 MiB/1 MiB. Share disk-write quotas are not implemented;
    a hostile worker can consume disk within the dedicated workspace until timeout.
12. **Scope:** tested on macOS ARM64 + local Linux ARM64 rootless Podman 6.1.4.
    Linux-rootless composition is implemented but unexecuted here. Native Windows/
    macOS security, live agent images/network/auth, work systems, ADO Server,
    deployments and production/security certification remain unverified/out of scope.
