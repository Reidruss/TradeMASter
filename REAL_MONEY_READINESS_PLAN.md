# Real-Money Readiness Plan

## Purpose

This plan defines the work required to move TradeMASter from real-account analysis and paper-order generation to supervised real-money portfolio management.

The objective is not to guarantee investment performance. The objective is to ensure that any live order is intentional, bounded, explainable, recoverable, and correctly reconciled with the Robinhood Agentic account.

## Current readiness

| Capability | Status | Notes |
| --- | --- | --- |
| Read the funded Agentic account | Implemented | OAuth and Robinhood MCP account selection are available. |
| Scan and research the U.S. equity universe | Implemented | Broad discovery with bounded deep analysis. |
| Produce quantitative target weights | Implemented | Covariance-aware HRP, conviction tilts, and exposure caps. |
| Audit proposed portfolio risk | Implemented | Concentration, turnover, volatility, VaR, drawdown, and ATR checks. |
| Generate paper rebalance orders | Implemented | Buffered limit-order proposals with small-trade suppression. |
| Persisted live authority policy | Implemented | Phase-one scope, hard ceilings, live-disabled lock, and emergency halt are enforced in deterministic code. |
| Immutable plans and exact human approval | Implemented | Hash-bound, expiring plans persist the Agentic snapshot, orders, targets, risk, provenance, policy version, and decision record. Approval never routes an order. |
| Submit and manage live orders safely | Not ready | Broker preflight, submission idempotency, reconciliation, and recovery controls are required. |
| Demonstrate strategy performance | Not ready | Walk-forward, shadow-mode, and guarded-pilot evidence is required. |

**Current operating boundary:** real-account analysis, immutable trade-plan review, and approval recording only. Approval does not submit or schedule orders. Live execution must remain disabled until every required launch gate in this document passes.

## Safety principles

1. Fail closed when account data, quotes, market state, storage, or broker connectivity is unavailable or stale.
2. Never allow an LLM to call the broker directly. Agents may recommend; deterministic application code validates and executes.
3. Approval applies to an immutable, expiring trade plan—not to future agent discretion.
4. Retrying a request or restarting the application must never duplicate an order.
5. Risk-reducing sells may remain possible during a circuit-breaker event; new exposure must be blocked.
6. Every input, decision, approval, broker request, response, fill, cancellation, and exception must be auditable.
7. Live authority must increase gradually and remain removable with one kill switch.

## Initial live-policy scope

The first production policy should be deliberately narrow:

- Robinhood Agentic account only.
- Long-only U.S. stocks and ETFs.
- No margin, shorting, options, or crypto.
- Limit orders only.
- Fractional orders only after the instrument and broker capability are verified.
- Configurable minimum cash reserve.
- Configurable single-position, sector, turnover, daily-trade, volatility, VaR, and drawdown limits.
- Scheduled rebalancing; no continuous autonomous trading.
- Mandatory human approval for every live order batch.
- Configurable pilot notional and daily-loss ceilings that cannot be exceeded by an agent.

These restrictions must be enforced in deterministic code and persisted policy—not expressed only in prompts.

## Target execution flow

```text
Analysis
  -> Immutable proposed plan
  -> Human review and approval
  -> Fresh account/quote preflight
  -> Risk revalidation
  -> Idempotent submission
  -> Fill/cancel/reject reconciliation
  -> Final portfolio verification
  -> Audit and performance observation
```

Every order must follow a durable state machine:

```text
Proposed -> Approved -> PreflightPassed -> Submitted
          -> Expired    -> Rejected

Submitted -> PartiallyFilled -> Filled
          -> CancelPending -> Cancelled
          -> Rejected / Failed / ReconciliationRequired
```

## Milestone 0 — Investment policy and authority boundary

### Deliverables

- [x] Create a persisted `LivePortfolioPolicy` separate from agent prompts.
- [x] Define allowed asset types, exchanges, order types, and trading sessions.
- [x] Add minimum cash, maximum notional, daily turnover, daily loss, position, sector, volatility, VaR, and drawdown constraints.
- [x] Define quote and account-data freshness limits.
- [x] Define approval expiry and material-drift tolerances.
- [x] Define order timeout and cancel/replace behavior. Automatic cancel/replace remains disabled until reconciliation exists.
- [x] Keep `LiveTradingEnabled` false by default in every environment and provide no phase-one API that can enable it.
- [x] Add a global, persisted emergency halt flag with exact-confirmation clearing.

