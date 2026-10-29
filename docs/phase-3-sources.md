# Official protocol sources inspected 29 October 2026

These are protocol references, not evidence of live account compatibility. Web
pages are rolling documentation; API versions and the local help version below
pin the actual implementation choices. No newer Attractor revision was adopted.

- [Codex noninteractive interface](https://learn.chatgpt.com/docs/non-interactive-mode)
  and local `codex --version` / `codex exec --help`: 0.164.0-alpha.11.1.
  JSONL events and structured completion text are parsed; no Codex model was invoked.
- [Claude headless](https://code.claude.com/docs/en/headless),
  [CLI reference](https://code.claude.com/docs/en/cli-reference),
  [sandbox scope](https://code.claude.com/docs/en/sandboxing).
  Claude CLI is not installed on the host. Fake-executable tests validate translation;
  bare/stream-json/json-schema/effort flags require a compatible reviewed Linux image.
- [GitHub pull requests](https://docs.github.com/en/rest/pulls/pulls),
  [workflow dispatch](https://docs.github.com/en/rest/actions/workflows),
  [native run observation/cancellation/logs](https://docs.github.com/en/rest/actions/workflow-runs).
  Adapter sends `X-GitHub-Api-Version: 2022-11-28`.
- [ADO PR create](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/create?view=azure-devops-rest-7.1),
  [queue build](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/queue?view=azure-devops-rest-7.1),
  [list/reconcile](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1),
  [cancel/update](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/update-build?view=azure-devops-rest-7.1),
  [version compatibility guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/concepts/rest-api-versioning?view=azure-devops).
  Every Azure request pins `api-version=7.1`; no Server version is inferred.
- [Podman run configuration](https://docs.podman.io/en/latest/markdown/podman-run.1.html)
  and [container runtime security assumptions](https://docs.docker.com/engine/security/).
  Actual feature checks and image identity are recorded separately under evidence.
