import { fieldError } from '../services/errors'
import { validateSpecifications } from '../lib/catalogValidation'
import { useMutation, useResource } from './adminCatalogHooks'
import { useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { SubmitButton, TextField } from '../components/primitives'
import { formatVnd } from '../lib/contracts'
import { catalog } from '../services/catalog'
import type { CatalogImage, Lookup, Product, ProductInput, Variant, VariantInput } from '../services/catalog'
import { ActionNotice, ActiveField, ResourceNotice, SelectField, Status, TextAreaField } from './AdminCatalogShared'
import type { MutationGate } from './adminCatalogHooks'

const emptyProduct: ProductInput = { name: '', slug: '', brandId: '', categoryId: '', description: '', specificationsJson: null, isActive: true }
export function AdminProductPage() {
  const { id } = useParams()
  const resource = useResource(id ?? 'new', async signal => {
    const [product, brands, categories] = await Promise.all([id ? catalog.product(id, signal) : Promise.resolve(undefined), catalog.lookups('brands', signal), catalog.lookups('categories', signal)])
    return { product, brands, categories }
  })
  return <section className="page-section"><Link to="/admin/products">← Danh sách sản phẩm</Link><h1>{id ? 'Chỉnh sửa sản phẩm' : 'Thêm sản phẩm'}</h1>{!resource.data ? <ResourceNotice error={resource.error} reload={resource.reload} /> : <ProductEditor key={id ?? 'new'} initial={resource.data.product} brands={resource.data.brands} categories={resource.data.categories} />}</section>
}
function ProductEditor({ initial, brands, categories }: { initial?: Product; brands: Lookup[]; categories: Lookup[] }) {
  const navigate = useNavigate()
  const [product, setProduct] = useState(initial)
  const [draft, setDraft] = useState<ProductInput>(() => initial ? { name: initial.name, slug: initial.slug, brandId: initial.brandId, categoryId: initial.categoryId, description: initial.description, specificationsJson: initial.specificationsJson, isActive: initial.isActive } : emptyProduct)
  const [invalidJson, setInvalidJson] = useState(false)
  const [pageBusy, setPageBusy] = useState(false)
  const [gate] = useState<MutationGate>(() => { let pending = false; return { acquire: () => { if (pending) return false; pending = true; setPageBusy(true); return true }, release: () => { pending = false; setPageBusy(false) } } })
  const action = useMutation(gate)
  function replace(next: Product) { setProduct(next); setDraft({ name: next.name, slug: next.slug, brandId: next.brandId, categoryId: next.categoryId, description: next.description, specificationsJson: next.specificationsJson, isActive: next.isActive }); setInvalidJson(false) }
  return <fieldset className="catalog-editor" disabled={pageBusy}>
    <section className="catalog-panel"><h2>Thông tin sản phẩm</h2><ActionNotice action={action} refresh={() => { if (product) void action.run(signal => catalog.product(product.id, signal), replace, 'Đã tải phiên bản mới.') }} />
      <form onSubmit={e => { e.preventDefault(); const valid = validateSpecifications(draft.specificationsJson ?? ''); setInvalidJson(!valid); if (!valid) return; void action.run(signal => catalog.saveProduct({ ...draft, specificationsJson: draft.specificationsJson?.trim() || null }, product, signal), next => { replace(next); if (!product) navigate(`/admin/products/${next.id}`, { replace: true }) }, 'Đã lưu sản phẩm.') }}>
        <fieldset disabled={action.busy}><div className="catalog-fields"><TextField error={fieldError(action.error?.errors ?? {}, "name")} label="Tên sản phẩm" required maxLength={200} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "slug")} label="Đường dẫn (slug)" required pattern="[a-z0-9]+(-[a-z0-9]+)*" maxLength={220} hint="Chữ thường, số và dấu gạch ngang." value={draft.slug} onChange={e => setDraft({ ...draft, slug: e.target.value })} />
          <SelectField error={fieldError(action.error?.errors ?? {}, "brandId")} label="Hãng" required value={draft.brandId} onChange={brandId => setDraft({ ...draft, brandId })}><option value="">Chọn hãng</option>{brands.map(x => <option key={x.id} value={x.id}>{x.name}{!x.isActive && ' (đã ẩn)'}</option>)}</SelectField><SelectField error={fieldError(action.error?.errors ?? {}, "categoryId")} label="Danh mục" required value={draft.categoryId} onChange={categoryId => setDraft({ ...draft, categoryId })}><option value="">Chọn danh mục</option>{categories.map(x => <option key={x.id} value={x.id}>{x.name}{!x.isActive && ' (đã ẩn)'}</option>)}</SelectField></div>
          <TextAreaField error={fieldError(action.error?.errors ?? {}, "description")} label="Mô tả" value={draft.description} onChange={description => setDraft({ ...draft, description })} maxLength={20000} /><TextAreaField error={fieldError(action.error?.errors ?? {}, "specificationsJson")} label="Thông số (JSON)" hint={'Để trống hoặc nhập đối tượng JSON, ví dụ {"screen":"6.7 inch"}.'} value={draft.specificationsJson ?? ''} onChange={specificationsJson => { setDraft({ ...draft, specificationsJson }); setInvalidJson(false) }} maxLength={20000} />{invalidJson && <p className="field-error" role="alert">Thông số phải là đối tượng JSON hợp lệ, tối đa 16 cấp.</p>}<ActiveField value={draft.isActive} onChange={isActive => setDraft({ ...draft, isActive })} /><SubmitButton busy={action.busy}>Lưu sản phẩm</SubmitButton>
        </fieldset>
      </form>
    </section>
    {product ? <><VariantEditor gate={gate} key={`variants-${product.id}`} product={product} onChange={variant => setProduct(p => p ? { ...p, variants: [...p.variants.filter(x => x.id !== variant.id), variant] } : p)} />
      <ImageEditor gate={gate} key={`images-${product.id}`} product={product} onAdd={image => setProduct(p => p ? { ...p, images: [...p.images, image] } : p)} onDelete={id => setProduct(p => p ? { ...p, images: p.images.filter(x => x.id !== id) } : p)} /></> : <p className="notice">Lưu sản phẩm trước để thêm phiên bản và ảnh.</p>}
  </fieldset>
}
const emptyVariant = { sku: '', color: '', storageGb: '128', ramGb: '8', price: '', isActive: true }
function VariantEditor({ product, onChange, gate }: { product: Product; onChange: (variant: Variant) => void; gate: MutationGate }) {
  const [current, setCurrent] = useState<Variant>()
  const [draft, setDraft] = useState(emptyVariant)
  const [invalid, setInvalid] = useState('')
  const action = useMutation(gate)
  function edit(v?: Variant) { if (action.busy) return; setCurrent(v); setDraft(v ? { sku: v.sku, color: v.color, storageGb: String(v.storageGb), ramGb: String(v.ramGb), price: String(v.price), isActive: v.isActive } : emptyVariant); setInvalid(''); action.clear() }
  function load(v: Variant) { setCurrent(v); setDraft({ sku: v.sku, color: v.color, storageGb: String(v.storageGb), ramGb: String(v.ramGb), price: String(v.price), isActive: v.isActive }); onChange(v) }
  return <section className="catalog-panel"><h2>Phiên bản</h2><p><Link to={`/admin/inventory?productId=${product.id}`}>Quản lý tồn kho các phiên bản</Link></p><p className="muted">Mỗi SKU và tổ hợp màu / dung lượng / RAM phải riêng biệt. Giá là số nguyên VND.</p>
    {product.variants.length === 0 ? <p>Chưa có phiên bản.</p> : <ul className="catalog-list">{product.variants.map(v => <li className="catalog-row" key={v.id}><div><strong>{v.sku}</strong><p>{v.color} · {v.storageGb} GB · RAM {v.ramGb} GB</p><p>{formatVnd(v.price)}</p><Status active={v.isActive} /></div><SubmitButton type="button" disabled={action.busy} className="button-outline" onClick={() => edit(v)}>Sửa {v.sku}</SubmitButton></li>)}</ul>}
    <h3>{current ? `Sửa phiên bản ${current.sku}` : 'Thêm phiên bản'}</h3><ActionNotice action={action} refresh={() => { if (current) void action.run(signal => catalog.product(product.id, signal), latest => { const found = latest.variants.find(v => v.id === current.id); if (found) load(found) }, 'Đã tải phiên bản mới.') }} />{invalid && <p className="field-error" role="alert">{invalid}</p>}
    <form onSubmit={e => { e.preventDefault(); const { storageGb, ramGb, price } = draft; if (![storageGb, ramGb, price].every(x => /^\d+$/.test(x)) || ![storageGb, ramGb, price].map(Number).every(Number.isSafeInteger) || Number(storageGb) < 1 || Number(storageGb) > 65536 || Number(ramGb) < 1 || Number(ramGb) > 1024) { setInvalid('Dung lượng/RAM phải là số nguyên trong giới hạn; giá VND là số nguyên không âm, tối đa 9007199254740991.'); return } setInvalid(''); const body: VariantInput = { ...draft, storageGb: Number(storageGb), ramGb: Number(ramGb), price: Number(price) }; void action.run(signal => catalog.saveVariant(product.id, body, current, signal), v => { onChange(v); setCurrent(v); setDraft({ sku: v.sku, color: v.color, storageGb: String(v.storageGb), ramGb: String(v.ramGb), price: String(v.price), isActive: v.isActive }) }, 'Đã lưu phiên bản.') }}>
      <fieldset disabled={action.busy}><div className="catalog-fields"><TextField error={fieldError(action.error?.errors ?? {}, "sku")} label="SKU" required pattern="[A-Za-z0-9][A-Za-z0-9._\-]*" maxLength={64} value={draft.sku} onChange={e => setDraft({ ...draft, sku: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "color")} label="Màu" required maxLength={50} value={draft.color} onChange={e => setDraft({ ...draft, color: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "storageGb")} label="Dung lượng (GB)" required inputMode="numeric" pattern="[0-9]+" value={draft.storageGb} onChange={e => setDraft({ ...draft, storageGb: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "ramGb")} label="RAM (GB)" required inputMode="numeric" pattern="[0-9]+" value={draft.ramGb} onChange={e => setDraft({ ...draft, ramGb: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "price")} label="Giá (VND)" required inputMode="numeric" pattern="[0-9]+" hint="Nhập số nguyên, không có dấu phân cách." value={draft.price} onChange={e => setDraft({ ...draft, price: e.target.value })} /></div><ActiveField value={draft.isActive} onChange={isActive => setDraft({ ...draft, isActive })} /><div className="catalog-actions"><SubmitButton busy={action.busy}>Lưu phiên bản</SubmitButton>{current && <SubmitButton type="button" className="button-outline" onClick={() => edit()}>Thêm phiên bản mới</SubmitButton>}</div></fieldset>
    </form>
  </section>
}
function ImageEditor({ product, onAdd, onDelete, gate }: { product: Product; onAdd: (image: CatalogImage) => void; onDelete: (id: string) => void; gate: MutationGate }) {
  const [file, setFile] = useState<File>()
  const [variantId, setVariantId] = useState('')
  const [altText, setAltText] = useState('')
  const [sortOrder, setSortOrder] = useState('0')
  const [invalid, setInvalid] = useState('')
  const [deleteId, setDeleteId] = useState('')
  const input = useRef<HTMLInputElement>(null)
  const action = useMutation(gate)
  return <section className="catalog-panel"><h2>Ảnh sản phẩm</h2><ActionNotice action={action} />{product.images.length === 0 ? <p>Chưa có ảnh.</p> : <ul className="catalog-images">{[...product.images].sort((a, b) => a.sortOrder - b.sortOrder).map(img => <li key={img.id}><img src={img.imageUrl} alt={img.altText} /><strong>{img.altText}</strong><p>{img.variantId ? product.variants.find(v => v.id === img.variantId)?.sku : 'Ảnh chung'} · Thứ tự {img.sortOrder}</p>{deleteId === img.id ? <div className="catalog-actions"><SubmitButton type="button" busy={action.busy} onClick={() => { void action.run(signal => catalog.deleteImage(img.id, signal), () => { onDelete(img.id); setDeleteId('') }, 'Đã xóa ảnh.') }}>Xác nhận xóa ảnh</SubmitButton><SubmitButton type="button" disabled={action.busy} className="button-outline" onClick={() => setDeleteId('')}>Giữ ảnh</SubmitButton></div> : <SubmitButton type="button" disabled={action.busy} className="button-outline" onClick={() => { action.clear(); setDeleteId(img.id) }}>Xóa ảnh {img.altText}</SubmitButton>}</li>)}</ul>}
    <h3>Thêm ảnh</h3><p className="muted">PNG RGB/RGBA 8 bit, không interlace, tối đa 2 MiB và 2048 × 2048 pixel.</p>{invalid && <p className="field-error" role="alert">{invalid}</p>}
    <form onSubmit={e => { e.preventDefault(); if (!file || !/\.png$/i.test(file.name) || file.type !== 'image/png' || file.size === 0 || file.size > 2 * 1024 * 1024 || !/^\d+$/.test(sortOrder) || Number(sortOrder) > 2147483647) { setInvalid('Chọn ảnh PNG tối đa 2 MiB và thứ tự là số nguyên từ 0 đến 2147483647.'); return } setInvalid(''); const form = new FormData(); form.set('file', file); form.set('altText', altText); form.set('sortOrder', sortOrder); if (variantId) form.set('variantId', variantId); void action.run(signal => catalog.upload(product.id, form, signal), img => { onAdd(img); setFile(undefined); setAltText(''); if (input.current) input.current.value = '' }, 'Đã thêm ảnh.') }}>
      <fieldset disabled={action.busy}><div className="field"><label htmlFor={`image-file-${product.id}`}>File PNG</label><input ref={input} id={`image-file-${product.id}`} type="file" accept="image/png,.png" required onChange={e => { setFile(e.target.files?.[0]); setInvalid('') }} /></div><div className="catalog-fields"><SelectField label="Ảnh thuộc phiên bản" value={variantId} onChange={setVariantId}><option value="">Ảnh chung cho sản phẩm</option>{product.variants.map(v => <option key={v.id} value={v.id}>{v.sku}{!v.isActive && ' (đã ẩn)'}</option>)}</SelectField><TextField error={fieldError(action.error?.errors ?? {}, "altText")} label="Mô tả ảnh" required maxLength={200} value={altText} onChange={e => setAltText(e.target.value)} /><TextField error={fieldError(action.error?.errors ?? {}, "sortOrder")} label="Thứ tự ảnh" required inputMode="numeric" pattern="[0-9]+" value={sortOrder} onChange={e => setSortOrder(e.target.value)} /></div><SubmitButton busy={action.busy}>Tải ảnh lên</SubmitButton></fieldset>
    </form>
  </section>
}