### Acceptance criteria

- [x] Policy validation rejects contradictory configurations and attempts to loosen the phase-one safety envelope.
- [x] Live analysis clamps caller-requested constraints to the persisted policy, and agents cannot enable live submission.
- [x] The deterministic live-order gate rejects disallowed securities and order types before broker authority is evaluated.
- [x] The emergency halt blocks new exposure in both the allocation risk gate and live-order gate while preserving reduction-only semantics.

### Phase-one implementation notes

- API: `GET/PUT /api/live-policy`, `POST /api/live-policy/emergency-halt`, and `POST /api/live-policy/resume`.
- Dashboard: persistent policy status, safety limits, halt reason, activate-halt control, and exact-confirmation resume control.
- Health: `/api/health` reports the live-policy version, disabled state, and emergency halt status.
- Storage: safe singleton policy seeded into both new and existing SQLite/PostgreSQL databases.
- Verification: domain/service regression tests cover safe defaults, persistence, unsafe expansion, halt behavior, disallowed assets, and the live-disabled lock.

## Milestone 1 — Immutable plans and human approval

### Deliverables

- [x] Persist every proposed plan and its source market/account snapshot.
- [x] Give each plan a unique ID, deterministic content hash, creation time, and expiry time.
- [x] Store the exact orders, rationale, data provenance, target weights, projected cash, turnover, volatility, VaR, and policy version.
- [x] Build a review screen showing current versus target holdings and every proposed order.
- [x] Require explicit approval or rejection of the exact plan hash.
- [x] Invalidate approval when the plan changes, expires, or exceeds configured account/price drift.
- [x] Require a second explicit confirmation when a plan liquidates a position or breaches a configurable notional threshold.

### Acceptance criteria

- [x] Approval cannot be reused for a different or recalculated plan.
- [x] Expired plans cannot advance through approval.
- [x] Refreshing, double-clicking, or retrying the approval request records at most one approval and cannot submit an order.
- [x] The user can inspect account state, targets, exact orders, risk evidence, provenance, and materiality reasons before approving.

### Milestone-one implementation notes

- Creation: each risk-approved live market run creates at most one `TradePlan`; mock runs never create approval artifacts.
- Integrity: the canonical payload is persisted with a SHA-256 hash and verified whenever it is read, approved, or rejected. Integrity failures invalidate the plan.
- Lifecycle: `Proposed`, `Approved`, `Rejected`, `Expired`, and `Invalidated` states are durable in SQLite/PostgreSQL.
- Approval: the exact plan hash and `APPROVE EXACT PLAN` phrase are required. Material plans also require `CONFIRM MATERIAL TRADE PLAN`.
- Drift: approval refreshes the Agentic account and invalidates on account identity, equity, cash, holding-set, quantity, price, policy-version, or halt-state changes beyond policy tolerances.
- API: `GET /api/trade-plans/latest`, `GET /api/trade-plans/{id}`, `POST /api/trade-plans/{id}/approve`, and `POST /api/trade-plans/{id}/reject`.
- Authority boundary: these endpoints persist review decisions only. They have no broker dependency and cannot route an order.
- Verification: domain/service tests cover exact confirmations, material-plan confirmation, expiry, idempotency, hash tampering, and equity/cash/holding/price/policy drift.

## Milestone 2 — Broker preflight and idempotent submission

### Deliverables

- [ ] Add a dedicated live Robinhood execution adapter; keep research agents isolated from it.
- [ ] Refresh account identity, holdings, open orders, buying power, and quotes immediately before submission.
- [ ] Recalculate quantities and risk from the fresh snapshot without changing the approved economic intent.
- [ ] Reject plans when holdings, prices, buying power, market state, or policy have drifted beyond configured tolerances.
- [ ] Verify symbol tradability and fractional-share eligibility.
- [ ] Reserve buying power across the entire batch before submitting any buy.
- [ ] Attach a stable local idempotency key to every broker order attempt.
- [ ] Prevent duplicate submission through a database uniqueness constraint and transactional outbox/inbox pattern.
- [ ] Enforce market-hours and holiday policy.
- [ ] Submit sells before dependent buys when the plan requires released buying power.
- [ ] Record sanitized broker requests and responses without tokens or secrets.

