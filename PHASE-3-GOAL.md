# Draft goal: portability and trusted execution

**For review only. This goal is not active.** Creating this document authorizes no implementation, runtime installation, account changes, model calls or work-system access. Once approved, this document can be linked to an explicit `/goal` request.

## Decisions to review before approval

1. **Initial isolation target:** propose a local Linux-container execution profile, usable from macOS development and later Linux CI. Native Windows/macOS execution security is not included automatically. Neither `docker` nor `podman` was found on the current PATH when this draft was prepared. A supported local runtime must be made available before the isolation acceptance gate can pass. Runtime installation is not authorized by this draft or proposed implementation scope.
2. **Integration target:** propose GitHub/GitHub Actions and **Azure DevOps Services** for a small common repository/CI workflow. Azure DevOps Server compatibility needs its actual version confirmed separately. Adapters will be exercised against fixtures, not work accounts.
3. **Completion level:** working adapter implementations and contract tests, plus an actually exercised filesystem isolation profile. Live agent/provider/account compatibility and the actual port into the work project remain separate, explicitly authorized steps.

Command/interface names below describe intended behavior, not existing features. These choices can be changed during review; do not replace a required security property with a weaker claim to make the phase pass.

---

## Goal

Prepare Nightshift for a later port into my work project by separating orchestration from coding-agent backends, repository/CI services and execution infrastructure, and by preventing an agent from changing authoritative run state or manufacturing verification evidence.

Implement and verify the approved scope autonomously. Preserve existing work, record assumptions and specification decisions, and leave `PHASE-3-REVIEW.md` with exact commands, evidence, capabilities, limitations and porting instructions. Do not stop after producing interfaces or a plan. If an environmental requirement blocks progress, continue independent work where possible, preserve it, and report the specific blocker without claiming full completion.

## 1. Boundaries and non-goals

- Work in this personal Nightshift repository and its local test fixtures. Creating Nightshift-owned private state directories outside the checkout is authorized when this goal is approved and executed, because that separation is a deliverable. **Do not access, clone, inspect or modify the work project, work systems, work repositories, pipeline endpoints or credentials.** This phase prepares the port; it does not perform it.
- Use C# and the existing supported .NET toolchain. Split projects only at real dependency/trust boundaries. Retain the installed CLI user experience and meaningful existing tests.
- Preserve the pinned Attractor baseline and external graph/status names. Record protective divergences and new acceptance-policy extensions explicitly. Preserve routing/retry/revisit semantics rather than silently rewriting them around a security feature.
- The seam is for **coding-agent backends**, such as Codex and Claude Code. Do not implement `unified-llm-spec.md`, `coding-agent-loop-spec.md`, a custom model client, an internal tool-use loop, or a multi-agent platform. The unified client spec is an architectural reference for normalized data/errors/capabilities, not an implementation requirement.
- Use fake agent executables, local Git fixtures and fake HTTP services for integration tests. Use real isolation-runtime processes for isolation tests. Do not invoke models, paid APIs, login flows or real hosted repository/CI operations without a separate explicit instruction.
- Network access is allowed for public authoritative documentation and necessary development dependencies. Any test-image download must use an explicitly identified official base image, record its resolved digest, and stay within bounded local verification. Never connect to a remote/work container context.
- No sudo, machine-wide installs/upgrades, changes to existing account/system permissions or SSH trust, new host/provider accounts or credentials, host provider-CLI installs/upgrades, shell-profile edits, remote pushes/PRs, deployments or package publication. Creating restrictive permissions and execution-policy records for new Nightshift-owned artifacts is allowed; altering unrelated user/system resources is not. Local commits/packages are allowed. After isolated package checks pass, updating only the current user's Nightshift installation is allowed when the goal is approved and executed.
- No production-readiness, compliance, general sandbox-escape resistance, or live work-environment compatibility claim. State exactly what the tested profile guarantees and assumes.

## 2. Threat model and architecture first

Create `docs/phase-3-threat-model.md` and a compact architecture diagram before committing to an isolation mechanism.

Assume the worker can ignore prompts, run arbitrary programs, spawn children, know the host run-store path, modify its writable checkout, and forge its own text, JSON and artifacts. Include repository-controlled tests, hooks, tool commands and pipeline YAML as potentially agent-modified input. Trust the coordinator, its installed binaries, approved policies, host OS and selected isolation runtime; kernel/container-runtime exploitation and a malicious host administrator are outside this phase's assurance claim.

Separate:

- **Trusted coordinator:** graph traversal, approved configuration/workflow/policy snapshots, credentials for repository/CI services, authoritative state/evidence and acceptance decisions.
- **Restricted execution:** coding-agent processes and any checkout-supplied programs, with a deliberately bounded workspace and environment.
- **Untrusted exchange:** worker responses and candidate artifacts collected under a documented protocol. They never become authoritative just because they are valid JSON or named `status.json`.

