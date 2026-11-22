import { Link } from 'react-router'
import { buttonClasses } from '../components/ui/buttonClasses'
import { Card } from '../components/ui/Card'
import { Icon } from '../components/ui/Icon'
import { PageHeader } from '../components/ui/PageHeader'
import { hasRole, useAuth } from '../features/auth/authContext'
import { ROLES } from '../features/auth/types'

export function AccountPage() {
  const { user } = useAuth()
  if (!user) {
    return null
  }
  const isPatron = hasRole(user, ROLES.patron)
  const isManager = hasRole(user, ROLES.manager)

  return (
    <div className="mx-auto max-w-3xl space-y-8">
      <PageHeader title="My account" description={`Hello, ${user.firstName}.`} />

      <Card as="section" aria-labelledby="details-heading">
        <h2 id="details-heading" className="flex items-center gap-2 text-xl font-semibold text-chocolate">
          <Icon name="user" className="size-5 text-raspberry" />
          Your details
        </h2>
        <dl className="mt-4 divide-y divide-line text-sm">
          <div className="flex flex-col gap-1 py-3 sm:flex-row sm:justify-between">
            <dt className="font-semibold text-chocolate">Email</dt>
            <dd className="break-all text-warm-grey">{user.email}</dd>
          </div>
          <div className="flex flex-col gap-1 py-3 sm:flex-row sm:justify-between">
            <dt className="font-semibold text-chocolate">Mobile number</dt>
            <dd className="text-warm-grey">{user.mobileNumber ?? 'Not provided'}</dd>
          </div>
          <div className="flex flex-col gap-1 py-3 sm:flex-row sm:justify-between">
            <dt className="font-semibold text-chocolate">Roles</dt>
            <dd className="text-warm-grey">{user.roles.join(', ')}</dd>
          </div>
        </dl>
      </Card>

      {(isPatron || isManager) && (
        <div className="flex flex-col gap-3 sm:flex-row">
          {isPatron && (
            <>
              <Link to="/order" className={buttonClasses()}>
                Build Your Milkshake
                <Icon name="arrow-right" />
              </Link>
              <Link to="/orders" className={buttonClasses({ variant: 'secondary' })}>
                My orders
              </Link>
            </>
          )}
          {isManager && (
            <Link to="/manager" className={buttonClasses({ variant: isPatron ? 'secondary' : 'primary' })}>
              Go to management
            </Link>
          )}
        </div>
      )}
    </div>
  )
}
