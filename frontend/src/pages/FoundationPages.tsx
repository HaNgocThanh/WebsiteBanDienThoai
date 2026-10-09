import { Link } from 'react-router'
import { EmptyState } from '../components/primitives'

export function ComingSoonPage({ title, message }: { title: string; message: string }) {
  return <section className="page-section"><span className="eyebrow">PHONESTORE</span><h1>{title}</h1><EmptyState title="Chức năng đang được chuẩn bị" action={<Link className="button button-outline" to="/">Về trang chủ</Link>}><p>{message}</p></EmptyState></section>
}

export function NotFoundPage({ admin = false }: { admin?: boolean }) {
  return <section className="page-section"><span className="eyebrow">404</span><h1>Không tìm thấy trang</h1><EmptyState title="Đường dẫn này không còn ở đây" action={<Link className="button" to={admin ? '/admin' : '/'}>{admin ? 'Về tổng quan' : 'Về trang chủ'}</Link>}><p>Kiểm tra lại đường dẫn hoặc quay về trang chính.</p></EmptyState></section>
}

export function AdminOverviewPage() {
  return <section className="page-section"><span className="eyebrow">QUẢN TRỊ CỬA HÀNG</span><h1>Tổng quan</h1><p className="muted">Quản lý danh mục, tồn kho, đơn hàng và thông báo của cửa hàng.</p><div className="admin-tiles"><Link to="/admin/products"><span className="tile-number">01</span><h2>Danh mục sản phẩm</h2><p>Tạo, chỉnh sửa và ẩn sản phẩm.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/brands"><span className="tile-number">02</span><h2>Hãng</h2><p>Quản lý các hãng điện thoại.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/categories"><span className="tile-number">03</span><h2>Danh mục</h2><p>Quản lý các nhóm sản phẩm.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/inventory"><span className="tile-number">04</span><h2>Tồn kho</h2><p>Nhập hàng, điều chỉnh và xem lịch sử kho.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/orders"><span className="tile-number">05</span><h2>Đơn hàng cửa hàng</h2><p>Xem tất cả đơn và ghi chú nội bộ.</p><span aria-hidden="true">↗</span></Link><Link to="/admin/notifications"><span className="tile-number">06</span><h2>Thông báo quản trị</h2><p>Theo dõi sự kiện đơn hàng của cửa hàng.</p><span aria-hidden="true">↗</span></Link></div></section>
}
