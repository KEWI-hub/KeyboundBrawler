#!/usr/bin/env bash
# Usage: tools/release.sh <version> testing|stable "notes"
#   testing: uploads to the "testing" prerelease and bumps TestingAssemblyVersion only.
#            Players get it only with "Receive plugin testing versions" turned on.
#   stable:  tags v<version>, makes it the latest release, and bumps both versions.
set -euo pipefail
cd "$(dirname "$0")/.."

version="${1:?version, e.g. 0.7.0.0}"
channel="${2:?testing or stable}"
notes="${3:-}"
zip=KeyboundBrawler/bin/x64/Release/KeyboundBrawler/latest.zip

sed -i "s#<Version>[^<]*</Version>#<Version>${version}</Version>#" KeyboundBrawler/KeyboundBrawler.csproj
python -I - "$version" "$channel" <<'EOF'
import json, sys, time
version, channel = sys.argv[1], sys.argv[2]
repo = json.load(open("repo.json", encoding="utf-8"))
entry = repo[0]
entry["TestingAssemblyVersion"] = version
if channel == "stable":
    entry["AssemblyVersion"] = version
entry["LastUpdate"] = int(time.time())
json.dump(repo, open("repo.json", "w", encoding="utf-8"), indent=2, ensure_ascii=False)
EOF

dotnet build -c Release | grep -E "error|Build succeeded"
unzip -p "$zip" KeyboundBrawler.json | grep -q "\"AssemblyVersion\": \"${version}\""

git add -A
git commit -q -m "${channel} ${version}

${notes}

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -q

if [ "$channel" = "stable" ]; then
    gh release create "v${version%.0}" "$zip" --title "Keybound Brawler Helper ${version%.0}" \
        --notes "${notes}

Support: https://ko-fi.com/kewiv"
else
    gh release view testing >/dev/null 2>&1 || \
        gh release create testing --prerelease --title "Testing build" --notes "Rolling testing build."
    gh release upload testing "$zip" --clobber
    gh release edit testing --prerelease --notes "Testing ${version}

${notes}"
fi
echo "Released ${version} to ${channel}. raw.githubusercontent.com caches repo.json for ~5 minutes."
