# Specification decisions

Baseline: [pinned specification](reference/attractor-spec.md), revision `fb57a55ed97372a27ac90102f436947e29f48426`. These choices make the implemented subset coherent; they do not resolve upstream ambiguities for other implementations. Evidence refers to tests authored in this implementation, not independent assurance.

## 1. Routing precedence and case

§3.3's detailed pseudocode takes priority over abbreviated summaries: true conditional edges first, weight descending then ordinal target ID; then normalized preferred label on unconditional edges in declaration order; then suggested IDs in suggestion order; then unconditional weight and ordinal lexical target. False-conditioned edges are never fallback candidates. Label normalization ignores case/accelerator prefixes; condition comparison and node IDs retain exact case. No locale-dependent sorting.

Tests: [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs), `ConditionalEdgesWinThenWeightAndOrdinalLexicalBreakTies`, `PreferredLabelsNormalizeAccelerators`, `SuggestionsUseSuggestionOrderAndFalseConditionsNeverFallback`.

## 2. Handler retries versus revisits

§3.5 says FAIL immediately terminates a handler attempt sequence, but §11.5 says FAIL retries too. Follow §3.5: RETRY and classified transient exceptions retry; FAIL, configuration/authentication errors and other exceptions are terminal to the attempt sequence. HTTP network/429/5xx errors preserve transient classification. Stage timeout is transient. `max_retries` is additional attempts; node override then graph `default_max_retries`, then legacy `default_max_retry`, then zero. Exhausted RETRY becomes FAIL unless `allow_partial=true`. PARTIAL_SUCCESS returns immediately and satisfies gates. Backoff defaults to capped exponential with jitter; public `Backoff`/`IRetryDelay` supports deterministic testing. Named preset selection is deferred.

A graph revisit allocates a new visit and fresh retry budget. Historical attempts remain. On crash, an uncertain in-flight attempt is replayed using a new artifact attempt ordinal without consuming an extra *classified failure* retry. The prior retry count is preserved. This at-least-once continuation is an extension, not an exactly-once guarantee.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `RetryMeansAdditionalAttemptsWithBackoffAndRetainedArtifacts`, `ErrorsKeepTransientVersusTerminalClassification`, `RetryExhaustionFailsUnlessPartialExplicitlyAccepted`, `FailedCheckRepairPassIsARevisitNotAHandlerRetry`; [ExtensibilityTests](../tests/Nightshift.Tests/ExtensibilityTests.cs), `BackoffCapsAndJittersAndSupportsConstantDelay`.

## 3. Failure routing cannot fall through success edges

§3.2 applies generic selection after any outcome, while §3.7 provides a dedicated failure path. Follow §3.7 for FAIL: only true condition edges containing an explicit positive `outcome=fail` clause qualify (AND clauses are allowed), then node retry_target, then node fallback_retry_target, then fail. Matching failure edges use weight/ordinal lexical ordering. A generic `context.*` condition without explicit failure intent cannot swallow a failure. Graph recovery targets are used for goal-gate exit checks, not ordinary stage failure. Missing configured targets are warned about and skipped in fallback resolution.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `FailNeverUsesUnrelatedUnconditionalSuccessEdge`, `FailedCheckRepairPassIsARevisitNotAHandlerRetry`, `GoalGateRecoveryFallsThroughInvalidTargetsAndAllowsPartialSuccess`.

## 4. Diamonds have their own outcome

§4.7's conditional handler returns its own SUCCESS. It does not expose the previous validator's outcome. The upstream §2.13 branch example can be misread on this point. Branch demos persist a business fact (`context.tests_passed`) or route directly from the failing validator.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `ConditionalOutcomeIsItsOwnSuccessBusinessFactCarriesPriorResult`; [branching example](../examples/branching.dot), [repair example](../examples/repair.dot).

## 5. Model and fidelity cascade

§2.6's table suggests stylesheet override of node model attributes; §8.3–8.5 explicitly says node values win. Follow §8: individual node declaration, stylesheet specificity/order, inherited node defaults, graph default, system default. Inherited node defaults are baseline values, not individual explicit declarations. `ExplicitKeys` preserves that distinction. Provider auto-detection is delegated to a backend; the Codex adapter accepts only absent/openai provider. Reasoning effort defaults to high. Resolved values carry DOT location or stylesheet selector/rule order provenance.

Follow §5.4 fidelity order: incoming edge, target node, graph default, compact. Thread order: target node, incoming edge, graph `thread_id` (chosen spelling for unspecified graph default thread), innermost derived subgraph class, previous node. `thread_id` at graph level is an extension because Appendix A omits its name.

Tests: [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs), `StylesheetCascadeSourcesAndExplicitDefaultsWork`, `FidelityAndThreadPrecedenceAreIndependent`; [ProcessTests](../tests/Nightshift.Tests/ProcessTests.cs), `OptInCodexProcessUsesDocumentedArgumentsStructuredOutcomeAndMinimalFidelity`.

## 6. Resume restores a continuation, not a skip set

