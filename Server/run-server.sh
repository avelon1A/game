#!/usr/bin/env bash
# Starts the VEIL test server: UDP game server (7777) + REST backend (5080) + SQLite (veil.db).
# Usage: ./run-server.sh [--match-seconds 300] [--players 15] [--name "My Server"]
set -euo pipefail
cd "$(dirname "$0")/Veil.Server"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec dotnet run -c Release -- "$@"
