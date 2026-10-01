#!/usr/bin/env bash
# Pack TextMagic and push it to nuget.org.
# The API key is NUGET_API_KEY in .agent-secrets, unlocked with ~/.ssh/id_rsa.
set -euo pipefail

root=$(cd "$(dirname "$0")" && pwd)
cd "$root"

if command -v agent-secrets >/dev/null 2>&1; then
  AS=$(command -v agent-secrets)
elif [[ -x "${HOME}/git/agent-secrets/agent-keys/target/release/agent-secrets" ]]; then
  AS="${HOME}/git/agent-secrets/agent-keys/target/release/agent-secrets"
else
  echo "agent-secrets is not on PATH." >&2
  exit 1
fi

if ! "$AS" kv get NUGET_API_KEY --no-newline >/dev/null 2>&1; then
  "$AS" close >/dev/null 2>&1 || true
  "$AS" unlock
fi

api_key=$("$AS" kv get NUGET_API_KEY --no-newline)
if [[ -z "${api_key}" ]]; then
  echo "NUGET_API_KEY is empty." >&2
  exit 1
fi

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/TextMagic/TextMagic.csproj)
if [[ -z "${version}" ]]; then
  echo "Could not read Version from src/TextMagic/TextMagic.csproj." >&2
  exit 1
fi

mkdir -p artifacts
dotnet pack src/TextMagic -c Release -o artifacts

pkg="artifacts/TextMagic.${version}.nupkg"
if [[ ! -f "${pkg}" ]]; then
  echo "Pack did not produce ${pkg}." >&2
  exit 1
fi

echo "Pushing TextMagic.${version}.nupkg to nuget.org"
dotnet nuget push "${pkg}" \
  --api-key "${api_key}" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
unset api_key
echo "Published TextMagic ${version}."
