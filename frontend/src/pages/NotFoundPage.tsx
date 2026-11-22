import { Link } from 'react-router'
import { MilkshakeIllustration } from '../components/illustrations/MilkshakeIllustration'
import { buttonClasses } from '../components/ui/buttonClasses'
import { Card } from '../components/ui/Card'

export function NotFoundPage() {
  return (
    <Card as="section" className="mx-auto max-w-xl text-center">
      <MilkshakeIllustration className="mx-auto w-36" animated={false} />
      <h1 className="mt-6 text-3xl font-semibold text-chocolate">Page not found</h1>
      <p className="mt-2 text-warm-grey">This page doesn’t exist or has moved.</p>
      <Link to="/" className={buttonClasses({ className: 'mt-6' })}>
        Back to the home page
      </Link>
    </Card>
  )
}
