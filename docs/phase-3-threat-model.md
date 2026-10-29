# Phase three threat model

The worker may ignore prompts, execute arbitrary programs and children, know every
host path, forge messages/status/evidence and modify its checkout, tests, hooks,
Git configuration, PATH tools and pipeline YAML. All checkout programs are hostile.
The trusted computing base is the coordinator and installed assemblies, operator
approved immutable policies, host OS and rootless Podman/VM. Kernel/runtime
exploitation and a malicious host administrator are excluded. This is a bounded
filesystem/process profile, not a general sandbox escape or correctness guarantee.

The coordinator alone owns state, credentials and acceptance. New protected runs
live outside checkouts and container sharing. Workers receive fresh byte copies,
never host Git administration, state, home, service credentials or runtime sockets.
All descendants share a nonprivileged, network-disabled Linux container. Local
verifiers use the same launcher and a frozen candidate copy. Agent CLI permission
prompts are not an enforcement boundary.

Collection occurs only after removing the owned container. Export is a bounded tar
stream read without extracting links or trusting archive paths. Links, special
files, duplicates and traversal are rejected. A fresh host snapshot has no worker
writers or pre-existing hardlink aliases. Worker JSON is always a claim. Evidence
is created only by coordinator code and binds repository, immutable snapshot,
approved policy digest, check identity and terminal result. Stale/unknown evidence
cannot accept code. Repo-controlled verification definitions need explicit adoption.

Host provider requests use coordinator-only authentication and origin-bound HTTP
with redirects disabled. Durable intents precede POST; ambiguous dispatch is
reconciled by native identities and exact candidate/correlation, or remains uncertain.
No whole-task transport retry or exactly-once hosted-effect claim is made.

Before mechanisms were implemented, inspection found stage paths exposed through
ATTRACTOR_STAGE_DIR, native host tools, mutable StageRequest backends and project
run storage. Those paths remain legacy inspection/development compatibility only;
protected execution must never enter them.
