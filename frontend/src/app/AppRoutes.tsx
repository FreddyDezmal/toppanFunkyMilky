import { Route, Routes } from 'react-router'
import { AppLayout } from '../components/layout/AppLayout'
import { RequireAuth } from '../features/auth/RequireAuth'
import { ROLES } from '../features/auth/types'
import { ManagementLayout } from '../features/management/ManagementLayout'
import { OrderLayout } from '../features/ordering/OrderLayout'
import { AccountPage } from '../pages/AccountPage'
import { HomePage } from '../pages/HomePage'
import { LoginPage } from '../pages/LoginPage'
import { ManagerPage } from '../pages/ManagerPage'
import { CatalogPage } from '../pages/management/CatalogPage'
import { DiscountTiersPage } from '../pages/management/DiscountTiersPage'
import { PricesPage } from '../pages/management/PricesPage'
import { RestaurantsPage } from '../pages/management/RestaurantsPage'
import { SettingsPage } from '../pages/management/SettingsPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { OrderDrinksPage } from '../pages/ordering/OrderDrinksPage'
import { OrderPickupPage } from '../pages/ordering/OrderPickupPage'
import { OrderQuantityPage } from '../pages/ordering/OrderQuantityPage'
import { OrderDetailsPage } from '../pages/orders/OrderDetailsPage'
import { OrderReviewPage } from '../pages/ordering/OrderReviewPage'
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

        <Route element={<RequireAuth role={ROLES.patron} />}>
          <Route path="order" element={<OrderLayout />}>
            <Route index element={<OrderQuantityPage />} />
            <Route path="drinks" element={<OrderDrinksPage />} />
            <Route path="pickup" element={<OrderPickupPage />} />
            <Route path="review" element={<OrderReviewPage />} />
          </Route>
          <Route path="orders/:reference" element={<OrderDetailsPage />} />
        </Route>

        <Route element={<RequireAuth role={ROLES.manager} />}>
          <Route path="manager" element={<ManagementLayout />}>
            <Route index element={<ManagerPage />} />
            <Route path="catalog/:kind" element={<CatalogPage />} />
            <Route path="catalog/:kind/:id/prices" element={<PricesPage />} />
            <Route path="restaurants" element={<RestaurantsPage />} />
            <Route path="settings" element={<SettingsPage />} />
            <Route path="discount-tiers" element={<DiscountTiersPage />} />
          </Route>
        </Route>

        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}