§5.3's completion-list skip wording conflicts with intentional graph cycles in §3.2. Completed IDs are an ordered history, including duplicates. Restore outcomes, context, retries, visits, next node, incoming edge and transitions. Save the chosen continuation atomically with completion state; never recalculate it against a different outcome after resume. A pending execution is checkpointed before launch; interruption replays it. The final checkpoint remains final. Source hash/schema mismatches are rejected. One runner holds an exclusive lease.

Checkpoint extensions: `schema_version`, `graph_hash`, `next_node`, `incoming_edge_index`, `node_outcomes`, `node_visits`, `pending_visit`, `attempt_ordinal`, `pending_retries`, `total_visits`, `transitions`, `revision`, `elapsed_seconds`, `state`, `final_outcome`. We use `context_values` consistently with §5.3's model, rather than the sample serializer's `context`. `node_retries` is retained. `logs` exists for compatibility but structured events are the actual append-only audit.

Artifact path extension: `<node>/visit-N/attempt-N/`, preserving all attempts and replay evidence. No single `<node>/status.json` alias that could conceal older visits. History checkpoints plus latest pointer are written using file flush and same-directory rename. Directory metadata fsync/power-loss guarantees are unverified. Events and checkpoints are separate durability boundaries; replay may duplicate events/effects. `ElapsedSeconds` is cumulative checkpointed active time; downtime and uncheckpointed time lost in a hard crash are not included.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `ResumeAfterCompletionPreservesOutcomesAndDoesNotSkipIntentionalRevisits`, `CancellationBeforeCompletionReplaysPendingStageWithNewArtifacts`, `ResumeRejectsChangedGraphAndRunDirectoryReuse`; [CliRecoveryTests](../tests/Nightshift.Tests/CliRecoveryTests.cs), `AbruptProcessInterruptionBeforeCompletionResumesFromDurableContinuation`.

## 7. Goal gates cover visited latest outcomes

§3.4 checks only entries in node_outcomes; §11.4 can be read as checking every declared goal gate. Follow §3.4: only visited, recorded gates; latest outcome per node; SUCCESS and PARTIAL_SUCCESS satisfy the gate. SKIPPED is not recorded (§5.2), so an earlier recorded failure remains unsatisfied. An unvisited gate does not prevent exit. This is not a complete artifact-versioned acceptance policy: later mutations can invalidate previously checked work. Recovery order is node retry, node fallback, graph retry, graph fallback; nonexistent targets are skipped. Failure without a valid target is explicit. First unsatisfied gate follows first-recorded-node order.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `GoalGateBlocksExitAndMissingRecoveryFails`, `GoalGateRecoveryFallsThroughInvalidTargetsAndAllowsPartialSuccess`, `UnvisitedGoalGatesAreNotAnArtifactAcceptancePolicy`.

## 8. Written condition grammar wins

§10.5 sample pseudocode supports truthiness and skips empty clauses, while §§10.2/10.7 prohibit these extensions. Enforce only `=`, `!=`, `&&`; keys are outcome/preferred_label/context paths. Reject empty clauses, OR, numeric comparison, bare truthiness and unqualified keys. Missing values are empty strings; context-prefixed lookup then unprefixed convenience fallback follows §10.4. Quoted literals can contain `&&` without splitting. Typed boolean/integer context values use JSON's invariant spelling.

Tests: [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs), `ConditionsAreCaseSensitiveMissingIsEmptyAndQuotedConjunctionIsLiteral`, `ConditionsRejectExtensionsAndMalformedClauses`.

## 9. Process completion and status are evidence of different things

§4.5 returns SUCCESS for backend text, Appendix C permits auto-status, and §4.10 omits explicit exit-code handling. Protective divergence: text alone fails unless `auto_status=true`; nonzero process exits always fail, even if a worker supplied success. A zero tool exit means command completion. Tool-supplied status is read if present and its raw file retained. Codex additionally needs a completed protocol event and valid structured final status. All model verdicts are labeled worker claims. No inference of independent correctness from fluency or process completion.

`status.json` uses Appendix C's required `outcome`, not internal `status`. `failure_reason` is an extension. Missing/unknown outcome is rejected. Exhausted retry conversion is retained in `effective-outcome.json`, preserving the raw handler status. Auto-status is only implemented at the codergen backend-result boundary; arbitrary handlers must return an Outcome.

