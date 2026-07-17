#!/usr/bin/env bash
set -euo pipefail

set -a
source infra/docker/.env
set +a

helm upgrade --install settlement-orchestrator \
  infra/helm/settlement-orchestrator \
  --namespace settlement \
  --create-namespace \
  --values infra/helm/settlement-orchestrator/values.local.yaml \
  --set-string oracle.password="${ORACLE_PASSWORD}" \
  --set-string oracle.appUser="${ORACLE_APP_USER}" \
  --set-string oracle.appPassword="${ORACLE_APP_PASSWORD}" \
  --set-string oracle.connectionString="${ConnectionStrings__Oracle}" \
  --set-string observability.otelEndpoint="${OTEL_EXPORTER_OTLP_ENDPOINT}" \
  --set-string operationalControls.intakeEnabled="${OperationalControls__IntakeEnabled}" \
  --set-string operationalControls.workflowPumpEnabled="${OperationalControls__WorkflowPumpEnabled}" \
  --set-string operationalControls.maxPumpWorkflows="${OperationalControls__MaxPumpWorkflows}" \
  --set-string operationalControls.pollIntervalMilliseconds="${OperationalControls__PollIntervalMilliseconds}" \
  --set-string operationalControls.maxRequestBodyBytes="${OperationalControls__MaxRequestBodyBytes}" \
  --set-string operationalControls.detailedErrors="${OperationalControls__DetailedErrors}" \
  --set-string logging.defaultLevel="${LOG_LEVEL_DEFAULT}" \
  --set-string logging.aspNetCoreLevel="${LOG_LEVEL_MICROSOFT_ASPNETCORE}"
