import { SePayResultPage } from './pages/SePayResultPage'
import { AdminAccountPage } from './pages/AdminAccount'
import { OrderListPage, OrderDetailPage, NotificationsPage } from './pages/OrderManagement'
import { useEffect, useRef } from 'react'
import { Route, Routes, useLocation } from 'react-router'
import { StorefrontLayout } from './layouts/StorefrontLayout'
import { AdminLayout } from './layouts/AdminLayout'
import { HomePage } from './pages/HomePage'
import { HealthPage } from './pages/HealthPage'
import { AdminOverviewPage, NotFoundPage } from './pages/FoundationPages'
import { AuthGuard, AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/AuthContext'
import { AccountPage, AuthPage } from './pages/AuthPages'
import { AdminLookupsPage, AdminProductsPage } from './pages/AdminCatalogList'
import { AdminProductPage } from './pages/AdminCatalogProduct'
import { AdminInventoryDetailPage, AdminInventoryPage } from './pages/AdminInventory'
import { StoreProductPage, StoreProductsPage } from './pages/StoreCatalog'
import { CartProvider } from './cart/CartProvider'
import { CartPage } from './pages/CartPage'
import { CheckoutPage, CheckoutSuccessPage } from './pages/CheckoutPage'
import { GuestAccessPage, GuestLookupPage, GuestOrderPage } from './pages/GuestOrders'

function RouteAccessibility() {
  const { pathname, search } = useLocation()
  const auth = useAuth()
  const routeKey = pathname + search
  const previous = useRef(routeKey)
  useEffect(() => {
    const main = document.getElementById('main-content')
    const heading = main?.querySelector('h1')?.textContent
    document.title = heading ? `${heading} | PhoneStore` : 'PhoneStore'
    if (previous.current !== routeKey) main?.focus()
    previous.current = routeKey
  }, [routeKey, auth.status])
  return null
}

export default function App() {
  return <AuthProvider><CartProvider><RouteAccessibility /><Routes>
    <Route element={<StorefrontLayout />}>
      <Route index element={<HomePage />} />
      <Route path="products" element={<StoreProductsPage />} />
      <Route path="products/:slug" element={<StoreProductPage />} />
      <Route path="cart" element={<CartPage />} />
      <Route path="checkout" element={<CheckoutPage />} />
      <Route path="checkout/success" element={<CheckoutSuccessPage />} /><Route path="payments/sepay/result" element={<SePayResultPage />} />
      <Route path="guest/lookup" element={<GuestLookupPage />} />
      <Route path="guest/access" element={<GuestAccessPage />} />
      <Route path="guest/order" element={<GuestOrderPage />} />
      <Route element={<AuthGuard />}><Route path="account" element={<AccountPage />} /><Route path="account/orders" element={<OrderListPage />} /><Route path="account/orders/:id" element={<OrderDetailPage />} /><Route path="account/notifications" element={<NotificationsPage />} /></Route>
      {(['login', 'register', 'forgot-password', 'resend-verification', 'verify-email', 'reset-password'] as const).map(kind => <Route key={kind} path={`auth/${kind}`} element={<AuthPage key={kind} kind={kind} />} />)}
      <Route path="health" element={<HealthPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Route>
    <Route element={<AuthGuard admin />}><Route path="admin" element={<AdminLayout />}>
      <Route index element={<AdminOverviewPage />} /><Route path="account" element={<AdminAccountPage />} />
      <Route path="products" element={<AdminProductsPage />} />
      <Route path="products/new" element={<AdminProductPage />} />
      <Route path="products/:id" element={<AdminProductPage />} />
      <Route path="brands" element={<AdminLookupsPage key="brands" kind="brands" />} />
      <Route path="categories" element={<AdminLookupsPage key="categories" kind="categories" />} />
      <Route path="inventory" element={<AdminInventoryPage />} />
      <Route path="inventory/:variantId" element={<AdminInventoryDetailPage />} />
      <Route path="orders" element={<OrderListPage admin />} /><Route path="orders/:id" element={<OrderDetailPage admin />} /><Route path="notifications" element={<NotificationsPage admin />} />
      <Route path="*" element={<NotFoundPage admin />} />
    </Route></Route>
  </Routes></CartProvider></AuthProvider>
}
