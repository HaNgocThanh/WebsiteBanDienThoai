import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { orderManagement } from '../services/orderManagement'
import { useResource } from '../pages/adminCatalogHooks'
export function NotificationLink({ admin = false }: { admin?: boolean }) {
  const auth = useAuth(), { pathname } = useLocation(), [revision, setRevision] = useState(0)
  useEffect(() => { const change = () => setRevision(r => r + 1); window.addEventListener('notifications-changed', change); return () => window.removeEventListener('notifications-changed', change) }, [])
  const resource = useResource((auth.user?.userId ?? '') + ':' + admin + ':' + pathname + ':' + revision, signal => auth.status === 'authenticated' ? orderManagement.notices(new URLSearchParams({ pageSize: '1' }), signal, admin) : Promise.resolve(null))
  if (!auth.user) return null
  return <Link to={admin ? '/admin/notifications' : '/account/notifications'}>{admin ? 'Thông báo quản trị' : 'Thông báo'}{resource.data ? ' (' + resource.data.unreadCount + ')' : ''}</Link>
}
