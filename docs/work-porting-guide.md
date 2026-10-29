# Later work-project port

This phase did not inspect or contact any work repository, system, endpoint or
credential. Use this guide only during a later, separately authorized integration.

## Assemblies and composition

Reuse `Nightshift.Core` for DOT preparation/traversal/routing, immutable `AgentTask`,
`ICodingAgent`, `IWorkerLauncher`, `IRepositoryService`, `IPipelineService`, candidate
and evidence contracts, `IRunStore`, and conservative `Acceptance.Decide`.
Reuse or adapt `Nightshift.Adapters` implementations of process protocols, Podman
profile, safe snapshots, external state, hosted adapters and durable intents.
`ProtectedWorkflow` is a small reference composition; integrate it into the work
application's own trusted coordinator. The CLI is optional composition/UI.
Legacy native classes are for explicit local development, with no protection claim.

Select agent, repository service and CI service separately. Inject `ServiceHttp`
with a reviewed HTTPS origin and host-only authentication callback. Its default
clients disable redirects/cookies. A supplied transport is trusted composition:
it must preserve that restriction. Credentials never enter graph/context/worker
requests. Use an approved secret source already provided by the future host; no
OAuth/SSO/account platform is included. The CLI deliberately exposes only offline
hosted fixture effects; live hosted composition requires a separately authorized host.

## Host/runtime prerequisites

Tested: macOS ARM64 host, AppleHV Linux ARM64 VM, rootless Podman 6.1.4, dedicated
same-path share, non-root UID 501. The VM must be manually available. Linux local
rootless selection (`--remote=false`, actual nonzero host UID/GID) is implemented
but not exercised here. Verify cgroups, seccomp, mount ownership and current runtime
identity in the target environment. Windows and native macOS execution do not
inherit this guarantee. No automatic runtime installation/start/reconfiguration exists.

Only an immutable, already installed image digest is used. The official Alpine
fixture image has fake agents/shell tools, not live coding agents, .NET or credentials.
Supply reviewed Linux agent/runtime images for a later smoke test, pin their digests,
and verify each CLI's actual help/protocol version. Never copy host macOS binaries
into a Linux container. Live network/auth are deliberately not part of the tested
profile; do not simply remove `--network=none` and call it equivalent. Design and
verify the needed restricted egress and agent-only credential delegation separately.
Repository/CI credentials must remain host-only even in that later design.

## Approval and migration

1. Start with a small nonproprietary fixture and an external Nightshift-owned state
   root. Do not mount the checkout, .git, host home, state root or runtime socket.
2. Review and explicitly adopt workflow, backend executable/image, policy checks,
   pinned verifier-definition files and service selections. Pin the complete trusted
   verification-definition dependency closure, including scripts/templates as needed.
   Built/tested application code remains hostile and always executes in a container.
3. Preserve legacy `.nightshift/runs` and source `runs` verbatim. Inspect explicit old
   paths; label their outcomes as untrusted. Create a fresh policy and fresh protected
   run from a reviewed candidate. Do not copy old JSON into the protected store, and
   do not translate old SUCCESS into passing evidence. Protected resume rejects it.
4. Protected resumes require the same coordinator hashes, profile/runtime/image,
   backend/version/capabilities, policy revision and service selections. A binary or
   policy upgrade starts a fresh run. Prior attempts/checkpoints remain available.
5. Bind hosted evidence only to an exact committed candidate produced from the
   immutable snapshot. `LocalGit` uses Git plumbing with filters/ignore rules bypassed;
   never import worker Git configuration, hooks or refs. Publishing that revision is
   a later explicitly authorized action, not part of the local fixture workflow.

## Hosted adapter prerequisites

GitHub REST is pinned to `2022-11-28`; Azure DevOps **Services** requests pin `7.1`.
ADO Server compatibility requires its actual release/version and API behavior to be
confirmed later. No Server version or work installation is assumed.

The reference uses repository namespace/name identities plus exact SHA and snapshot
digest. GitHub approved pipeline revision is `native_workflow_id:uppercase_SHA256`
of the root YAML at that SHA. Declare workflow_dispatch inputs
`nightshift_correlation` and `nightshift_candidate`; use the run-name
`nightshift-${{ inputs.nightshift_correlation }}`. Every observed run must return the
same repository, exact SHA, workflow ID, event and correlation. Azure uses
`definition_revision:uppercase_SHA256` of YAML and a numeric build definition ID;
queue sets exact sourceVersion, sourceBranch and `nightshift.correlation` properties.
The fixture validates these translations. Confirm retention/visibility of custom
properties and run-name behavior in a later live smoke before relying on recovery.

`HostedWorkflow.AdvanceAsync` persists intent before POST and performs a bounded
native lookup on each advance; call it again to observe pending/running checks.
Use `IPipelineService.CancelAsync` for deliberate cancellation, then observe the
actual terminal result. Cancelling local waiting does not imply cancelling a hosted
run. A missing/ambiguous lookup after dispatch stays uncertain and cannot trigger
another POST; reconcile with the operator/native system rather than deleting the
intent. Searches are bounded to 100 responses; old/paginated matches may therefore
remain uncertain. No merge/delete/policy-update operations are implemented.

## Smoke checks to run later

- Run fixture suite and hostile isolation suite on the target OS/runtime, retaining
  known absolute paths, denied operations, hashes, socket/environment/fd probes,
  link attacks, child cancellation and no-fallback failures.
- Exercise installed command discovery, adopted snapshots and external store outside
  the source tree. Restart the host process mid-worker and after hosted dispatch.
- With separate authorization, run one small fresh agent task and verify its actual
  protocol, bounded cancellation, agent-only auth and isolated verifier behavior.
- With separate authorization, create/read one candidate PR and queue one configured
  check for its exact SHA. Record the native ID, reconcile after lost response,
  observe to terminal, retrieve bounded logs/artifact references, cancel a second
  bounded check, and prove a different SHA cannot reuse the first check's evidence.
- Review pipeline templates/actions/service settings, token scopes, redirects/log
  redaction and target retention/pagination. Never substitute branch-latest results.

Scope the resulting claim to the tested runtime, image, policy and candidate. This
is not production readiness, compliance, sandbox-escape resistance or arbitrary
code correctness, and it does not certify a work-environment integration.
