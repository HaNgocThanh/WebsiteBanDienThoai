import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { EmptyState, SubmitButton, TextField } from '../components/primitives'
import { formatVnd } from '../lib/contracts'
import { storeCatalog } from '../services/storeCatalog'
import type { StoreProduct, StoreSummary } from '../services/storeCatalog'
import type { Lookup } from '../services/catalog'
import { ResourceNotice, SelectField } from './AdminCatalogShared'
import { useResource } from './adminCatalogHooks'

const queryKeys = ['search', 'brandId', 'categoryId', 'minPrice', 'maxPrice', 'storageGb', 'ramGb', 'inStock', 'sort', 'page', 'pageSize']
function publicQuery(params: URLSearchParams) { const query = new URLSearchParams(); for (const key of queryKeys) { const value = params.get(key); if (value) query.set(key, value) } return query }
function ProductPhoto({ src, alt }: { src: string | null; alt: string }) {
  const [failed, setFailed] = useState(false)
  return src && !failed ? <img src={src} alt={alt} loading="lazy" onError={() => setFailed(true)} /> : <div className="store-photo-empty" role="img" aria-label="Chưa có ảnh sản phẩm"><span aria-hidden="true">◇</span><small>Chưa có ảnh</small></div>
}
function ProductCards({ items }: { items: StoreSummary[] }) {
  return <ul className="store-products">{items.map(p => <li key={p.id}><Link className="store-product-link" to={`/products/${p.slug}`}><ProductPhoto key={p.imageUrl} src={p.imageUrl} alt={p.name} /><div><h2>{p.name}</h2><p className="store-price">{p.minPrice === null ? 'Chưa có giá' : 'Từ ' + formatVnd(p.minPrice)}</p><span className="muted">Xem các phiên bản ↗</span></div></Link></li>)}</ul>
}
export function HomeCatalog() {
  const resource = useResource('home-newest', signal => storeCatalog.products(new URLSearchParams({ pageSize: '4', sort: 'newest' }), signal))
  return <section className="store-home-catalog"><div className="catalog-heading"><div><span className="eyebrow">KHÁM PHÁ</span><h2>Điện thoại mới nhất</h2></div><Link to="/products">Xem tất cả ↗</Link></div>{!resource.data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải sản phẩm…" /> : resource.data.items.length === 0 ? <p className="notice">Chưa có sản phẩm đang bán.</p> : <ProductCards items={resource.data.items} />}</section>
}
export function StoreProductsPage() {
  const [params, setParams] = useSearchParams()
  const query = publicQuery(params)
  const parents = useResource('public-parents', async signal => { const [brands, categories] = await Promise.all([storeCatalog.brands(signal), storeCatalog.categories(signal)]); return { brands, categories } })
  const products = useResource(query.toString(), signal => storeCatalog.products(query, signal))
  const page = products.data
  return <section className="page-section"><span className="eyebrow">KHÁM PHÁ</span><h1>Điện thoại</h1>{params.get('search') && <p className="search-query">Bạn đang tìm: <strong>{params.get('search')}</strong></p>}
    <ProductFilters key={query.toString()} params={params} brands={parents.data?.brands ?? []} categories={parents.data?.categories ?? []} onApply={setParams} />
    {parents.error && <ResourceNotice error={parents.error} reload={parents.reload} />}
    {!page ? <ResourceNotice error={products.error} reload={products.reload} loadingText="Đang tải sản phẩm…" /> : <>{page.items.length === 0 ? <EmptyState title="Không tìm thấy sản phẩm phù hợp" action={<SubmitButton type="button" className="button-outline" onClick={() => setParams({})}>Xóa bộ lọc</SubmitButton>}><p>Thử đổi từ khóa, giá hoặc thông số.</p></EmptyState> : <ProductCards items={page.items} />}
      <nav className="catalog-pagination" aria-label="Phân trang điện thoại"><SubmitButton type="button" disabled={page.page <= 1} onClick={() => { const next = publicQuery(params); next.set('page', String(page.page - 1)); setParams(next) }}>Trang trước</SubmitButton><span>Trang {page.page} / {Math.max(1, Math.ceil(page.totalCount / page.pageSize))} · {page.totalCount} sản phẩm</span><SubmitButton type="button" disabled={page.page * page.pageSize >= page.totalCount} onClick={() => { const next = publicQuery(params); next.set('page', String(page.page + 1)); setParams(next) }}>Trang sau</SubmitButton></nav></>}
  </section>
}
function ProductFilters({ params, brands, categories, onApply }: { params: URLSearchParams; brands: Lookup[]; categories: Lookup[]; onApply: (params: URLSearchParams) => void }) {
  const [draft, setDraft] = useState(() => Object.fromEntries(queryKeys.map(key => [key, params.get(key) ?? ''])))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const change = (key: string, value: string) => { setDraft(p => ({ ...p, [key]: value })); setErrors(p => ({ ...p, [key]: '' })) }
  return <form className="store-filters" noValidate onSubmit={e => {
    e.preventDefault(); const invalid: Record<string, string> = {}
    for (const [key, max] of [['minPrice', 9007199254740991], ['maxPrice', 9007199254740991], ['storageGb', 65536], ['ramGb', 1024]] as const) {
      const value = draft[key]; if (value && (!/^\d+$/.test(value) || !Number.isSafeInteger(Number(value)) || Number(value) > max || Number(value) < (key.endsWith('Gb') ? 1 : 0))) invalid[key] = `Nhập số nguyên từ ${key.endsWith('Gb') ? 1 : 0} đến ${max}.`
    }
    if (draft.minPrice && draft.maxPrice && Number(draft.minPrice) > Number(draft.maxPrice)) invalid.maxPrice = 'Giá tối đa phải lớn hơn hoặc bằng giá tối thiểu.'
    if (draft.search.length > 200) invalid.search = 'Từ khóa tối đa 200 ký tự.'
    setErrors(invalid); if (Object.keys(invalid).length) return
    const next = new URLSearchParams(); for (const key of queryKeys.filter(x => x !== 'page')) if (draft[key].trim()) next.set(key, draft[key].trim()); onApply(next)
  }}>
    <div className="store-filter-fields"><TextField label="Tên điện thoại" value={draft.search} maxLength={200} error={errors.search} onChange={e => change('search', e.target.value)} />
      <SelectField label="Hãng" value={draft.brandId} onChange={v => change('brandId', v)}><option value="">Tất cả hãng</option>{brands.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</SelectField><SelectField label="Danh mục" value={draft.categoryId} onChange={v => change('categoryId', v)}><option value="">Tất cả danh mục</option>{categories.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</SelectField>
      <TextField label="Giá tối thiểu (VND)" inputMode="numeric" value={draft.minPrice} error={errors.minPrice} onChange={e => change('minPrice', e.target.value)} /><TextField label="Giá tối đa (VND)" inputMode="numeric" value={draft.maxPrice} error={errors.maxPrice} onChange={e => change('maxPrice', e.target.value)} /><TextField label="Dung lượng (GB)" inputMode="numeric" value={draft.storageGb} error={errors.storageGb} onChange={e => change('storageGb', e.target.value)} /><TextField label="RAM (GB)" inputMode="numeric" value={draft.ramGb} error={errors.ramGb} onChange={e => change('ramGb', e.target.value)} />
      <SelectField label="Tình trạng hàng" value={draft.inStock} onChange={v => change('inStock', v)}><option value="">Tất cả</option><option value="true">Còn hàng</option><option value="false">Hết hàng</option></SelectField><SelectField label="Sắp xếp" value={draft.sort || 'newest'} onChange={v => change('sort', v)}><option value="newest">Mới nhất</option><option value="name">Tên sản phẩm</option><option value="priceAsc">Giá tăng dần</option><option value="priceDesc">Giá giảm dần</option></SelectField><SelectField label="Số sản phẩm mỗi trang" value={draft.pageSize || '20'} onChange={v => change('pageSize', v)}>{[1, 10, 20, 50].map(x => <option key={x} value={x}>{x}</option>)}</SelectField></div>
    <div className="catalog-actions"><SubmitButton>Áp dụng bộ lọc</SubmitButton><SubmitButton type="button" className="button-outline" onClick={() => { setDraft(Object.fromEntries(queryKeys.map(key => [key, '']))); setErrors({}); onApply(new URLSearchParams()) }}>Xóa bộ lọc</SubmitButton></div>
  </form>
}
export function StoreProductPage() {
  const { slug = '' } = useParams()
  const resource = useResource(slug, signal => storeCatalog.product(slug, signal))
  if (resource.error?.status === 404) return <section className="page-section"><h1>Không tìm thấy sản phẩm</h1><p>Sản phẩm hiện không có trong danh mục.</p><Link className="button" to="/products">Xem điện thoại khác</Link></section>
  return <section className="page-section"><Link to="/products">← Tất cả điện thoại</Link><h1>{resource.data?.name ?? 'Chi tiết điện thoại'}</h1>{!resource.data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải chi tiết…" /> : <ProductDetail key={slug} product={resource.data} />}</section>
}
function ProductDetail({ product }: { product: StoreProduct }) {
  const [variantId, setVariantId] = useState(product.variants[0].id)
  const [selectedImage, setSelectedImage] = useState('')
  const variant = product.variants.find(x => x.id === variantId)!
  const images = product.images.filter(x => x.variantId === null || x.variantId === variantId).sort((a, b) => a.sortOrder - b.sortOrder)
  const current = images.find(x => x.id === selectedImage) ?? images[0]
  useEffect(() => { document.title = product.name + ' | PhoneStore' }, [product.name])
  return <><div className="store-detail"><section className="store-gallery" aria-label="Ảnh sản phẩm"><div className="store-main-photo"><ProductPhoto key={current?.imageUrl ?? ''} src={current?.imageUrl ?? null} alt={current?.altText ?? product.name} /></div>{images.length > 1 && <div className="store-thumbnails">{images.map((x, i) => <button key={x.id} type="button" aria-label={`Xem ảnh ${i + 1}: ${x.altText}`} aria-pressed={current?.id === x.id} onClick={() => setSelectedImage(x.id)}><img src={x.imageUrl} alt="" /></button>)}</div>}</section>
    <section className="store-detail-info"><span className="eyebrow">{product.brand.name} / {product.category.name}</span><p className="store-product-subtitle">{product.name}</p><p className="store-price" aria-live="polite">{formatVnd(variant.price)}</p><fieldset className="store-variants"><legend>Chọn phiên bản</legend>{product.variants.map(x => <label key={x.id} className={variantId === x.id ? 'is-selected' : ''}><input type="radio" name={`variant-${product.id}`} checked={variantId === x.id} value={x.id} onChange={() => { setVariantId(x.id); setSelectedImage('') }} /><span>{x.color} · {x.storageGb} GB · RAM {x.ramGb} GB</span></label>)}</fieldset><p className={`store-stock ${variant.available ? '' : 'is-empty'}`} role="status">{variant.available ? `Còn ${variant.available.toLocaleString('vi-VN')} sản phẩm` : 'Hết hàng'}</p><small className="muted">SKU: {variant.sku}</small></section></div>
    <section className="catalog-panel store-description"><h2>Mô tả</h2><p>{product.description || 'Chưa có mô tả sản phẩm.'}</p></section>{product.specificationsJson && <section className="catalog-panel"><h2>Thông số bổ sung</h2><pre className="store-specifications">{product.specificationsJson}</pre></section>}
  </>
}
