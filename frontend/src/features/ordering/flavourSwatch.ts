/**
 * Decorative colour for a flavour card, guessed from its name (unknown names get a neutral swatch). Purely visual:
 * the name and price are always written on the card, and the swatch is hidden from screen readers.
 */
const SWATCHES: [RegExp, string][] = [
  [/strawberr|berry|raspberr/i, 'bg-strawberry'],
  [/vanilla|cream/i, 'bg-[#f3e3c3]'],
  [/coffee|mocha|espresso/i, 'bg-[#8b5e3c]'],
  [/oreo|cookie/i, 'bg-[#2f2a28]'],
  [/bar ?one|caramel|toffee/i, 'bg-[#c08a4a]'],
  [/choc/i, 'bg-chocolate'],
  [/banana/i, 'bg-[#f2d16b]'],
  [/mint|pistachio/i, 'bg-pistachio'],
]

export function flavourSwatch(name: string): string {
  return SWATCHES.find(([pattern]) => pattern.test(name))?.[1] ?? 'bg-strawberry-tint'
}
