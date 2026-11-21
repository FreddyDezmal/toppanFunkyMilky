/**
 * Security response headers for the website (M13 SEC; OWASP ZAP findings 10020, 10021, 10038, 10063 and 90004).
 *
 * ONE source of truth: `vite preview` (the local E2E stack) serves them now, and the production host must serve the
 * same set (M14 deployment checklist), plus Strict-Transport-Security once the site has its HTTPS domain:
 *   Strict-Transport-Security: max-age=31536000
 * The API sets its own headers (backend/src/Milkshake.Api/Security/SecurityHeaders.cs).
 *
 * The policy allows only this site's own files: the built page loads one script, one stylesheet and its icons from
 * the same origin, and calls the API on the same origin. Paystack checkout is a full-page navigation, which a
 * Content-Security-Policy does not restrict. React's `style={{…}}` sets styles through the DOM, which `style-src
 * 'self'` allows.
 */
export const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "font-src 'self'",
  "connect-src 'self'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ')

export const SITE_SECURITY_HEADERS: Record<string, string> = {
  'Content-Security-Policy': CONTENT_SECURITY_POLICY,
  'X-Frame-Options': 'DENY',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'Cross-Origin-Embedder-Policy': 'require-corp',
}
