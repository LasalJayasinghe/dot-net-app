# Overnight Development Task Queue

## Objective

Work autonomously on the existing .NET 9 trading and portfolio application.

Most major features already exist.

The objective is **NOT to rebuild existing functionality**.

Instead:

1. Understand the existing implementation.
2. Verify that it works correctly.
3. Find incomplete, incorrect, fragile, duplicated, or poorly structured code.
4. Fix verified problems.
5. Improve reliability, performance, security, maintainability, and test coverage.
6. Do not rewrite working code without a clear reason.

---

# Critical Agent Rules

## Do NOT blindly implement features

Before changing anything:

* Search the entire repository.
* Identify whether the requested functionality already exists.
* Trace the complete execution path.
* Understand how the frontend, API, services, database and background workers interact.

If functionality already exists and works:

**Do not recreate it.**

Instead, evaluate whether it needs:

* Bug fixes
* Missing validation
* Error handling
* Performance improvements
* Security improvements
* Tests
* Refactoring
* Better logging
* Documentation

---

## Change Policy

Only modify code when there is a clear reason.

Prefer:

```text
Existing implementation
        ↓
Understand
        ↓
Verify
        ↓
Identify problem
        ↓
Small targeted improvement
        ↓
Test
```

Avoid:

```text
Existing implementation
        ↓
Rewrite everything
```

Do not introduce unnecessary libraries.

Do not change frameworks or major architecture unless the existing architecture is demonstrably preventing the required functionality.

Do not modify unrelated functionality.

---
# Task: BTC $1,000 Paper Trading Simulation

The existing BTC trading algorithm is already implemented. check and see why is it not running. fix it and make sure it is working as expected with backtesting. if it is not working as expected, then fix it and make sure it is working as expected with backtesting.

DO NOT rewrite or recreate the trading strategy.

The goal is to run the EXISTING BTC strategy in a completely simulated paper-trading environment with an initial virtual balance of $1,000 USD.

## Requirements

1. Starting capital

Create/use a paper-trading account with:

- Initial USD balance: $1,000.00
- Initial BTC balance: 0 BTC
- No real Binance account
- No Binance trading API
- No real orders
- No real money

The $1,000 must exist only inside the application's paper-trading simulation.
make sure to create a log file to note down the transcations and total usd balance and btc balance left

## 2. Use the existing BTC strategy

Find the existing BTC trading strategy and trace how it currently produces BUY/SELL signals.

Use that exact strategy.

Do NOT:
- create another strategy
- change EMA/RSI parameters
- change signal-generation logic
- introduce a different trading algorithm
- connect trading execution to Binance

The simulator should consume the existing strategy's signals.

## 3. Simulated execution

When the existing strategy generates a BUY signal:

- Use the current BTC market price available to the application.
- Calculate how much BTC can be purchased using the available USD balance.
- Execute the simulated order internally.
- Deduct the Binance trading fee.
- Update USD and BTC balances.
- Record the simulated trade.

When the strategy generates a SELL signal:

- Sell the simulated BTC position at the current BTC price.
- Deduct the Binance trading fee.
- Update USD and BTC balances.
- Record the simulated trade.

Do not send anything to Binance.

## 4. Binance trading fees

The simulator MUST account for Binance spot trading fees.

Make the fee configurable rather than hardcoding it throughout the code.

Default:

- Trading fee: 0.10% per executed side

For example:

BUY:
$1,000 available
BTC price = $100,000
Fee = 0.10%

Do NOT allow the purchase calculation to spend more than the available balance including the fee.

SELL:
Calculate the gross sale value, then deduct the 0.10% trading fee from the proceeds.

Use decimal types for monetary calculations.

Do NOT use double/float for money.

## 5. Position state

Track at minimum:

- USD cash balance
- BTC quantity
- Current BTC price
- Position status
- Entry price
- Entry timestamp
- Exit price
- Exit timestamp
- Entry fee
- Exit fee
- Realized P/L
- Unrealized P/L
- Total fees
- Portfolio/equity value

