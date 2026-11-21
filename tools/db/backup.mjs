// M14 backup (decision D-12): pg_dump of schema "milkshake" in a postgres:17 container, encrypted on this machine.
//
//   node tools/db/backup.mjs <output-file.msbk>
//
// Environment (set by the GitHub Actions workflow from repository secrets, or by the local E2E drill):
//   PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE, PGSSLMODE (optional)   where to read from (read-only use)
//   BACKUP_PASSPHRASE                                                      encrypts the file (at least 20 characters)
//
// Read-only: pg_dump only reads. The values are passed to the container by name (`-e PGPASSWORD`), never on the
// command line, and are never printed. Writes <file> (encrypted) and <file>.json (size, SHA-256, time; no data).
import { createHash } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { POSTGRES_IMAGE, assertDumpArchive, docker, encrypt, log, passphrase } from './backup-lib.mjs'

const output = process.argv[2]
if (!output) {
  console.error('Usage: node tools/db/backup.mjs <output-file.msbk>')
  process.exit(2)
}

try {
  const secret = passphrase()
  for (const name of ['PGHOST', 'PGUSER', 'PGPASSWORD', 'PGDATABASE']) {
    if (!process.env[name]) {
      throw new Error(`${name} is not set.`)
    }
  }

  const started = Date.now()
  const passThrough = ['PGHOST', 'PGPORT', 'PGUSER', 'PGPASSWORD', 'PGDATABASE', 'PGSSLMODE']
    .filter((name) => process.env[name])
    .flatMap((name) => ['-e', name])
  log(`pg_dump of schema milkshake (${POSTGRES_IMAGE})`)
  const dump = docker(['run', '--rm', '--add-host', 'host.docker.internal:host-gateway', ...passThrough, POSTGRES_IMAGE,
    'pg_dump', '--schema=milkshake', '--format=custom', '--compress=6', '--no-owner', '--no-privileges'])
  assertDumpArchive(dump)

  const file = encrypt(dump, secret)
  mkdirSync(path.dirname(path.resolve(output)), { recursive: true })
  writeFileSync(output, file)
  const meta = {
    createdAtUtc: new Date().toISOString(),
    schema: 'milkshake',
    format: 'pg_dump custom, AES-256-GCM (MSBK1)',
    dumpBytes: dump.length,
    fileBytes: file.length,
    fileSha256: createHash('sha256').update(file).digest('hex'),
    seconds: Math.round((Date.now() - started) / 100) / 10,
  }
  writeFileSync(`${output}.json`, JSON.stringify(meta, null, 2))
  log(`written ${output} (${meta.fileBytes} bytes, sha256 ${meta.fileSha256.slice(0, 16)}…) in ${meta.seconds} s`)
} catch (error) {
  console.error(`[backup] FAILED: ${error instanceof Error ? error.message : String(error)}`)
  process.exit(1)
}
