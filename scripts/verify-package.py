#!/usr/bin/env python3
"""Exercise only local packages, scripted workers and fake Codex; retain evidence."""
import json
import os
import re
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parent.parent
root = Path(tempfile.mkdtemp(prefix="package-check-", dir=repo / "artifacts"))
feed = root / "feed"
tools = root / "tools"
project = root / "personal project"
project.mkdir()
log_path = repo / "docs/evidence/phase-3/package-legacy.txt"
log_path.parent.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, NUGET_PACKAGES=str(root / "cache"))
env["PATH"] = str(tools) + os.pathsep + env.get("PATH", "")

with log_path.open("w") as log:
    def run(args, cwd=repo, expected=0):
        if str(args[0]) == "nightshift" and len(args) > 1 and args[1] not in ("--version", "help"):
            args = [*args, "--profile", "native-development"]
        log.write("\n$ " + shlex.join(map(str, args)) + "\n")
        log.flush()
        result = subprocess.run(list(map(str, args)), cwd=cwd, env=env, text=True, capture_output=True, timeout=120)
        log.write(result.stdout + result.stderr + f"[exit {result.returncode}; expected {expected}]\n")
        log.flush()
        if result.returncode != expected:
            raise RuntimeError(f"Unexpected result; inspect {log_path}")
        return result.stdout

    for version in ("0.2.0", "0.3.1"):
        run(["dotnet", "pack", "src/Nightshift.Cli", "-c", "Release", "--no-restore", "-p:Version=" + version, "-o", feed])
    run(["dotnet", "tool", "install", "Nightshift.Cli", "--tool-path", tools, "--source", feed, "--version", "0.2.0"])
    assert "nightshift 0.2.0" in run(["nightshift", "--version"], project)
    fake = root / "fake codex with spaces"
    fake.write_text('''#!/bin/sh
case "$1:$2" in
  --version:) echo 'codex-cli 1.2.3'; exit 0 ;;
  exec:--help) echo '--json --ephemeral --sandbox --output-schema --output-last-message --cd --config --model'; exit 0 ;;
  login:status) echo 'private authentication diagnostic'; exit 0 ;;
esac
output=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = '--output-last-message' ]; then shift; output=$1; fi
  shift
done
cat > "$NIGHTSHIFT_EXCHANGE_DIR/input.txt"
if grep -q 'Hello from Nightshift' "$NIGHTSHIFT_EXCHANGE_DIR/input.txt"; then
  printf 'Hello from Nightshift\\n' > report.txt
else
  printf 'PACKAGE_VERIFIED\\n' > generated.txt
fi
printf '%s' '{"outcome":"success","notes":"fake package worker claim"}' > "$output"
printf '%s\\n' '{"type":"turn.completed"}'
''')
    fake.chmod(0o700)
    # Remove source assets temporarily: only package assets may satisfy demo/init.
    assets = repo / "src/Nightshift.Cli/Assets"
    held_assets = root / "source-assets-held"
    assets.rename(held_assets)
    try:
        doctor = run(["nightshift", "doctor", "--codex", fake, "--json"], project)
        assert "private authentication diagnostic" not in doctor
        assert json.loads(doctor)["healthy"]
        demo = json.loads(run(["nightshift", "demo", "--json"], project))
        assert Path(demo["outputs"][0]["path"]).read_bytes() == b"NIGHTSHIFT_DEMO_OK\n"
        assert "verify: success" in str(demo["tool_verdicts"])
        run(["nightshift", "init", "--backend", "codex", "--enable-codex", "--codex", fake], project)
    finally:
        held_assets.rename(assets)
    config_path = project / "nightshift.json"
    config = json.loads(config_path.read_text())
    config["outputs"] = ["generated.txt"]
    config_path.write_text(json.dumps(config))
    (project / "workflow.dot").write_text('''digraph PackageCheck {
      graph [default_fidelity=truncate]
      start [shape=Mdiamond]
      worker [prompt="Write the package fixture"]
      verify [type=tool, tool_command="printf 'PACKAGE_VERIFIED\\\\n' | cmp -s - generated.txt", goal_gate=true]
      exit [shape=Msquare]
      start -> worker -> verify -> exit
    }''')
    run(["nightshift", "validate"], project)
    run(["nightshift", "run", "--pause-after", "1"], project, 3)
    run(["nightshift", "status", "--latest"], project)
    config_path.write_text(json.dumps(dict(config, backend="scripted", sandbox="workspace-write", working_directory="missing")))
    resumed = run(["nightshift", "resume", "--latest"], project)
    assert "codex-process" in resumed
    config_path.write_text(json.dumps(config))
    result = json.loads(run(["nightshift", "result", "--latest", "--json"], project))
    assert result["backend"] == "codex" and result["sandbox"] == "read-only"
    assert result["state"] == "success"
    assert Path(result["outputs"][0]["path"]).read_bytes() == b"PACKAGE_VERIFIED\n"
    run(["nightshift", "resume", result["run_directory"], "--backend", "scripted"], project, 2)
    tutorial = root / "tutorial project"
    tutorial.mkdir()
    run(["nightshift", "init", "--backend", "codex", "--enable-codex", "--codex", fake, "--sandbox", "workspace-write", "--output", "report.txt"], tutorial)
    for guide in ("codex-project.md", "workflows.md"):
        blocks = re.findall(r"```dot\n(.*?)\n```", (repo / "docs/guides" / guide).read_text(), re.S)
        for index, block in enumerate(blocks):
            workflow = tutorial / (guide + str(index) + ".dot")
            workflow.write_text(block)
            run(["nightshift", "validate", workflow], tutorial)
            run(["nightshift", "run", workflow, "--max-visits", "12", "--max-seconds", "30", "--answers", "A"], tutorial)
            assert (tutorial / "report.txt").read_bytes() == b"Hello from Nightshift\n"
            if "review [" in block:
                run(["nightshift", "run", workflow, "--max-visits", "12", "--max-seconds", "30", "--answers", "R,A"], tutorial)
    # Pause after demo artifact creation; removing/corrupting it must fail the installed verifier.
    for damage in ("missing", "wrong"):
        run_dir = root / ("demo-" + damage)
        run(["nightshift", "demo", "--dir", run_dir, "--pause-after", "3"], project, 3)
        manifest = json.loads((run_dir / "manifest.json").read_text())
        artifact = Path(manifest["execution"]["outputs"][0])
        artifact.unlink() if damage == "missing" else artifact.write_text("wrong\n")
        run(["nightshift", "resume", run_dir], project, 1)
        assert json.loads((run_dir / "checkpoint.json").read_text())["state"] == "failed"
    run(["dotnet", "tool", "update", "Nightshift.Cli", "--tool-path", tools, "--source", feed, "--version", "0.3.1"])
    assert "nightshift 0.3.1" in run(["nightshift", "--version"], project)
    run(["dotnet", "tool", "uninstall", "Nightshift.Cli", "--tool-path", tools])
    assert not (tools / "nightshift").exists()
    log.write(f"\nPASS: package lifecycle and installed onboarding. Evidence retained: {root}\n")
print(f"PASS: installed package checks. Transcript: {log_path}")
