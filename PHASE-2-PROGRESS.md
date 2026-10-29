# Phase 2 progress

Approved brief: NEXT-PHASE-GOAL.md. Starting from commit 88713db, clean checkout, SDK 10.0.103. Existing user-run real read/write/resume smoke evidence is preserved; implementation checks will use fake workers only.

Sequence: A package/assets and isolated installation; B discovery/doctor; C project configuration/init; D recorded resume identity/latest selection; E progress/results; F installed demos/docs/visuals; completion audit and user-level installation.

Decisions: keep the current three-project structure; use a versioned snake_case nightshift.json file, public CLI services for testability, optional execution metadata on existing run manifests, and packaged asset files. No new orchestration handlers or model capabilities. Build/test temporary files stay in ignored repository directories.

Completion evidence will be collected under docs/evidence/phase-2/. No implementation blocker identified.

Core CLI milestones implemented and baseline regression suite passed (91 tests). Expanded usability suite passed 111 tests after fixing cancellation log draining. The first expanded run exposed an existing fixture signaling readiness before expected output was emitted; moved its marker after output emission without weakening the assertion, and bounded post-cancellation pipe draining to preserve emitted audit evidence. Initial packaging rejected duplicate assets; removed duplicate pack inclusion. A subsequent interim pack overlapped new source creation and failed on a not-yet-created type; final coherent build/package succeeded. Isolated 0.2.0 installation verified; final package lifecycle/onboarding verification and user-level installation remain.

Configuration decisions: config-relative paths resolve at the config root; CLI-relative paths at invocation; discovery stops at Git root/home boundary; explicit run paths do not depend on current project config. Resume keeps saved execution identity plus audited same-backend executable overrides; incompatible backend/sandbox/workspace changes are rejected. Runtime simulation defaults remain safe; demo always defaults offline even in real-worker-configured projects.

Final audit fixes: persisted deliberate resume-bound overrides in optional ExecutionSettings.Limits (with per-invocation pause excluded); recorded full path/limit/output provenance; refused nonempty new run folders; converted numeric overflow into CLI input errors. Added targeted tests rather than weakening existing assertions. Final version is 0.2.1.

Completed verification: 116/116 tests in Debug and Release. scripts/verify-package.py executed local pack/install/update/uninstall, source-hidden demo/init, configured run/status/result/resume with fake Codex and changed defaults, extracted guide workflows with queued accept/revise, and negative missing/wrong artifact checks. Actual user tool installed as 0.2.0 then updated to 0.2.1 after isolated checks; final installed-by-name doctor/demo/result and saved demo limits checked outside source with the documented PATH addition. Real CLI probes were version/help/login-status only; no model/login flow. Current PATH omits ~/.dotnet/tools as an expanded directory; handoff provides one-line setup and no profile was edited.

Handoff: PHASE-2-REVIEW.md. Requirements/evidence: docs/phase-2-acceptance.md. Four installed-use guides contain first-use, runtime and directory Mermaid visuals; source-based historical material preserved. No implementation blocker remains; all remaining limitations are explicit in the review.
