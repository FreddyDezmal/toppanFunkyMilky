// M13 PERF-01 (AC-13.4): order and payment pages respond within 2 seconds at 50 concurrent users.
// Run by `npm run test:e2e:local -- --perf` (grafana/k6 in Docker) against the disposable local E2E stack only.
// Each virtual patron registers once, then repeats the patron journey with a pause between steps, as a person would:
// order page → menu → quote → place order → start payment (Paystack simulator) → order page and order.
import { check, sleep } from 'k6'
import exec from 'k6/execution'
import http from 'k6/http'
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js'

const BASE = __ENV.BASE || 'https://host.docker.internal:5173'
const PAUSE = 1.5 // seconds between steps: about 40 ordering calls a minute per patron, under the 60-a-minute limit

export const options = {
  insecureSkipTLSVerify: true, // the local stack uses the ASP.NET development certificate
  noCookiesReset: true, // each virtual patron stays signed in across iterations, as a browser keeps its session cookie
  scenarios: {
    patrons: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 50 },
        { duration: '2m', target: 50 },
        { duration: '15s', target: 0 },
      ],
      gracefulRampDown: '15s',
    },
  },
  thresholds: {
    // AC-13.4, per step: 95% of responses within 2 seconds, and no step slower than 2 s at the 95th percentile.
    'http_req_duration{kind:page}': ['p(95)<2000'],
    'http_req_duration{step:options}': ['p(95)<2000'],
    'http_req_duration{step:quote}': ['p(95)<2000'],
    'http_req_duration{step:place-order}': ['p(95)<2000'],
    'http_req_duration{step:start-payment}': ['p(95)<2000'],
    'http_req_duration{step:order}': ['p(95)<2000'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
}

let csrf = null // per virtual patron (k6 keeps one cookie jar per VU)

function json(body) {
  return JSON.stringify(body)
}

/** Stops the whole test at once if the site cannot be reached from this container (nothing would be measured). */
export function setup() {
  const response = http.get(`${BASE}/api/health`, { tags: { step: 'setup' } })
  if (response.status !== 200) {
    exec.test.abort(`The site is not reachable from Docker at ${BASE} (status ${response.status}, ${response.error || 'no error text'}).`)
  }
}

function fetchCsrf() {
  const response = http.get(`${BASE}/api/auth/csrf`, { tags: { step: 'csrf' } })
  if (!check(response, { 'csrf 200': (r) => r.status === 200 })) {
    return null
  }
  const body = response.json()
  return { [body.headerName]: body.token }
}

function headers(extra = {}) {
  return { 'Content-Type': 'application/json', ...csrf, ...extra }
}

/** Registers this virtual patron; false (after a pause) if it could not, so a failure never loops at full speed. */
function register() {
  csrf = fetchCsrf()
  if (csrf === null) {
    sleep(PAUSE)
    return false
  }
  const email = `perf-${__VU}-${Date.now()}@example.test`
  const response = http.post(
    `${BASE}/api/auth/register`,
    json({ firstName: 'Perf', email, mobileNumber: '+27 82 123 4567', password: 'Milkshake1Test', confirmPassword: 'Milkshake1Test' }),
    { headers: headers(), tags: { step: 'register' } },
  )
  if (!check(response, { 'registered': (r) => r.status === 201 })) {
    csrf = null
    sleep(PAUSE)
    return false
  }
  // The anti-forgery token belongs to the signed-in session, so it is fetched again after registering.
  csrf = fetchCsrf()
  return csrf !== null
}

export default function () {
  if (csrf === null && !register()) {
    return
  }

  const page = http.get(`${BASE}/order`, { tags: { kind: 'page', step: 'order-page' } })
  check(page, { 'order page 200': (r) => r.status === 200 })
  sleep(PAUSE)

  const optionsResponse = http.get(`${BASE}/api/ordering/options`, { tags: { kind: 'api', step: 'options' } })
  if (!check(optionsResponse, { 'options 200': (r) => r.status === 200 })) {
    console.warn(`options: HTTP ${optionsResponse.status}`)
    sleep(PAUSE)
    return
  }
  const menu = optionsResponse.json()
  const drinks = [{ flavourId: menu.flavours[0].id, consistencyId: menu.consistencies[0].id, toppingId: menu.toppings[0].id }]
  sleep(PAUSE)

  const quote = http.post(`${BASE}/api/ordering/quote`, json({ drinks }), { headers: headers(), tags: { kind: 'api', step: 'quote' } })
  if (!check(quote, { 'quote 200': (r) => r.status === 200 })) {
    sleep(PAUSE)
    return
  }
  const totals = quote.json().totals
  sleep(PAUSE)

  const placed = http.post(
    `${BASE}/api/ordering/orders`,
    json({
      restaurantId: menu.restaurants[0].id,
      pickupLocal: menu.pickup.latestLocal,
      drinks,
      expectedTotal: totals.total,
      expectedCurrencyCode: totals.currencyCode,
    }),
    { headers: headers({ 'Idempotency-Key': uuidv4() }), tags: { kind: 'api', step: 'place-order' } },
  )
  if (!check(placed, { 'order placed 201': (r) => r.status === 201 })) {
    sleep(PAUSE)
    return
  }
  const reference = placed.json().reference
  sleep(PAUSE)

  const payment = http.post(`${BASE}/api/ordering/orders/${reference}/payments`, json({}), {
    headers: headers({ 'Idempotency-Key': uuidv4() }),
    tags: { kind: 'api', step: 'start-payment' },
  })
  check(payment, { 'checkout opened 200': (r) => r.status === 200 && String(r.json('checkoutUrl')).startsWith('https://checkout.paystack.com/') })
  sleep(PAUSE)

  const orderPage = http.get(`${BASE}/orders/${reference}`, { tags: { kind: 'page', step: 'order-page' } })
  check(orderPage, { 'order page 200': (r) => r.status === 200 })
  const order = http.get(`${BASE}/api/ordering/orders/${reference}`, { tags: { kind: 'api', step: 'order' } })
  check(order, { 'order 200': (r) => r.status === 200 && r.json('reference') === reference })
  sleep(PAUSE)
}
