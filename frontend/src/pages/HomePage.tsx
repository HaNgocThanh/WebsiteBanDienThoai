import { Link } from 'react-router'
import { HomeCatalog } from './StoreCatalog'
import { lazy, Suspense } from 'react'
const PhoneBrands = lazy(() => import('./PhoneBrands').then(module => ({ default: module.PhoneBrands })))

export function HomePage() {
  return <>
    <section className="home-hero">
      <div className="hero-copy"><span className="eyebrow"><span className="tiny-dot" /> CỬA HÀNG ĐIỆN THOẠI PHONESTORE</span><h1>Điện thoại <br />phù hợp <em>với bạn.</em></h1><p>Chọn điện thoại theo hãng, loại máy và cấu hình. Xem giá, phiên bản và lượng hàng còn trước khi đặt mua.</p><Link to="/products" className="button">Khám phá điện thoại <span aria-hidden="true">↗</span></Link><span className="hero-caption">Điện thoại của bạn. Câu chuyện của bạn.</span></div>
      <div className="phone-art" aria-hidden="true"><div className="art-orbit" /><div className="art-label">KẾT NỐI THEO CÁCH CỦA BẠN</div><div className="phone-back"><div className="camera-block"><i /><i /><i /><b /></div><span className="phone-logo">P.</span></div><div className="phone-front"><div className="phone-island" /><div className="phone-screen"><span>hello.</span><div className="screen-orbit" /><small>một ngày mới</small></div></div><small className="illustration-label">Hình minh họa</small></div>
    </section>
    <Suspense fallback={<p role="status">Đang tải hãng điện thoại…</p>}><PhoneBrands /></Suspense>
    <section className="home-links" aria-label="Khám phá PhoneStore"><Link to="/products"><span className="tile-number">01</span><div><h2>Chọn điện thoại</h2><p>Lọc theo hãng, loại điện thoại, giá và cấu hình.</p></div><span aria-hidden="true">↗</span></Link><Link to="/account/orders"><span className="tile-number">02</span><div><h2>Đơn hàng của bạn</h2><p>Theo dõi đơn đã đặt và trạng thái giao hàng.</p></div><span aria-hidden="true">↗</span></Link></section>
    <HomeCatalog />
  </>
}
