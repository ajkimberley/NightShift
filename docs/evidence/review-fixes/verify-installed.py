from pathlib import Path
import json
import os
import shlex
import subprocess
import uuid

repo = Path(__file__).resolve().parents[3]
evidence = Path(__file__).resolve().parent
cli = Path.home() / '.dotnet/tools/nightshift'
root = Path.home() / 'Library/Application Support/Nightshift/review-fixes-installed-smoke' / uuid.uuid4().hex
project = root / 'project'
project.mkdir(parents=True)
state = root / 'state'
transcript = []
def run(*args, expected=0, seconds=30):
    command = [str(cli), *map(str, args)]
    transcript.append('$ ' + shlex.join(command))
    result = subprocess.run(command, cwd=project, text=True, capture_output=True, timeout=seconds)
    transcript.extend([result.stdout, result.stderr, f'exit={result.returncode}'])
    assert result.returncode == expected, result.stdout + result.stderr
    return result
try:
    assert 'nightshift 0.3.2' in run('--version').stdout
    run('init', '--state-root', state)
    path = project / 'nightshift-policy.json'
    policy = json.loads(path.read_text())
    policy['workflow'] = '''digraph routing {
      start -> probe;
      probe [type=tool, tool_command="printf ready"];
      probe -> expected [condition="context.tool.output=ready && context.tool.exit_code=0"];
      probe -> wrong;
      expected [type=tool, tool_command="printf checked > candidate.txt"];
      wrong [type=tool, tool_command="exit 9"];
      expected -> exit; wrong -> exit;
    }'''
    policy['checks'] = [{'id': 'bytes', 'command': 'printf checked | cmp -s - candidate.txt'}]
    path.write_text(json.dumps(policy))
    run('adopt', '--state-root', state)
    result = json.loads(run('run', '--state-root', state, '--max-seconds', '15', '--json').stdout)
    assert result['acceptance']['accepted']
    checkpoint = json.loads(Path(result['run']['checkpoint']).read_text())
    assert checkpoint['completed_nodes'] == ['start', 'probe', 'expected', 'exit']
    updates = checkpoint['node_outcomes']['probe']['context_updates']
    assert updates['tool.output'] == 'ready' and updates['tool.exit_code'] == 0
    os.mkfifo(project / 'named-pipe', 0o600)
    refused = run('run', '--state-root', state, '--max-seconds', '1', expected=2, seconds=5)
    assert 'not a regular file' in refused.stderr
    transcript.append('PASS: installed 0.3.2 routes on untrusted tool output and exit code; initial FIFO refuses promptly; protected acceptance remains independent.')
finally:
    (evidence / 'installed-smoke.txt').write_text('\n'.join(transcript))
print(transcript[-1])
