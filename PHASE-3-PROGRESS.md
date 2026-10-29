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
