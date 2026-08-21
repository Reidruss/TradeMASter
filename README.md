# ⚡ TradeMASter

> **Human-reviewed portfolio research and paper-rebalancing platform**
> Built with **ASP.NET Core Minimal APIs (.NET 10)**, **SvelteKit + Svelte 5 Runes**, and **SignalR WebSockets**.

Robinhood holdings are read through Robinhood's official Trading MCP and OAuth flow. TradeMASter does **not** collect your Robinhood password and, with its production authority locks disabled, does **not** send live Robinhood orders. Generated rebalances must pass deterministic risk checks; the optimization pipeline remains paper-only while a separate supervised broker preflight boundary records inspectable, submission-blocked batches.

---

## 🌟 Architecture Overview

TradeMASter is a multi-agent portfolio-research and paper-rebalancing system. It coordinates specialized research roles, deterministic portfolio math, and hard local risk controls. It is designed for human review; it is not a registered adviser, fiduciary, or autonomous live-trading system.

```
┌────────────────────────────────────────────────────────────────────────┐
│                     SvelteKit Frontend (Svelte 5)                      │
│  - Dashboard & Blotter      - Real-Time Live Ticker   - Charting       │
│  - Agent War Room           - SignalR Stream Client   - Backtesting    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ WebSocket & REST
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   ASP.NET Core Minimal API Backend                     │
│  - SignalR Hubs (/hubs/debate, /hubs/market)                           │
│  - REST Endpoints (/api/market, /api/portfolio, /api/agents)           │
│  - Scalar Interactive OpenAPI Documentation (/scalar/v1)               │
├────────────────────────────────────────────────────────────────────────┤
│             Multi-Agent Deliberation & Intelligence Tier               │
│  - Technical Analyst        - Fundamental Analyst     - Sentiment      │
│  - Risk Guard (Hard Veto)   - Portfolio Arbiter       - LLM Providers  │
├────────────────────────────────────────────────────────────────────────┤
│           Quantitative Backtesting & Paper Broker Engine               │
│  - Historical Bar Replay    - Slippage & Fee Models   - Stop/Target    │
│  - Sharpe / Sortino Ratios  - Drawdown Analysis       - Trade Blotter  │
├────────────────────────────────────────────────────────────────────────┤
│              Data Access, Cache & Infrastructure Tier                  │
│  - EF Core + PostgreSQL / InMemory DB                 - Redis Cache    │
│  - Yahoo Finance Live Provider & Geometric Brownian Motion Simulator   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 🤖 Market-wide agent pipeline

The primary workflow no longer starts with one ticker or only the current holdings. It downloads the broad U.S. listed-stock universe in one discovery pass, applies price/market-cap/liquidity and operating-security filters, diversifies the shortlist by sector, and then performs bounded deep research on the finalists.

1. **Market Intelligence & Research**
   - **Macro Regime Observer** classifies Risk-On, Risk-Off, Stagflation, or Defensive conditions from VIX, the 10-year yield, and broad-market momentum, then sets the equity/cash baseline.
   - **Fundamental Researcher** derives a reproducible 1–100 health score from SEC Company Facts data: profitability, growth, free cash flow, leverage, and valuation. Live candidates without verified SEC data are blocked rather than silently filled with synthetic values.
   - **Technical Strategist** scores daily price history with EMA, RSI, a true 12/26/9 MACD, ATR, and Bollinger indicators and estimates annualized volatility.
   - **Sentiment Scout** checks current news and material catalysts and blocks high-confidence deteriorating news cycles.
2. **Allocation & Optimization**
   - **Asset Selection & Candidate Screener** combines the research scores into an approved, sector-diversified candidate set.
   - **Quantitative Allocator** estimates a shrunk empirical covariance matrix, applies hierarchical risk parity, tilts by consensus conviction, and phases the transition under the macro equity target and turnover ceiling. Existing holdings that leave the candidate set receive explicit, turnover-budgeted exit targets.
3. **Governance & Risk Control**
   - **Risk & Compliance Auditor** evaluates the entire target portfolio and enforces single-equity and sector caps, turnover, projected annualized volatility, parametric one-day 95% VaR, drawdown controls, and ATR-based stops. Risk-reducing phased exits remain possible when the starting portfolio is already over a cap.
4. **Execution & Maintenance**
   - **Execution & Rebalancing Manager** reconciles target weights with the funded Robinhood Agentic account, suppresses tiny trades, and emits buffered limit-order payloads to the paper broker only.
   - **Post-Mortem & Reflection Agent** persists structured, mode-separated equity observations and calculates realized Sharpe, maximum drawdown, win rate, and cumulative return when enough observations exist. It does not invent a Sharpe value from one snapshot.

`POST /api/market-intelligence/scan` runs the pipeline. `GET /api/market-intelligence/latest` returns the latest completed result. Deep-analysis breadth defaults to eight finalists and is capped at twenty to control latency and research cost; discovery still considers the full downloaded universe.

Set `isMockRun: true` in the scan request—or click **Run Mock Analysis** on the dashboard—to exercise the complete pipeline without Robinhood or OpenAI. Mock mode uses current public universe/price data, deterministic simulated research, and a configurable synthetic all-cash portfolio (`mockPortfolioEquity`, default `$10,000`). Mock reflections are stored separately from live observations, and all generated orders remain paper proposals.

The scan request also exposes the hard gates: `minimumFundamentalHealthScore` (55), `maxCandidateVolatilityPercent` (80), `maxProjectedPortfolioVolatilityPercent` (35), and `maxDailyVaR95Percent` (3), in addition to asset, sector, and turnover caps. Percentages are expressed as whole percentage points. Parametric VaR is a model estimate based on recent daily returns—not a worst-case loss bound.

### Persisted live-safety boundary

Phase one of the real-money readiness plan is implemented as a singleton `LivePortfolioPolicy`. It restricts the initial scope to long-only U.S. stocks/ETFs, limit orders, regular market hours, a 20% minimum cash reserve, and hard notional, turnover, loss, exposure, volatility, VaR, drawdown, freshness, drift, and timeout ceilings. Updates can tighten these limits but cannot loosen them beyond the compiled phase-one envelope. Live submission remains locked off in both policy and application configuration.

The dashboard exposes the policy state and persisted emergency halt. The API is available at `GET/PUT /api/live-policy`, `POST /api/live-policy/emergency-halt`, and `POST /api/live-policy/resume`. Clearing the halt requires the exact confirmation `RESUME SUPERVISED OPERATIONS` and does not enable live trading.

### Immutable plan review

Risk-approved live analyses now persist one immutable `TradePlan` per market run. The payload captures the exact Agentic account snapshot, target weights, proposed limit orders, risk metrics, rationale, data provenance, policy version, creation time, and expiry time, then binds it all to a SHA-256 hash. Mock analyses do not create plans.

The dashboard review gate requires the exact phrase `APPROVE EXACT PLAN` for that hash. Full liquidations and plans at or above the configured material-notional threshold also require `CONFIRM MATERIAL TRADE PLAN`. Before recording approval, the backend refreshes the Agentic account and invalidates the plan when identity, equity, cash, holdings, prices, policy, or the emergency-halt state has materially changed. Repeated approval of the same already-approved hash is idempotent.

Plan review is available through `GET /api/trade-plans/latest`, `GET /api/trade-plans/{id}`, `POST /api/trade-plans/{id}/approve`, and `POST /api/trade-plans/{id}/reject`. Approval records a review decision only and never routes an order.

### Broker preflight, lifecycle, and reconciliation boundary

An approved plan can enter a second, explicit broker gate using the exact phrase `SUBMIT APPROVED PLAN`. The backend refreshes the Agentic account, holdings, open orders, buying power, quotes, and symbol eligibility; verifies the immutable hash, expiry, policy, market session, risk, price/position drift, and aggregate cash reserve; obtains Robinhood's order review; then stores deterministic sell-first attempts in a durable outbox. Broker requests and receipts are sanitized, use stable client order IDs, and are protected by uniqueness constraints plus a transactional receipt inbox.

The dashboard shows this batch through `GET /api/trade-plans/{id}/execution`; `POST /api/trade-plans/{id}/execute` runs the gate, and `POST /api/trade-plans/{id}/execution/reconcile` performs an immediate lifecycle refresh. A background worker also reconciles active batches every 15 seconds after startup. Only one order may be active at a time, allowing each material fill to refresh cash, buying power, holdings, open orders, concentration, sector, daily-loss, volatility, VaR, drawdown, halt, and policy-version evidence before another order advances.

Robinhood order observations are stored as append-only sanitized events. Partial fills, cancellations, expirations, rejects, and client/broker-ID recovery are reflected durably. Unknown orders or symbol/side/quantity/limit divergence require manual intervention and block new live activity. Timed-out orders are cancelled by exact broker ID and never automatically replaced. Terminal batches verify final quantities within `0.000001` share, cash within `$0.05`, and zero open equity orders.

In normal builds execution still ends as `SubmissionBlocked`: the persisted live policy and `Robinhood:LiveTradingEnabled` are both false, with no API that can enable the persisted authority. Milestone 3 is implemented, but Milestones 4–6 still contain required launch gates, so the project remains **not ready for real-money management**.

### Data provenance and remaining model risk

- SEC-derived fundamental values carry their filing dates, data-quality label, and direct Company Facts/filing source URLs in each candidate result. Mock fundamentals are explicitly labeled synthetic.
- Yahoo Finance supplies the discovery snapshot and adjusted daily price history. The macro regime currently uses market proxies (VIX, 10-year yield, and SPY momentum), not a complete economic calendar or a full macroeconomic forecasting model.
- OpenAI web research contributes the qualitative current-news/sentiment assessment in live mode. It does not replace the deterministic SEC score or quantitative allocator.
- Historical covariance, normal-distribution VaR, and ATR stops can fail during gaps, regime changes, illiquidity, and market closures. A risk-approved plan is not a prediction or guarantee.

---

## 🚀 Quickstart & Running

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20.19+, 22.12+, or 24+](https://nodejs.org/)
- A Robinhood account with access to [Robinhood Agentic Trading](https://robinhood.com/us/en/support/articles/agentic-trading-overview/)
- An OpenAI API key for live-account analysis; the built-in simulated analyst is deliberately limited to demo mode

### 1. Configure local secrets

```bash
cp .env.example .env
```

Set `OPENAI_API_KEY` and replace `SEC__UserAgent` with an application name and a monitored contact email in `.env`. Do not commit `.env`. Browser OAuth is the preferred Robinhood connection method, so no Robinhood secret is required in the file. `ROBINHOOD_MCP_ACCESS_TOKEN` is an optional non-interactive alternative for short-lived development sessions.

Local development uses a git-ignored SQLite database (`trademaster.db`) so encrypted OAuth sessions and reflection history survive hot reloads. Container deployments use PostgreSQL when `Database__UsePostgreSql=true`.

### 2. Run in Development Mode (Full-Stack Hot Reload)
From the root directory:

```bash
# Start both Backend (.NET watch) and Frontend (Vite) concurrently
npm run dev
```

Open [`http://localhost:5173`](http://localhost:5173), choose **Connect with Robinhood OAuth**, and approve the request on Robinhood. The backend dynamically registers its OAuth client, uses PKCE plus a server-held state value, exchanges the callback code, and encrypts saved access/refresh tokens with ASP.NET Core Data Protection.

