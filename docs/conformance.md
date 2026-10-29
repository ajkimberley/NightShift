# Conformance and evidence

Phase-two usability extensions are documented in [the phase-two review](../PHASE-2-REVIEW.md). The engine feature scope and original interpretation decisions below remain in force; installed commands now add project settings, diagnostics, results and recorded worker identity.

Baseline: [pinned spec](reference/attractor-spec.md), including §11 and Appendices A–D. This is **partial conformance**. Tests below were authored during implementation; they are not independent assurance. Latest execution evidence: [verification transcript](evidence/verification.txt), [Release check](evidence/release-verification.txt), [development findings](evidence/development-findings.md).

States: **Implemented/tested** means behavior exercised deterministically; **Implemented/unverified** means code exists without that specific behavior exercised; **Partial** means a specified portion or documented divergence; **Deferred** means no working feature claim; **Ambiguous** means competing spec readings, with the implemented choice tested and explained in [spec decisions](spec-decisions.md). No live-model feature is verified.

Checklist references use `section/item` numbering from the pinned source, in original bullet order.

## §11.1 — DOT parsing

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.1/1 | Supported directed subset | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/2 | Graph attributes | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/3 | Multiline attributes and strings | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/4 | Edge attributes | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/5 | Chained edge expansion | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/6 | Ordered scoped defaults | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/7 | Flattened subgraphs and derived classes | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/8 | Stylesheet class matching | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/9 | Quoted and bare typed values | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.1/10 | Lexical comment handling | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |

## §11.2 — Validation

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.2/1 | Exactly one start | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/2 | Exactly one exit | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/3 | No incoming start edges | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/4 | No outgoing exit edges | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/5 | Reachability | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/6 | Existing edge endpoints | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/7 | Codergen prompt warning | Ambiguous | §7.2 prompt-or-label versus §11.2 prompt-only; chosen §7.2 behavior tested (decision 13). |
| 11.2/8 | Condition syntax validation | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/9 | Refuse error diagnostics | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.2/10 | Diagnostic fields and severity | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |

## §11.3 — Execution

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.3/1 | Start discovery | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.3/2 | Shape/type dispatch | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.3/3 | Handler request and outcome | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.3/4 | Status artifact layout | Partial | External outcome schema tested; visit/attempt directories replace flat node path (decision 6). |
| 11.3/5 | Five-step edge selection | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs): conditional priority, accelerators, suggestions, false guards and tiebreaks. |
| 11.3/6 | Traversal and revisits | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.3/7 | Terminal completion | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.3/8 | Goal-gated pipeline result | Ambiguous | Visited/latest gates and PARTIAL_SUCCESS per §3.4; tested (decision 7). |

## §11.4 — Goal gates

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.4/1 | Track gate outcomes | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.4/2 | Check gates at exit | Ambiguous | Only visited/recorded gates; latest outcome, not artifact-versioned acceptance. |
| 11.4/3 | Recovery target traversal | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.4/4 | Fail without recovery | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |

## §11.5 — Retries

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.5/1 | RETRY versus FAIL behavior | Ambiguous | §3.5 followed over §11.5: FAIL is terminal; RETRY/transient exceptions retry. |
| 11.5/2 | Additional attempts and counters | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.5/3 | Configurable backoff | Partial | Public backoff supports constant/exponential with cap; named presets/graph policy selection deferred. |
| 11.5/4 | Jitter | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.5/5 | Exhaustion and failure routing | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |

## §11.6 — Handlers

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.6/1 | Start no-op | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.6/2 | Exit no-op | Implemented/unverified | Exit registry no-op exists; traversal short-circuits terminal and tests that path rather than directly invoking handler. |
| 11.6/3 | Backend prompts and response artifacts | Partial | Explicit backend outcomes required by default; no text-to-success inference. Prompt/response paths tested. |
| 11.6/4 | Human choice routing | Ambiguous | §4.6 suggested target IDs followed rather than §11.6 preferred label; queues and real async timeout tested. |
| 11.6/5 | Conditional own outcome | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.6/6 | Parallel fan-out | Deferred | Rejected by validation; no scheduling or filesystem isolation claim. |
| 11.6/7 | Fan-in join | Deferred | Rejected by validation; no unconditional-success placeholder. |
| 11.6/8 | External commands | Implemented/tested | See ProcessTests: stdout/stderr, status override, nonzero exit, timeout and attached-child cleanup. |
| 11.6/9 | Custom handler registry | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |

