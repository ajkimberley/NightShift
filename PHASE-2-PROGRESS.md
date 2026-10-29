# Phase 2 progress

Approved brief: NEXT-PHASE-GOAL.md. Starting from commit 88713db, clean checkout, SDK 10.0.103. Existing user-run real read/write/resume smoke evidence is preserved; implementation checks will use fake workers only.

Sequence: A package/assets and isolated installation; B discovery/doctor; C project configuration/init; D recorded resume identity/latest selection; E progress/results; F installed demos/docs/visuals; completion audit and user-level installation.

Decisions: keep the current three-project structure; use a versioned snake_case nightshift.json file, public CLI services for testability, optional execution metadata on existing run manifests, and packaged asset files. No new orchestration handlers or model capabilities. Build/test temporary files stay in ignored repository directories.

Completion evidence will be collected under docs/evidence/phase-2/. No implementation blocker identified.

Core CLI milestones implemented and baseline regression suite passed (91 tests). Expanded usability suite passed 111 tests after fixing cancellation log draining. The first expanded run exposed an existing fixture signaling readiness before expected output was emitted; moved its marker after output emission without weakening the assertion, and bounded post-cancellation pipe draining to preserve emitted audit evidence. Initial packaging rejected duplicate assets; removed duplicate pack inclusion. A subsequent interim pack overlapped new source creation and failed on a not-yet-created type; final coherent build/package succeeded. Isolated 0.2.0 installation verified; final package lifecycle/onboarding verification and user-level installation remain.

Configuration decisions: config-relative paths resolve at the config root; CLI-relative paths at invocation; discovery stops at Git root/home boundary; explicit run paths do not depend on current project config. Resume keeps saved execution identity plus audited same-backend executable overrides; incompatible backend/sandbox/workspace changes are rejected. Runtime simulation defaults remain safe; demo always defaults offline even in real-worker-configured projects.