The OAuth callback must return to the same running backend instance within ten minutes. In Docker, the Data Protection keys are persisted in the `data_protection_keys` volume so saved tokens remain decryptable after a restart.

### 3. Safe demo mode

Choose **Demo Sandbox** to validate the entire holdings-sync, committee, risk-review, and paper-rebalance flow without connecting an account. Simulated LLM output is accepted only in this mode.

### 4. Service Endpoints

| View / Service | URL | Description |
| :--- | :--- | :--- |
| **Command Center Dashboard** | [`http://localhost:5173/`](http://localhost:5173/) | Funded Agentic-account balance, holdings, full-market intelligence scan, immutable trade-plan review, exact approval/rejection, and the persisted live-safety boundary. |
| **Agent War Room** | [`http://localhost:5173/agents`](http://localhost:5173/agents) | Real-time SignalR WebSocket streaming of multi-agent debate, cross-exam, and consensus synthesis. |
| **Backtesting Lab** | [`http://localhost:5173/backtesting`](http://localhost:5173/backtesting) | Historical strategy simulation, equity & drawdown curves, Sharpe/Sortino ratios, and trade blotters. |
| **Market Screener** | [`http://localhost:5173/market`](http://localhost:5173/market) | OHLCV historical candle viewer with volume histograms and multi-timeframe toggles. |
| **Portfolio & Risk Blotter** | [`http://localhost:5173/portfolio`](http://localhost:5173/portfolio) | Live holdings, position weights, P&L attribution, and configurable circuit breaker limits. |
| **Scalar API Reference** | [`http://localhost:5173/scalar/v1`](http://localhost:5173/scalar/v1) | Interactive OpenAPI reference to test REST endpoints in-browser. |

---

## 🧪 Testing & Verification

Run the automated test suite covering Domain entities, Technical indicators, Risk Guard veto rules, and the Backtesting engine:

```bash
dotnet test backend/TradeMASter.slnx
```

Run TypeScript and Svelte 5 rune validation:

```bash
npm run check
```

---

## 🐳 Docker Container Deployment

Deploy the entire stack with PostgreSQL and Redis via Docker Compose:

```bash
docker compose up -d --build
```

Change `POSTGRES_PASSWORD` in `.env` before exposing the stack outside your machine. Docker serves the statically exported Svelte application and persists PostgreSQL, Redis, and token-encryption keys in named volumes. Open [`http://localhost:5126`](http://localhost:5126) after the containers become healthy.

## Robinhood MCP configuration

The checked-in defaults use:

- MCP server: `https://agent.robinhood.com/mcp/trading`
- Authorization: `https://robinhood.com/oauth`
- Dynamic client registration: `https://agent.robinhood.com/oauth/trading/register`
- Token exchange/refresh: `https://api.robinhood.com/oauth2/token/`
- OAuth scope: `internal`

These can be overridden with standard ASP.NET configuration keys such as `Robinhood__McpServerUrl`. `Robinhood:LiveTradingEnabled` remains `false`; changing that setting alone does not enable live execution because the separate persisted live policy must also authorize it, and no current API can do so. The optimizer remains wired to `PaperBrokerService`.

Robinhood states that agents can act without per-trade confirmation when an Agentic account grants trading access. This project intentionally stops short of that capability: inspect the holdings and rationale, then treat every recommendation as decision support rather than personalized financial advice.

The staged engineering and validation work required before supervised live submission is tracked in [REAL_MONEY_READINESS_PLAN.md](REAL_MONEY_READINESS_PLAN.md).

---

## 📁 Repository Structure

```
TradeMASter/
├── backend/
│   ├── TradeMASter.Core/            # Domain entities, Value Objects, Enums, Result monad
│   ├── TradeMASter.Infrastructure/  # EF Core, PostgreSQL/InMemory, Yahoo Finance & Simulated data feeds
│   ├── TradeMASter.Agents/          # Personas, Deliberation Engine, Indicators, LLM clients, Backtester
│   ├── TradeMASter.Api/             # Minimal API Endpoints, SignalR Hubs, Hosted Broadcaster
│   └── TradeMASter.Tests/           # xUnit, FluentAssertions, Moq test suites
├── frontend/
│   ├── src/
│   │   ├── lib/
│   │   │   ├── api/                 # Typed API client services
│   │   │   ├── components/          # Svelte 5 Charts, Blotters, Tickers, Widgets
│   │   │   └── realtime/            # SignalR WebSocket connection manager
│   │   └── routes/                  # SvelteKit pages (Dashboard, Market, Agents, Backtesting, Portfolio)
├── desktop/                         # Native Electron desktop wrapper shell
├── Dockerfile                       # Production multi-stage Docker build
└── docker-compose.yml               # Complete container stack (App + Postgres + Redis)
```
