import { fieldError } from '../services/errors'
import { useMutation, useResource } from './adminCatalogHooks'
import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { EmptyState, SubmitButton, TextField } from '../components/primitives'
import { catalog } from '../services/catalog'
import type { Lookup, LookupKind } from '../services/catalog'
import { ActionNotice, ActiveField, ResourceNotice, SelectField, Status } from './AdminCatalogShared'

export function AdminProductsPage() {
  const [params, setParams] = useSearchParams()
  const latestParams = useRef(params)
  useEffect(() => { latestParams.current = params }, [params])
  function navigateParams(next: URLSearchParams) { latestParams.current = next; setParams(next) }
  const query = new URLSearchParams()
  for (const key of ['search', 'brandId', 'categoryId', 'isActive', 'sort', 'page', 'pageSize']) if (params.get(key)) query.set(key, params.get(key)!)
  const parents = useResource('catalog-parents', async signal => { const [brands, categories] = await Promise.all([catalog.lookups('brands', signal), catalog.lookups('categories', signal)]); return { brands, categories } })
  const products = useResource(query.toString(), signal => catalog.products(query, signal))
  const data = products.data && parents.data ? { page: products.data, ...parents.data } : undefined
  const resource = { error: products.error ?? parents.error, reload: () => { products.reload(); parents.reload() } }
  function change(key: string, value: string) { const next = new URLSearchParams(latestParams.current); next.delete('page'); if (value) next.set(key, value); else next.delete(key); navigateParams(next) }
  const page = data?.page
  return <section className="page-section"><div className="catalog-heading"><div><span className="eyebrow">QUẢN TRỊ DANH MỤC</span><h1>Danh mục sản phẩm</h1></div><Link className="button" to="/admin/products/new">Thêm sản phẩm</Link></div>
    <form className="catalog-filters" onSubmit={e => { e.preventDefault(); const form = new FormData(e.currentTarget); change('search', String(form.get('search') ?? '').trim()) }}>
      <TextField key={params.get('search') ?? ''} label="Tìm theo tên" name="search" maxLength={200} defaultValue={params.get('search') ?? ''} /><SubmitButton>Tìm kiếm</SubmitButton>
      <SelectField label="Hãng" value={params.get('brandId') ?? ''} onChange={v => change('brandId', v)}><option value="">Tất cả hãng</option>{parents.data?.brands.map(x => <option key={x.id} value={x.id}>{x.name}{!x.isActive && ' (đã ẩn)'}</option>)}</SelectField>
      <SelectField label="Danh mục" value={params.get('categoryId') ?? ''} onChange={v => change('categoryId', v)}><option value="">Tất cả danh mục</option>{parents.data?.categories.map(x => <option key={x.id} value={x.id}>{x.name}{!x.isActive && ' (đã ẩn)'}</option>)}</SelectField>
      <SelectField label="Trạng thái" value={params.get('isActive') ?? ''} onChange={v => change('isActive', v)}><option value="">Tất cả trạng thái</option><option value="true">Hiển thị</option><option value="false">Đã ẩn</option></SelectField>
      <SelectField label="Sắp xếp" value={params.get('sort') ?? 'newest'} onChange={v => change('sort', v)}><option value="newest">Mới nhất</option><option value="name">Tên sản phẩm</option></SelectField>
      <SelectField label="Số dòng mỗi trang" value={params.get('pageSize') ?? '20'} onChange={v => change('pageSize', v)}>{[1, 10, 20, 50].map(x => <option key={x} value={x}>{x}</option>)}</SelectField>
      <SubmitButton type="button" className="button-outline" onClick={() => navigateParams(new URLSearchParams())}>Xóa bộ lọc</SubmitButton>
    </form>
    {!data ? <ResourceNotice error={resource.error} reload={resource.reload} /> : <>{page!.items.length === 0 ? <EmptyState title="Không có sản phẩm phù hợp"><p>Thử đổi bộ lọc hoặc thêm sản phẩm mới.</p></EmptyState> : <ul className="catalog-list">{page!.items.map(x => <li className="catalog-row" key={x.id}><div><h2><Link to={`/admin/products/${x.id}`}>{x.name}</Link></h2><p className="muted">{data.brands.find(b => b.id === x.brandId)?.name} / {data.categories.find(c => c.id === x.categoryId)?.name}</p><small>/{x.slug}</small></div><div className="catalog-actions"><Status active={x.isActive} /><Link className="button button-outline" to={`/admin/products/${x.id}`}>Chỉnh sửa</Link></div></li>)}</ul>}
      <nav className="catalog-pagination" aria-label="Phân trang sản phẩm"><SubmitButton type="button" disabled={page!.page <= 1} onClick={() => { const next = new URLSearchParams(latestParams.current); next.set('page', String(page!.page - 1)); navigateParams(next) }}>Trang trước</SubmitButton><span>Trang {page!.page} / {Math.max(1, Math.ceil(page!.totalCount / page!.pageSize))} · {page!.totalCount} sản phẩm</span><SubmitButton type="button" disabled={page!.page * page!.pageSize >= page!.totalCount} onClick={() => { const next = new URLSearchParams(latestParams.current); next.set('page', String(page!.page + 1)); navigateParams(next) }}>Trang sau</SubmitButton></nav></>}
  </section>
}

