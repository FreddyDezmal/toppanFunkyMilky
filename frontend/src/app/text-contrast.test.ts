import { describe, expect, it } from 'vitest'

// M14 DEF-10: grey text lighter than slate-600 fails WCAG AA (4.5:1) on the pink page background (brand-50, #fdf2f8):
// slate-500 measured 4.36:1. axe only saw it when it scanned during a brief loading message, so this check reads the
// component sources instead of relying on timing. Text must be slate-600 or darker.
const sources = import.meta.glob(['../**/*.tsx', '!../**/*.test.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>
const tooLight = /\btext-(?:slate|gray|zinc|neutral|stone)-(?:50|100|200|300|400|500)\b/g

describe('text contrast (DEF-10)', () => {
  it('reads the component sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(30)
  })

  it('uses no grey text lighter than slate-600', () => {
    const found = Object.entries(sources).flatMap(([file, text]) => (text.match(tooLight) ?? []).map((cls) => `${file}: ${cls}`))
    expect(found).toEqual([])
  })
})
