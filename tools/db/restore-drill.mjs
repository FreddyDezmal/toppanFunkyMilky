// M14 restore drill (decision D-12, AC-14.4 "tested once"): restores a backup into a DISPOSABLE PostgreSQL 17 container
// that this script creates and removes. It never connects to any other database, so it cannot overwrite live data.
//
//   node tools/db/restore-drill.mjs <backup.msbk> [--expect-counts <counts.json>] [--report <report.json>]
//
// Environment: BACKUP_PASSPHRASE.
// Checks: the file decrypts and is a pg_dump archive; pg_restore succeeds with --exit-on-error; the 18 integrity checks
// (tools/db/integrity-checks.sql) report 0; the audit tables still refuse changes (append-only triggers restored);
// the migration history is present; optionally every table's row count equals the expected counts.
// The report holds counts and check results only, never row content.
import { randomBytes } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { POSTGRES_IMAGE, TABLE_COUNTS_SQL, assertDumpArchive, decrypt, docker, log, parseCounts, passphrase } from './backup-lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const args = process.argv.slice(2)
const option = (name) => {
  const index = args.indexOf(name)
  return index >= 0 ? args[index + 1] : undefined
}
const input = args.find((arg, index) => !arg.startsWith('--') && !['--expect-counts', '--report'].includes(args[index - 1]))
if (!input) {
  console.error('Usage: node tools/db/restore-drill.mjs <backup.msbk> [--expect-counts <counts.json>] [--report <report.json>]')
  process.exit(2)
}

const container = `milkshake-restore-drill-${process.pid}`
const report = { backup: path.basename(input), startedAtUtc: new Date().toISOString(), checks: {} }

/** Runs SQL in the drill database. With allowError, returns { error: stderr } instead of throwing. */
function psql(sql, { allowError = false } = {}) {
  try {
    return docker(['exec', '-i', container, 'psql', '-U', 'postgres', '-d', 'drill', '-v', 'ON_ERROR_STOP=1', '-A', '-F', '|', '-t'],
      { input: sql, encoding: 'utf8', stdio: ['pipe', 'pipe', allowError ? 'pipe' : 'inherit'] })
  } catch (error) {
    if (allowError) {
      return { error: String(error?.stderr ?? error) }
    }
    throw error
  }
}

async function main() {
  const dump = decrypt(readFileSync(input), passphrase())
  assertDumpArchive(dump)
  report.checks.decrypted = true

  log(`starting disposable PostgreSQL (${POSTGRES_IMAGE}) as ${container}; no port is published`)
  docker(['run', '-d', '--rm', '--name', container, '-e', `POSTGRES_PASSWORD=${randomBytes(18).toString('base64url')}`,
    '-e', 'POSTGRES_DB=drill', POSTGRES_IMAGE], { stdio: ['ignore', 'ignore', 'inherit'] })
  const until = Date.now() + 60_000
  for (;;) {
    try {
      // pg_isready can answer during the image's initialisation restart, so also run a real query.
      docker(['exec', container, 'psql', '-U', 'postgres', '-d', 'drill', '-c', 'SELECT 1'], { stdio: 'ignore' })
      break
    } catch {
      if (Date.now() > until) {
        throw new Error('The disposable PostgreSQL did not start within 60 s.')
      }
      await new Promise((resolve) => setTimeout(resolve, 1000))
    }
  }

  const started = Date.now()
  docker(['exec', '-i', container, 'pg_restore', '-U', 'postgres', '-d', 'drill', '--no-owner', '--no-privileges', '--exit-on-error'],
    { input: dump, stdio: ['pipe', 'inherit', 'inherit'] })
  report.restoreSeconds = Math.round((Date.now() - started) / 100) / 10
  report.checks.restored = true
  log(`restored in ${report.restoreSeconds} s`)

  // 1. Integrity checks (read-only).
  const integrity = psql(readFileSync(path.join(here, 'integrity-checks.sql'), 'utf8'))
  const results = parseCounts(integrity)
  report.integrity = results
  const violations = Object.entries(results).filter(([, count]) => count > 0)
  report.checks.integrityChecks = Object.keys(results).length
  report.checks.integrityViolations = violations.length
  log(violations.length === 0
    ? `integrity: ${Object.keys(results).length} checks, no violations`
    : `integrity FAILED: ${violations.map(([name, count]) => `${name}=${count}`).join(', ')}`)

  // 2. Row counts.
  report.counts = parseCounts(psql(TABLE_COUNTS_SQL))
  log(`tables: ${Object.keys(report.counts).length}; rows: ${Object.values(report.counts).reduce((a, b) => a + b, 0)}`)
  const expectedFile = option('--expect-counts')
  let countMismatches = []
  if (expectedFile) {
    const expected = JSON.parse(readFileSync(expectedFile, 'utf8'))
    const tables = new Set([...Object.keys(expected), ...Object.keys(report.counts)])
    countMismatches = [...tables].filter((table) => expected[table] !== report.counts[table])
      .map((table) => `${table}: expected ${expected[table] ?? 'missing'}, restored ${report.counts[table] ?? 'missing'}`)
    report.checks.countsMatchSource = countMismatches.length === 0
    log(countMismatches.length === 0 ? 'row counts match the source exactly' : `row counts DIFFER:\n  ${countMismatches.join('\n  ')}`)
  }

  // 3. The audit trail is still append-only after the restore (its triggers came back with it).
  const triggers = Number(psql(`SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'milkshake' AND NOT t.tgisinternal;`).trim())
  report.checks.auditTriggers = triggers
  let auditImmutable = 'not tested (no audit rows)'
  if ((report.counts.audit_logs ?? 0) > 0) {
    const attempt = psql(`BEGIN; UPDATE milkshake.audit_logs SET action = action
      WHERE id = (SELECT min(id) FROM milkshake.audit_logs); ROLLBACK;`, { allowError: true })
    // Only the trigger's own message counts as a refusal; any other error means the check itself is broken.
    auditImmutable = typeof attempt === 'string' ? 'ALLOWED'
      : attempt.error.includes('append-only') ? 'refused' : `ERROR (${attempt.error.split('\n')[0]})`
  }
  report.checks.auditUpdate = auditImmutable
  log(`audit triggers: ${triggers}; changing an audit row: ${auditImmutable}`)

  // 4. Migration history (latest applied migration).
  const latest = psql('SELECT * FROM milkshake.__ef_migrations_history ORDER BY 1 DESC LIMIT 1;').trim().split('|')[0]
  report.latestMigration = latest
  log(`latest migration: ${latest}`)

  const ok = violations.length === 0 && countMismatches.length === 0 && triggers >= 4 && !/^(ALLOWED|ERROR)/.test(auditImmutable) && Boolean(latest)
  report.result = ok ? 'PASSED' : 'FAILED'
  return ok
}

let ok = false
try {
  ok = await main()
} catch (error) {
  report.result = 'FAILED'
  report.error = error instanceof Error ? error.message : String(error)
  console.error(`[backup] restore drill FAILED: ${report.error}`)
} finally {
  try {
    docker(['rm', '-f', container], { stdio: 'ignore' })
  } catch {
    // never started
  }
  report.finishedAtUtc = new Date().toISOString()
  const reportFile = option('--report')
  if (reportFile) {
    writeFileSync(reportFile, JSON.stringify(report, null, 2))
  }
}
log(`restore drill ${ok ? 'PASSED' : 'FAILED'}`)
process.exit(ok ? 0 : 1)
