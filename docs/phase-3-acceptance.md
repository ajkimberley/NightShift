# Phase three capability and acceptance evidence

Completion is scoped to macOS 26.5 ARM64, the manually running local rootless Podman
6.1.4 Linux ARM64 VM and immutable official Alpine fixture image. The complete
suite has **193 passing tests, zero skips**. Existing 116 tests remain meaningful;
legacy execution fixtures now explicitly select their unprotected profile and use
an untrusted exchange instead of an authoritative stage directory.

| Required property | Capability / evidence | Classification |
|---|---|---|
| Threat model, independent agent/repository/CI/runtime seams | [Architecture](phase-3-architecture.md), [threat model](phase-3-threat-model.md), Core/TrustedContracts; no provider DTOs/dependencies in Core | Code + architecture |
| Immutable agent requests, same graph with two agents | `Same_graph_runs_through_both_fake_agent_executables_with_no_core_provider_branch`; fake Codex and Claude executables in real containers | Actual isolation + protocol fixture |
| Structured completion/errors, partial/malformed streams, nonzero/zero-error exits, cancellation, unsupported features | `TrustedContractTests` and `AgentExecutableFailureTests`; no task transport retries; normalized observed events, bounded logs | Unit + actual executable/container |
| External coordinator-only state, immutable policies, exclusive leases, atomic persistence, historical attempts | `ProtectedState`, `IRunStore`, existing persistence/lease tests and `Abrupt_coordinator_crash_recovers_owned_execution_retains_attempts_and_accepts_replayed_candidate` | Actual crash/resume |
| Backend/runtime/profile/version/policy retention | Full references change current policy to scripted before resuming saved fake agent; crash test rejects changed profile; current project config cannot redirect state in package test | Actual containers + installed CLI |
| Host state, manifests, checkpoints, execution identity, policy/workflow, binaries and Git admin protected | `Hostile_worker_and_descendants_cannot_access_protected_state_binaries_git_or_credentials`; [actual stdout, identities, before/after hashes](evidence/phase-3/hostile-enforcement.json) | Enforced runtime, known absolute paths, five denied operations per target |
| Host credentials/control paths/handles absent; useful permitted edits | Same hostile test: dummy env/file/open host fd, home/socket absence, nonroot/read-only root/cgroups/network probes; permitted candidate collected | Enforced runtime |
| Symlink/hardlink/rename/traversal/special/.git ingestion | `Real_worker_link_rename_and_git_ingestion_attacks_are_refused` plus archive contract tests; frozen tar bytes, no link extraction | Enforced attacks + malformed archives |
| Forged status/evidence/context/legacy files cannot accept | `Forged_status_context_evidence_and_legacy_files_never_pass_acceptance`; successful graph outcome coexists with failed independent acceptance | Enforced worker + protected ledger |
| Changed verifier definitions/PATH/pipeline YAML require refusal/review | `Edited_verifier_definitions_require_review_and_workspace_PATH_tools_cannot_replace_image_tools`; `Changed_commit_definition_duplicate_correlation_and_ambiguous_dispatch_never_pass` | Actual isolation + hosted fixture |
| Exact candidate and policy binding; no dirty-HEAD assumption | `EvidenceAndGitTests` proves ignored CRLF content survives Git attributes byte-for-byte; `Acceptance_requires_exact_provenance_candidate_policy_check_and_terminal_pass` | Local Git + acceptance contract |
| Evidence ordering cannot regress on wall-clock reversal | `Evidence_order_survives_wall_clock_reversal_and_later_failure_overrides_prior_pass`; monotonic files in `EvidenceLedger` | Persistence contract |
| Timeout/cancellation children, bounded cleanup and logs | `Cancellation_kills_the_owned_container_with_children_and_preserves_partial_logs`, both agent cancellation tests, `ProtectedDeadlineTests` (resumable graph deadline + bounded hanging verifier), existing engine timeout tests | Actual processes/container removal |
| Abrupt process loss and owned-unit cleanup | Crash test kills the CLI process, recovers the outstanding container by persisted identity, preserves prior attempt, completes replay | Actual process interruption |
| Missing runtime/remote connection/invalid mount/mutable image fail closed | `Missing_runtime_remote_context_invalid_mount_or_mutable_image_never_start_the_worker`; no fallback code path | Real launcher negative checks |
| GitHub + ADO PR read/create, native check trigger/reconcile/observe/cancel, logs/artifacts | `HostedAdapterTests`; all four independent repository/pipeline combinations; [API pins](phase-3-sources.md) | Real translations against fake HTTP only |
| Auth/authz/transport errors, pending/cancelled/unknown states, native IDs, stale SHA, ambiguous POST | Hosted adapter tests; durable intent persisted before POST; one PR/run after simulated lost responses; uncertainty never resends | Fake HTTP contract |
| No credentials on arbitrary artifact/log redirects | `Credentials_are_never_forwarded_to_arbitrary_log_redirects_or_cross_origin_requests`; anonymous allowlisted GH log download path; artifact URLs are metadata | Contract / code |
| Full local workflow + stale rejection + resume without duplicate effects | `Full_reference_has_isolated_agent_exact_git_candidate_hosted_recovery_and_stale_rejection` for GitHub and ADO; installed `reference` commands | Actual local isolation + Git + HTTP fixtures |
| Config/doctor/run/status/result/resume/package from outside source | [Protected package transcript](evidence/phase-3/package-protected.txt), source assets hidden/restored; operator-only state root, saved identity despite modified project defaults | Installed package + actual containers |
| Legacy inspectability, conservative transition | Legacy summary label; protected store refuses legacy paths/checkpoints; [migration guide](work-porting-guide.md) | Unit/CLI + preserved historical data |
| Existing installed native UX and negative verifier checks | [Legacy package lifecycle](evidence/phase-3/package-legacy.txt), explicit native mode; no assertions relaxed | Installed compatibility |

Full test logs name every case: [Debug](evidence/phase-3/tests-debug-final-state.txt),
[Release](evidence/phase-3/tests-release-final-state.txt). Earlier failures and their
corrections are retained, including initial collection/path handling, fixture
correlation, explicit legacy profile changes, DOT fixture quoting and cancellation
grace-timeout behavior. [Decisions](phase-3-decisions.md) records assumptions and
protective divergences from historical behavior.

Unexecuted: real Codex/Claude tasks, paid APIs, live hosted accounts, restricted live
network/auth, Linux-host enforcement, ADO Server and actual work integration.
No work endpoint/secret appears in these fixtures. No production readiness,
compliance, kernel/runtime escape resistance or arbitrary correctness claim is made.
