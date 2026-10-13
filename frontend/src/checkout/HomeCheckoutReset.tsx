import { useEffect } from 'react'
import { useLocation } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { orders } from '../services/orders'
import { isCheckoutConfirmed, readPendingCheckout, savePendingCheckout } from './pending'

// Returning home starts a new shopping journey only after the server confirms the old order exists.
export function HomeCheckoutReset() {
  const location = useLocation(), auth = useAuth()
  useEffect(() => {
    if (location.pathname !== '/' || auth.status === 'loading' || auth.status === 'error') return
    const controller = new AbortController()
    try {
      const operation = readPendingCheckout()
      if (!operation) return
      if (isCheckoutConfirmed(operation.key)) { savePendingCheckout(null); return }
      void orders.result(operation.key, controller.signal).then(result => {
        if (controller.signal.aborted || !result.order) return
        // Do not erase a newer operation started while this lookup was in flight.
        if (readPendingCheckout()?.key === operation.key) savePendingCheckout(null)
      }).catch(() => {
        // Unknown delivery/owner/storage failures retain recovery and idempotency, never assume no order.
      })
    } catch { /* Keep unreadable storage for the checkout's explicit recovery UI. */ }
    return () => controller.abort()
  }, [location.pathname, location.key, auth.status, auth.user?.userId])
  return null
}
