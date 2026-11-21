// Paystack simulator for the local E2E stack (M13, decision D2). TEST ONLY: never deployed, never used with a real key.
//
// The API under test is pointed here with Payments:Paystack:BaseUrl. The simulator answers the four calls
// PaystackGateway makes (transaction/initialize, transaction/verify/{reference}, refund, refund?reference=) in Paystack's
// snake_case envelope. It ASSERTS what the server sends: the bearer key, an integer amount in cents, currency ZAR. It
// signs webhooks exactly as Paystack does: HMAC-SHA512 over the raw body, in x-paystack-signature.
//
// Tests drive it through /__control/... endpoints. It listens on 127.0.0.1 only.
//
// Environment: SIM_PORT (4010), SIM_SECRET (the throwaway key the API is configured with), SIM_WEBHOOK_URL.
import { createHmac, randomInt } from 'node:crypto'
import { createServer } from 'node:http'
import { request as httpsRequest } from 'node:https'

const port = Number(process.env.SIM_PORT ?? 4010)
const secret = process.env.SIM_SECRET ?? ''
const webhookUrl = new URL(process.env.SIM_WEBHOOK_URL ?? 'https://localhost:7298/api/payments/paystack/webhook')

if (!secret.startsWith('sk_test_e2e_')) {
  console.error('Refusing to start: SIM_SECRET must be a throwaway E2E key (sk_test_e2e_…).')
  process.exit(1)
}

/** reference → { id, reference, email, amount, currency, callbackUrl, outcome, paidAmount, delayMs, unavailable } */
const transactions = new Map()
/** refund reference → { id, reference, transaction, amount, currency, status } */
const refunds = new Map()
/** Every API call received, for assertions (no keys stored). */
const calls = []

const VERIFY_STATUS = { open: 'ongoing', paid: 'success', failed: 'failed', abandoned: 'abandoned' }

function send(res, status, body) {
  const text = JSON.stringify(body)
  res.writeHead(status, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(text) })
  res.end(text)
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = []
    req.on('data', (chunk) => chunks.push(chunk))
    req.on('end', () => resolve(Buffer.concat(chunks)))
    req.on('error', reject)
  })
}

function json(buffer) {
  try {
    return buffer.length === 0 ? {} : JSON.parse(buffer.toString('utf8'))
  } catch {
    return undefined
  }
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function authorised(req) {
  return req.headers.authorization === `Bearer ${secret}`
}

/** Posts a signed webhook to the API, exactly as Paystack would. Resolves to the API's HTTP status. */
function postWebhook(payload) {
  const body = Buffer.from(JSON.stringify(payload), 'utf8')
  const signature = createHmac('sha512', secret).update(body).digest('hex')
  return new Promise((resolve, reject) => {
    const req = httpsRequest(
      webhookUrl,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Content-Length': body.length, 'x-paystack-signature': signature },
        rejectUnauthorized: false, // the local ASP.NET Core development certificate
      },
      (res) => {
        res.resume()
        res.on('end', () => resolve(res.statusCode))
      },
    )
    req.on('error', reject)
    req.end(body)
  })
}

function chargeSuccess(tx) {
  return {
    event: 'charge.success',
    data: { id: tx.id, reference: tx.reference, amount: tx.paidAmount ?? tx.amount, currency: tx.currency, status: 'success' },
  }
}

async function paystackApi(req, res, url) {
  if (!authorised(req)) {
    calls.push({ path: url.pathname, unauthorised: true })
    return send(res, 401, { status: false, message: 'Invalid key' })
  }

  // POST transaction/initialize
  if (req.method === 'POST' && url.pathname === '/transaction/initialize') {
    const body = json(await readBody(req))
    calls.push({ path: url.pathname, body })
    const valid =
      body && typeof body.email === 'string' && Number.isInteger(body.amount) && body.amount > 0 && body.currency === 'ZAR' &&
      /^MSP-[0-9A-Z]{12}$/.test(body.reference ?? '') && typeof body.callback_url === 'string'
    if (!valid) {
      return send(res, 400, { status: false, message: 'Invalid initialize request' })
    }
    const existing = transactions.get(body.reference)
    const tx = existing ?? {
      id: randomInt(1_000_000, 2_000_000_000),
      reference: body.reference,
      outcome: 'open',
      paidAmount: undefined,
      delayMs: 0,
      unavailable: false,
    }
    Object.assign(tx, { email: body.email, amount: body.amount, currency: body.currency, callbackUrl: body.callback_url })
    transactions.set(tx.reference, tx)
    return send(res, 200, {
      status: true,
      message: 'Authorization URL created',
      data: {
        authorization_url: `https://checkout.paystack.com/e2e-${tx.reference.toLowerCase()}`,
        access_code: `e2e_${tx.id}`,
        reference: tx.reference,
      },
    })
  }

  // GET transaction/verify/{reference}
  const verify = /^\/transaction\/verify\/([^/]+)$/.exec(url.pathname)
  if (req.method === 'GET' && verify) {
    const reference = decodeURIComponent(verify[1])
    calls.push({ path: '/transaction/verify', reference })
    const tx = transactions.get(reference)
    if (!tx) {
      return send(res, 404, { status: false, message: 'Transaction reference not found' })
    }
    if (tx.delayMs > 0) {
      await delay(tx.delayMs)
    }
    if (tx.unavailable) {
      return send(res, 503, { status: false, message: 'Service unavailable' })
    }
    return send(res, 200, {
      status: true,
      message: 'Verification successful',
      data: {
        id: tx.id,
        status: VERIFY_STATUS[tx.outcome] ?? 'ongoing',
        reference: tx.reference,
        amount: tx.paidAmount ?? tx.amount,
        currency: tx.currency,
        gateway_response: tx.outcome === 'failed' ? 'Declined' : tx.outcome === 'paid' ? 'Successful' : 'Pending',
      },
    })
  }

  // POST refund
  if (req.method === 'POST' && url.pathname === '/refund') {
    const body = json(await readBody(req))
    calls.push({ path: url.pathname, body })
    const tx = body && transactions.get(body.transaction)
    if (!tx || !Number.isInteger(body.amount) || body.amount <= 0 || body.currency !== 'ZAR' || typeof body.reference !== 'string') {
      return send(res, 400, { status: false, message: 'Invalid refund request' })
    }
    if (refunds.has(body.reference.toUpperCase())) {
      return send(res, 409, { status: false, message: 'Refund already exists' })
    }
    const refund = {
      id: randomInt(1_000_000, 2_000_000_000),
      reference: body.reference.toUpperCase(),
      transaction: tx.reference,
      amount: body.amount,
      currency: body.currency,
      status: 'pending',
    }
    refunds.set(refund.reference, refund)
    return send(res, 200, { status: true, message: 'Refund has been queued for processing', data: refundData(refund) })
  }

  // GET refund?reference=
  if (req.method === 'GET' && url.pathname === '/refund') {
    const reference = (url.searchParams.get('reference') ?? '').toUpperCase()
    calls.push({ path: '/refund (lookup)', reference })
    const refund = refunds.get(reference)
    return send(res, 200, { status: true, message: 'Refunds retrieved', data: refund ? [refundData(refund)] : [] })
  }

  return send(res, 404, { status: false, message: 'Not found' })
}

