#!/usr/bin/env bash
# Build a local package and manage Nightshift for the current user. No model calls.
set -euo pipefail
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
action=${1:-install}
package_id=Nightshift.Cli
package_version=0.3.1
tool_dir="$HOME/.dotnet/tools"
case "$action" in
  install|update|uninstall) ;;
  *) echo "Usage: bash scripts/install.sh install|update|uninstall" >&2; exit 2 ;;
esac
installed=$(dotnet tool list --global | awk 'tolower($1)=="nightshift.cli" {print $2}')
if [ "$action" = uninstall ]; then
  if [ -z "$installed" ]; then echo 'Nightshift is not installed for this user.'; exit 0; fi
  dotnet tool uninstall --global "$package_id"
  echo 'Run evidence and project configurations were retained.'
  exit 0
fi
if [ -z "$installed" ] && { [ -e "$tool_dir/nightshift" ] || command -v nightshift >/dev/null 2>&1; }; then
  echo 'An unrelated nightshift command already exists. Resolve the name collision before installing.' >&2
  exit 2
fi
cd "$repo_dir"
dotnet restore --locked-mode
dotnet pack src/Nightshift.Cli -c Release --no-restore -o artifacts/packages
if [ -n "$installed" ]; then
  dotnet tool update --global "$package_id" --source "$repo_dir/artifacts/packages" --version "$package_version" --no-cache
else
  dotnet tool install --global "$package_id" --source "$repo_dir/artifacts/packages" --version "$package_version" --no-cache
fi
"$tool_dir/nightshift" --version
case ":$PATH:" in
  *":$tool_dir:"*) echo 'Ready: nightshift doctor; nightshift demo' ;;
  *)
    echo "Installed executable: $tool_dir/nightshift"
    echo 'The tools directory is missing from PATH. For this Terminal tab, run:'
    echo '  export PATH="$HOME/.dotnet/tools:$PATH"'
    echo 'Then run: nightshift doctor; nightshift demo'
    echo 'No shell startup file was modified.'
    ;;
esac
