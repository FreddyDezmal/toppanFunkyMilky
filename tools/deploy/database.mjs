// M14 Step 8: the deployed database, from the developer's machine. Never run by CI.
//
//   node tools/deploy/database.mjs status            --confirm <project-ref>
//   node tools/deploy/database.mjs script                                        (no connection needed)
//   node tools/deploy/database.mjs apply             --confirm <project-ref> [--seed]
//   node tools/deploy/database.mjs provision-manager --confirm <project-ref> --email <address> --first-name <name>
//
// Connection (environment, set in the shell; none of them is a secret except the password):
//   MILKSHAKE_DEPLOY_HOST      e.g. aws-0-eu-west-1.pooler.supabase.com (Supabase session pooler)
//   MILKSHAKE_DEPLOY_PORT      default 5432
//   MILKSHAKE_DEPLOY_DATABASE  default postgres
//   MILKSHAKE_DEPLOY_USER      e.g. postgres.<project-ref>
//   MILKSHAKE_DEPLOY_PASSWORD  optional: if not set, you are asked for it (typed input is hidden)
//   MILKSHAKE_DEPLOY_SSLMODE   default require; "disable" only for a rehearsal against a local throwaway PostgreSQL
//
// Safety:
// - --confirm must equal the project ref in MILKSHAKE_DEPLOY_USER (or host), so a command meant for one database
//   cannot run against another by mistake.
// - status is read-only. script writes a reviewable SQL file and touches no database.
// - apply runs the reviewed migrations with `dotnet ef database update` (forward only; never drops or resets).
//   --seed adds the demonstration data and is refused unless the database has no flavours yet (first deployment only):
//   seeding again would bring back items a manager renamed.
// - The password reaches child processes only through their environment and is masked in all output.
import { spawnSync } from 'node:child_process'
import { mkdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')
const [command, ...rest] = process.argv.slice(2)
const option = (name) => {
  const index = rest.indexOf(name)
  return index >= 0 ? rest[index + 1] : undefined
}
const flag = (name) => rest.includes(name)
let password = process.env.MILKSHAKE_DEPLOY_PASSWORD ?? ''

function log(message) {
  console.log(`[database] ${message}`)
}

function masked(text) {
  return password ? String(text).replaceAll(password, '***') : String(text)
}

function fail(message) {
  console.error(`[database] ${masked(message)}`)
  process.exit(1)
}

/** Reads a line without echoing it (for passwords). */
function askHidden(question) {
  return new Promise((resolve) => {
    process.stdout.write(question)
    const stdin = process.stdin
    let value = ''
    stdin.setRawMode?.(true)
    stdin.resume()
    stdin.setEncoding('utf8')
    const onData = (chunk) => {
      for (const ch of chunk) {
        if (ch === '\r' || ch === '\n') {
          stdin.setRawMode?.(false)
          stdin.pause()
          stdin.off('data', onData)
          process.stdout.write('\n')
          resolve(value)
          return
        }
        if (ch === '\u0003') {
          process.stdout.write('\n')
          process.exit(130)
        }
        value = ch === '\u0008' || ch === '\u007f' ? value.slice(0, -1) : value + ch
      }
    }
    stdin.on('data', onData)
  })
}

function connection() {
  const host = process.env.MILKSHAKE_DEPLOY_HOST
  const user = process.env.MILKSHAKE_DEPLOY_USER
  if (!host || !user) {
    fail('Set MILKSHAKE_DEPLOY_HOST and MILKSHAKE_DEPLOY_USER first (see docs/guides/operations-guide.md §4).')
  }
  const confirm = option('--confirm')
  if (!confirm || !(user.endsWith(`.${confirm}`) || host.includes(confirm))) {
    fail(`--confirm <project-ref> is required and must match the project in MILKSHAKE_DEPLOY_USER (${user}).`)
  }
  const sslMode = process.env.MILKSHAKE_DEPLOY_SSLMODE || 'require'
  const local = ['localhost', '127.0.0.1'].includes(host)
  if (sslMode !== 'require' && !local) {
    fail('MILKSHAKE_DEPLOY_SSLMODE may only be changed for a local rehearsal database.')
  }
  return {
    host,
    user,
    local,
    sslMode,
    port: process.env.MILKSHAKE_DEPLOY_PORT || '5432',
    database: process.env.MILKSHAKE_DEPLOY_DATABASE || 'postgres',
  }
}

async function ensurePassword() {
  if (!password) {
    password = await askHidden('Database password (hidden): ')
  }
  if (!password) {
    fail('No password given.')
  }
}

/** Read-only query through psql in a postgres:17 container; the password is passed by name (-e PGPASSWORD). */
function query(c, sql) {
  // psql runs in a container: a local rehearsal database is reached through host.docker.internal.
  const result = spawnSync('docker', ['run', '--rm', '-i', '--add-host', 'host.docker.internal:host-gateway', '-e', 'PGPASSWORD',
    '-e', `PGSSLMODE=${c.sslMode}`, 'postgres:17', 'psql', '-h', c.local ? 'host.docker.internal' : c.host, '-p', c.port, '-U', c.user, '-d', c.database, '-v', 'ON_ERROR_STOP=1', '-q', '-A', '-F', '|', '-t',
    '-c', 'SET default_transaction_read_only = on;', '-c', sql],
  { input: '', encoding: 'utf8', env: { ...process.env, PGPASSWORD: password } })
  if (result.status !== 0) {
    fail(`query failed: ${result.stderr || result.error?.message || 'unknown error'}`)
  }
  // -q hides command tags such as "SET"; take the last line anyway, so only the query's own answer is read.
  return result.stdout.trim().split(/\r?\n/).filter(Boolean).at(-1) ?? ''
}

function status(c) {
  // Table names are checked before a CASE is evaluated, so look for the tables first and query them only if they exist.
  const [history, flavourTable] = query(c, `SELECT to_regclass('milkshake.__ef_migrations_history') IS NOT NULL,
    to_regclass('milkshake.flavours') IS NOT NULL;`).split('|')
  let applied = 0
  let latest = 'none'
  if (history === 't') {
    ;[applied, latest] = query(c, `SELECT count(*) || '|' || coalesce((SELECT x.id FROM (SELECT * FROM milkshake.__ef_migrations_history
      ORDER BY 1 DESC LIMIT 1) AS x(id, product_version)), 'none') FROM milkshake.__ef_migrations_history;`).split('|')
  }
  const flavours = flavourTable === 't' ? Number(query(c, 'SELECT count(*) FROM milkshake.flavours;')) : 0
  if (!Number.isInteger(Number(applied)) || !Number.isInteger(flavours)) {
    fail(`could not read the database state (migrations "${applied}", flavours "${flavours}").`)
  }
  log(`migrations applied: ${applied}; latest: ${latest}; flavours: ${flavours}`)
  return { applied: Number(applied), latest, flavours }
}

/** The environment for dotnet: a neutral environment name, so neither Development settings nor user secrets load. */
function dotnetEnv(c, extra = {}) {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Deploy',
    DOTNET_ENVIRONMENT: 'Deploy',
    ConnectionStrings__MilkshakeDb: `Host=${c.host};Port=${c.port};Database=${c.database};Username=${c.user};SSL Mode=${c.sslMode === 'disable' ? 'Disable' : 'Require'};Timeout=30;Command Timeout=120`,
    Database__Password: password,
    ...extra,
  }
}