## §11.7 — State

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.7/1 | Shared JSON context | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.7/2 | Handler context updates | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.7/3 | Merge updates | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.7/4 | Completion checkpoints | Partial | Core state plus documented continuation extensions; historical checkpoints and atomic rename tested. |
| 11.7/5 | Restore continuation | Implemented/tested | [EngineTests](../tests/Nightshift.Tests/EngineTests.cs). |
| 11.7/6 | Visit/attempt artifacts | Partial | All files retained under visit/attempt subdirectories instead of flat node directory. |

## §11.8 — Human interaction

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.8/1 | Ask interface | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |
| 11.8/2 | Four question types | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |
| 11.8/3 | Explicit test auto-approver | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |
| 11.8/4 | Console interviewer | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |
| 11.8/5 | Callback interviewer | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |
| 11.8/6 | Queued answers | Implemented/tested | [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs). |

## §11.9 — Conditions

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.9/1 | Equals | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/2 | Not equals | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/3 | AND conjunction | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/4 | Current outcome | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/5 | Preferred label | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/6 | Context lookup and missing keys | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.9/7 | Empty condition | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |

## §11.10 — Model stylesheet

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.10/1 | Parse graph stylesheet | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.10/2 | Shape selectors | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.10/3 | Class selectors | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.10/4 | ID selectors | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.10/5 | Specificity and later rules | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |
| 11.10/6 | Explicit node precedence | Implemented/tested | [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs). |

## §11.11 — Extensibility

| Item | Capability | State | Evidence / qualification |
|---|---|---|---|
| 11.11/1 | Graph transforms | Partial | Fresh parsed graph protects source; custom transform implementations remain responsible for purity. |
| 11.11/2 | Transform interface | Implemented/tested | [ExtensibilityTests](../tests/Nightshift.Tests/ExtensibilityTests.cs). |
| 11.11/3 | Goal expansion | Implemented/tested | Goal expansion verified in ParsingRoutingTests and stage prompt artifact. |
| 11.11/4 | Custom transform order | Implemented/tested | [ExtensibilityTests](../tests/Nightshift.Tests/ExtensibilityTests.cs). |
| 11.11/5 | HTTP server | Deferred | No server/UI/SSE per brief. |

## §11.12 — cross-feature parity