export function AdminLookupsPage({ kind }: { kind: LookupKind }) {
  const title = kind === 'brands' ? 'Hãng' : 'Danh mục'
  const resource = useResource(kind, signal => catalog.lookups(kind, signal))
  const [current, setCurrent] = useState<Lookup>()
  const [draft, setDraft] = useState({ name: '', slug: '', isActive: true })
  const action = useMutation()
  function edit(item?: Lookup) { if (action.busy) return; setCurrent(item); setDraft(item ? { name: item.name, slug: item.slug, isActive: item.isActive } : { name: '', slug: '', isActive: true }); action.clear() }
  return <section className="page-section"><span className="eyebrow">QUẢN TRỊ DANH MỤC</span><h1>{title}</h1><div className="catalog-columns">
    <section className="catalog-panel"><h2>{current ? `Sửa ${title.toLowerCase()}` : `Thêm ${title.toLowerCase()}`}</h2><ActionNotice action={action} />
      <form onSubmit={e => { e.preventDefault(); void action.run(signal => catalog.saveLookup(kind, draft, current?.id, signal), result => { setCurrent(result); setDraft({ name: result.name, slug: result.slug, isActive: result.isActive }); resource.reload() }, 'Đã lưu thay đổi.') }}>
        <fieldset disabled={action.busy}><TextField error={fieldError(action.error?.errors ?? {}, "name")} label="Tên" required maxLength={100} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /><TextField error={fieldError(action.error?.errors ?? {}, "slug")} label="Đường dẫn (slug)" required maxLength={150} pattern="[a-z0-9]+(-[a-z0-9]+)*" hint="Chữ thường, số và dấu gạch ngang; ví dụ dien-thoai." value={draft.slug} onChange={e => setDraft({ ...draft, slug: e.target.value })} /><ActiveField value={draft.isActive} onChange={isActive => setDraft({ ...draft, isActive })} /><div className="catalog-actions"><SubmitButton busy={action.busy}>Lưu {title.toLowerCase()}</SubmitButton>{current && <SubmitButton type="button" className="button-outline" onClick={() => edit()}>Thêm mới</SubmitButton>}</div></fieldset>
      </form></section>
    <section className="catalog-panel"><h2>Danh sách {title.toLowerCase()}</h2>{!resource.data ? <ResourceNotice error={resource.error} reload={resource.reload} /> : resource.data.length === 0 ? <p>Chưa có dữ liệu.</p> : <ul className="catalog-list">{resource.data.map(x => <li className="catalog-row" key={x.id}><div><strong>{x.name}</strong><p>/{x.slug}</p><Status active={x.isActive} /></div><SubmitButton type="button" className="button-outline" disabled={action.busy} onClick={() => edit(x)}>Sửa {x.name}</SubmitButton></li>)}</ul>}</section>
  </div></section>
}
