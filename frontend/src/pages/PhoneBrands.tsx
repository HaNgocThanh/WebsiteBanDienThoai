import { Link } from 'react-router'
import { storeCatalog } from '../services/storeCatalog'
import { useResource } from './adminCatalogHooks'
import { ResourceNotice } from './AdminCatalogShared'

export function PhoneBrands() {
  const resource = useResource('home-phone-brands', signal => storeCatalog.brands(signal))
  return <section className="phone-brands" aria-labelledby="phone-brands-title"><div className="catalog-heading"><div><span className="eyebrow">THƯƠNG HIỆU ĐIỆN THOẠI</span><h2 id="phone-brands-title">Chọn điện thoại theo hãng</h2></div><Link to="/products">Tất cả điện thoại ↗</Link></div>
    {!resource.data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải hãng điện thoại…" /> : resource.data.length === 0 ? <p>Chưa có hãng điện thoại đang hiển thị.</p> : <ul className="phone-brand-links">{resource.data.map(brand => <li key={brand.id}><Link to={`/products?brandId=${brand.id}`}><strong>{brand.name}</strong><span aria-hidden="true">↗</span></Link></li>)}</ul>}
  </section>
}