function refundData(refund) {
  return { id: refund.id, status: refund.status, reference: refund.reference, amount: refund.amount }
}

async function control(req, res, url) {
  const path = url.pathname.slice('/__control'.length)

  if (req.method === 'GET' && path === '/health') {
    return send(res, 200, { ok: true })
  }
  if (req.method === 'GET' && path === '/calls') {
    return send(res, 200, calls)
  }

  const tx = /^\/transactions\/([^/]+)(\/webhook)?$/.exec(path)
  if (tx) {
    const reference = decodeURIComponent(tx[1])
    const record = transactions.get(reference)
    if (req.method === 'GET' && !tx[2]) {
      return record ? send(res, 200, record) : send(res, 404, { error: 'unknown transaction' })
    }
    if (!record) {
      return send(res, 404, { error: 'unknown transaction' })
    }
    // POST /transactions/{ref}  { outcome?, paidAmount?, delayMs?, unavailable? }
    if (req.method === 'POST' && !tx[2]) {
      const body = json(await readBody(req)) ?? {}
      for (const key of ['outcome', 'paidAmount', 'delayMs', 'unavailable']) {
        if (key in body) {
          record[key] = body[key]
        }
      }
      return send(res, 200, record)
    }
    // POST /transactions/{ref}/webhook  { times? } → charge.success, signed, sent times times
    if (req.method === 'POST' && tx[2]) {
      const body = json(await readBody(req)) ?? {}
      const statuses = []
      for (let i = 0; i < (body.times ?? 1); i++) {
        statuses.push(await postWebhook(chargeSuccess(record)))
      }
      return send(res, 200, { statuses })
    }
  }

  // GET /transactions?email=…  → the newest transaction for a patron (tests know the patron, not the payment reference)
  if (req.method === 'GET' && path === '/transactions') {
    const email = url.searchParams.get('email')
    const matches = [...transactions.values()].filter((t) => t.email === email)
    return matches.length ? send(res, 200, matches.at(-1)) : send(res, 404, { error: 'no transaction for this email' })
  }

  // POST /refunds/by-transaction/{ref}  { status: 'processed' | 'failed', webhook?: boolean }
  const byTx = /^\/refunds\/by-transaction\/([^/]+)$/.exec(path)
  if (req.method === 'POST' && byTx) {
    const reference = decodeURIComponent(byTx[1])
    const body = json(await readBody(req)) ?? {}
    const matches = [...refunds.values()].filter((r) => r.transaction === reference)
    if (matches.length === 0) {
      return send(res, 404, { error: 'no refund for this transaction' })
    }
    const statuses = []
    for (const refund of matches) {
      refund.status = body.status
      if (body.webhook) {
        statuses.push(
          await postWebhook({
            event: `refund.${body.status}`,
            data: { refund_reference: refund.reference, transaction_reference: refund.transaction, status: body.status },
          }),
        )
      }
    }
    return send(res, 200, { refunds: matches, statuses })
  }

  return send(res, 404, { error: 'unknown control endpoint' })
}

const server = createServer((req, res) => {
  const url = new URL(req.url ?? '/', `http://127.0.0.1:${port}`)
  const handler = url.pathname.startsWith('/__control') ? control : paystackApi
  handler(req, res, url).catch((error) => {
    console.error(error)
    if (!res.headersSent) {
      send(res, 500, { status: false, message: 'Simulator error' })
    }
  })
})

server.listen(port, '127.0.0.1', () => console.log(`Paystack simulator listening on http://127.0.0.1:${port}`))
