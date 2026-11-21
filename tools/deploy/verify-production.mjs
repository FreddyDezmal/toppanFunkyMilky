// M14 Step 9: read-only checks of the deployed site from the outside. Sends a few dozen ordinary requests; never signs
// in, never changes data, never load-tests.
//
//   node tools/deploy/verify-production.mjs https://milkyshaky.onrender.com
//
// Writes a report to .m14-deploy/verify-<time>.md and exits 1 if any check fails. Expected header values are read from
// backend/src/Milkshake.Api/Security/SecurityHeaders.cs, so the checks follow the code.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')
const base = (process.argv[2] ?? '').replace(/\/+$/, '')
if (!/^https:\/\/[^/]+$/.test(base)) {
  console.error('Usage: node tools/deploy/verify-production.mjs https://<host>')
  process.exit(2)
}
const host = new URL(base).host

// Expected values, from the code.
const headersSource = readFileSync(path.join(repo, 'backend', 'src', 'Milkshake.Api', 'Security', 'SecurityHeaders.cs'), 'utf8')
function constant(name) {
  const match = headersSource.match(new RegExp(`const string ${name}\\s*=([\\s\\S]*?");`))
  if (!match) {
    throw new Error(`${name} not found in SecurityHeaders.cs`)
  }
  return [...match[1].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]).join('')
}
const SITE_CSP = constant('SiteContentSecurityPolicy')
const API_CSP = constant('ApiContentSecurityPolicy')
const PERMISSIONS = constant('PermissionsPolicy')

