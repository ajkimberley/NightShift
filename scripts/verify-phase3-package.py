#!/usr/bin/env python3
"""Installed protected workflow checks. Uses existing local runtime and offline fixtures only."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parent.parent
evidence = repo / 'docs/evidence/phase-3'
evidence.mkdir(parents=True, exist_ok=True)
transcript = []
# Place fixtures outside both source and protected state; do not modify source permissions.
base = Path.home() / 'Library/Application Support/Nightshift/package-fixtures' if os.uname().sysname == 'Darwin' else Path.home() / '.local/share/nightshift/package-fixtures'
base.mkdir(parents=True, exist_ok=True)
root = Path(tempfile.mkdtemp(prefix='phase3-', dir=base))
tools = root / 'tools'
project = root / 'project'
project.mkdir()
state = root / 'state'

def run(args, expected=0, cwd=project, tool=True):
    command = [str(x) for x in args]
    if tool and command[0] == 'nightshift':
        command[0] = str(tools / 'nightshift')
    transcript.append('$ ' + repr(command))
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True, timeout=120)
    transcript.extend([result.stdout, result.stderr, f'exit={result.returncode}'])
    assert result.returncode == expected, '\n'.join(transcript[-4:])
    return result.stdout

try:
    run(['dotnet', 'pack', repo / 'src/Nightshift.Cli', '-c', 'Release', '--no-restore', '-o', root / 'packages'], cwd=repo, tool=False)
    run(['dotnet', 'tool', 'install', 'Nightshift.Cli', '--tool-path', tools, '--source', root / 'packages', '--version', '0.3.0', '--no-cache'], tool=False)
    assert 'nightshift 0.3.0' in run(['nightshift', '--version'])
    run(['nightshift', 'doctor', '--state-root', state, '--json'])
    run(['nightshift', 'run', '--state-root', state], expected=2)
    run(['nightshift', 'init', '--state-root', state])
    policy_file = project / 'nightshift-policy.json'
    policy = json.loads(policy_file.read_text())
    policy['workflow'] = 'digraph installed { start -> produce -> exit; produce [type=tool, tool_command="printf verified > candidate.txt"]; }'
    policy['checks'] = [{'id': 'bytes', 'command': "printf verified | cmp -s - candidate.txt"}]
    policy_file.write_text(json.dumps(policy, indent=2))
    run(['nightshift', 'adopt', '--policy', policy_file, '--state-root', state])
    # Hide source assets during all installed work; finally restores them even on failure.
    assets = repo / 'src/Nightshift.Cli/Assets'
    hidden = repo / 'src/Nightshift.Cli/Assets.phase3-verification-hidden'
    assets.rename(hidden)
    try:
        run(['nightshift', 'run', '--state-root', state, '--pause-after', '1'], expected=3)
        run(['nightshift', 'status', '--latest', '--state-root', state])
        # Untrusted project configuration cannot redirect an active protected store or backend.
        (project / 'nightshift.json').write_text('{"backend":"codex","run_output_root":"/tmp/forged-state"}')
        run(['nightshift', 'resume', '--latest', '--state-root', state])
        result = json.loads(run(['nightshift', 'result', '--latest', '--state-root', state, '--json']))
        assert result['acceptance']['accepted']
        assert result['identity']['agent']['name'] == 'scripted'
        assert result['identity']['runtime']['profile'] == 'linux-container-v1'
        assert all(e['source'] == 'controlled_local_check' for e in result['evidence'])
        for repository, pipeline in [('github', 'github-actions'), ('azure-devops', 'azure-pipelines')]:
            ref = json.loads(run(['nightshift', 'reference', '--state-root', state, '--repository-service', repository, '--pipeline-service', pipeline]))
            assert ref['accepted'] and ref['stale_rejected'] and ref['dispatches'] == ref['pull_requests'] == 1
    finally:
        hidden.rename(assets)
    run(['dotnet', 'tool', 'uninstall', 'Nightshift.Cli', '--tool-path', tools], tool=False)
    assert not (tools / 'nightshift').exists()
    transcript.append('PASS: installed protected commands, external state, source-independent assets, actual containers, both hosted fixtures, package uninstall.')
finally:
    (evidence / 'package-protected.txt').write_text('\n'.join(transcript))
    shutil.rmtree(root)
print(transcript[-1])
