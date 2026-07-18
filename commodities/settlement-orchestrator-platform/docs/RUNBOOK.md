# Settlement Orchestrator Runbook

## Local Startup

1. Copy `infra/docker/.env.example` to `infra/docker/.env`.
2. Keep `Database__RunMigrationsOnStartup=true` for local API startup.
3. Run `scripts/dev-up.sh`.
4. Check `http://localhost:8080/health`.
5. Check traces in Jaeger at `http://localhost:16686`.
6. Check metrics and dashboards in Grafana at `http://localhost:3000`.

## Production Migration Strategy

Do not run migrations from every replica.

Use one of these patterns:

- Preferred: run migrations as a one-shot deployment job before API/worker rollout.
- Acceptable for local only: set `Database__RunMigrationsOnStartup=true` on the API and keep it `false` on the worker.

For a manual local migration after Oracle is running:

```bash
scripts/db-update-local.sh
```

## Rollback Criteria

Rollback immediately when:

- API readiness fails for more than 5 minutes.
- Oracle connectivity fails after deployment.
- duplicate settlement or duplicate invoice invariant is violated.
- outbox dead-letter volume rises above the agreed threshold.
- p95 API latency doubles for 15 minutes after release.

## Operator Actions

- Pause intake: set `OperationalControls__IntakeEnabled=false`.
- Pause workflow execution: set `OperationalControls__WorkflowPumpEnabled=false`.
- Pause payment publishing: set `OperationalControls__OutboxDispatcherEnabled=false`.
- Reduce pressure: lower `OperationalControls__MaxPumpWorkflows` and `OperationalControls__OutboxBatchSize`.

## Alerts

Local Prometheus rules live in `infra/docker/prometheus/rules`.

Required production alerts:

- API down.
- Oracle readiness failure.
- outbox dead-letter count above threshold.
- retry rate above threshold.
- workflow age above settlement SLA.
- p95/p99 API latency above target.
