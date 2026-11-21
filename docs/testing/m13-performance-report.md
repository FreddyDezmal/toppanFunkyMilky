# M13 Performance Report – PERF-01 Smoke Test

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Task:** T13.4 · **Acceptance criterion:** AC-13.4 – order and payment pages respond within 2 seconds at 50 concurrent
users \[Rec target, to confirm – Q-15\]
**Date:** 21 November 2026 · **Result:** **Met.** At 50 concurrent patrons every step's 95th percentile is under 165 ms,
with 0 failed requests.

Related: [test plan](m13-test-plan.md) · [security report](m13-security-report.md)

## 1. Method

| Item | Detail |
| --- | --- |
| Tool | k6 (`grafana/k6` in Docker), script `frontend/e2e-local/perf/order-and-pay.js` |
| Command | `npm run test:e2e:local -- --no-e2e --perf` |
| Environment | Local E2E stack on the developer's Windows 11 machine: production build of the site (`vite preview`, HTTPS), ASP.NET Core API (`E2E` environment), PostgreSQL 17 in Docker, Paystack simulator. Requests go through the site's origin, as a browser's do |
| Load | 50 virtual patrons: ramp 0 → 50 in 30 s, hold 50 for 2 min, ramp down in 15 s |
| Journey (each patron, repeated) | Order page → menu (`GET /api/ordering/options`) → quote → place order (idempotency key) → start payment (checkout created at the Paystack simulator) → order page and order (`GET /api/ordering/orders/{reference}`) |
| Think time | 1.5 s between steps (a person reading), which also keeps each patron within the 60-a-minute ordering limit |
| Pass criteria (k6 thresholds) | For every step, 95th percentile < 2 s; failed requests < 1 %; checks > 99 % |

Each patron registers once and stays signed in, with its own session cookie and anti-forgery token, as a browser does.

## 2. Results (run `2026-11-21T05-53-08-262Z`)

| Step | Median | 90th pct | 95th pct | 99th pct | Max | Target (95th) |
| --- | --- | --- | --- | --- | --- | --- |
| Pages (order page, order details) | 6 ms | 14 ms | **20 ms** | 42 ms | 136 ms | < 2,000 ms |
| Menu | 34 ms | 60 ms | **92 ms** | 278 ms | 412 ms | < 2,000 ms |
| Quote | 34 ms | 69 ms | **117 ms** | 207 ms | 352 ms | < 2,000 ms |
| Place order | 51 ms | 106 ms | **143 ms** | 376 ms | 612 ms | < 2,000 ms |
| Start payment | 46 ms | 97 ms | **161 ms** | 487 ms | 656 ms | < 2,000 ms |
| View order | 21 ms | 40 ms | **56 ms** | 186 ms | 276 ms | < 2,000 ms |
| All requests | 31 ms | 67 ms | 109 ms | 278 ms | 1.79 s | – |

| Measure | Result |
| --- | --- |
| Journeys completed | 793 |
| Requests | 5,702 (33 a second) |
| Failed requests | **0 (0.00 %)** |
| Checks passed | **5,701 of 5,701 (100 %)** |
| Database integrity checks after the run | 18 of 18 at 0 |

The slowest single response (1.79 s) was not in any measured step (their maximums are at most 656 ms). It was one of
the set-up calls, registration or the anti-forgery token request, made by every patron during the ramp-up.
Registration hashes the password deliberately slowly. Even so it was under 2 s.

## 3. Issues found while setting up the test

| Issue | Resolution |
| --- | --- |
| Docker could not reach the site ("connection refused"): `vite preview` on `localhost` listened on IPv6 only, and Docker Desktop forwards to IPv4 | In the E2E stack the preview server listens on `127.0.0.1`. The runner now checks that Docker can reach the site, and k6 aborts at once if it cannot |
| A failing request made each patron loop without a pause (87 MB log) | Every failure path now pauses; the script aborts in `setup()` if the site is unreachable |
| k6 clears cookies between iterations by default, so patrons were signed out after their first journey | `noCookiesReset: true`, as a browser keeps its session |

None of these were defects in the application.

## 4. Limits and next steps

- The figures are from one developer machine with every component on it, not from production hardware. They show
  large headroom (about 12 times under the target at the 95th percentile) rather than production capacity.
- Paystack's real checkout pages are outside the application and are not measured. The simulator answers in about
  1 ms, so "start payment" shows the application's own cost.
- Q-15 (performance and availability targets) is still open with the stakeholder. AC-13.4 uses the roadmap's
  recommended target. M14 repeats a smoke test on the production environment (AC-14.3).
