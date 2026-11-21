// Shared helpers for the M14 backup and restore drill (decision D-12). No secrets are ever printed: connection
// settings reach pg_dump only as environment variables passed by name to `docker run -e NAME`.
import { createCipheriv, createDecipheriv, randomBytes, scryptSync } from 'node:crypto'
import { execFileSync } from 'node:child_process'

export const POSTGRES_IMAGE = 'postgres:17'
const MAGIC = Buffer.from('MSBK1\n', 'ascii')
const SCRYPT = { N: 2 ** 15, r: 8, p: 1, maxmem: 64 * 1024 * 1024 }

/** The passphrase from BACKUP_PASSPHRASE; refuses a short one. */
export function passphrase() {
  const value = process.env.BACKUP_PASSPHRASE ?? ''
  if (value.length < 20) {
    throw new Error('BACKUP_PASSPHRASE must be set and at least 20 characters long.')
  }
  return value
}

/** AES-256-GCM with a key derived by scrypt from the passphrase. Layout: MAGIC | salt(16) | iv(12) | data | tag(16). */
export function encrypt(plain, secret) {
  const salt = randomBytes(16)
  const iv = randomBytes(12)
  const cipher = createCipheriv('aes-256-gcm', scryptSync(secret, salt, 32, SCRYPT), iv)
  const data = Buffer.concat([cipher.update(plain), cipher.final()])
  return Buffer.concat([MAGIC, salt, iv, data, cipher.getAuthTag()])
}

/** Reverses encrypt(); fails if the file was changed or the passphrase is wrong. */
export function decrypt(file, secret) {
  if (file.length < MAGIC.length + 44 || !file.subarray(0, MAGIC.length).equals(MAGIC)) {
    throw new Error('Not a Milky Shaky backup file (MSBK1).')
  }
  let offset = MAGIC.length
  const salt = file.subarray(offset, (offset += 16))
  const iv = file.subarray(offset, (offset += 12))
  const data = file.subarray(offset, file.length - 16)
  const decipher = createDecipheriv('aes-256-gcm', scryptSync(secret, salt, 32, SCRYPT), iv)
  decipher.setAuthTag(file.subarray(file.length - 16))
  try {
    return Buffer.concat([decipher.update(data), decipher.final()])
  } catch {
    throw new Error('The backup could not be decrypted: wrong passphrase, or the file is damaged.')
  }
}

/** A pg_dump custom-format archive starts with "PGDMP". */
export function assertDumpArchive(buffer) {
  if (buffer.length < 5 || buffer.subarray(0, 5).toString('ascii') !== 'PGDMP') {
    throw new Error('The data is not a pg_dump custom-format archive.')
  }
}

export function log(message) {
  console.log(`[backup] ${message}`)
}

/** Runs docker without a shell; returns stdout (Buffer or string). Stderr is passed through. */
export function docker(args, options = {}) {
  return execFileSync('docker', args, { stdio: ['pipe', 'pipe', 'inherit'], maxBuffer: 1024 * 1024 * 1024, ...options })
}

/**
 * Row counts of every table in schema milkshake, as one query (no table names hard-coded). Counts only: never any
 * row content, so the result can be logged and kept next to a backup.
 */
export const TABLE_COUNTS_SQL = `
SELECT table_name || '|' || (xpath('/row/c/text()',
         query_to_xml(format('SELECT count(*) AS c FROM milkshake.%I', table_name), false, true, '')))[1]::text
FROM information_schema.tables
WHERE table_schema = 'milkshake' AND table_type = 'BASE TABLE'
ORDER BY table_name;`

export function parseCounts(output) {
  return Object.fromEntries(
    output
      .split(/\r?\n/)
      .filter((line) => line.includes('|'))
      .map((line) => {
        const [table, count] = line.trim().split('|')
        return [table, Number(count)]
      }),
  )
}
