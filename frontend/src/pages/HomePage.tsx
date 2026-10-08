import { Link } from 'react-router'
import { HomeCatalog } from './StoreCatalog'

export function HomePage() {
  return <>
    <section className="home-hero">
      <div className="hero-copy"><span className="eyebrow"><span className="tiny-dot" /> CHÀO BẠN ĐẾN PHONESTORE</span><h1>Mở ra một <br />điều <em>mới.</em></h1><p>Tìm chiếc điện thoại phù hợp với cách bạn sống, làm việc và kết nối mỗi ngày.</p><Link to="/products" className="button">Khám phá điện thoại <span aria-hidden="true">↗</span></Link><span className="hero-caption">Điện thoại của bạn. Câu chuyện của bạn.</span></div>
      <div className="phone-art" aria-hidden="true"><div className="art-orbit" /><div className="art-label">KẾT NỐI THEO CÁCH CỦA BẠN</div><div className="phone-back"><div className="camera-block"><i /><i /><i /><b /></div><span className="phone-logo">P.</span></div><div className="phone-front"><div className="phone-island" /><div className="phone-screen"><span>hello.</span><div className="screen-orbit" /><small>một ngày mới</small></div></div><small className="illustration-label">Hình minh họa</small></div>
    </section>
    <section className="home-links" aria-label="Khám phá PhoneStore"><Link to="/products"><span className="tile-number">01</span><div><h2>Chọn điện thoại</h2><p>Khám phá không gian sản phẩm.</p></div><span aria-hidden="true">↗</span></Link><Link to="/account"><span className="tile-number">02</span><div><h2>Không gian của bạn</h2><p>Tài khoản và những kết nối cá nhân.</p></div><span aria-hidden="true">↗</span></Link></section>
    <HomeCatalog />
  </>
}
