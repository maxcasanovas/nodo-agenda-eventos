#!/usr/bin/env bash
# Wipes volumes (clean DB with seeds) and rebuilds everything.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose down -v
docker compose up --build -d
