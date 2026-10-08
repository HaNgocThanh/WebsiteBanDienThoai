import { useRef, useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { Brand } from './StorefrontLayout'

export function AdminLayout() {
  const [open, setOpen] = useState(false)
  const button = useRef<HTMLButtonElement>(null)
  return <div className="admin-shell" onKeyDown={event => { if (event.key === 'Escape' && open) { setOpen(false); button.current?.focus() } }}>
    <a className="skip-link" href="#main-content">Đến nội dung chính</a>
    <aside className="admin-sidebar"><Brand admin />
      <button ref={button} className="menu-button admin-menu-button" aria-expanded={open} aria-controls="admin-nav" aria-label={open ? 'Đóng menu quản trị' : 'Mở menu quản trị'} onClick={() => setOpen(!open)}>{open ? '✕' : '☰'}</button>
      <nav id="admin-nav" className={open ? 'is-open' : ''} aria-label="Điều hướng quản trị">
        <small>KHÔNG GIAN QUẢN LÝ</small><NavLink to="/admin" end onClick={() => setOpen(false)}>Tổng quan</NavLink><NavLink to="/admin/products" onClick={() => setOpen(false)}>Danh mục sản phẩm</NavLink><NavLink to="/admin/brands" onClick={() => setOpen(false)}>Hãng</NavLink><NavLink to="/admin/categories" onClick={() => setOpen(false)}>Danh mục</NavLink><NavLink to="/admin/inventory" onClick={() => setOpen(false)}>Tồn kho</NavLink><NavLink to="/admin/orders" onClick={() => setOpen(false)}>Đơn hàng</NavLink><Link to="/health">Trạng thái kết nối ↗</Link>
      </nav><Link to="/" className="back-store">← Về cửa hàng</Link>
    </aside>
    <div className="admin-body"><header className="admin-header"><span>PhoneStore / Quản trị</span><span className="status-tag">Quản lý danh mục</span></header><main id="main-content" tabIndex={-1} className="admin-main"><Outlet /></main></div>
  </div>
}
