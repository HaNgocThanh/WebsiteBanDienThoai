import { Link, useSearchParams } from 'react-router'
import { EmptyState } from '../components/primitives'

export function ComingSoonPage({ title, message }: { title: string; message: string }) {
  return <section className="page-section"><span className="eyebrow">PHONESTORE</span><h1>{title}</h1><EmptyState title="Chức năng đang được chuẩn bị" action={<Link className="button button-outline" to="/">Về trang chủ</Link>}><p>{message}</p></EmptyState></section>
}

export function ProductsPage() {
  const [params] = useSearchParams()
  const search = params.get('search')
  return <section className="page-section"><span className="eyebrow">KHÁM PHÁ</span><h1>Điện thoại</h1>{search && <p className="search-query">Bạn đang tìm: <strong>{search}</strong></p>}<EmptyState title="Danh mục đang được chuẩn bị" action={<Link className="button button-outline" to="/">Về trang chủ</Link>}><p>Các sản phẩm sẽ xuất hiện tại đây khi sẵn sàng.</p></EmptyState></section>
}

export function NotFoundPage({ admin = false }: { admin?: boolean }) {
  return <section className="page-section"><span className="eyebrow">404</span><h1>Không tìm thấy trang</h1><EmptyState title="Đường dẫn này không còn ở đây" action={<Link className="button" to={admin ? '/admin' : '/'}>{admin ? 'Về tổng quan' : 'Về trang chủ'}</Link>}><p>Kiểm tra lại đường dẫn hoặc quay về trang chính.</p></EmptyState></section>
}

export function AdminOverviewPage() {
  return <section className="page-section"><span className="eyebrow">QUẢN TRỊ CỬA HÀNG</span><h1>Tổng quan</h1><p className="muted">Không gian quản lý sản phẩm và đơn hàng của PhoneStore.</p><div className="notice">Các công cụ quản trị đang được chuẩn bị. Chưa có thao tác quản lý khả dụng.</div><div className="admin-tiles"><Link to="/admin/products"><span className="tile-number">01</span><h2>Danh mục sản phẩm</h2><p>Đi đến khu vực sản phẩm.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/orders"><span className="tile-number">02</span><h2>Đơn hàng</h2><p>Đi đến khu vực đơn hàng.</p><span aria-hidden="true">↗</span></Link></div></section>
}
