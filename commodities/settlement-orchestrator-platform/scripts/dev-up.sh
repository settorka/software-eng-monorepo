#!/usr/bin/env bash
set -euo pipefail

docker compose --env-file infra/docker/.env -f infra/docker/compose.yaml up --build
