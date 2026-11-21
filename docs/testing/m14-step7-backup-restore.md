# M14 Step 7: backup and restore evidence (2026-11-21)

Decision D-12: Supabase Free has no platform backups, so the project backs itself up with GitHub Actions and proves
every backup by restoring it. Nothing touched Supabase in this step: the evidence uses the disposable E2E database.

| Item | Where |
| --- | --- |
| Backup tool | `tools/db/backup.mjs`: read-only `pg_dump --schema=milkshake` in `postgres:17`; AES-256-GCM, key from `BACKUP_PASSPHRASE` (scrypt); connection settings passed to the container by name, never on a command line |
| Restore drill | `tools/db/restore-drill.mjs`: disposable PostgreSQL 17 it creates and removes; decrypt, restore with `--exit-on-error`, 18 integrity checks, append-only audit check, migration history, optional exact row counts; counts-only report |
| Schedule | `.github/workflows/backup.yml`: daily 01:23 UTC and on demand; backup then drill on the runner; encrypted file + report kept 30 days. **Off** until `BACKUP_ENABLED=true` (Step 8) |
| Local drill | `npm run test:e2e:local -- --backup-drill` (with or without `--container`) |
| Procedure | `docs/guides/operations-guide.md` §7 |

## Results

| Run | E2E | Backup | Restore drill |
| --- | --- | --- | --- |
| `2026-11-21T10-25-58-944Z` (`--container --backup-drill`) | 17 passed, 1 failed (DEF-10), 1 not run | 105 KB, 2.3 s | **Passed**: 27 tables, 512 rows equal to source; integrity 18 × 0; 4 audit triggers; audit change refused |
| `2026-11-21T10-42-51-261Z` (`--container --backup-drill`) | 19 of 19 | 107 KB, 3.3 s, sha256 `1a1ebfc4…` | **Passed**: 27 tables, 551 rows equal to source; integrity 18 × 0; 4 audit triggers; audit change refused; latest migration `20261121091600_AddDataProtectionKeys`; restore 1 s |

Further checks on the second run: the encrypted file contains none of `PGDMP`, `milkshake`, `audit_logs` or any email
domain; the report contains no email addresses; the backup and drill logs contain no passwords or keys. Encryption
unit check: round trip succeeds; a wrong passphrase and a single changed byte are both refused.

## Not yet evidenced

The first backup of the live database and its drill on GitHub (Step 8, after the secrets are added and
`BACKUP_ENABLED` is set).
