import { describe, expect, it } from 'vitest'

// M14 DEF-10: grey text lighter than slate-600 fails WCAG AA (4.5:1) on the old pink page background (#fdf2f8):
// slate-500 measured 4.36:1. axe only saw it when it scanned during a brief loading message, so this check reads the
// component sources instead of relying on timing. Text must be slate-600 or darker.
const sources = import.meta.glob(['../**/*.tsx', '!../**/*.test.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>
const tooLight = /\btext-(?:slate|gray|zinc|neutral|stone)-(?:50|100|200|300|400|500)\b/g
// Redesign tokens that are decoration only: strawberry 1.8:1 and pistachio 1.5:1 on cream, line-strong 3.7:1 (enough
// for a border, not for text).
// The earlier brand-* colour names were removed in the redesign (Stage 7); using one again would silently do nothing.
const removedBrand = /\b(?:bg|text|border|ring|outline|from|to|divide|fill|stroke)-brand-\d+\b/g
const decorationOnly = /\btext-(?:strawberry(?:-tint|-wash)?|pistachio(?:-tint)?|line(?:-strong)?|cream)(?![\w-])/g

describe('text contrast (DEF-10)', () => {
  it('reads the component sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(30)
  })

  it('uses no grey text lighter than slate-600', () => {
    const found = Object.entries(sources).flatMap(([file, text]) => (text.match(tooLight) ?? []).map((cls) => `${file}: ${cls}`))
    expect(found).toEqual([])
  })

  it('uses no decoration-only colour for text', () => {
    const found = Object.entries(sources).flatMap(([file, text]) =>
      (text.match(decorationOnly) ?? []).map((cls) => `${file}: ${cls}`),
    )
    expect(found).toEqual([])
  })

  it('uses none of the removed brand-* colour names', () => {
    const found = Object.entries(sources).flatMap(([file, text]) => (text.match(removedBrand) ?? []).map((cls) => `${file}: ${cls}`))
    expect(found).toEqual([])
  })
})
