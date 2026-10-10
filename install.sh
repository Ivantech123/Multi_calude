#!/bin/sh
# Multi_calude installer for macOS and Linux.
#
#   curl -fsSL https://raw.githubusercontent.com/Ivantech123/Multi_calude/main/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/Ivantech123/Multi_calude/main/install.sh | sh -s -- --uninstall
#
# Environment:
#   MULTI_CALUDE_VERSION   a release tag like v0.1.0 (default: latest)
#   MULTI_CALUDE_DIR       where the program goes (default: ~/.local/share/multi_calude)
#   MULTI_CALUDE_BIN       where the cli link goes (default: ~/.local/bin)

set -eu

REPO="Ivantech123/Multi_calude"
DIR="${MULTI_CALUDE_DIR:-$HOME/.local/share/multi_calude}"
BIN="${MULTI_CALUDE_BIN:-$HOME/.local/bin}"
VERSION="${MULTI_CALUDE_VERSION:-latest}"

if [ "${1:-}" = "--uninstall" ]; then
    [ -x "$DIR/cli" ] && "$DIR/cli" --uninstall || true
    [ -L "$BIN/cli" ] && rm -f "$BIN/cli"
    rm -rf "$DIR"
    echo "Multi_calude удалён. Аккаунты (~/.claude-acc*) и сессии не тронуты."
    exit 0
fi

case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=osx ;;
    *) echo "Неподдерживаемая система: $(uname -s). Для Windows есть install.ps1." >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    arm64 | aarch64) arch=arm64 ;;
    *) echo "Неподдерживаемый процессор: $(uname -m)" >&2; exit 1 ;;
esac

name="multi_calude-$os-$arch.tar.gz"
if [ "$VERSION" = "latest" ]; then
    url="https://github.com/$REPO/releases/latest/download/$name"
else
    url="https://github.com/$REPO/releases/download/$VERSION/$name"
fi

if [ -e "$BIN/cli" ] && [ ! -L "$BIN/cli" ]; then
    echo "В $BIN уже есть свой cli — не перезаписываю. Задай MULTI_CALUDE_BIN=<другая папка>." >&2
    exit 1
fi

echo "Скачиваю $url"
mkdir -p "$DIR" "$BIN"
curl -fsSL "$url" | tar -xz -C "$DIR"
chmod +x "$DIR/cli"
if [ "$os" = "osx" ]; then xattr -d com.apple.quarantine "$DIR/cli" 2>/dev/null || true; fi
ln -sf "$DIR/cli" "$BIN/cli"

"$DIR/cli" --install

case ":$PATH:" in
    *":$BIN:"*) echo; echo "Готово. Набери: cli" ;;
    *) echo; echo "Готово. Добавь $BIN в PATH (например, в ~/.bashrc или ~/.zshrc):"
       echo "  export PATH=\"$BIN:\$PATH\""
       echo "Затем набери: cli" ;;
esac
