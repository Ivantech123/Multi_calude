#!/bin/sh
# Local macOS / Linux build: dist/multi_calude-<rid>.tar.gz
#
#   scripts/build.sh              for this machine
#   scripts/build.sh linux-arm64  for another platform (build macOS ones on a Mac)

set -eu
cd "$(dirname "$0")/.."

rid="${1:-}"
if [ -z "$rid" ]; then
    case "$(uname -s)" in Linux) os=linux ;; Darwin) os=osx ;; *) echo "unknown OS" >&2; exit 1 ;; esac
    case "$(uname -m)" in x86_64 | amd64) arch=x64 ;; arm64 | aarch64) arch=arm64 ;; *) echo "unknown CPU" >&2; exit 1 ;; esac
    rid="$os-$arch"
fi

out="dist/$rid"
rm -rf "$out"
dotnet publish src/cli/cli.csproj -c Release -r "$rid" -o "$out" --nologo
chmod +x "$out/cli"
cp README.md LICENSE "$out/"
tar -czf "dist/multi_calude-$rid.tar.gz" -C "$out" cli README.md LICENSE
echo "-> dist/multi_calude-$rid.tar.gz"
