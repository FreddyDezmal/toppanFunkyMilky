import { Route, Routes } from 'react-router'
import { AppLayout } from '../components/layout/AppLayout'
import { RequireAuth } from '../features/auth/RequireAuth'
import { ROLES } from '../features/auth/types'
import { AccountPage } from '../pages/AccountPage'
import { HomePage } from '../pages/HomePage'
import { LoginPage } from '../pages/LoginPage'
import { ManagerPage } from '../pages/ManagerPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { RegisterPage } from '../pages/RegisterPage'

/** All routes. Separate from the router so tests can render it inside a MemoryRouter. */
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<HomePage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />

        <Route element={<RequireAuth />}>
          <Route path="account" element={<AccountPage />} />
        </Route>

        <Route element={<RequireAuth role={ROLES.manager} />}>
          <Route path="manager" element={<ManagerPage />} />
        </Route>

        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}