#!/usr/bin/env bash
# Run from the repository root. Only deterministic workers and local shell fixtures execute.
set -euo pipefail
cli=(dotnet run --no-build --project src/Nightshift.Cli --)
# Use a fresh deterministic evidence directory; refuse to overwrite earlier runs.
run_root=${1:-runs/verification}
if [ -e "$run_root" ]; then
  echo "Evidence directory already exists: $run_root (choose another directory)" >&2
  exit 2
fi
mkdir -p "$run_root"
dotnet --version
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --logger 'console;verbosity=minimal'
"${cli[@]}" help
"${cli[@]}" validate examples/linear.dot
"${cli[@]}" run examples/linear.dot --dir "$run_root/linear"
"${cli[@]}" run examples/branching.dot --dir "$run_root/branching"
"${cli[@]}" run examples/repair.dot --dir "$run_root/repair"
"${cli[@]}" run examples/human.dot --dir "$run_root/human" --answers R,A
set +e
"${cli[@]}" run examples/resume.dot --dir "$run_root/resume" --pause-after 2
pause_code=$?
set -e
if [ "$pause_code" -ne 3 ]; then
  echo "Expected interrupted exit 3; got $pause_code" >&2
  exit 1
fi
"${cli[@]}" resume "$run_root/resume"
"${cli[@]}" inspect "$run_root/repair" > "$run_root/repair-inspect.json"
"${cli[@]}" events "$run_root/repair" > "$run_root/repair-events.jsonl"
echo "Verified deterministic evidence: $run_root"
