/**
 * Original milkshake illustration drawn for Milky Shaky (no third-party artwork). Decorative: hidden from screen
 * readers. Colours come from the design tokens; the gentle float stops when the visitor prefers reduced motion.
 */
interface MilkshakeIllustrationProps {
  className?: string
  animated?: boolean
  /** Set when drawn on a strawberry-tint panel, so the accent dots stay visible. */
  onTint?: boolean
}

export function MilkshakeIllustration({ className = '', animated = true, onTint = false }: MilkshakeIllustrationProps) {
  const dot = onTint ? 'fill-surface' : 'fill-strawberry-tint'
  return (
    <svg viewBox="0 0 320 380" aria-hidden="true" focusable="false" className={className}>
      <circle cx="160" cy="200" r="150" className="fill-pistachio-tint" />
      <circle cx="262" cy="86" r="22" className={dot} />
      <circle cx="58" cy="300" r="14" className={dot} />

      <g className={animated ? 'motion-safe:animate-float' : undefined}>
        {/* Straw */}
        <path d="M188 118 L222 28" strokeWidth="14" strokeLinecap="round" className="stroke-raspberry" />
        <path d="M190 112 L223 26" strokeWidth="4" strokeLinecap="round" strokeDasharray="8 10" className="stroke-surface" />

        {/* Glass and shake */}
        <path d="M92 150 H228 L208 342 Q207 350 199 350 H121 Q113 350 112 342 Z" className="fill-surface stroke-chocolate" strokeWidth="5" strokeLinejoin="round" />
        <path d="M100 176 H220 L204 336 Q203 341 198 341 H122 Q117 341 116 336 Z" className="fill-strawberry" />
        <path d="M108 230 H212" strokeWidth="5" strokeLinecap="round" className="stroke-surface" opacity="0.55" />
        <path d="M112 270 H208" strokeWidth="5" strokeLinecap="round" className="stroke-surface" opacity="0.35" />

        {/* Rim drips */}
        <path
          d="M88 150 H232 V160 Q232 172 224 172 Q216 172 216 162 Q216 184 206 184 Q196 184 196 166 Q196 176 186 176 Q176 176 176 162 Q176 196 164 196 Q152 196 152 166 Q152 178 142 178 Q132 178 132 162 Q132 188 120 188 Q108 188 108 166 Q108 172 100 172 Q88 172 88 160 Z"
          className="fill-raspberry"
        />

        {/* Whipped cream */}
        <circle cx="122" cy="132" r="26" className="fill-cream stroke-chocolate" strokeWidth="4" />
        <circle cx="198" cy="132" r="26" className="fill-cream stroke-chocolate" strokeWidth="4" />
        <circle cx="160" cy="112" r="34" className="fill-cream stroke-chocolate" strokeWidth="4" />
        <path d="M100 148 H220" strokeWidth="10" strokeLinecap="round" className="stroke-cream" />

        {/* Berry on top */}
        <circle cx="164" cy="74" r="15" className="fill-raspberry stroke-chocolate" strokeWidth="4" />
        <path d="M166 60 Q172 46 184 44" strokeWidth="4" strokeLinecap="round" fill="none" className="stroke-pistachio-deep" />

        {/* Sprinkles */}
        <rect x="132" y="98" width="12" height="5" rx="2.5" transform="rotate(-30 138 100)" className="fill-pistachio-deep" />
        <rect x="178" y="104" width="12" height="5" rx="2.5" transform="rotate(25 184 106)" className="fill-raspberry" />
        <rect x="112" y="122" width="12" height="5" rx="2.5" transform="rotate(15 118 124)" className="fill-chocolate" />
        <rect x="200" y="124" width="12" height="5" rx="2.5" transform="rotate(-20 206 126)" className="fill-pistachio-deep" />
        <rect x="152" y="128" width="12" height="5" rx="2.5" transform="rotate(40 158 130)" className="fill-raspberry" />
      </g>
    </svg>
  )
}
