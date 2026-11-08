#!/usr/bin/env bash
# Compila contra a instalação do jogo e (com --install) copia o plugin para o BepInEx do jogo.
set -euo pipefail
GAME=${GK2_DIR:-/mnt/d/SteamLibrary/steamapps/common/Graveyard Keeper 2}
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$HERE/refs/Managed" "$HERE/refs/core"
cp -L "$GAME"/GraveyardKeeper2_Data/Managed/*.dll "$HERE/refs/Managed/"
cp -L "$GAME"/BepInEx/core/*.dll "$HERE/refs/core/"
docker run --rm -v "$HERE":/src -w /src/GK2Insight mcr.microsoft.com/dotnet/sdk:8.0.425 \
  dotnet build -c Release -nologo -v q
DLL="$HERE/GK2Insight/bin/Release/GK2Insight.dll"
sha256sum "$DLL"
if [ "${1:-}" = "--install" ]; then
  # Pacote local no formato de item do Workshop; o jogo copia para <jogo>/BepInEx/plugins na próxima abertura.
  WINHOME=$(wslpath "$(powershell.exe -NoProfile -Command '$env:USERPROFILE' | tr -d '\r')")
  PACK="$WINHOME/AppData/LocalLow/Lazy Bear Games/Graveyard Keeper 2/Mods/Code/GK2Insight"
  mkdir -p "$PACK/BepInEx/plugins/GK2Insight"
  cp "$HERE/modinfo.json" "$PACK/modinfo.json"
  cp "$DLL" "$PACK/BepInEx/plugins/GK2Insight/GK2Insight.dll"
  echo "instalado em $PACK (vale na próxima abertura do jogo)"
fi