The architecture must allow choosing the agent, repository service and pipeline service independently. Provider/runtime SDK types and protocol details must not leak into the orchestration core.

## 3. Establish protected storage and enforced execution

### A. Coordinator-owned run store

Move new run state outside the writable project/worktree and worker scratch/exchange areas. Use an explicit host state root with stable project/run identity, initially a conventional user state directory on the development host. Make it configurable for later work/CI environments.

The coordinator alone writes checkpoints, manifests, execution identity, trusted workflow/policy snapshots, verification records and audit logs. Keep atomic persistence, exclusive leases, historical attempts and resume semantics behind a narrow run-store contract. Never use the project file or agent-provided context to redirect an active run's protected root or broaden its policy.

Protect the coordinator's binaries, Git administration data and approved control/verification policy as well as checkpoint files. A worker changing project configuration, a DOT file, Git hooks/config or pipeline YAML must not silently change what the coordinator trusts. Provide a small explicit operator-controlled adoption/update path for policy changes; do not build a general enterprise permission platform.

### B. One real isolation profile

Implement a worker-launch contract with one genuinely enforced profile for the approved initial target. The proposed first profile is local Linux containers. Verify local-runtime prerequisites and context identity before use. Do not install, start or reconfigure a container daemon/VM, or redirect to a remote context, automatically. Launching bounded test containers on an already available local runtime is within scope.

Give the worker only its permitted checkout, private scratch/exchange area, required executable/runtime assets and explicitly approved environment. Do not expose the protected run store, host home, host service credentials, inherited privileged handles, Docker/Podman socket, host PID namespace or broad host mounts. Use an explicitly constrained nonprivileged profile; do not use privileged containers, sandbox bypass flags or an unsandboxed retry fallback. Use network-disabled fixtures for adversarial tests. Describe live network/auth requirements separately and do not claim an untested live profile is protected.

The same enforcement boundary must cover native agent editing tools, shell tools and descendants. An agent's own permission prompts, file conventions, hidden directories, chmod on a directory owned by the same user, or a moved path alone are insufficient evidence.

Checkout-supplied code used by a verifier/tool must also run without access to protected state/credentials. A coordinator-selected check may be trusted as a policy, while the code it builds/runs remains untrusted.

Cancellation/timeout must terminate the owned execution unit, bound log draining and leave resumable coordinator state. Unsupported or unavailable enforcement must **fail closed before the agent starts**. A legacy native-development mode, if retained, must require explicit selection and say it lacks this protection; never advertise it as the work profile or silently fall back to it.

### C. Narrow exchange and artifact ingestion

Remove `ATTRACTOR_STAGE_DIR` and any equivalent authoritative path/capability from worker and untrusted tool environments. Capture stdout/stderr through coordinator-owned channels. If a CLI requires an output file, place it in the untrusted exchange area and import bounded, schema-validated bytes; the parent writes the authoritative record.

Retain Appendix C outcome serialization as an audit format, but stop treating a file left in an agent-writable location as an authoritative status or verifier result. Any compatibility path must label its contents as an untrusted claim and cannot satisfy a protected acceptance gate.

Reject traversal and unsafe symlink/hardlink/rename paths during ingestion. Avoid validation-then-open races; stop/freeze owned writers or collect a stable snapshot before trusting artifact identity. Host-side collectors must not follow a worker-created link into protected files. Use fresh isolated copies or another justified mechanism to avoid pre-existing writable aliases to trusted host files.

## 4. Refactor the coding-agent seam

Retain Attractor's codergen handler semantics through a small compatibility facade where needed, while making the implementation boundary agent-oriented.

Use a narrow immutable task request and structured result/event contract: task instructions, approved context snapshot, workspace access, limits and declared capabilities. Do not pass the mutable graph/context, run-store object, host file handles or arbitrary coordinator callbacks into a provider backend.

Implement separate Codex and Claude Code process adapters using current official documentation and any already-installed read-only help. Reuse their existing coding loops. Separate argument construction, output/error translation, cancellation and capability mapping from the protected launcher. Run both through the same enforcement contract.

Support a coherent initial common subset: fresh tasks, structured completion claims, bounded execution, cancellation and progress where available. Explicitly report unsupported session/fidelity/steering/hooks behavior. Reject unsupported requested behavior; no silent provider changes, invented usage or pretend summaries. Preserve the current deterministic backend for offline orchestration tests.

Agent completion is a **claim**, distinct from successful independent verification. Do not blindly apply HTTP/client retries to a whole task that may already have external effects. Distinguish transient transport/worker failure, incomplete/cancelled execution, terminal configuration/authentication error and explicit task failure. Persist the selected backend/runtime/profile and their versions/capabilities so resume cannot switch or weaken them silently.

