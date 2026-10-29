# Development verification history

All tests were authored during this implementation. They are not independent assurance. Assertions were not weakened or deleted to obtain green results.

| Command | Observed result | Correction / follow-up |
|---|---|---|
| `dotnet --info` | SDK 10.0.103, runtime 10.0.3, macOS arm64 | SDK pinned; no installation |
| `dotnet build --no-restore` (initial model/parser) | Passed, 0 warnings/errors | Continued to behavior tests |
| `dotnet test --no-restore --logger 'console;verbosity=minimal'` (first parser/routing suite) | 32 passed, 1 failed / 33 | Boolean context conversion produced `True` instead of `true`; use invariant JSON spelling. Unchanged assertion then passed 33/33. |
| Same command (engine suite) | 57 passed | Retries, gates, visits, resume, cancellation |
| Same command (human/process suite) | 78 passed, 1 failed / 79 | `IgnoreReadOnlyProperties` omitted anonymous JSON fields, including schema/config data. Remove broad setting; explicitly ignore computed Outcome.IsSuccess. Unchanged assertion then passed 79/79. Added schema/config payload assertions later. |
| `dotnet test --logger 'console;verbosity=minimal'` (CLI crash suite) | 86 passed, 1 failed / 87 | Test fixture used empty `touch` sentinel but helper waits for nonempty evidence. Changed the *fixture command* to write `effect` to marker; recovery/retained-evidence assertions unchanged. |
| `dotnet test --no-restore --logger 'console;verbosity=minimal'` | 87 passed | CLI kill/resume and opt-in/exit-code checks |
| `bash scripts/verify.sh runs/verification > docs/evidence/verification.txt 2>&1` | Passed; 87 tests and all demos | Expected pause exit 3 explicitly checked |
| `dotnet test --no-restore --logger 'console;verbosity=minimal'` (final review additions) | 88 passed | Bare start/end fallback, event callback, cancellation-aware pipe reads, node-default/style precedence |

Final verification transcript is regenerated after the final meaningful code changes. It supersedes the intermediate demo transcript above. See [verification.txt](verification.txt) and [release-verification.txt](release-verification.txt).

Concrete review fixes: original external tool status is now retained before nonzero-exit override; inherited model defaults yield to stylesheet rules while explicit node declarations win; bare entry/exit IDs resolve to no-op types; null context values resolve to an empty/default string; pipe reads respect cancellation. Tests cover these behaviors. No real model sessions ran.

Final meaningful changes passed **91/91** tests in both Debug and Release. Added recovery assertions preserve pending retry budgets and unsatisfied goal-gate outcomes, and prevent no-edge completion from bypassing the terminal. Console question types and explicit test auto-approver behavior were checked. The final deterministic demo suite completed with exit 0 and its expected pause exit 3.

## 2026-10-29 — practical Codex readiness follow-up

`dotnet test --no-restore --filter FullyQualifiedName~OptInCodexProcessUsesDocumentedArgumentsStructuredOutcomeAndMinimalFidelity --logger 'console;verbosity=minimal'` failed 1/1 after adding an assertion for JSON Schema's exact `additionalProperties` spelling. The shared snake-case serializer had emitted `additional_properties`. Fixed schema emission using explicit dictionary keys, leaving the assertion intact. Re-ran `dotnet test --no-restore --logger 'console;verbosity=minimal'` and its Release equivalent; see [follow-up transcript](codex-schema-followup.txt). This defect demonstrates why fake-process success alone does not establish live Codex compatibility. No model-backed invocation was authorized or performed.
