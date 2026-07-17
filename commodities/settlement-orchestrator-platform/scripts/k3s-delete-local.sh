#!/usr/bin/env bash
set -euo pipefail

kubectl delete -k infra/k3s/local

