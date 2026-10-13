import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { ApiError } from '../services/errors'
import { getMembership, getConfiguration, getSpend, getHistory, getCustomers, tierNames } from '../services/membership'
import type { Configuration, Membership, Page, Spend, History, Customer } from '../services/membership'
const vnd = (amount: number) => new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount)
const date = (value: string) => new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(value))
const failure = (v: unknown) => v instanceof ApiError ? v : new ApiError(0, 'NETWORK_ERROR')

export function MembershipPage({ admin = false }: { admin?: boolean }) {
  const { userId } = useParams()
  const customer = admin ? userId : undefined
  const [data, setData] = useState<Membership>(), [spend, setSpend] = useState<Page<Spend>>(), [history, setHistory] = useState<Page<History>>()
  const [page, setPage] = useState(1), [historyPage, setHistoryPage] = useState(1), [retry, setRetry] = useState(0), [error, setError] = useState<ApiError>()
  useEffect(() => {
    const abort = new AbortController()
    Promise.all([getMembership(abort.signal, customer), getSpend(page, abort.signal, customer), getHistory(historyPage, abort.signal, customer)]).then(([m, s, h]) => { if (!abort.signal.aborted) { setData(m); setSpend(s); setHistory(h); setError(undefined) } }).catch(e => { if (!abort.signal.aborted) setError(failure(e)) })
    return () => abort.abort()
  }, [page, historyPage, retry, customer])
  return <section className="page-section membership-page">{admin ? <h2>Hạng thành viên</h2> : <h1>Hạng thành viên</h1>}{error && <><ErrorNotice error={error} /><button onClick={() => setRetry(n => n + 1)}>Thử lại</button></>}{!data && !error && <p role="status">Đang tải hạng thành viên…</p>}
    {data && <><h2>{data.enabled ? data.tierCode ? tierNames[data.tierCode] : 'Chưa có hạng' : 'Chính sách hạng chưa được cấu hình'}</h2><p>Chi tiêu hợp lệ: <strong>{vnd(data.eligibleSpend)}</strong></p>
      {data.nextTier && <p>Còn {vnd(data.remainingSpend)} để đạt hạng {data.nextTier.name}.</p>}
      <p>Chỉ tính tiền hàng sau giảm giá của đơn đã hoàn tất. Phí giao hàng, đơn chưa hoàn tất và đơn hủy không được tính. Tiền hàng đã hoàn thành hoàn tiền sẽ được trừ và có thể làm giảm hạng.</p>
      <h2>Quyền lợi và ngưỡng thành viên</h2><ul>{data.tiers.map(t => <li key={t.code}><strong>{t.name}</strong>: từ {vnd(t.minimumSpend)}</li>)}</ul><p>Ưu đãi dành cho từng hạng được áp dụng theo chương trình được công bố. Lịch sử và thông tin đơn đã mua được giữ lại.</p>
      <h2>Sổ chi tiêu</h2>{spend?.items.length === 0 && <p>Chưa có chi tiêu đủ điều kiện.</p>}<ul className="membership-ledger">{spend?.items.map(e => <li key={e.id}><Link to={(admin ? '/admin/orders/' : '/account/orders/') + e.orderId}>Đơn #{e.orderId}</Link> · {e.kind === 'Refund' ? 'Hoàn tiền hàng' : 'Đơn hoàn tất'} · <strong>{vnd(e.amount)}</strong><small>{date(e.createdAt)}</small></li>)}</ul>
      {spend && <div className="pagination"><button disabled={page === 1} onClick={() => setPage(n => n - 1)}>Chi tiêu trước</button><span>Trang {spend.page}</span><button disabled={page * spend.pageSize >= spend.totalCount} onClick={() => setPage(n => n + 1)}>Chi tiêu tiếp</button></div>}
      <h2>Lịch sử hạng</h2>{history?.items.length === 0 && <p>Chưa có thay đổi hạng.</p>}<ul className="membership-ledger">{history?.items.map(e => <li key={e.id}>{e.previousTierCode ? tierNames[e.previousTierCode] : 'Chưa có hạng'} → {e.newTierCode ? tierNames[e.newTierCode] : 'Chưa có hạng'} · {vnd(e.eligibleSpend)}<small>{e.reason} · {date(e.createdAt)}</small></li>)}</ul>
      {history && <div className="pagination"><button disabled={historyPage === 1} onClick={() => setHistoryPage(n => n - 1)}>Lịch sử trước</button><span>Trang {history.page}</span><button disabled={historyPage * history.pageSize >= history.totalCount} onClick={() => setHistoryPage(n => n + 1)}>Lịch sử tiếp</button></div>}</>}
  </section>
}

export function AdminCustomersPage() {
  const [params, setParams] = useSearchParams(), [search, setSearch] = useState(params.get('search') ?? ''), [data, setData] = useState<Page<Customer>>(), [config, setConfig] = useState<Configuration>(), [error, setError] = useState<ApiError>(), [retry, setRetry] = useState(0)
  const query = params.toString()
  useEffect(() => {
    const abort = new AbortController()
    Promise.all([getCustomers(new URLSearchParams(query), abort.signal), getConfiguration(true, abort.signal)]).then(([list, c]) => { if (!abort.signal.aborted) { setData(list); setConfig(c); setError(undefined) } }).catch(e => { if (!abort.signal.aborted) setError(failure(e)) })
    return () => abort.abort()
  }, [query, retry])
  function filter(key: string, value: string) { const next = new URLSearchParams(params); if (value) next.set(key, value); else next.delete(key); next.delete('page'); setParams(next) }
  function move(page: number) { const next = new URLSearchParams(params); next.set('page', String(page)); setParams(next) }
  return <section className="page-section membership-page"><h1>Quản lý khách hàng</h1><form onSubmit={e => { e.preventDefault(); filter('search', search.trim()) }}><TextField label="Tìm tên hoặc email khách hàng" value={search} maxLength={150} onChange={e => setSearch(e.target.value)} /><SubmitButton>Tìm khách hàng</SubmitButton></form>
    <label>Hạng khách hàng<select value={params.get('tierId') ?? ''} onChange={e => filter('tierId', e.target.value)}><option value="">Tất cả hạng</option><option value="none">Chưa có hạng</option>{config?.tiers.map(t => <option key={t.code} value={t.id ?? ''}>{t.name}</option>)}</select></label>
    <label>Trạng thái tài khoản<select value={params.get('isActive') ?? ''} onChange={e => filter('isActive', e.target.value)}><option value="">Tất cả trạng thái</option><option value="true">Đang hoạt động</option><option value="false">Đã khóa</option></select></label>
    {error && <><ErrorNotice error={error} /><button onClick={() => setRetry(n => n + 1)}>Thử tải lại</button></>}{!data && !error && <p role="status">Đang tải khách hàng…</p>}{data?.items.length === 0 && <p>Không có khách hàng phù hợp.</p>}
    <ul className="membership-ledger">{data?.items.map(c => <li key={c.userId}><Link to={'/admin/customers/' + c.userId}>{c.fullName}</Link><small>{c.email} · {c.phone ?? 'Chưa có số điện thoại'}</small><small>{c.isActive ? 'Đang hoạt động' : 'Đã khóa'} · {c.emailConfirmed ? 'Email đã xác minh' : 'Email chưa xác minh'}</small>{c.tierCode ? tierNames[c.tierCode] : 'Chưa có hạng'} · {vnd(c.eligibleSpend)}</li>)}</ul>
    {data && <div className="pagination"><button disabled={data.page === 1} onClick={() => move(data.page - 1)}>Khách hàng trước</button><span>Trang {data.page} · {data.totalCount} khách hàng</span><button disabled={data.page * data.pageSize >= data.totalCount} onClick={() => move(data.page + 1)}>Khách hàng tiếp</button></div>}
  </section>
}