Prevent invalid states such as:

- Buying without sufficient USD
- Selling more BTC than owned
- Multiple BUY entries when already holding a position
- SELL when no BTC position exists

## 6. Trade history

Every simulated trade must be persisted/recorded.

At minimum:

- Trade ID
- Symbol: BTCUSDT
- Side: BUY/SELL
- Quantity
- Execution price
- Gross value
- Fee
- Net value
- Timestamp
- Strategy/signal that caused the trade

Make it possible to inspect the complete paper-trading history.

## 7. Portfolio valuation

At any point calculate:

BTC market value = BTC quantity × current BTC price

Total portfolio value:

USD balance + BTC market value

Also calculate:

Total return:
current portfolio value - $1,000

Return percentage:
((current portfolio value / $1,000) - 1) × 100

Include total trading fees paid.

## 8. Important execution rule

Do NOT assume that every market-data update is a new trade.

The existing strategy may produce repeated signals.

Only execute a BUY when transitioning into a long position.

Only execute a SELL when transitioning out of a long position.

The simulator must not generate duplicate trades from repeated identical signals.

## 9. Market data

Continue using the application's existing BTC market-data source if it is already available.

Do NOT connect Binance order/trading APIs.

Market-data access is acceptable if the existing application already uses it.

The execution layer must remain completely simulated.

## 10. Architecture

Keep the existing strategy independent from execution.

Prefer:

Market Data
    ↓
Existing BTC Strategy
    ↓
BUY / SELL Signal
    ↓
Paper Trading Executor
    ↓
Paper Portfolio
    ↓
Trade History

The strategy should NOT know whether execution is:

- paper trading
- real trading
- backtesting

Do not tightly couple the strategy to Binance execution.

If an appropriate abstraction already exists, reuse it.

Do not introduce unnecessary architecture if the existing structure already supports this cleanly.

## 11. Testing

Create deterministic tests for:

### BUY

Verify:

- BTC quantity calculation
- trading fee
- USD balance
- BTC balance
- total portfolio value

### SELL

Verify:

- gross proceeds
- trading fee
- final USD balance
- BTC balance becomes zero
- realized P/L

### Fees

Test that:

BUY fee = trade value × 0.001

SELL fee = trade value × 0.001

### Example

Initial balance:

$1,000

BTC price:

$100,000

Fee:

0.10%

The simulator should correctly calculate the maximum BTC quantity that can be purchased without exceeding the $1,000 balance after the BUY fee.

### Duplicate signals

BUY + BUY while already holding BTC:

Only one trade.

SELL + SELL while holding no BTC:

Only one trade.

### P/L

Create a deterministic BUY → price increase → SELL scenario and verify realized P/L after both trading fees.

## 12. Logging

Log paper trades clearly, for example:

PAPER BUY BTCUSDT
Price: $100,000
Quantity: 0.00999...
Gross: $999...
Fee: $...
USD Balance: $...
BTC Balance: ...

And similarly for SELL.

Do not log secrets.

## 13. UI/API

First inspect whether the existing application already has portfolio/trade endpoints and UI.

If suitable functionality already exists, extend it rather than creating duplicate functionality.

Expose enough information to inspect:

- Current paper balance
- BTC position
- Current portfolio value
- P/L
- Total fees
- Trade history
- Current position
- Strategy-generated trades

## 14. Persistence

If the application already has suitable trading/portfolio entities, reuse them.

If paper trading entities are missing, add the minimum required persistence model.

Clearly distinguish paper trades from real trades.

There must be no possibility for this paper-trading task to accidentally execute a real Binance order.

## 15. Safety requirement

This task MUST NOT introduce or enable real Binance order execution.

Do not add:

POST /order
real Binance trading credentials
real order placement
real account balance queries

The $1,000 is purely virtual.

## 16. Before modifying code

Follow the repository's existing development rules:

1. Search the entire repository.
2. Find the existing BTC strategy.
3. Find existing portfolio/trading models.
4. Find existing paper-trading functionality.
5. Find existing Binance integration.
6. Trace the complete signal → execution path.
7. Reuse existing functionality wherever possible.