Tests: [ProcessTests](../tests/Nightshift.Tests/ProcessTests.cs), `ToolCommandCapturesOutputAndNonzeroOverridesAClaimedSuccess`, `ToolCanReturnExplicitStatusAndContextUpdates`, fake Codex negative cases; [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `StatusRequiresExternalOutcomeAndRejectsInvalidEnum`, `MissingStatusTextDoesNotBecomeVerifiedSuccessAndAutoStatusIsExplicit`.

## 10. Human choice is explicit and identity is unverified

§4.6 falls back to the first option on unknown answers; reject unknown/ambiguous values and duplicate accelerator keys instead. Accept unique keys (case-insensitive), exact labels or target IDs. Timeout uses `human.default_choice` (target ID preferred; unique key/label also accepted) or returns RETRY; skip returns FAIL. `human.timeout` is a duration extension. No implicit auto-approver. Queue exhaustion is SKIPPED. Persist question, answer, timestamp and identity string, but identity is not authenticated. Console/queue answers do not constitute an approval-security system.

§4.6 returns suggested IDs, whereas §11.6 asks for preferred_label. Follow the detailed §4.6 target suggestion to avoid duplicate label ambiguity. Generic true condition edges still outrank suggestions by §3.3; authors should use unconditional choice edges for human gates.

Tests: [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs), `QueuedAcceptReviseSkipTimeoutAndUnknownAreDeterministic`, `HumanDefaultIsATargetAndTimeoutWithoutDefaultMayRetry`, `HumanTimeoutCancelsAsyncInterviewerAndExternalCancellationIsPreserved`.

## 11. Parallel/join/supervisor contracts deferred

§§3.8/4.8/4.9 leave subgraph join boundaries and scheduling unspecified; all-failed fan-in must fail despite the ranking pseudocode. Completing that coherently needs branch isolation, join detection and cancellation tests. Parallel, fan-in and manager nodes are not registered and fail validation. No placeholder returns SUCCESS. Thus branch context isolation under actual scheduling and filesystem isolation are not implemented or claimed. Context cloning alone is tested as a data primitive. Filesystem sandboxing is not supplied by the engine. `loop_restart` and per-tool hooks are also rejected, rather than approximated.

Tests: [ExtensibilityTests](../tests/Nightshift.Tests/ExtensibilityTests.cs), `DeferredHandlersAreRejectedInsteadOfReturningPlaceholderSuccess`, `UnsupportedHooksAndLoopRestartAreExecutionErrors`.

## 12. Fidelity is a backend capability

§5.4/§9.2 defines behavior beyond merely naming a mode. The scripted backend accepts all strings solely as a deterministic simulation; it performs no LLM handoff. The real process adapter supports only truncate, with a fresh session and minimal goal/run identity plus the stage prompt. It rejects full, compact and all summaries. No fabricated summary is described as preservation-tested. Full-session resume degradation in §5.3 is deferred because no backend supplies full sessions. Model/fidelity resolution can be verified without live models.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `UnsupportedFidelityFailsAndCustomHandlerReplacementExecutes`; [ProcessTests](../tests/Nightshift.Tests/ProcessTests.cs), `AdapterDisabledAndUnsupportedCapabilitiesNeverLaunchAProcess`. See [adapter capability table](codex-adapter.md).

## 13. Validation severities and implicit DOT nodes

§7.2 calls reachability an ERROR, while §11.12 says orphan warning. Follow §7.2. Keep required warnings for unknown type/invalid fidelity, adding execution errors so these cannot quietly fall back. Structurally valid custom types work only when registered. Prompt warnings follow §7.2's prompt-or-explicit-label reading, rather than §11.2's prompt-only checklist. DOT edge references create implicit nodes at first mention, capturing current defaults; `edge_target_exists` also guards graphs built by custom transforms/API.

Bare start/Start and exit/end IDs without explicit shapes resolve to no-op handlers. Explicit type/shape declarations otherwise retain their semantics. Unknown shapes default to codergen per registry semantics; known but deferred shapes never do.

Tests: [ParsingRoutingTests](../tests/Nightshift.Tests/ParsingRoutingTests.cs), structural/default diagnostics; [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `BareStartAndEndIdentifiersResolveToNoOpHandlersAndPublishTypedEvents`.

## 14. Terminal completion and bounded execution extensions

§3.2 returns SUCCESS when no edge is eligible, even before an exit, potentially bypassing gates. Protective divergence: no eligible edge before the terminal is failed/incomplete. Only a terminal exit with satisfied visited gates completes successfully. Run-level visits include entry and exit; finite limits produce a final incomplete outcome. Retry attributes are bounded to 0..1000 to prevent accidental enormous retry budgets. Handler timeouts are per attempt. Custom in-process handlers must cooperate with cancellation; process handlers kill attached trees and cancel pipe reads. Detached/reparented processes can escape and are a documented limit.

Tests: [EngineTests](../tests/Nightshift.Tests/EngineTests.cs), `RunVisitAndTimeLimitsAreNeverSuccess`; [ProcessTests](../tests/Nightshift.Tests/ProcessTests.cs), `ProcessTimeoutKillsTheChildAndPreservesOutput`, `StageTimeoutIsTransientAndExhaustsIntoFail`.

## 15. Artifact catalogue and transform limits

§5.5's threshold store is available as a host utility, including memory/file backing, CRUD and safe IDs. It is not wired into automatic stage output indexing and its in-memory catalogue is not restored on resume. Stage files and checkpoints are durable independently. Custom transforms are applied in registration order to a freshly parsed graph; they are responsible for their own purity. There is no speculative graph merge/subpipeline platform. HTTP, UI, provider clients and the companion coding-agent/LLM specs are outside scope.

Tests: [HumanArtifactTests](../tests/Nightshift.Tests/HumanArtifactTests.cs), `ContextDeepCloneAndArtifactThresholdStorageWork`; [ExtensibilityTests](../tests/Nightshift.Tests/ExtensibilityTests.cs), `CustomTransformsRunInOrderAndCustomDiagnosticsPreserveSeverity`.