| Row | Capability | State | Evidence / decision |
|---|---|---|---|
| 1 | Simple linear parsing | Implemented/tested | ParsingRoutingTests + MultiStageRunPersistsStatusContextTransitionsAndFinalCheckpoint |
| 2 | Goal and label parsing | Implemented/tested | DotDefaultsAreOrderedScopedAndDoNotRetroactivelyChangeNodes |
| 3 | Multiline attributes | Implemented/tested | MultilineStringsAndTypedValuesArePreserved |
| 4 | Missing start | Implemented/tested | MissingStartExitDanglingEdgesAndDuplicateStartsAreErrors |
| 5 | Missing exit | Implemented/tested | Same structural validation test |
| 6 | Orphan severity | Ambiguous | §7.2 ERROR chosen over parity warning; ValidationProducesStructuralTypedAndCapabilityDiagnostics |
| 7 | Linear execution | Implemented/tested | MultiStageRunPersistsStatusContextTransitionsAndFinalCheckpoint |
| 8 | Success/fail branching | Implemented/tested | FailedCheckRepairPassIsARevisitNotAHandlerRetry + demos |
| 9 | Retry on failure | Ambiguous | ErrorsKeepTransientVersusTerminalClassification; FAIL terminal per §3.5 |
| 10 | Unsatisfied goal blocks | Implemented/tested | GoalGateBlocksExitAndMissingRecoveryFails |
| 11 | Satisfied goal permits exit | Implemented/tested | MultiStageRunPersistsStatusContextTransitionsAndFinalCheckpoint |
| 12 | Human selection routing | Implemented/tested | QueuedAcceptReviseSkipTimeoutAndUnknownAreDeterministic |
| 13 | Conditions outrank weight | Implemented/tested | ConditionalEdgesWinThenWeightAndOrdinalLexicalBreakTies |
| 14 | Unconditional weight | Implemented/tested | Same routing test |
| 15 | Ordinal lexical fallback | Implemented/tested | Same routing test |
| 16 | Context handoff | Implemented/tested | ConditionalOutcomeIsItsOwnSuccessBusinessFactCarriesPriorResult |
| 17 | Checkpoint/resume parity | Implemented/tested | CliRecoveryTests + ResumeAfterCompletionPreservesOutcomesAndDoesNotSkipIntentionalRevisits |
| 18 | Stylesheet shape | Implemented/tested | StylesheetCascadeSourcesAndExplicitDefaultsWork |
| 19 | Goal expansion | Implemented/tested | Same stylesheet test + stage prompt artifact |
| 20 | Parallel/join | Deferred | No handlers registered; validation rejection tested |
| 21 | Custom handler | Implemented/tested | UnsupportedFidelityFailsAndCustomHandlerReplacementExecutes |
| 22 | 10+ stages | Implemented/tested | TenPlusNodePipelineAndLegacyRetryDefaultWork |

## §11.13 — live integration smoke test

**Implemented/unverified:** equivalent deterministic multi-stage engine behavior is tested; the specified real LLM callback smoke test was **not executed**, as instructed. Fake process protocol tests do not establish live compatibility, auth, model quality or task correctness. The exact upstream smoke graph also contains failed-check cycles governed by decisions 2/3/7.

## Appendices A–D and related contracts