### Acceptance criteria

- Network timeouts, API retries, double-clicks, and application restarts cannot create duplicate orders.
- A stale or materially changed plan fails closed and returns to human review.
- Total submitted notional never exceeds available buying power or policy limits.
- A broker error leaves the plan in a recoverable, inspectable state.

## Milestone 3 — Order lifecycle and reconciliation

### Deliverables

- [ ] Poll or subscribe to Robinhood order status until every order reaches a terminal or intervention state.
- [ ] Support partial fills, rejects, cancels, expirations, and manual Robinhood-side changes.
- [ ] Implement deterministic timeout and cancel/replace rules.
- [ ] Reconcile local orders against Robinhood order history after every restart.
- [ ] Detect unknown broker orders and local/broker state divergence.
- [ ] Recompute portfolio risk after each material fill.
- [ ] Stop remaining buys if fills cause cash or risk limits to change materially.
- [ ] Verify final holdings, cash, and open orders after the batch completes.
- [ ] Require manual intervention when reconciliation cannot prove the account state.

### Acceptance criteria

- Partial fills and Robinhood-side cancellations are reflected correctly.
- Restarting during any order state recovers without duplication or lost tracking.
- Unknown or inconsistent orders trigger an alert and block new live activity.
- The final local portfolio snapshot matches Robinhood within explicit rounding tolerances.

## Milestone 4 — Data, model, and portfolio realism

### Deliverables

- [ ] Add redundant price retrieval and source/timestamp provenance.
- [ ] Reject stale, crossed, missing, zero, or obviously anomalous quotes.
- [ ] Handle splits, dividends, mergers, delistings, and symbol changes.
- [ ] Add spread, slippage, and transaction-cost estimates.
- [ ] Add tax-lot and wash-sale awareness before proposing taxable sells.
- [ ] Use point-in-time fundamentals and a survivorship-bias-aware historical universe for validation.
- [ ] Add stress tests for equity crashes, rate shocks, volatility spikes, correlation convergence, illiquidity, and overnight gaps.
- [ ] Add expected shortfall and scenario losses alongside parametric VaR.
- [ ] Compare every optimized strategy against cash and simple broad-market benchmark portfolios.
- [ ] Version every scoring formula, optimizer configuration, and risk policy.

### Acceptance criteria

- No live plan is created from stale or untraceable data.
- Corporate actions do not produce phantom gains, losses, or quantities.
- Backtest results include estimated costs and known data limitations.
- The optimized strategy must meet predetermined risk-adjusted criteria against its benchmarks before launch.

## Milestone 5 — Security and production operations

### Deliverables

- [ ] Store OAuth tokens and application secrets in an environment-appropriate secret manager.
- [ ] Verify encryption-at-rest, key rotation, token refresh, revocation, and disconnect behavior.
- [ ] Add authentication and authorization for every approval and live-trading endpoint.
- [ ] Add append-only audit records with actor, timestamp, plan hash, policy version, and correlation ID.
- [ ] Add health checks for Robinhood, market data, SEC data, database, background reconciliation, and clock drift.
- [ ] Add alerts for failed preflight, rejected orders, partial fills, reconciliation drift, circuit breakers, and emergency halt activation.
- [ ] Add database backup, restore, and disaster-recovery procedures.
- [ ] Remove secrets and account identifiers from application logs and error telemetry.
- [ ] Document incident response and the procedure for revoking Robinhood access.

### Acceptance criteria

- Only an authenticated authorized user can approve a live plan.
- Sensitive values do not appear in logs, browser state, source maps, or error responses.
- Backup restoration and order-state recovery are tested.
- A documented operator can stop trading and revoke access without deploying code.

## Milestone 6 — Validation and staged launch

### Stage A: Historical walk-forward validation

- [ ] Test multiple bull, bear, sideways, high-volatility, and rate-shock periods.
- [ ] Separate training/calibration windows from evaluation windows.
- [ ] Measure return, volatility, Sharpe, Sortino, maximum drawdown, turnover, hit rate, tail loss, and benchmark-relative performance.
- [ ] Record every assumption and known source of bias.

