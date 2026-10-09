import { useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { TextField } from '../components/primitives'
import { useAuth } from '../auth/AuthContext'

export function Brand({ admin = false }: { admin?: boolean }) {
  return <Link to={admin ? '/admin' : '/'} className="brand" aria-label={admin ? 'PhoneStore quản trị' : 'PhoneStore trang chủ'}>
    <span className="brand-mark" aria-hidden="true"><span /></span><span>Phone<span className="brand-accent">Store</span>{admin && <small>QUẢN TRỊ</small>}</span>
  </Link>
}

export function StorefrontLayout() {
  const auth = useAuth()
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string>()
  const menuButton = useRef<HTMLButtonElement>(null)
  const navigate = useNavigate()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const query = search.trim()
    if (!query) { setError('Nhập tên điện thoại bạn muốn tìm.'); return }
    setError(undefined)
    setOpen(false)
    navigate(`/products?search=${encodeURIComponent(query)}`)
  }
  function closeWithEscape(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape' && open) { setOpen(false); menuButton.current?.focus() }
  }
  return <div className="storefront" onKeyDown={closeWithEscape}>
    <a className="skip-link" href="#main-content">Đến nội dung chính</a>
    <div className="announcement">Một lựa chọn mới cho chiếc điện thoại của bạn.</div>
    <header className="store-header">
      <div className="header-inner">
        <Brand />
        <form className="search-form" role="search" onSubmit={submit} noValidate>
          <TextField label="Tìm điện thoại" visuallyHiddenLabel type="search" placeholder="Bạn đang tìm điện thoại nào?" value={search} error={error} onChange={event => { setSearch(event.target.value); setError(undefined) }} />
          <button type="submit" aria-label="Tìm kiếm" className="search-button">↗</button>
        </form>
        <div className="header-actions"><Link to="/cart" className="cart-link">Giỏ hàng <span aria-hidden="true">↗</span></Link><Link to={auth.user ? '/account' : '/auth/login'} className="button button-small button-outline">{auth.user ? 'Tài khoản' : 'Đăng nhập'}</Link></div>
        <button ref={menuButton} className="menu-button" aria-expanded={open} aria-controls="store-nav" aria-label={open ? 'Đóng menu' : 'Mở menu'} onClick={() => setOpen(!open)}>{open ? '✕' : '☰'}</button>
      </div>
      <nav id="store-nav" className={`store-nav ${open ? 'is-open' : ''}`} aria-label="Điều hướng cửa hàng">
        <NavLink to="/" end onClick={() => setOpen(false)}>Trang chủ</NavLink><NavLink to="/products" onClick={() => setOpen(false)}>Điện thoại</NavLink><NavLink to="/account" onClick={() => setOpen(false)}>Tài khoản</NavLink>
        <NavLink to="/guest/lookup" onClick={() => setOpen(false)}>Tra cứu đơn</NavLink>
        <Link to="/health" className="nav-utility" onClick={() => setOpen(false)}>Trạng thái kết nối <span aria-hidden="true">↗</span></Link>
      </nav>
    </header>
    <main id="main-content" tabIndex={-1} className="store-main"><Outlet /></main>
    <footer className="store-footer"><div><Brand /><p>Một chiếc điện thoại. Nhiều điều mới.</p></div><nav aria-label="Điều hướng cuối trang"><Link to="/products">Điện thoại</Link><Link to="/account">Tài khoản</Link><Link to="/health">Trạng thái kết nối</Link></nav><small>PhoneStore · Khách hàng &amp; quản trị</small></footer>
  </div>
}
