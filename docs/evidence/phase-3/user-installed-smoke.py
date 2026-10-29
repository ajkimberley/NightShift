from pathlib import Path
import hashlib
import json
import shlex
import shutil
import subprocess
import uuid

repo = Path('/Users/adamkimberley/Repos/nightshift')
evidence = repo / 'docs/evidence/phase-3'
cli = Path.home() / '.dotnet/tools/nightshift'
project = Path.home() / 'Library/Application Support/Nightshift/user-installed-smoke' / uuid.uuid4().hex
project.mkdir(parents=True)
lines = []
def run(*args):
    command = [str(cli), *args]
    lines.append('$ ' + shlex.join(command))
    result = subprocess.run(command, cwd=project, text=True, capture_output=True, timeout=120)
    lines.extend([result.stdout, result.stderr, 'exit=' + str(result.returncode)])
    assert result.returncode == 0, result.stdout + result.stderr
    return result.stdout
try:
    assert 'nightshift 0.3.1' in run('--version')
    run('doctor', '--json')
    demo = json.loads(run('demo', '--json'))
    assert demo['acceptance']['accepted']
    run('status', demo['run']['run_directory'])
    inspected = json.loads(run('result', demo['run']['run_directory'], '--json'))
    assert inspected['acceptance']['accepted']
    for repository, pipeline, label in [('github', 'github-actions', 'github'), ('azure-devops', 'azure-pipelines', 'azure')]:
        result = json.loads(run('reference', '--repository-service', repository, '--pipeline-service', pipeline))
        assert result['accepted'] and result['stale_rejected'] and result['dispatches'] == result['pull_requests'] == 1
        run_dir = Path(result['directory'])
        assert run_dir.is_relative_to(Path.home() / 'Library/Application Support/Nightshift/state')
        copied = evidence / ('installed-reference-' + label)
        copied.mkdir(exist_ok=True)
        for name in ['manifest.json', 'checkpoint.json', 'protected-identity.json', 'approved-policy.json', 'hosted-evidence.json', 'hosted-output.json', 'stale-candidate.json', 'fixture-requests.json', 'dispatch-interruption.txt']:
            shutil.copyfile(run_dir / name, copied / name)
        shutil.copyfile(run_dir / 'hosted/intent.json', copied / 'hosted-intent.json')
        (copied / 'reference-result.json').write_text(json.dumps(result, indent=2))
        (copied / 'PROVENANCE.txt').write_text('Audit copies from the coordinator-owned run path in reference-result.json. Real local containers/Git; fake HTTP only. These checkout copies are not an authoritative run store.\n')
    lines.append('PASS: user installation 0.3.1; doctor, enforced demo, explicit-path status/result outside project, GitHub and ADO references; no model or live account.')
finally:
    (evidence / 'user-installed-smoke.txt').write_text('\n'.join(lines))
print(lines[-1])
