import { Link } from 'react-router'
import { useAuth } from '../auth/AuthContext'
export function AdminAccountPage() {
  const auth = useAuth()
  return <section className="page-section"><span className="eyebrow">QUẢN TRỊ CỬA HÀNG</span><h1>Tài khoản quản trị</h1><section className="catalog-panel"><h2>Thông tin đăng nhập</h2><p>Họ tên: {auth.user?.fullName}</p><p>Email: {auth.user?.email}</p><p>Vai trò: Quản trị viên</p><Link className="button button-outline" to="/auth/forgot-password">Đặt lại mật khẩu</Link></section></section>
}
