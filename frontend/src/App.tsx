import { SurveyListPage, SurveyPage } from './pages/Surveys'
const AdminSurveyPage = lazy(() => import('./pages/AdminSurveys').then(module => ({ default: module.AdminSurveyPage })))
const SurveyResultsPage = lazy(() => import('./pages/AdminSurveys').then(module => ({ default: module.SurveyResultsPage })))
import { AdminReviewsPage } from './pages/Reviews'
import { AdminVouchersPage } from './pages/AdminVouchers'
import { AdminPromotionsPage, AdminPromotionPage } from './pages/AdminPromotions'
import { AdminCustomerPage } from './pages/AdminCustomerPage'
import { MembershipPage, AdminCustomersPage } from './pages/MembershipPage'
import { SePayResultPage } from './pages/SePayResultPage'
import { AdminAccountPage } from './pages/AdminAccount'
import { OrderListPage, OrderDetailPage, NotificationsPage } from './pages/OrderManagement'
import { lazy, Suspense, useEffect, useRef } from 'react'
import { Route, Routes, useLocation } from 'react-router'
import { StorefrontLayout } from './layouts/StorefrontLayout'
import { AdminLayout } from './layouts/AdminLayout'
import { HomePage } from './pages/HomePage'
import { HealthPage } from './pages/HealthPage'
import { NotFoundPage } from './pages/FoundationPages'
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
const AdminOverviewPage = lazy(() => import('./pages/AdminOverview'))

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
      <Route element={<AuthGuard />}><Route path="account" element={<AccountPage />} /><Route path="account/surveys" element={<SurveyListPage />} /><Route path="account/surveys/:id" element={<SurveyPage />} /><Route path="account/membership" element={<MembershipPage />} /><Route path="account/orders" element={<OrderListPage />} /><Route path="account/orders/:id" element={<OrderDetailPage />} /><Route path="account/notifications" element={<NotificationsPage />} /></Route>
      {(['login', 'register', 'forgot-password', 'resend-verification', 'verify-email', 'reset-password'] as const).map(kind => <Route key={kind} path={`auth/${kind}`} element={<AuthPage key={kind} kind={kind} />} />)}
      <Route path="health" element={<HealthPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Route>
    <Route element={<AuthGuard admin />}><Route path="admin" element={<AdminLayout />}>
      <Route index element={<Suspense fallback={<p role="status">Đang tải tổng quan…</p>}><AdminOverviewPage /></Suspense>} /><Route path="account" element={<AdminAccountPage />} />
      <Route path="surveys" element={<SurveyListPage admin />} /><Route path="surveys/new" element={<Suspense fallback={<p role="status">Đang tải quản trị khảo sát…</p>}><AdminSurveyPage create /></Suspense>} /><Route path="surveys/:id" element={<Suspense fallback={<p role="status">Đang tải quản trị khảo sát…</p>}><AdminSurveyPage /></Suspense>} /><Route path="surveys/:id/results" element={<Suspense fallback={<p role="status">Đang tải kết quả khảo sát…</p>}><SurveyResultsPage /></Suspense>} /><Route path="reviews" element={<AdminReviewsPage />} /><Route path="vouchers" element={<AdminVouchersPage />} /><Route path="promotions" element={<AdminPromotionsPage />} /><Route path="promotions/new" element={<AdminPromotionPage />} /><Route path="promotions/:id" element={<AdminPromotionPage />} /><Route path="customers" element={<AdminCustomersPage />} /><Route path="customers/:userId" element={<AdminCustomerPage />} /><Route path="products" element={<AdminProductsPage />} />
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