If paper trading already exists, improve/fix it instead of creating another implementation.

## 17. Final verification

Run:

- Backend build
- Existing tests
- New paper-trading tests
- Relevant frontend tests/build if UI was changed

Then report:

- Files changed
- Existing components reused
- Paper-trading flow
- Fee calculation
- Tests added
- Test results
- Any remaining issues

Do not mark the task complete until the simulated $1,000 can be followed from:

$1,000 USD
→ existing BTC strategy BUY
→ simulated BTC position
→ fee deduction
→ existing BTC strategy SELL
→ fee deduction
→ final USD balance
→ realized P/L

Everything must remain paper trading.


# Phase 1 — Full System Discovery

## Task 1 — Map the existing application

* [x] Inspect the complete repository.
* [x] Identify frontend applications.
* [x] Identify .NET projects.
* [x] Identify controllers/endpoints.
* [x] Identify services.
* [x] Identify background workers.
* [x] Identify database entities.
* [x] Identify repositories/data-access code.
* [x] Identify Binance integration.
* [x] Identify CSE integration.
* [x] Identify Telegram integration.
* [x] Identify authentication.
* [x] Identify portfolio functionality.
* [x] Identify alert functionality.
* [x] Identify trading strategies.
* [x] Identify backtesting functionality.
* [x] Identify tests.

Create:

`docs/current-architecture.md`

Document what actually exists.

Do not change application code during this task unless required to make the application build.

---

## Task 2 — Create feature status report

For every major feature, determine:

* Existing
* Partially implemented
* Broken
* Missing
* Untested

Evaluate:

* Authentication
* Authorization
* Portfolio
* Watchlist
* CSE market data
* Binance WebSocket
* Alerts
* Telegram
* Trading strategies
* Historical data
* Backtesting
* Dashboard
* Logging
* Background services

Create:

`docs/feature-status.md`

Do not implement features simply because the README says they are TODO.

The actual source code is the source of truth.

---

# Phase 2 — Backend Quality Audit

## Task 3 — API/controller audit

Inspect every API/controller.

Look for:

* Business logic inside controllers
* Missing validation
* Incorrect HTTP status codes
* Duplicate code
* Missing authorization
* Unnecessary database queries
* N+1 queries
* Unhandled exceptions
* Exposed internal exceptions
* Missing async operations
* Missing cancellation support
* Inconsistent API responses

Fix only verified issues.

Add tests for important fixes.

---

## Task 4 — Service/business-logic audit

Inspect application services.

Look for:

* Duplicate business logic
* Incorrect responsibilities
* Tight coupling
* Services depending directly on infrastructure
* Difficult-to-test code
* Missing interfaces where abstraction provides real value
* Excessive service complexity

Refactor only where there is a measurable maintainability benefit.

Do not perform architecture changes for the sake of architecture.

---

## Task 5 — Database/query audit

Inspect:

* EF Core queries
* MySQL queries
* Entity relationships
* Indexes
* Transactions
* Pagination
* Tracking behavior
* Large queries
* Repeated queries

Look specifically for:

* N+1 queries
* Missing indexes
* Loading entire tables unnecessarily
* `ToList()` before filtering
* Unnecessary `Include()`
* Missing `AsNoTracking()` for read-only queries
* Unbounded result sets
* Synchronous database calls

Fix verified performance problems.

Document significant findings.

---

# Phase 3 — Market Data Reliability

## Task 6 — Binance WebSocket audit

Inspect the existing Binance implementation.

Verify:

* Connection handling
* Reconnection
* Network failures
* Cancellation
* Disposal
* Invalid messages
* Binance disconnects
* Logging
* Multiple subscriptions
* Duplicate subscriptions
* Memory/resource leaks

Do not rewrite the integration if it already works.

Fix actual reliability issues.

---

## Task 7 — CSE integration audit

Inspect the existing CSE API integration.

Verify:

* HTTP client lifecycle
* Timeout handling
* Retry behavior
* API failures
* Invalid responses
* Data validation
* Logging
* Rate limiting
* Duplicate requests
* Caching where appropriate

Fix only verified problems.

---

# Phase 4 — Alert System Audit

## Task 8 — Trace the complete alert pipeline

Trace:

```text
Frontend Alert Configuration
        ↓
API
        ↓
Database
        ↓
Market Data
        ↓
Alert Evaluation
        ↓
Trigger
        ↓
Notification
        ↓
Telegram
```

Determine exactly where the current implementation stops.

If the complete pipeline already works:

* Test it.
* Improve reliability where necessary.
* Do not rebuild it.

---

## Task 9 — Alert correctness

Review:

* Duplicate triggering
* Race conditions
* Disabled alerts
* Trigger frequency
* Alert state
* Concurrent market events
* Database consistency
* Notification failures
* Telegram failures

Ensure one market event cannot accidentally generate unlimited notifications.

Add tests for discovered edge cases.

---

# Phase 5 — Trading Engine Audit

## Task 10 — Strategy architecture audit

Inspect the existing trading strategy implementation.

Determine:

* How strategies are represented
* How market data reaches strategies
* How signals are generated
* How positions are tracked
* How indicators are calculated
* How strategies are configured

Verify that strategy logic is separated from:

* API controllers
* Telegram
* Database implementation
* Binance network code

Only refactor if the current implementation has a real problem.

---

## Task 11 — BTC strategy correctness

Inspect the existing BTC strategy.

Verify:

* EMA9 calculation
* EMA21 calculation
* RSI14 calculation
* Candle handling
* Signal generation
* Duplicate signals
* Position state
* Edge cases
* Insufficient candle history

Create deterministic tests for indicator calculations and strategy decisions.

Do not connect the strategy to real-money trading.

---

## Task 12 — Paper trading audit

If paper trading already exists:

* Trace the complete lifecycle.
* Verify entry/exit calculations.
* Verify fees.
* Verify P/L.
* Verify position state.
* Verify trade history.

If it does not exist, document this as a gap rather than automatically building it unless it is required by an existing feature.

---

# Phase 6 — Background Services

## Task 13 — Audit all BackgroundService implementations

For every worker verify:

* CancellationToken
* Exception handling
* Retry behavior
* Logging
* Graceful shutdown
* Resource disposal
* Database connection handling
* WebSocket handling
* Infinite loops
* Delay behavior

Pay particular attention to workers that can silently stop after an exception.

Fix verified problems.

---

# Phase 7 — Security Audit

## Task 14 — Authentication audit

Inspect the actual authentication implementation.

Verify:

* Password handling
* Google OAuth
* JWT/token handling
* Token expiration
* Refresh mechanism if applicable
* Claims
* User identity
* Unauthorized responses
* Secret management

Do not replace the authentication system simply because the README says JWT is TODO.

If it is already implemented, test and harden it.

---

## Task 15 — Authorization audit

Check every protected endpoint.

Verify:

```text
User A
    ↓
Cannot access
    ↓
User B's portfolio
```

Also verify administrative endpoints.

Fix any authorization bypasses.

---

## Task 16 — Configuration/secrets audit

Search the repository for:

* API keys
* Telegram tokens
* Binance credentials
* Database passwords
* OAuth secrets
* JWT secrets

Ensure secrets are not hardcoded or committed.

Do not print discovered secrets in logs or documentation.

---

# Phase 8 — Testing

## Task 17 — Test existing business logic

First inspect existing tests.

Do not duplicate tests.

Prioritize:

* Portfolio calculations
* Alert evaluation
* Trading indicators
* Trading signals
* P/L calculations
* Authentication
* Authorization

Add tests specifically for uncovered/high-risk logic.

---

## Task 18 — Integration test audit

Determine whether integration tests already exist.

Test critical flows such as:

```text
Login
 ↓
Authenticated API
 ↓
Portfolio
```

and:

```text
Market Data
 ↓
Alert
 ↓
Notification
```

and, where supported:

```text
Historical Data
 ↓
Strategy
 ↓
Backtest
 ↓
Result
```

Do not create fake tests that merely verify mocks instead of real behavior.

---

# Phase 9 — Performance Audit

## Task 19 — Identify measurable bottlenecks

Inspect:

* Database queries
* API latency
* WebSocket processing
* Background workers
* Market-data processing
* Repeated calculations
* Frontend API calls

Fix obvious inefficiencies.

Do not optimize code based purely on assumptions.

Prefer measurable improvements.

---

## Task 20 — Frontend performance audit

Inspect the React application.

Look for:

* Duplicate API requests
* Incorrect React effects
* Unnecessary rerenders
* Missing query caching
* Large payloads
* Unnecessary chart recalculations
* Poor loading/error handling

Use the existing TanStack Query infrastructure where appropriate.

Do not rewrite components unnecessarily.

---

# Phase 10 — Logging & Observability

## Task 21 — Serilog audit

Verify:

* Structured logging
* Log levels
* Daily rolling files
* Exception logging
* Sensitive data exclusion
* Background worker logging
* External API failure logging

Avoid excessive logging inside high-frequency market-data loops.

---

## Task 22 — Health checks

Determine whether health checks already exist.

If they exist:

* Verify correctness.
* Improve missing checks.

If they don't exist:

* Add appropriate health checks.

At minimum consider:

* Application
* MySQL

Do not expose sensitive infrastructure information through health endpoints.

---

# Phase 11 — Documentation

## Task 23 — Update README

Compare the README against the actual application.

Remove outdated TODO items.

Update:

* Features
* Architecture
* Setup
* Environment variables
* Running locally
* Testing
* Market data
* Trading strategies
* Alerts
* Telegram
* Deployment

The README must describe what the application actually does.

---

## Task 24 — Developer documentation

Create/update:

`docs/architecture.md`

`docs/feature-status.md`

`docs/trading-engine.md`

`docs/development.md`

Only document functionality that actually exists.

---

# Phase 12 — Final Verification

## Task 25 — Full verification

Run:

* [ ] Backend build
* [ ] Backend tests
* [ ] Frontend build
* [ ] Frontend tests if available
* [ ] Static analysis where available
* [ ] Database migration validation
* [ ] Git diff review

Check for:

* Debug code
* Temporary files
* Hardcoded secrets
* TODOs introduced by the agent
* Broken configuration
* Unnecessary dependencies
* Unrelated modifications

---

# Agent Completion Rules

A task may be marked `[x]` only when:

1. The repository was inspected.
2. The task's current implementation was understood.
3. Any discovered problem was fixed or verified as not requiring changes.
4. Relevant tests were run.
5. The application still builds.
6. Documentation was updated where appropriate.

If functionality is already correct:

**Mark the audit complete without rewriting the implementation.**

If something is missing:

**Document the gap instead of automatically implementing it unless the task explicitly authorizes implementation.**

If something is broken:

**Fix it, test it, and document the fix.**

---

# Progress

## Completed

- Task: BTC $1,000 Paper Trading Simulation
- Task 1 — Map the existing application
- Task 2 — Create feature status report
- Task 3 — API/controller audit
- Task 4 — Service/business-logic audit
- Task 5 — Database query audit
- Task 6 — Binance WebSocket audit
- Task 7 — CSE integration audit
- Task 8 — Authentication/authorization audit
- Task 9 — Input validation and sanitization
- Task 10 — Test coverage analysis
- Task 11 — End-to-end testing
- Task 12 — Final polish
- Task 13 — Audit all BackgroundService implementations
- Task 14 — Authentication audit
- Task 15 — Authorization audit
- Task 16 — Configuration/secrets audit
- Task 17 — Test existing business logic
- Task 18 — Integration test audit
- Task 19 — Identify measurable bottlenecks
- Task 20 — Frontend performance audit
- Task 21 — Serilog audit
- Task 22 — Health checks
- Task 23 — Update README
- Task 24 — Developer documentation
- Task 25 — Full verification

## Blocked

None.