### Stage B: Real-account shadow mode

- [ ] Run the production pipeline against the real account without submitting orders.
- [ ] Record recommendations, hypothetical fills, slippage, policy violations, and operational failures.
- [ ] Require a predetermined number of successful runs with no unexplained state divergence.

### Stage C: Manual replication

- [ ] Let the application generate approved plans while the user manually places any chosen orders in Robinhood.
- [ ] Compare intended orders, actual fills, cash, and resulting weights.
- [ ] Resolve every discrepancy before enabling broker submission.

### Stage D: Guarded live pilot

- [ ] Enable live submission only for a tightly bounded, configurable pilot notional.
- [ ] Require approval for every batch.
- [ ] Allow only the initial policy scope defined above.
- [ ] Review every fill, cancellation, alert, and portfolio reconciliation.
- [ ] Return immediately to shadow mode after any unexplained failure.

### Stage E: Expanded supervised operation

- [ ] Expand limits only after the pilot satisfies predetermined reliability and risk thresholds.
- [ ] Preserve mandatory approval and the global kill switch.
- [ ] Revalidate after any material code, model, broker, policy, or data-provider change.

## Required automated test matrix

### Policy and approval

- [x] Invalid and contradictory policy configurations.
- [x] Plan tampering and hash mismatch.
- [x] Expired approval.
- [ ] Unauthorized approval attempt.
- [x] Price, holding, cash, and policy drift before approval.

### Submission safety

- [ ] Double-click and concurrent approval requests.
- [ ] Timeout before and after the broker accepts an order.
- [ ] Duplicate webhook/status events.
- [ ] Database failure during submission.
- [ ] Application termination at every order state.
- [ ] Broker throttling and transient failure.

### Reconciliation

- [ ] Partial fills across multiple updates.
- [ ] Broker rejection and expiration.
- [ ] User cancellation from Robinhood.
- [ ] Unknown Robinhood-side order.
- [ ] Split or symbol change during an open plan.
- [ ] Final quantity and cash rounding differences.

### Risk and failure modes

- [ ] Stale and anomalous quotes.
- [ ] Buying-power reduction before submission.
- [ ] Drawdown or daily-loss breaker during a batch.
- [ ] Correlation/volatility spike.
- [ ] Robinhood disconnect or token revocation.
- [ ] Emergency halt during submission and reconciliation.

## Launch gates

Live supervised submission is ready only when all of the following are true:

- [ ] Every order requires an explicit, expiring approval tied to an immutable plan.
- [ ] Duplicate submission is prevented under retries, concurrency, and restarts.
- [ ] Fresh preflight checks invalidate stale or materially changed plans.
- [ ] Partial fills, rejects, cancellations, and manual broker changes reconcile correctly.
- [ ] Stale data, disconnected dependencies, and uncertain state always fail closed.
- [ ] The emergency halt and Robinhood-disconnect procedure are tested.
- [ ] The audit trail reconstructs every decision and account mutation.
- [ ] Backtest and shadow-mode results meet documented, predetermined criteria.
- [ ] The guarded live pilot completes without unexplained account discrepancies.
- [ ] The user has reviewed and accepted the final live policy and operating procedures.

Unattended execution should remain out of scope until supervised live operation has accumulated substantially more evidence and a separate readiness review is completed.

## Recommended implementation order

1. Milestone 0 — persisted live policy and emergency halt.
2. Milestone 1 — immutable plans and approval UI.
3. Milestone 2 — preflight and idempotent Robinhood submission.
4. Milestone 3 — durable order lifecycle and reconciliation.
5. Milestone 5 — security, audit, health, and recovery controls.
6. Milestone 4 — deeper portfolio/data realism and validation tooling.
7. Milestone 6 — walk-forward, shadow, manual, and guarded-live stages.

## External references

- [Robinhood Agentic Trading overview](https://robinhood.com/us/en/support/articles/agentic-trading-overview/)
- [Robinhood Trading MCP](https://agent.robinhood.com/mcp/trading)
- [SEC EDGAR APIs](https://www.sec.gov/search-filings/edgar-application-programming-interfaces)

Robinhood states that connected agents can place orders and may act without per-trade confirmation when granted that authority. TradeMASter should therefore preserve its own mandatory approval boundary even if the broker connection permits broader access.
