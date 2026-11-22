import { CATALOG_LABELS, isCatalogKind } from '../features/management/types'

const SITE = 'Milky Shaky'

const EXACT: Record<string, string> = {
  '/': 'Build your milkshake',
  '/login': 'Log in',
  '/register': 'Create your account',
  '/account': 'My account',
  '/order': 'How many milkshakes? – Order',
  '/order/drinks': 'Choose your milkshakes – Order',
  '/order/pickup': 'Where and when? – Order',
  '/order/review': 'Review your order – Order',
  '/orders': 'My orders',
  '/manager': 'Overview – Management',
  '/manager/orders': 'Orders – Management',
  '/manager/emails': 'Emails – Management',
  '/manager/reports': 'Reports – Management',
  '/manager/restaurants': 'Restaurants – Management',
  '/manager/settings': 'Settings – Management',
  '/manager/discount-tiers': 'Discount tiers – Management',
}

/**
 * The browser tab title for a path (WCAG 2.4.2): what the page is, then the site name, e.g. "My orders – Milky Shaky".
 * Unknown paths get the not-found title, as the router shows the not-found page for them.
 */
export function routeTitle(pathname: string): string {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname
  const exact = EXACT[path]
  if (exact) {
    return `${exact} – ${SITE}`
  }
  const order = /^\/orders\/([^/]+)$/.exec(path)
  if (order?.[1]) {
    return `Order ${decodeURIComponent(order[1])} – ${SITE}`
  }
  const catalog = /^\/manager\/catalog\/([^/]+)(\/\d+\/prices)?$/.exec(path)
  if (catalog?.[1] && isCatalogKind(catalog[1])) {
    const section = CATALOG_LABELS[catalog[1]].title
    return `${catalog[2] ? `Prices – ${section}` : section} – Management – ${SITE}`
  }
  return `Page not found – ${SITE}`
}