const results = []
function check(area, name, ok, detail = '') {
  results.push({ area, name, ok, detail })
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${area}: ${name}${detail ? ` (${detail})` : ''}`)
}
function info(area, name, detail) {
  results.push({ area, name, ok: null, detail })
  console.log(`INFO  ${area}: ${name} (${detail})`)
}

async function get(url, init = {}) {
  const started = performance.now()
  const response = await fetch(url, { redirect: 'manual', signal: AbortSignal.timeout(120_000), ...init })
  const body = await response.text()
  return { response, body, ms: Math.round(performance.now() - started) }
}

const percentile = (values, p) => [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.ceil((p / 100) * values.length) - 1)]

try {
  // 1. Wake-up and health (a sleeping free service takes about a minute).
  const first = await get(`${base}/api/health`)
  info('Availability', 'first request (includes waking up if the service was asleep)', `${first.ms} ms, HTTP ${first.response.status}`)
  const health = JSON.parse(first.body)
  check('Availability', 'health is Healthy', health.status === 'Healthy', `status ${health.status}`)
  for (const name of ['self', 'database', 'workers']) {
    const entry = (health.checks ?? []).find((c) => c.name === name)
    check('Availability', `health check "${name}" present and Healthy`, entry?.status === 'Healthy', entry ? entry.status : 'missing')
  }
  check('Availability', 'health response holds no email addresses or order references',
    !/@|MS-[0-9A-Z]{4}-/.test(first.body), `${first.body.length} bytes`)

  // 2. HTTPS and HSTS (D-6: one year, no includeSubDomains, no preload on a shared platform domain).
  const plain = await fetch(`http://${host}/`, { redirect: 'manual', signal: AbortSignal.timeout(60_000) })
  const location = plain.headers.get('location') ?? ''
  check('HTTPS', 'plain HTTP redirects to HTTPS', [301, 302, 307, 308].includes(plain.status) && location.startsWith('https://'),
    `HTTP ${plain.status} → ${location || 'no Location'}`)
  const site = await get(`${base}/`)
  const hsts = site.response.headers.get('strict-transport-security') ?? ''
  check('HTTPS', 'HSTS sent with max-age of one year', /max-age=31536000/.test(hsts), hsts || 'missing')
  check('HTTPS', 'HSTS without includeSubDomains or preload', hsts !== '' && !/includesubdomains|preload/i.test(hsts), hsts || 'missing')

  // 3. Website headers.
  const h = (r, name) => r.response.headers.get(name) ?? ''
  check('Site', 'home page served (text/html, 200)', site.response.status === 200 && h(site, 'content-type').startsWith('text/html'),
    `HTTP ${site.response.status} ${h(site, 'content-type')}`)
  check('Site', 'Content-Security-Policy is the site policy', h(site, 'content-security-policy') === SITE_CSP, h(site, 'content-security-policy') || 'missing')
  check('Site', 'X-Content-Type-Options nosniff', h(site, 'x-content-type-options') === 'nosniff')
  check('Site', 'X-Frame-Options DENY', h(site, 'x-frame-options') === 'DENY')
  check('Site', 'Referrer-Policy strict-origin-when-cross-origin', h(site, 'referrer-policy') === 'strict-origin-when-cross-origin', h(site, 'referrer-policy'))
  check('Site', 'Permissions-Policy as in the code', h(site, 'permissions-policy') === PERMISSIONS, h(site, 'permissions-policy'))
  check('Site', 'Cross-Origin-Opener-Policy same-origin', h(site, 'cross-origin-opener-policy') === 'same-origin')
  check('Site', 'index.html is not cached', /no-cache/.test(h(site, 'cache-control')), h(site, 'cache-control'))
  const asset = (site.body.match(/\/assets\/[^"']+\.js/) ?? [])[0]
  if (asset) {
    const a = await get(`${base}${asset}`)
    check('Site', 'hashed assets cached for a year (immutable)', /max-age=31536000/.test(h(a, 'cache-control')) && /immutable/.test(h(a, 'cache-control')), h(a, 'cache-control'))
  } else {
    check('Site', 'hashed assets cached for a year (immutable)', false, 'no /assets/*.js found in index.html')
  }
  const route = await get(`${base}/orders/MS-AAAA-AAAA`)
  check('Site', 'client-side routes fall back to the site', route.response.status === 200 && h(route, 'content-type').startsWith('text/html'))
  info('Site', 'Server / X-Powered-By headers', `server: ${h(site, 'server') || '-'}; x-powered-by: ${h(site, 'x-powered-by') || '-'}`)

  // 4. API headers, unknown API paths, no API description in production.
  const api = await get(`${base}/api/health`)
  check('API', 'Content-Security-Policy is the API policy', h(api, 'content-security-policy') === API_CSP, h(api, 'content-security-policy'))
  check('API', 'Referrer-Policy no-referrer', h(api, 'referrer-policy') === 'no-referrer')
  const unknown = await get(`${base}/api/no-such-endpoint`)
  check('API', 'unknown /api path is 404, not the site', unknown.response.status === 404 && !h(unknown, 'content-type').startsWith('text/html'),
    `HTTP ${unknown.response.status} ${h(unknown, 'content-type')}`)
  const openapi = await get(`${base}/openapi/v1.json`)
  check('API', 'no OpenAPI document published', !/"openapi"\s*:/.test(openapi.body), `HTTP ${openapi.response.status} ${h(openapi, 'content-type')}`)
  const scalar = await get(`${base}/scalar`)
  check('API', 'no Scalar API browser published', !/@scalar|api-reference/i.test(scalar.body), `HTTP ${scalar.response.status}`)

  // 5. Cookies (anti-forgery cookie from GET /api/auth/csrf; the session cookie is checked in the browser smoke test).
  const csrf = await get(`${base}/api/auth/csrf`)
  const cookies = csrf.response.headers.getSetCookie?.() ?? []
  const anti = cookies.find((c) => c.startsWith('milkshake.antiforgery=')) ?? ''
  check('Cookies', 'anti-forgery cookie is Secure, HttpOnly, SameSite=Strict, host-only',
    /;\s*secure/i.test(anti) && /;\s*httponly/i.test(anti) && /samesite=strict/i.test(anti) && !/;\s*domain=/i.test(anti), anti ? 'flags checked' : 'cookie missing')
  check('Cookies', 'CSRF token endpoint returns a token', /"token"\s*:\s*"[^"]+"/.test(csrf.body))

  // 6. CSRF and CORS.
  const noToken = await get(`${base}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
  check('CSRF', 'a write without the anti-forgery token is refused', noToken.response.status >= 400 && noToken.response.status < 500, `HTTP ${noToken.response.status}`)
  const preflight = await get(`${base}/api/auth/login`, {
    method: 'OPTIONS',
    headers: { Origin: 'https://attacker.example', 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'content-type,x-xsrf-token' },
  })
  check('CORS', 'cross-origin preflight is not allowed', !h(preflight, 'access-control-allow-origin'), `HTTP ${preflight.response.status}, ACAO ${h(preflight, 'access-control-allow-origin') || 'none'}`)
  const crossGet = await get(`${base}/api/health`, { headers: { Origin: 'https://attacker.example' } })
  check('CORS', 'cross-origin reads get no Access-Control-Allow-Origin', !h(crossGet, 'access-control-allow-origin'))

  // 7. Light response-time check (sequential, 20 requests each; not a load test).
  for (const [label, url] of [['GET /api/health', `${base}/api/health`], ['GET / (site)', `${base}/`], ['GET /api/ordering/options (sign-in required: 401)', `${base}/api/ordering/options`]]) {
    const times = []
    for (let i = 0; i < 20; i++) {
      times.push((await get(url)).ms)
    }
    info('Response time', label, `p50 ${percentile(times, 50)} ms, p95 ${percentile(times, 95)} ms, max ${Math.max(...times)} ms (20 sequential, from this machine)`)
  }
} catch (error) {
  check('Run', 'checks completed', false, error instanceof Error ? error.message : String(error))
}

const failed = results.filter((r) => r.ok === false)
const stamp = new Date().toISOString().replace(/[:.]/g, '-')
const lines = [
  `# Production verification – ${base}`,
  '',
  `Run: ${new Date().toISOString()} from the developer machine. ${results.filter((r) => r.ok).length} passed, ${failed.length} failed.`,
  '',
  '| Area | Check | Result | Detail |',
  '| --- | --- | --- | --- |',
  ...results.map((r) => `| ${r.area} | ${r.name} | ${r.ok === null ? 'info' : r.ok ? 'PASS' : '**FAIL**'} | ${String(r.detail).replaceAll('|', '\\|')} |`),
  '',
]
mkdirSync(path.join(repo, '.m14-deploy'), { recursive: true })
const report = path.join(repo, '.m14-deploy', `verify-${stamp}.md`)
writeFileSync(report, lines.join('\n'))
console.log(`\n${failed.length === 0 ? 'ALL CHECKS PASSED' : `${failed.length} CHECK(S) FAILED`} – report: ${report}`)
process.exit(failed.length === 0 ? 0 : 1)
