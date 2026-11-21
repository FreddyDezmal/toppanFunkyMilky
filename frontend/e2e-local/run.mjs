// Local E2E stack for M13 (decisions D1 to D3). Run from frontend/:  npm run test:e2e:local [-- <playwright args>]
//
// 1. Starts a disposable PostgreSQL 17 and a disposable Mailpit (Docker). The database password is random for each run
//    and lives only in this process's memory.
// 2. Builds the database from zero: dotnet ef database update (all migrations, plus seed data).
// 3. Creates an E2E manager with provision-manager (random password, passed to Playwright in memory).
// 4. Starts the Paystack simulator, the API (environment "E2E", so user secrets, Supabase and the real Paystack key
//    are never loaded) and the production build of the site (vite build, then vite preview).
// 5. Runs Playwright (playwright.local.config.ts), then the read-only integrity checks, then removes everything.
//
// Needs Docker running, the dotnet-ef tool, and the normal dev API and Vite STOPPED (it uses the same ports).
import { execFileSync, spawn } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { createWriteStream, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { request as httpsRequest } from 'node:https'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repo = path.resolve(frontend, '..')
const runId = new Date().toISOString().replace(/[:.]/g, '-')
const logDir = path.join(repo, '.m13-logs', 'e2e', runId)
mkdirSync(logDir, { recursive: true })

const PORTS = { api: 7298, vite: 5173, simulator: 4010, db: 55432, smtp: 1026, mailpitUi: 8026 }
const dbContainer = `milkshake-e2e-db-${process.pid}`
const mailContainer = `milkshake-e2e-mail-${process.pid}`
const dbPassword = randomBytes(24).toString('base64url')
const managerPassword = `E2e-${randomBytes(18).toString('base64url')}1a`
const managerEmail = 'e2e-manager@milkyshaky.test'
const simulatorSecret = `sk_test_e2e_${randomBytes(16).toString('hex')}`
const children = []
const isWindows = process.platform === 'win32'
// The development certificate's key is exported here for Vite and deleted when the run ends.
const certDir = mkdtempSync(path.join(tmpdir(), 'milkshake-e2e-cert-'))
const site = `https://localhost:${PORTS.vite}`

function log(message) {
  console.log(`[e2e-local] ${message}`)
}

/** Never prints the random database password or the E2E secrets. */
function masked(text) {
  return text.replaceAll(dbPassword, '***').replaceAll(managerPassword, '***').replaceAll(simulatorSecret, '***')
}

function run(command, args, options = {}) {
  log(masked(`$ ${command} ${args.join(' ')}`))
  // No shell: docker and dotnet are executables, and arguments (e.g. psql's '|') must reach them unchanged.
  return execFileSync(command, args, { stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', ...options })
}

function portFree(port) {
  return new Promise((resolve) => {
    const server = createServer()
    server.once('error', () => resolve(false))
    server.once('listening', () => server.close(() => resolve(true)))
    server.listen(port, '127.0.0.1')
  })
}

function start(name, command, args, env) {
  const out = createWriteStream(path.join(logDir, `${name}.log`))
  const child = spawn(command, args, { cwd: repo, env, stdio: ['ignore', 'pipe', 'pipe'] })
  child.stdout.pipe(out)
  child.stderr.pipe(out)
  children.push({ name, child })
  return child
}

function httpsOk(url) {
  return new Promise((resolve) => {
    const req = httpsRequest(url, { rejectUnauthorized: false, timeout: 3000 }, (res) => {
      res.resume()
      resolve((res.statusCode ?? 500) < 500)
    })
    req.on('error', () => resolve(false))
    req.on('timeout', () => {
      req.destroy()
      resolve(false)
    })
    req.end()
  })
}

async function httpOk(url) {
  try {
    return (await fetch(url)).status < 500
  } catch {
    return false
  }
}

async function waitFor(what, check, timeoutMs) {
  const until = Date.now() + timeoutMs
  while (Date.now() < until) {
    if (await check()) {
      log(`${what} is ready`)
      return
    }
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
  throw new Error(`${what} did not become ready in ${timeoutMs / 1000} s (logs: ${logDir})`)
}

/** Flattens a configuration object into ASP.NET Core environment variables (Section__Key__0__Name=…). */
function flatten(prefix, value, into = {}) {
  if (value !== null && typeof value === 'object') {
    for (const [key, child] of Object.entries(value)) {
      flatten(`${prefix}__${key}`, child, into)
    }
  } else {
    into[prefix] = String(value)
  }
  return into
}

/**
 * The reference data (flavours, toppings, consistencies, restaurants, settings) comes from the SeedData section of
 * appsettings.Development.json, so Development stays its single source. Only that section is read: no secrets are in
 * that file, and user secrets are never loaded in the E2E environment.
 */
function seedDataEnvironment() {
  const file = path.join(repo, 'backend', 'src', 'Milkshake.Api', 'appsettings.Development.json')
  const settings = JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''))
  if (!settings.SeedData?.Enabled) {
    throw new Error('appsettings.Development.json has no enabled SeedData section.')
  }
  return flatten('SeedData', settings.SeedData)
}

/** Settings for every dotnet process. Environment "E2E": appsettings.Development.json and user secrets are NOT loaded. */
function apiEnvironment() {
  return {
    ...process.env,
    ...seedDataEnvironment(),
    ASPNETCORE_ENVIRONMENT: 'E2E',
    DOTNET_ENVIRONMENT: 'E2E',
    ASPNETCORE_URLS: `https://localhost:${PORTS.api}`,
    ConnectionStrings__MilkshakeDb: `Host=127.0.0.1;Port=${PORTS.db};Database=milkshake_e2e;Username=postgres;Timeout=15;Command Timeout=60`,
    Database__Password: dbPassword,
    Cors__AllowedOrigins__0: site,
    Payments__Paystack__SecretKey: simulatorSecret,
    Payments__Paystack__BaseUrl: `http://127.0.0.1:${PORTS.simulator}/`,
    Payments__Paystack__ReturnBaseUrl: `${site}/`,
    Email__Provider: 'Mailpit',
    Email__Mailpit__Host: '127.0.0.1',
    Email__Mailpit__Port: String(PORTS.smtp),
    Email__FromAddress: 'orders@milkyshaky.test',
    Email__FromName: 'Milky Shaky Custom Drinks',
    Email__PublicBaseUrl: `${site}/`,
    Email__Dispatch__IntervalSeconds: '5',
    // Every simulated patron and the manager sign in from 127.0.0.1, so the per-address limit on login and registration
    // (10 a minute by default) would refuse the suite itself (M13 DEF-05). The limit is proven by the integration tests.
    RateLimiting__AuthPermitLimit: '300',
    Logging__LogLevel__Default: 'Information',
    'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning',
    ...(process.env.E2E_LOCAL_EXTRA_ENV ? JSON.parse(process.env.E2E_LOCAL_EXTRA_ENV) : {}),
  }
}

async function cleanup() {
  for (const { name, child } of children.reverse()) {
    if (child.exitCode === null) {
      log(`stopping ${name}`)
      try {
        if (isWindows) {
          execFileSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
        } else {
          child.kill('SIGTERM')
        }
      } catch {
        // already gone
      }
    }
  }
  rmSync(certDir, { recursive: true, force: true })
  for (const container of [dbContainer, mailContainer]) {
    try {
      execFileSync('docker', ['rm', '-f', container], { stdio: 'ignore' })
    } catch {
      // not running
    }
  }
}

async function main() {
  for (const [name, port] of Object.entries(PORTS)) {
    if (!(await portFree(port))) {
      throw new Error(`Port ${port} (${name}) is in use. Stop the dev API, Vite and dev Mailpit first.`)
    }
  }

  // 1. Disposable database and mail capture, bound to this machine only.
  run('docker', ['run', '-d', '--rm', '--name', dbContainer, '-e', `POSTGRES_PASSWORD=${dbPassword}`, '-e', 'POSTGRES_DB=milkshake_e2e',
    '-p', `127.0.0.1:${PORTS.db}:5432`, 'postgres:17'])
  run('docker', ['run', '-d', '--rm', '--name', mailContainer, '-p', `127.0.0.1:${PORTS.smtp}:1025`, '-p', `127.0.0.1:${PORTS.mailpitUi}:8025`,
    'axllent/mailpit'])
  await waitFor('PostgreSQL', async () => {
    try {
      run('docker', ['exec', dbContainer, 'pg_isready', '-U', 'postgres', '-d', 'milkshake_e2e'])
      return true
    } catch {
      return false
    }
  }, 60_000)
  await waitFor('Mailpit', () => httpOk(`http://127.0.0.1:${PORTS.mailpitUi}/api/v1/info`), 60_000)

  // 2. Schema from zero (every migration and the seed data), and 3. the E2E manager.
  const env = apiEnvironment()
  run('dotnet', ['build', 'Milkshake.slnx', '-v', 'q', '-nologo'], { cwd: repo, env })
  const migrate = run('dotnet', ['ef', 'database', 'update', '--no-build', '--project', 'backend/src/Milkshake.Infrastructure',
    '--startup-project', 'backend/src/Milkshake.Api'], { cwd: repo, env })
  writeFileSync(path.join(logDir, 'migrate.log'), migrate)
  const provision = run('dotnet', ['run', '--no-build', '--no-launch-profile', '--project', 'backend/src/Milkshake.Api', '--',
    'provision-manager', '--email', managerEmail, '--first-name', 'E2E'], { cwd: repo, env: { ...env, MILKSHAKE_MANAGER_PASSWORD: managerPassword } })
  writeFileSync(path.join(logDir, 'provision.log'), provision)

  // The site is served over HTTPS with the ASP.NET development certificate (WebKit needs it for Secure cookies).
  const cert = path.join(certDir, 'localhost.pem')
  run('dotnet', ['dev-certs', 'https', '--export-path', cert, '--format', 'Pem', '--no-password'])

  // 4. Simulator, API and Vite.
  start('simulator', 'node', [path.join(frontend, 'e2e-local', 'support', 'paystack-simulator.mjs')], {
    ...process.env,
    SIM_PORT: String(PORTS.simulator),
    SIM_SECRET: simulatorSecret,
    SIM_WEBHOOK_URL: `https://localhost:${PORTS.api}/api/payments/paystack/webhook`,
  })
  start('api', 'dotnet', ['run', '--no-build', '--no-launch-profile', '--project', 'backend/src/Milkshake.Api'], env)
  // The production build, served by `vite preview` (same port, HTTPS and /api proxy as dev). The dev server's hundreds
  // of unbundled modules made each full page load take 15-20 s in WebKit, so journeys timed out (DEF-01).
  // npm and npx are .cmd scripts on Windows, so they need a shell; their arguments are fixed and safe.
  log('$ npm run build')
  execFileSync('npm', ['run', 'build'], { cwd: frontend, shell: isWindows, stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8' })
  const vite = spawn('npm', ['run', 'preview'], {
    cwd: frontend,
    shell: isWindows,
    stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, E2E_VITE_CERT: cert, E2E_VITE_KEY: path.join(certDir, 'localhost.key') },
  })
  const viteLog = createWriteStream(path.join(logDir, 'vite.log'))
  vite.stdout.pipe(viteLog)
  vite.stderr.pipe(viteLog)
  children.push({ name: 'vite', child: vite })

  await waitFor('Paystack simulator', () => httpOk(`http://127.0.0.1:${PORTS.simulator}/__control/health`), 30_000)
  await waitFor('API', () => httpsOk(`https://localhost:${PORTS.api}/api/health`), 180_000)
  await waitFor('Vite', () => httpsOk(`${site}/`), 60_000)

  // 5. Playwright, with the manager credentials and service addresses in memory only.
  const playwright = spawn('npx', ['playwright', 'test', '-c', 'playwright.local.config.ts', ...process.argv.slice(2)], {
    cwd: frontend,
    shell: isWindows,
    stdio: 'inherit',
    env: {
      ...process.env,
      E2E_LOCAL: '1',
      E2E_LOCAL_MANAGER_EMAIL: managerEmail,
      E2E_LOCAL_MANAGER_PASSWORD: managerPassword,
      E2E_SIMULATOR_URL: `http://127.0.0.1:${PORTS.simulator}`,
      E2E_MAILPIT_URL: `http://127.0.0.1:${PORTS.mailpitUi}`,
      E2E_LOG_DIR: logDir,
      E2E_SITE: site,
    },
  })
  const exitCode = await new Promise((resolve) => playwright.on('exit', (code) => resolve(code ?? 1)))

  // AC-13.5: read-only integrity checks on everything the run created.
  const sql = readFileSync(path.join(repo, 'tools', 'db', 'integrity-checks.sql'), 'utf8')
  const integrity = execFileSync('docker', ['exec', '-i', dbContainer, 'psql', '-U', 'postgres', '-d', 'milkshake_e2e', '-v', 'ON_ERROR_STOP=1',
    '-A', '-F', '|', '-t'], { input: sql, encoding: 'utf8' })
  writeFileSync(path.join(logDir, 'integrity.txt'), integrity)
  const violations = integrity.split(/\r?\n/).filter((line) => line.includes('|')).filter((line) => Number(line.split('|')[1]) > 0)
  log(violations.length === 0 ? 'integrity checks: no violations' : `integrity checks FAILED:\n${violations.join('\n')}`)

  log(`logs: ${logDir}`)
  return exitCode === 0 && violations.length === 0 ? 0 : 1
}

let code = 1
try {
  code = await main()
} catch (error) {
  console.error(`[e2e-local] ${error instanceof Error ? error.message : String(error)}`)
} finally {
  await cleanup()
}
process.exit(code)
