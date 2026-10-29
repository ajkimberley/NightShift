# Local container runtime

Installed and checked on 29 October 2026 as a separately authorized prerequisite for phase three. Phase-three implementation has not started; Nightshift does not yet launch workers through this runtime.

## What is installed

**Podman 6.1.4**, with matching Linux server version, installed through the existing Homebrew installation at `/opt/homebrew/bin/podman`. Podman is open source and needs no paid subscription for this use. Its engine uses the [Apache 2.0 license](https://github.com/podman-container-tools/podman/blob/main/LICENSE); bundled helper components have their own open-source licenses.

On macOS, Podman runs containers inside a Linux VM. [Upstream installation guidance](https://podman.io/docs/installation) recommends its package installer and cautions that the Homebrew distribution is community maintained. Homebrew was chosen here to use the existing package manager without an administrator installer, and the installed runtime was exercised rather than relying on installation success alone.

| Setting | Installed value |
| --- | --- |
| Machine | `nightshift-dev` |
| Provider | Apple Virtualization (`applehv`) |
| Architecture | Linux ARM64 guest on Apple Silicon |
| Resources | 4 CPUs, 4096 MiB RAM, 40 GiB virtual disk capacity |
| Container service | Rootless; guest user `core`, UID 501 |
| Connection | `nightshift-dev`, SSH to this VM through loopback |
| Host sharing | Only the dedicated directory below, read/write |
| Startup | Manual; no login-startup service installed |

The shared directory has the same path on the host and in the VM:

```text
/Users/adamkimberley/Library/Application Support/Nightshift/container-workspaces
```

The repository, entire home directory and protected run store are not shared. The future launcher will need to prepare disposable worker workspaces inside this directory; binding the current source checkout directly is not part of this setup. VM sharing alone does not establish a per-worker security boundary.

No Docker Desktop, Podman Desktop, provider account, privileged socket helper or shell-profile changes were needed. Homebrew automatic updates and installation cleanup were disabled for this installation.

## Start and stop

Run these commands from Terminal. No custom shell variables or source-folder working directory are required.

```sh
podman machine start nightshift-dev
podman --connection nightshift-dev info
```

The VM is intentionally left **stopped** after verification. Start it before container development or the phase-three isolation checks. Stopping it releases its running memory allocation:

```sh
podman machine stop nightshift-dev
```

To inspect its state without starting it:

```sh
podman machine list
```

Do not run `machine init` again for normal use. The machine and downloaded images persist across clean stops.

## Small smoke check

After starting the machine, this runs the already-downloaded official Alpine image by its recorded digest. It has networking disabled and removes the container when finished:

```sh
podman --connection nightshift-dev run --rm --pull=never --network=none \
  docker.io/library/alpine@sha256:2e0b81d60597f449baa56dc25d355367869d2dca4cd57e866f1e2b52af3eb55f \
  sh -c 'echo container-ready'
```

Expected output: `container-ready`.

## What was verified

The [recorded checks](evidence/container-runtime/verification.txt) used `docker.io/library/alpine:3.22`, resolved to the immutable digest above, and confirmed:

- Matching 6.1.4 client/server, rootless service and ARM64 image.
- A non-root container could read and write its single fixture mount, with host-side readback of the result.
- The source checkout and usual container-runtime socket paths were absent inside that container.
- Only the loopback network interface was present with `--network=none`.
- The root filesystem mount was read-only and a write failed.
- Effective capabilities were zero and `NoNewPrivs` was enabled.
- Cgroups enforced the requested 128 MiB memory, 64-process and one-CPU limits.
- No containers or temporary fixture files remained afterward; the VM stopped cleanly.

The [restart check](evidence/container-runtime/restart-check.txt) records a subsequent clean start, container run and clean stop. The [installation fixture script](evidence/container-runtime/prerequisite-check.py) preserves the exact checks; it starts and stops this named VM and is evidence of this one-time setup, not a general machine-management command.

During setup, ending the automation shell terminated background VM processes. An interrupted initial boot left the new VM unusable; only this newly created, empty machine was recreated. Verification then ran start, checks and clean stop within one shell session. Normal Terminal use should use the explicit lifecycle commands above. The initial failure is not counted as a successful check.

These checks establish available runtime features. They do not prove Nightshift protects run state, validates artifacts, handles malicious worker descendants or supports live Codex/Claude authentication inside containers. Those remain phase-three deliverables. No model or work-system access was used here.
