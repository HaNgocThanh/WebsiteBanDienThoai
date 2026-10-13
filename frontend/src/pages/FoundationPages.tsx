import { Link } from 'react-router'
import { EmptyState } from '../components/primitives'

export function ComingSoonPage({ title, message }: { title: string; message: string }) {
  return <section className="page-section"><span className="eyebrow">PHONESTORE</span><h1>{title}</h1><EmptyState title="Chức năng đang được chuẩn bị" action={<Link className="button button-outline" to="/">Về trang chủ</Link>}><p>{message}</p></EmptyState></section>
}

export function NotFoundPage({ admin = false }: { admin?: boolean }) {
  return <section className="page-section"><span className="eyebrow">404</span><h1>Không tìm thấy trang</h1><EmptyState title="Đường dẫn này không còn ở đây" action={<Link className="button" to={admin ? '/admin' : '/'}>{admin ? 'Về tổng quan' : 'Về trang chủ'}</Link>}><p>Kiểm tra lại đường dẫn hoặc quay về trang chính.</p></EmptyState></section>
}
