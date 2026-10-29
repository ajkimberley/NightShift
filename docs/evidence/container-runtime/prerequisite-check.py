import json, pathlib, subprocess, tempfile, shutil
root = pathlib.Path('docs/evidence/container-runtime')
share = pathlib.Path('/Users/adamkimberley/Library/Application Support/Nightshift/container-workspaces')
fixture = pathlib.Path(tempfile.mkdtemp(prefix='prerequisite-', dir=share))
connection = ['podman', '--connection', 'nightshift-dev']

def run(args, *, timeout=60):
    print('+', ' '.join(str(x) for x in args), flush=True)
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if result.stdout:
        print(result.stdout, end='' if result.stdout.endswith('\n') else '\n', flush=True)
    if result.stderr:
        print(result.stderr, end='' if result.stderr.endswith('\n') else '\n', flush=True)
    result.check_returncode()
    return result.stdout

try:
    run(['podman', 'machine', 'start', '--no-info', 'nightshift-dev'], timeout=90)
    info = json.loads(run(connection + ['info', '--format', 'json']))
    assert info['host']['security']['rootless']
    assert info['host']['arch'] == 'arm64'
    assert info['version']['Version'] == '6.1.4'
    run(connection + ['pull', 'docker.io/library/alpine:3.22'], timeout=180)
    image = json.loads(run(connection + ['image', 'inspect', 'docker.io/library/alpine:3.22']))[0]
    digest = next(x for x in image['RepoDigests'] if x.startswith('docker.io/library/alpine@sha256:'))
    assert image['Architecture'] == 'arm64'
    (root / 'image-digest.txt').write_text(digest + '\n')
    (fixture / 'input.txt').write_text('nightshift prerequisite\n')
    checks = r'''
set -eu
[ "$(id -u)" = 501 ]
[ "$(cat /workspace/input.txt)" = 'nightshift prerequisite' ]
printf 'container wrote this\n' > /workspace/output.txt
[ ! -e /Users/adamkimberley/Repos/nightshift ]
[ ! -S /run/podman/podman.sock ]
[ ! -S /var/run/docker.sock ]
[ "$(ls /sys/class/net)" = lo ]
awk '$2 == "/" { if ($4 !~ /(^|,)ro(,|$)/) exit 1; found=1 } END { if (!found) exit 1 }' /proc/mounts
if touch /root-filesystem-write 2>/dev/null; then exit 1; fi
grep -Eq '^CapEff:[[:space:]]+0+$' /proc/self/status
grep -Eq '^NoNewPrivs:[[:space:]]+1$' /proc/self/status
[ "$(cat /sys/fs/cgroup/memory.max)" = 134217728 ]
[ "$(cat /sys/fs/cgroup/pids.max)" = 64 ]
[ "$(cat /sys/fs/cgroup/cpu.max)" = '100000 100000' ]
printf 'PASS: uid, workspace read/write, hidden checkout, no runtime sockets, loopback only, read-only root, dropped capabilities, no-new-privileges, memory/pids/cpu limits\n'
'''
    run(connection + ['run', '--rm', '--pull=never', '--network=none', '--read-only',
        '--cap-drop=ALL', '--security-opt=no-new-privileges', '--pids-limit=64',
        '--memory=128m', '--cpus=1', '--userns=keep-id', '--user=501:501',
        '--volume', str(fixture) + ':/workspace:rw', '--workdir=/workspace',
        digest, 'sh', '-c', checks])
    assert (fixture / 'output.txt').read_text() == 'container wrote this\n'
    print('PASS: host read back container output', flush=True)
    containers = json.loads(run(connection + ['ps', '--all', '--format', 'json']))
    assert containers == [], containers
    print('PASS: no leftover containers', flush=True)
finally:
    shutil.rmtree(fixture)
    run(['podman', 'machine', 'stop', 'nightshift-dev'], timeout=45)
print('ALL PREREQUISITE CHECKS PASSED; VM stopped cleanly', flush=True)
