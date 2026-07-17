#!/usr/bin/env bash
set -euo pipefail

helm uninstall settlement-orchestrator --namespace settlement

