# Post-review fixes — 0.3.2

Both findings from the read-only phase-three review are addressed.

- Successful protected tool stages now publish `tool.output` and `tool.exit_code`
  as untrusted routing context, preserving the existing graph contract. Independent
  acceptance still uses coordinator-owned evidence for the exact candidate.
- Initial snapshot capture refuses nonregular files before opening them. The open
  uses nonblocking/no-follow/close-on-exec flags, and the resulting descriptor is
  checked again before any read. A regular file replaced with a FIFO cannot hang
  open. Size bounds and executable mode come from the descriptor; reads remain
  bounded even if a regular file grows. Capture also observes caller cancellation.
- `RegularSnapshotFile` supports the explicit macOS/Linux ARM64 and x64 stat ABIs;
  other platforms fail closed. macOS ARM64 was actually exercised here; Linux-host
  and macOS x64 execution remain unverified. Darwin constants/layout came from the
  installed SDK's `sys/stat.h` and `sys/fcntl.h`. Linux declarations were checked
  against the upstream glibc [generic stat layout](https://github.com/bminor/glibc/blob/master/sysdeps/unix/sysv/linux/bits/struct_stat.h),
  [x64 stat layout](https://github.com/bminor/glibc/blob/master/sysdeps/unix/sysv/linux/x86/bits/struct_stat.h)
  and [open flags](https://github.com/bminor/glibc/blob/master/sysdeps/unix/sysv/linux/bits/fcntl-linux.h).

## Verification

```sh
dotnet restore --locked-mode
dotnet test --no-restore --logger 'console;verbosity=normal'
dotnet test -c Release --no-restore --logger 'console;verbosity=normal'
python3 docs/evidence/review-fixes/verify-packages.py
bash scripts/install.sh update
python3 docs/evidence/review-fixes/verify-installed.py
```

**198/198 pass in Debug and Release.** Five new cases cover conditional tool routing
in the actual container profile, prompt FIFO refusal in a bounded CLI subprocess,
Unix sockets, regular bytes/mode/size bounds, and cancellation/symlinks. Both existing
package verifiers pass against isolated installations; historical phase-three logs
are restored byte-for-byte by the wrapper. The current-user CLI is updated to 0.3.2;
its installed conditional graph passes independent verification, and its FIFO run
fails promptly before starting a worker. No model or hosted CI operation ran.

The first full Debug run exposed the existing native timeout fixture's 50 ms startup
race (one of two attempts timed out before its PID artifact existed). The command
still sleeps 30 seconds; a 500 ms deadline now allows startup under parallel load.
All assertions on two attempts, retry exhaustion and failure remain. The original
failure is retained beside the passing transcripts.

See [review evidence](evidence/review-fixes/), including [Debug](evidence/review-fixes/tests-debug.txt),
[Release](evidence/review-fixes/tests-release.txt), [installed smoke](evidence/review-fixes/installed-smoke.txt)
and the preserved [initial failure](evidence/review-fixes/tests-debug-initial-failure.txt).
New coordinator binaries retain the existing policy: old runs remain inspectable;
resuming across a coordinator hash change requires a fresh protected run.

## Branch integration

The operator requested staging, committing, pushing and retiring `implement/attractor`.
Inspection found no `main` locally or remotely; the implement branch was the only
remote branch and its default. There is no separate main history to replay onto.
The complete existing history is therefore retained when moving to `main`; no
history rewrite or force push is needed. The approved goal and runtime prerequisite
files previously left local are now committed as part of the same requested work.
The default branch is switched only after `main` has been pushed, and the old remote
branch is removed only after confirming its commits are ancestors of `main`.