## 5. Separate repository and pipeline services

There is currently no engine-level GitHub service implementation to replace; ordinary Git operations and hosted-service APIs are different boundaries.

Keep shared Git workspace/snapshot/ref operations separate from hosted repository metadata/PR operations, and keep repository hosting separate from CI. The worker must not receive repository/CI credentials or be able to modify host Git hooks/config used by the coordinator. Worker-private disposable Git metadata is allowed where an agent requires a repository, but the host must not adopt its hooks/config/ref claims as trusted administration data.

Implement narrow GitHub and Azure DevOps Services adapters for the reference path, with injected host-only authentication and fake HTTP fixtures. Do not introduce an OAuth/SSO platform or a universal provider factory.

Required common reference path:

1. Resolve a repository and exact candidate revision using shared Git/local fixtures and backend-neutral identities.
2. Read/create a pull request for that candidate through the selected repository service. No automatic merge, deletion or policy changes.
3. Request a configured GitHub Actions/Azure Pipelines check, obtain/reconcile its actual native run identity, observe it to a terminal result, and support documented cancellation.
4. Retrieve bounded log output and artifact metadata/references for that identified run. Preserve native IDs/links in adapter metadata without requiring the core to understand provider DTOs.

Implement real request/response translations for this small path; interfaces, TODOs or unconditional-success fake implementations do not constitute hosted adapters. Unsupported operations outside the path must be explicit. Pin the documented API versions used, and do not assume Azure DevOps Services means a particular on-premises Server version is supported.

Model pending/running/passed/failed/cancelled/unknown states conservatively. Triggering a workflow is not a passing check. Never attach "the latest build on this branch" as proof for a different commit. Use authentic provider responses plus candidate/pipeline/run correlation, not worker-supplied URLs or status text.

Persist intent/correlation before side effects. Handle timeout-after-dispatch and process interruption without blindly creating duplicate PRs or CI runs. When an outcome cannot be reconciled safely, record uncertainty and require reconciliation; do not claim exactly-once hosted effects. Keep authentication/authorization failures distinct from retryable transport errors and redact secrets in diagnostics/logs. Do not forward credentials to arbitrary artifact URLs or redirects.

## 6. Bind verification to the exact candidate

Introduce provenance-tagged evidence separate from mutable worker context and existing stage outcome strings. Distinguish worker claims, controlled local checks, hosted CI results and human decisions. A worker cannot assert or overwrite the protected verification namespace through JSON/context aliases, labels or routing suggestions.

For code acceptance, bind evidence to repository identity, exact commit or immutable snapshot digest, approved verifier/policy revision, check/run identity and terminal result. If code changes after a successful check, that evidence still describes the old candidate and cannot accept the new one. A dirty/mutable checkout needs a captured snapshot, not an assumed HEAD identity.

Use an approved immutable verification definition. An agent changing/skipping tests, replacing an executable via PATH, or editing pipeline YAML must not alter the authoritative check unnoticed. Scope the guarantee to the specified trusted checks; do not claim general task correctness from a zero exit or a test report alone.

Preserve the Attractor outcome/routing contract and document any new protected acceptance-policy extension explicitly. Worker SUCCESS may complete a task stage; it must not manufacture the separate code-acceptance decision. Pending, missing, unknown, cancelled, mismatched or stale evidence never passes that decision.

Demonstrate one full local reference workflow: fake agent produces a candidate, repository/CI adapters use local fixtures, protected evidence accepts the matching checked revision, a later change invalidates acceptance, and an interrupted run resumes without changing backend/profile or duplicating hosted effects.

## 7. Preserve usable CLI behavior and existing evidence

Extend configuration, doctor, run/status/result/resume and installed packaging for agent/repository/pipeline/profile selection. Use explicit capabilities and clear identities rather than a generic ambiguous `provider` switch. Keep simple configured daily commands and offline demos; model/service effects still require explicit permission.

Doctor must distinguish: configured; fixture/contract tested; actual isolation verified on this platform; authenticated live integration tested. A missing runtime is actionable and never silently advertised as secure. Package assets and external state discovery must work outside the source checkout.

Keep old runs inspectable without rewriting their evidence. Legacy agent-writable run data is not retroactively trusted: label it accordingly, and do not import its success as verified acceptance. Provide a documented non-destructive transition to the new store. Protected execution must not resume an old writable checkpoint merely because its JSON parses; require an explicit safe reconstruction/adoption policy or reject that resume.

## 8. Acceptance evidence and tampering tests

Run the existing meaningful suite plus focused tests. Preserve failures/corrections; never weaken assertions or turn required runtime tests into skipped checks to claim completion.

**Contract and integration tests:**

