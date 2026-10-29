# Phase-two completion audit

Baseline: approved [NEXT-PHASE-GOAL.md](../NEXT-PHASE-GOAL.md). This audit matches the original scope; it does not substitute engine feature expansion or a demo-only deliverable. All listed commands are implemented. Source/behavior evidence is separate from unexecuted live-model experiments.

| Brief requirement | Authoritative evidence | Result |
|---|---|---|
| Ordinary installed `nightshift`, no source DLL path in daily use | [User install/update](evidence/phase-2/user-update.txt), [installed 0.2.1 first use](evidence/phase-2/user-installed-smoke.txt) | Complete. Runtime/source distinction documented. PATH limitation below. |
| Local package, assets included, no public publication | [Package lifecycle](evidence/phase-2/package-lifecycle.txt); `Nightshift.Cli.csproj`, four packaged Assets | Complete. Demo/init worked while source Assets were hidden. |
| Install/update/uninstall and unrelated-directory invocation | Package lifecycle installs 0.2.0, updates 0.2.1, uninstalls; installed command selected by PATH outside checkout | Complete; actual user's 0.2.1 retained. |
| Version/help and SDK/runtime/PATH troubleshooting | `CliApplication.Version/Help`, installed logs; [getting started](guides/getting-started.md) | Complete; existing SDK/runtime unchanged. |
| Only user-level Nightshift installation after isolated checks | Helper transcripts and code; initial install then update after package checks | Complete. No sudo/profile changes or unrelated tool replacements. |
| Codex order: CLI, config, actual PATH file, bundle | `CodexDiscoveryUsesExplicitThenConfigThenPathThenBundleAndDoesNotFallThroughInvalidExplicitValues` | Complete. Invalid explicit/configured paths never fall through. |
| Empty/missing/directory/alias/nonexecutable/spaced executable paths | Same discovery test, fake worker named with spaces, installed personal-project fixture | Complete. No shell alias/function execution or chmod of real programs. |
| Doctor setup/config/path/backend/sandbox/version/interface/auth reporting | `DoctorReport`, `Doctor`, [actual installed doctor](evidence/phase-2/user-installed-smoke.txt) | Complete; stored credentials distinguished from live model access. |
| Bounded read-only probes; no login/model/secrets | Doctor timeout/redaction/failed-auth/missing-flag tests; source only calls version/help/login-status | Complete. Raw diagnostic/auth output is not printed. |
| Small versioned config, all required fields, precedence | `ProjectConfiguration`, `NestedDiscoveryConfigurationPathsAndCliOverridesUseTheirOwnOrigins`, malformed-config data tests | Complete. Effective settings and origins recorded. |
| `init` minimal useful truncate workflow, real opt-in, safe defaults | `Project.InitializeAsync`; installed init plus saved-identity tests | Complete. It creates files but launches no workflow/model. |
| Preserve existing config/workflow/ignore content; no noninteractive hang | `InitPreservesExistingWorkflowIgnoreRulesAndExistingConfiguration`; init uses explicit arguments/defaults | Complete. Existing config refused; workflow retained; ignore appended narrowly. |
| Config/CLI path origins, nearest config and documented boundary | `Project.Discover`, nested/Git-boundary and explicit-config tests; [path guide](guides/codex-project.md) | Complete. Without config, invocation directory is the project. |
| Working-directory override and real permission independent of discovery | Saved opt-in tests, path override tests, `Execute` backend/permission guard | Complete. read-only is default; workspace-write requires a deliberate selection. |
| Persist backend/executable/sandbox/consent/workspace/limits/provenance | Optional `ExecutionSettings` on manifest, audited execution-history/pointer; saved-identity and limit-override tests | Complete. No credentials stored. |
| Never switch real worker to simulator on resume | `SavedOptInRunsFakeCodexAndResumeIgnoresChangedProjectDefaults`; installed resume with changed defaults | Complete. Incompatible backend override rejected. |
| Deliberate same-backend executable replacement, recorded | `ExecutableReplacementIsRecordedAndUsedBySubsequentResume` | Complete; subsequent resume uses replacement. |
| Explicit limit overrides survive subsequent resumes | `DeliberateResumeLimitOverridesPersistAcrossLaterResumes` | Complete; pause-after remains per invocation. |
| No silent sandbox/workspace broadening | Resume checks and saved-identity tests | Complete. This implementation rejects any sandbox/workspace change on resume; use a fresh run. |
| Legacy runs readable; missing identity requires explicit choice | `LegacyResumeRequiresExplicitBackendAndPermission`; existing core/CLI recovery suites | Complete. Reading raw state remains available; real stages require permission. |
| Preserve final checkpoints, outcomes, transitions and attempts | Existing engine/recovery tests plus new resume metadata written under run lease | Complete. No exactly-once/session-reuse claim. |
| `--latest` scoped by project/output root; correct state selection | `LatestSelectsOnlyThisProjectAndOnlyResumableRunsForResume`; `RunViews.Latest` lease exclusion | Complete. Final runs skipped for resume, no fallback to other projects. |
| Startup identity, stage/visit/attempt/retry/elapsed progress | CLI integration tests, `ProgressDisplay`, installed transcripts | Complete. Quiet-stage heartbeat every five seconds; no percentage/fixed visit denominator. |
| Completion/failure response/error and exact evidence paths | `CliRetriesAndFinalFailuresExposeActualStateAndPaths`, cancellation test, RunViews/Print | Complete. Worker claim versus tool verdict labeled. |
| Useful status/result and declared output existence | Offline demo/results tests and installed JSON/human result checks | Complete. No output paths inferred from model prose or existence-as-correctness claim. |
| Plain redirection, machine output, preserved exit codes | JSON parse tests, progress stderr, raw inspect/events commands, original CLI exit tests | Complete. No ANSI output; human noninteractive gates require explicit answers. |
| Self-contained installed offline demo, independent verifier | Source-hidden installed demo, exact-byte check; missing/wrong-artifact installed resumes fail | Complete. No account/model dependency; always defaults offline. |
| Explicit finite real read/write modes, no retries/automatic live calls | Fake read/write demo tests; bounded packaged graph templates; one model stage followed by tool gate | Implemented/fake-process verified. Live phase-two model calls intentionally unexecuted. |
| README/short guides: setup/demo/real project/results/failures/resume/authoring | [README](../README.md) and four [guides](guides/getting-started.md) | Complete; actual installed commands exercised where authorized. |
| Static first-use/runtime/directory visual guides | Mermaid in getting-started, workflows and results/resume, plus directory tree | Complete as repository-native Mermaid sources; no raster-render QA claim. |
| Task, independent check, explicit facts/branch, queued human choices | Two guide DOT graphs extracted/validated/executed with fake workers; accept and revise/accept paths in package transcript | Complete; real facts and simulator fixture attributes clearly separated. |
| Existing suite plus focused tests, actual commands/results/failures retained | [Debug](evidence/phase-2/tests-debug.txt), [Release](evidence/phase-2/tests-release.txt): 116 each; package/user transcripts and initial failure logs | Complete. Authored tests are not independent third-party assurance. |
| Handoff/progress/local commits and compatibility/decision record | [PHASE-2-REVIEW](../PHASE-2-REVIEW.md), [progress](../PHASE-2-PROGRESS.md), [decisions](phase-2-decisions.md), Git history | Complete. No new engine feature claim. |

## Explicit scope exceptions and limitations

The brief permits reporting a missing tools PATH rather than changing shell startup files. The executable is installed and verified at `/Users/adamkimberley/.dotnet/tools/nightshift`; current PATH does not include that directory. The exact one-time `export PATH="$HOME/.dotnet/tools:$PATH"` is printed/documented. Installed-by-name verification applied that documented addition in the verification subprocess environment; no shell profile was changed.

Live model runs, login flows, registry publication, cross-platform execution, self-contained/native distribution, Homebrew/release automation, new orchestration capabilities and dashboards were not required/authorized and are not claimed. Historical user-run live Codex success is preserved separately. Mermaid sources were reviewed, not rendered into publication-ready raster images. The tests/package checks prove the implemented local user path; they do not prove model correctness, complete sandbox isolation or exactly-once effects.
