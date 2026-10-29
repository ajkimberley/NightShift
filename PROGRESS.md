# Implementation progress

Baseline: empty repository, no AGENTS.md, .NET SDK 10.0.103 / net10.0. Read full pinned specification including §11 and Appendices A–D. No existing visual guide is present in this checkout.

Sequence: (A) typed model / recursive-descent DOT parser / diagnostics; (B) routing, sequential engine, handlers and deterministic backend; (C) checkpoints/resume/interviewers/CLI; (D) stylesheet/fidelity/artifacts; (E) process adapter with fake-process tests; final review and evidence.

Parallel/fan-in and manager supervision are candidates for explicit deferral after the sequential core. No real model invocations, remote writes, work systems or machine installs are authorised.

Milestones A/B/C: typed lexer/parser, diagnostics, exact routing, preparation/cascade, sequential engine, attempt-aware checkpoint persistence, handlers, CLI and deterministic examples implemented. Build passes; 57 behavior tests pass. First 33-test run caught boolean JSON stringification (`True` vs `true`); fixed implementation, unchanged assertion. No assertion weakened. Remaining: interview/process integration tests, adapter review, documented demos, conformance and handoff.

Milestones C/D/E: 87 tests pass, including actual CLI process termination/resume, CLI command exit codes, queued accept/revise/timeout/skip, fake Codex process protocol, child cleanup, and artifact storage. Integration testing found anonymous-object JSON fields were omitted by IgnoreReadOnlyProperties; narrowed exclusion to Outcome.IsSuccess. A crash fixture initially used an empty touch sentinel while its helper waits for nonempty data; changed the fixture to write actual effect evidence, retaining the recovery assertions.

Scope decision: parallel/fan-in, manager supervision, loop_restart, per-tool hooks and HTTP/UI are deferred and rejected where executable. Real Codex adapter supports only truncate fidelity; other modes are rejected explicitly. Scripted backend fidelity is simulation, not evaluated model behavior. No real model sessions were launched.

Finished: 91/91 tests pass in Debug and Release, both builds clean. `scripts/verify.sh runs/final-verification` executed help/validate/linear/branch/repair/queued-human/pause-resume/inspect/events successfully. Evidence transcripts and sample repair checkpoint/events saved in docs/evidence. Conformance covers every §11 checklist item, parity row, and Appendices A–D. MORNING-REVIEW.md records setup, actual results, architecture, decisions, limitations and next experiment. No real Codex/model session, remote write, system installation or work-system access occurred. No remaining implementation blocker; deferred capabilities are explicit.

2026-10-29 follow-up: practical readiness clarified in MORNING-REVIEW and adapter docs. Found/fixed JSON Schema keyword casing defect with a failing regression assertion. Full Debug and Release suites rerun; no real Codex invocation. Live smoke test still outstanding.

2026-10-29 walkthrough: added docs/real-codex-walkthrough.md plus read-only and independently verified write smoke graphs. Checked local Codex version/login help/exec help and current official CLI/non-interactive documentation. Both graphs validate; all 14 shell blocks passed bash syntax checks; links resolve. Exercised write verifier with scripted workers against missing and exact-byte artifacts. No real Codex invocation or authentication action performed. Guide covers account/executable setup, scratch workspace, unique read sentinel, artifacts, failure diagnosis, write verification and explicit real-worker selection on resume.
