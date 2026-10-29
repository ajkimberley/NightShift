# Phase three progress

- Inspected goal, README, phase-two review, runtime guidance and existing code.
- Preserved pre-existing goal/README/runtime-documentation edits.
- Recorded threat model and architecture before implementing isolation.
- Runtime read-only inspection: named VM already running, rootless loopback identity;
  no machine start, installation or reconfiguration performed.
- Initial create_goal call found the /goal already active; continued that goal.
- Implemented neutral immutable contracts, separate adapter assembly, protected store,
  rootless launcher with bounded collection and owned-unit crash recovery, Codex/Claude
  process protocols, repository/CI HTTP translations and durable hosted intent ledger.
- Moved native legacy processes/providers out of Core; explicit native-development
  selection now preserves historical tests while labeling their evidence untrusted.
- Baseline: 116 passed. First runtime smoke passed. 44 new contract tests passed.
- Reference GitHub workflow passed with real containers and fake HTTP; one PR and
  one CI dispatch after interruption; matching acceptance passed, later change rejected.
- Retained initial failures: nonexistent-path attribute check, fixture marker offset,
  legacy CLI tests requiring explicit profile, missing legacy schema audit import.
  Corrections retained assertions and isolated legacy compatibility explicitly.
- Hostile enforcement covers actual known host paths, sentinel hashes, descendants,
  credentials/environment/open-fd absence, useful edits, forged claims, symlink/
  hardlink/rename/.git ingestion refusal, pinned verifier tampering and PATH tools.
- Fixed cancellation grace timeout using Podman rm --force --time=0; corrected
  test DOT quoting rather than relaxing parser/assertions. Both corrections passed.
- Abrupt coordinator kill/recovery passed. Both full GitHub and ADO references passed.
- Added exact-byte Git plumbing and monotonic evidence ledger after final review;
  focused tests passed. Debug 185 (before two ledger/Git tests) and Release 187 passed.
- Installed protected package check passed outside source with source assets hidden.
  Legacy package lifecycle is being retained and verified separately.

- Final audit corrected protected graph deadlines to leave resumable state, added
  independent verifier deadlines and durable pending/cancelled evidence, and kept
  native deadline semantics unchanged. Added two real-runtime deadline tests.
- Full final suite: 193/193 Debug and Release, zero skips. Both isolated package
  checks passed. User installation updated through 0.3.0 to corrected 0.3.1;
  installed doctor/demo/result and both reference fixtures passed outside source.
- No owned containers/workspaces remain. Operator-started VM left running.
  Completion/evidence/limits/porting handoff recorded; no live model/account/work use.