function flatten(prefix, value, into = {}) {
  if (Array.isArray(value)) {
    value.forEach((child, index) => flatten(`${prefix}__${index}`, child, into))
  } else if (value !== null && typeof value === 'object') {
    for (const [key, child] of Object.entries(value)) {
      flatten(`${prefix}__${key}`, child, into)
    }
  } else {
    into[prefix] = String(value)
  }
  return into
}

function run(executable, args, env) {
  log(`$ ${executable} ${args.join(' ')}`)
  const result = spawnSync(executable, args, { cwd: repo, env, encoding: 'utf8', shell: false })
  process.stdout.write(masked(result.stdout ?? ''))
  process.stderr.write(masked(result.stderr ?? ''))
  if (result.status !== 0) {
    fail(`${executable} failed (exit ${result.status ?? result.error?.message})`)
  }
}

const EF = ['--project', 'backend/src/Milkshake.Infrastructure', '--startup-project', 'backend/src/Milkshake.Api']

switch (command) {
  case 'status': {
    const c = connection()
    await ensurePassword()
    status(c)
    break
  }
  case 'script': {
    const dir = path.join(repo, '.m14-deploy')
    mkdirSync(dir, { recursive: true })
    const file = path.join(dir, `migrations-${new Date().toISOString().slice(0, 10)}.sql`)
    // Generating SQL connects to nothing, but the app's start-up needs a connection string: give it a placeholder.
    run('dotnet', ['ef', 'migrations', 'script', '--idempotent', '--output', file, ...EF], {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Deploy',
      DOTNET_ENVIRONMENT: 'Deploy',
      ConnectionStrings__MilkshakeDb: 'Host=placeholder.invalid;Database=unused',
    })
    log(`review this file before applying: ${file}`)
    break
  }
  case 'apply': {
    const c = connection()
    await ensurePassword()
    const before = status(c)
    let extra = {}
    if (flag('--seed')) {
      if (before.flavours > 0) {
        fail('Refusing --seed: the database already has flavours. Seeding is for the first deployment only.')
      }
      const settings = JSON.parse(readFileSync(path.join(repo, 'backend', 'src', 'Milkshake.Api', 'appsettings.Development.json'), 'utf8').replace(/^﻿/, ''))
      extra = flatten('SeedData', settings.SeedData)
      log('seeding the demonstration data (first deployment)')
    } else {
      extra = { SeedData__Enabled: 'false' }
    }
    run('dotnet', ['ef', 'database', 'update', ...EF], dotnetEnv(c, extra))
    const after = status(c)
    log(`done: ${before.applied} -> ${after.applied} migrations; latest ${after.latest}`)
    break
  }
  case 'provision-manager': {
    const c = connection()
    const email = option('--email')
    const firstName = option('--first-name')
    if (!email || !firstName) {
      fail('--email and --first-name are required.')
    }
    await ensurePassword()
    let managerPassword = process.env.MILKSHAKE_MANAGER_PASSWORD ?? ''
    if (!managerPassword) {
      managerPassword = await askHidden(`Password for ${email} (hidden): `)
      if (managerPassword !== (await askHidden('Same password again (hidden): '))) {
        fail('The two passwords differ.')
      }
    }
    const env = dotnetEnv(c, { MILKSHAKE_MANAGER_PASSWORD: managerPassword, SeedData__Enabled: 'false' })
    log(`$ dotnet run --project backend/src/Milkshake.Api -- provision-manager --email ${email} --first-name ${firstName}`)
    const result = spawnSync('dotnet', ['run', '--no-launch-profile', '--project', 'backend/src/Milkshake.Api', '--',
      'provision-manager', '--email', email, '--first-name', firstName], { cwd: repo, env, encoding: 'utf8' })
    const output = `${result.stdout ?? ''}${result.stderr ?? ''}`.replaceAll(managerPassword, '***')
    process.stdout.write(masked(output))
    if (result.status !== 0) {
      fail(`provision-manager failed (exit ${result.status})`)
    }
    break
  }
  default:
    console.error('Usage: node tools/deploy/database.mjs status|script|apply|provision-manager (see the comments at the top)')
    process.exit(2)
}
