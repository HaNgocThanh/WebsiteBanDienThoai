import { Link, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { authCommand } from '../services/auth'
import { SubmitButton } from './primitives'
import { useMutation } from '../pages/adminCatalogHooks'
import { ActionNotice } from '../pages/AdminCatalogShared'
export function AdminSessionControls() {
  const auth = useAuth(), navigate = useNavigate(), action = useMutation()
  return <div className="admin-session-actions"><Link className="button button-small button-outline" to="/admin/account">Tài khoản quản trị</Link><SubmitButton type="button" busy={action.busy} onClick={() => void action.run(signal => authCommand('logout', {}, signal), () => { auth.clear(); navigate('/auth/login', { replace: true }) }, '')}>Đăng xuất</SubmitButton><ActionNotice action={action} /></div>
}
