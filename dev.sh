#!/usr/bin/env bash
# Compila o plugin, copia a DLL pro Jellyfin de dev e (re)inicia o container.
#   ./dev.sh         -> build + deploy + sobe/reinicia
#   ./dev.sh reset   -> apaga TODO o estado do Jellyfin de dev (banco, config, .strm) e sobe limpo
#   ./dev.sh logs    -> segue os logs do Jellyfin
set -euo pipefail
cd "$(dirname "$0")"

COMPOSE="podman-compose -f compose.dev.yml"
command -v podman-compose >/dev/null || COMPOSE="docker compose -f compose.dev.yml"

case "${1:-}" in
  logs) exec $COMPOSE logs -f ;;
  reset) $COMPOSE down || true; rm -rf .dev ;;
esac

dotnet build -c Debug
mkdir -p .dev/config/plugins/MyIPTV .dev/cache .dev/media/movies .dev/media/tvshows
cp bin/Debug/net9.0/Jellyfin.Plugin.MyIPTV.dll .dev/config/plugins/MyIPTV/

if [ -n "$($COMPOSE ps -q 2>/dev/null)" ]; then
  $COMPOSE restart
else
  $COMPOSE up -d
fi
echo "Jellyfin de dev: http://localhost:8096"