- Same graph/task contract works through fake Codex and fake Claude Code adapters without core/provider-specific branches; cancellation, malformed output, partial streams, nonzero exits, error payloads despite a zero exit, and unsupported capabilities fail correctly.
- Same repository/CI reference workflow works with GitHub and ADO request/response fixtures, including authentication errors, pending/cancelled/unknown states, ID mapping, stale/different commits, logs/artifact references, correlation and ambiguous POST/retry recovery.
- Protected storage survives crash/resume, keeps prior attempts, retains profile/backend/policy identity and handles old run inspection/migration conservatively.
- Installed CLI/package/user guides exercise the new selection, diagnostics and result provenance with no source-path or shell-variable dependency.

**Actual enforcement tests using an intentionally hostile process inside the supported runtime:**

- Try to read/write/delete/replace checkpoints, manifests, execution identity, trusted workflow/policy, coordinator binaries and host Git administration data, using known absolute paths as well as traversal and descendants. Verify protected contents remain unchanged and access is denied/unavailable.
- Plant plausible status files and forged success/evidence/context updates in the checkout/exchange/legacy locations. Verify they cannot alter coordinator state or pass acceptance.
- Attempt symlink/hardlink/rename-based artifact ingestion attacks and alteration of verifier definitions, Git hooks/config, PATH-selected tools and pipeline YAML. Verify refusal or explicit review requirements, not silent trust.
- Seed harmless dummy credentials/control-path sentinels on the host and prove they are absent from worker environment/mounts/handles. No real secrets are needed for this test.
- Prove the worker can still edit its permitted workspace and produce collectable artifacts; deny-everything is not a useful implementation.
- Cancel/time out a worker with children; verify termination, bounded output cleanup, preserved state and an incomplete/interrupted result that is never accepted as success.
- Prove the same launcher cannot silently fall back to unisolated execution when the runtime, mount layout or policy is invalid.

Record actual OS/runtime/version/image digest/profile, commands, outcomes, denied attempts and before/after file hashes under `docs/evidence/phase-3/`. Mock launchers and a moved directory are not isolation evidence. Missing enforcement infrastructure is a genuine blocker to this gate, not permission to downgrade the goal. Continue independent work where useful, but do not mark the full phase complete until this gate actually passes.

## 9. Deliverables and completion

Keep concise progress notes and local commits. Provide:

- Runnable code, packaged CLI and documented configuration for the approved profile and adapter subset.
- `docs/phase-3-architecture.md`, `docs/phase-3-threat-model.md`, and `docs/phase-3-decisions.md`, including a diagram that shows filesystem/process/credential trust boundaries.
- `docs/work-porting-guide.md`: assemblies/contracts to reuse, composition/authentication seams, runtime/OS/ADO-version assumptions, migration steps, and work-environment smoke checks to run later. Include no work endpoints, secrets or proprietary material.
- A capability/conformance table and acceptance evidence linking each required property to a real test/result, clearly distinguishing fixture tests from enforced isolation and unexecuted live integrations.
- `PHASE-3-REVIEW.md` with exact setup/build/test/demo commands, installation changes, observed results, evidence paths, the five best files to review, unresolved decisions, blockers/limits and next experiment.

Completion means **backend substitution has working evidence, repository/CI provider details stay out of the core, and a hostile worker actually cannot alter protected run state or create accepted verification evidence in the supported execution profile.** The result must be ready for a deliberate later work-project integration, without claiming that integration or production security has already been verified.

Stop when those conditions pass. If blocked, preserve the work and handoff honestly. Do not start unrelated orchestration features, access work systems or run live accounts to fill a verification gap.

---

## Reference material for the implementation phase

Inspect current official sources when implementing; pin any upstream spec revision adopted as a reference. The existing pinned Attractor orchestration baseline remains authoritative for existing behavior. These sources are context, not authorization to expand scope or execute their examples:

- [Attractor orchestration spec](https://github.com/strongdm/attractor/blob/main/attractor-spec.md): graph engine and codergen backend layering.
- [Unified LLM client spec](https://github.com/strongdm/attractor/blob/main/unified-llm-spec.md): informative API-layer patterns; deliberately not implemented here.
- [Coding-agent-loop spec](https://github.com/strongdm/attractor/blob/main/coding-agent-loop-spec.md): clarifies the layer delegated to existing agents; deliberately not implemented here.
- [Claude Code programmatic interface](https://code.claude.com/docs/en/headless) and [sandbox boundaries](https://code.claude.com/docs/en/sandboxing): verify actual CLI/protocol behavior and avoid equating a tool permission with whole-process isolation.
- [Azure DevOps API/version guidance](https://learn.microsoft.com/en-us/rest/api/azure/devops/): confirm Services/Server compatibility instead of guessing.
- [Docker security](https://docs.docker.com/engine/security/): runtime configuration and daemon privileges form part of the deployment assumptions; containers are not an absolute security guarantee.
