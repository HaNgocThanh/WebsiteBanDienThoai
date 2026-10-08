import { useEffect, useRef } from 'react'
import { Route, Routes, useLocation } from 'react-router'
import { StorefrontLayout } from './layouts/StorefrontLayout'
import { AdminLayout } from './layouts/AdminLayout'
import { HomePage } from './pages/HomePage'
import { HealthPage } from './pages/HealthPage'
import { AdminOverviewPage, ComingSoonPage, NotFoundPage, ProductsPage } from './pages/FoundationPages'
import { AuthGuard, AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/AuthContext'
import { AccountPage, AuthPage } from './pages/AuthPages'

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
  return <AuthProvider><RouteAccessibility /><Routes>
    <Route element={<StorefrontLayout />}>
      <Route index element={<HomePage />} />
      <Route path="products" element={<ProductsPage />} />
      <Route path="cart" element={<ComingSoonPage title="Giỏ hàng" message="Không gian giỏ hàng sẽ sớm có mặt." />} />
      <Route element={<AuthGuard />}><Route path="account" element={<AccountPage />} /></Route>
      {(['login', 'register', 'forgot-password', 'resend-verification', 'verify-email', 'reset-password'] as const).map(kind => <Route key={kind} path={`auth/${kind}`} element={<AuthPage key={kind} kind={kind} />} />)}
      <Route path="health" element={<HealthPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Route>
    <Route element={<AuthGuard admin />}><Route path="admin" element={<AdminLayout />}>
      <Route index element={<AdminOverviewPage />} />
      <Route path="products" element={<ComingSoonPage title="Danh mục sản phẩm" message="Công cụ quản lý danh mục sẽ sớm có mặt." />} />
      <Route path="orders" element={<ComingSoonPage title="Đơn hàng" message="Công cụ quản lý đơn hàng sẽ sớm có mặt." />} />
      <Route path="*" element={<NotFoundPage admin />} />
    </Route></Route>
  </Routes></AuthProvider>
}
