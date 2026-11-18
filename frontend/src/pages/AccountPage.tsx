import { useAuth } from '../features/auth/authContext'

export function AccountPage() {
  const { user } = useAuth()
  if (!user) {
    return null
  }

  return (
    <section className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-2xl font-bold tracking-tight">My account</h1>
      <p className="mt-1 text-slate-600">Hello, {user.firstName}.</p>
      <dl className="mt-6 divide-y divide-slate-100 text-sm">
        <div className="flex justify-between py-3">
          <dt className="font-medium text-slate-900">Email</dt>
          <dd className="text-slate-600">{user.email}</dd>
        </div>
        <div className="flex justify-between py-3">
          <dt className="font-medium text-slate-900">Mobile number</dt>
          <dd className="text-slate-600">{user.mobileNumber ?? 'Not provided'}</dd>
        </div>
        <div className="flex justify-between py-3">
          <dt className="font-medium text-slate-900">Roles</dt>
          <dd className="text-slate-600">{user.roles.join(', ')}</dd>
        </div>
      </dl>
    </section>
  )
}