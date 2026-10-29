"""Run current package verifiers while retaining the historical phase-three logs."""
from pathlib import Path
import subprocess

repo = Path(__file__).resolve().parents[3]
evidence = Path(__file__).resolve().parent
for script, name in [('verify-package.py', 'package-legacy.txt'), ('verify-phase3-package.py', 'package-protected.txt')]:
    historical = repo / 'docs/evidence/phase-3' / name
    original = historical.read_bytes()
    try:
        result = subprocess.run(['python3', str(repo / 'scripts' / script)], cwd=repo, text=True, capture_output=True, timeout=180)
        (evidence / (script + '.summary.txt')).write_text(result.stdout + result.stderr + f'\nexit={result.returncode}\n')
        assert result.returncode == 0, result.stdout + result.stderr
    finally:
        if historical.exists():
            (evidence / name).write_bytes(historical.read_bytes())
        historical.write_bytes(original)
print('PASS: isolated native lifecycle and protected package checks; historical logs retained.')