| Source | Capability | State | Evidence / limits |
|---|---|---|---|
| A graph | goal, label | Implemented/tested | Graph parsing, mirrored graph.goal and prompt expansion. |
| A graph | model_stylesheet | Implemented/tested | Cascade/parser tests; provenance in configuration.json. |
| A graph | default_max_retries and legacy alias | Implemented/tested | Additional attempts + legacy test; node override tested. |
| A graph | default_fidelity | Partial | Precedence tested; runtime fidelity depends on backend. Codex supports truncate only. |
| A graph | retry_target, fallback_retry_target | Implemented/tested | Gate recovery; invalid target warning and fallback test. |
| A graph | stack.child_dotfile, stack.child_workdir | Deferred | No manager handler. Passive custom attributes remain parseable. |
| A graph | tool_hooks.pre, tool_hooks.post | Deferred | Nonempty hooks rejected at graph and node level. |
| A node | label, shape, external type | Implemented/tested | Parser/registry/no-op fallback/custom handler tests. |
| A node | prompt | Implemented/tested | Goal expansion, fallback label, prompt/response files. |
| A node | max_retries | Implemented/tested | Additional attempts, exhaustion and crash budget restoration. |
| A node | goal_gate | Ambiguous | Visited/latest outcomes, including partial; decision 7 tests. |
| A node | retry_target, fallback_retry_target | Implemented/tested | Failure and goal recovery; missing target fallback. |
| A node | fidelity | Partial | Resolution and invalid value rejection tested; actual model contract only truncate. |
| A node | thread_id | Partial | Resolution tested; live session reuse deferred. |
| A node | class | Implemented/tested | Explicit classes and derived subgraph classes, cascade tests. |
| A node | timeout | Implemented/tested | Per-attempt process timeout and transient retry. |
| A node | llm_model, llm_provider, reasoning_effort | Partial | Resolved values/source maps and fake CLI args tested. No provider autodetection/platform abstraction. |
| A node | auto_status | Partial | Explicit codergen text fallback only; warned, never independent verification. |
| A node | allow_partial | Implemented/tested | Retry exhaustion with and without opt-in. |
| A edge | label, condition, weight | Implemented/tested | Exact five-step routing tests. |
| A edge | fidelity, thread_id | Partial | Independent resolution order tested; no actual reusable backend sessions. |
| A edge | loop_restart | Deferred | Rejected; bounded graph revisits supported instead. |
| B | Mdiamond/start, Msquare/exit | Implemented/tested | Entry/terminal engine behavior; direct exit registry invocation unverified. |
| B | box/codergen | Partial | Deterministic backend and strict process backend seam; live model unverified. |
| B | hexagon/wait.human | Implemented/tested | Queued and async timeout behavior, console stream read. |
| B | diamond/conditional | Implemented/tested | Own SUCCESS with persisted business fact routing. |
| B | component/parallel | Deferred | Validation rejects type. |
| B | tripleoctagon/parallel.fan_in | Deferred | Validation rejects type. |
| B | parallelogram/tool | Implemented/tested | Actual local shell subprocess tests and status mapping. |
| B | house/stack.manager_loop | Deferred | Validation rejects type. |
| C | required external outcome | Implemented/tested | StatusRequiresExternalOutcomeAndRejectsInvalidEnum. |
| C | preferred_label, suggested_next_ids | Implemented/tested | Routing unit tests and human target suggestions. |
| C | context_updates, notes | Implemented/tested | Typed status roundtrip and multi-stage context tests. |
| C | auto-status | Partial | Deliberately narrow: explicit codergen opt-in; no generic missing-status success. |
| C | stage directory/status path | Partial | Visit/attempt extension; raw worker status and effective exhaustion verdict retained separately. |
| D | retryable errors | Implemented/tested | TransientException, network/HTTP429/5xx and timeout tests. |
| D | terminal errors | Implemented/tested | FAIL, HTTP401 and configuration exceptions do not retry. |
| D | pipeline errors | Implemented/tested | Structural errors reject before run; invalid runtime continuation terminates. |
| 5.1 | thread-safe context/deep clone | Implemented/tested | JSON snapshot/clone primitive tested; concurrent branch scheduling not supplied. |
| 5.3 | resume full-to-summary degradation | Deferred | No backend supports full sessions; unsupported modes fail. |
| 5.5 | threshold artifact storage CRUD | Partial | In-memory/file-backed utility tested; catalogue not integrated into automatic stage indexing/resume. |
| 6 | recording, cancellation and default answers | Implemented/tested | HumanArtifactTests; recorded identity is unverified. |
| 9.6 | typed events and JSONL | Partial | Callbacks, persisted lifecycle/stage/interview/checkpoint events; parallel events absent. Duration/artifact-count fields are not fully spec-shaped. |
| 9.4 | composition/graph merge | Deferred | Custom transforms/handlers are seams, not a composition implementation. |
| 9.5 | HTTP/SSE/web gate controls | Deferred | Out of scope per brief. |

## Evidence navigation

- [Parsing and routing](../tests/Nightshift.Tests/ParsingRoutingTests.cs): grammar, defaults, diagnostics, conditions, routing and resolved configuration.
- [Engine](../tests/Nightshift.Tests/EngineTests.cs): retries, gates, cycles, limits, status, checkpoints and cancellation.
- [Human/artifacts](../tests/Nightshift.Tests/HumanArtifactTests.cs): queues, console/callback, timeouts, recording, context and storage.
- [Processes](../tests/Nightshift.Tests/ProcessTests.cs): fake Codex success/failure protocol, shell outcomes and process cleanup.
- [CLI recovery](../tests/Nightshift.Tests/CliRecoveryTests.cs): real abrupt CLI termination, durable continuation, CLI commands and exit codes.
- [Extensibility](../tests/Nightshift.Tests/ExtensibilityTests.cs): transform order, custom lint, jitter/backoff and rejected deferred features.
- [Run transcript](evidence/verification.txt): actual deterministic CLI demos, not merely inspected examples.
