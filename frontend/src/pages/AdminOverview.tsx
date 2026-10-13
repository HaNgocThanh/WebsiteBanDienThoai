import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { formatVietnamTime, formatVnd } from '../lib/contracts'
import { orderStatuses } from '../services/orders'
import { formatSignedVnd, reports, reportDate } from '../services/reports'
import type { OverviewReport } from '../services/reports'
import { SubmitButton, TextField } from '../components/primitives'
import { ResourceNotice } from './AdminCatalogShared'
import { useResource } from './adminCatalogHooks'

function dayLabel(date: string) { const [year, month, day] = date.split('-'); return `${day}/${month}/${year}` }
function shiftDate(date: string, days: number) { const value = new Date(date + 'T00:00:00Z'); value.setUTCDate(value.getUTCDate() + days); return value.toISOString().slice(0, 10) }
function CashChart({ report }: { report: OverviewReport }) {
  const max = Math.max(1, ...report.daily.flatMap(d => [d.received, d.refunded]))
  const x = (i: number) => 20 + i * 660 / Math.max(1, report.daily.length - 1), y = (value: number) => 150 - value * 130 / max
  return <><svg className="report-chart" viewBox="0 0 700 185" role="img" aria-label="Biểu đồ tiền nhận và hoàn tiền theo ngày"><title>Tiền đã nhận và tiền đã hoàn trong kỳ</title><line x1="20" y1="150" x2="680" y2="150" stroke="#bcc8bb" />
    <polyline points={report.daily.map((d, i) => `${x(i)},${y(d.received)}`).join(' ')} fill="none" stroke="#17614f" strokeWidth="3" />
    <polyline points={report.daily.map((d, i) => `${x(i)},${y(d.refunded)}`).join(' ')} fill="none" stroke="#b65836" strokeWidth="3" />
    {report.daily.map((d, i) => <g key={d.date}><circle cx={x(i)} cy={y(d.received)} r="3" fill="#17614f"><title>{dayLabel(d.date)}: Nhận {formatVnd(d.received)}</title></circle><circle cx={x(i)} cy={y(d.refunded)} r="3" fill="#b65836"><title>{dayLabel(d.date)}: Hoàn {formatVnd(d.refunded)}</title></circle></g>)}
    <text x="20" y="177" fontSize="12">{dayLabel(report.from)}</text><text x="680" y="177" textAnchor="end" fontSize="12">{dayLabel(report.to)}</text>
  </svg><p className="report-legend"><span><i className="received-dot" />Tiền nhận</span><span><i className="refund-dot" />Tiền hoàn</span><span>Mức cao nhất: {formatVnd(max === 1 && report.received === 0 && report.refunded === 0 ? 0 : max)}</span></p></>
}
export default function AdminOverviewPage() {
  const [params, setParams] = useSearchParams(), [draft, setDraft] = useState<{ from: string; to: string } | null>(null), [error, setError] = useState('')
  const query = new URLSearchParams(); for (const key of ['from', 'to']) if (params.get(key)) query.set(key, params.get(key)!)
  const resource = useResource(query.toString(), signal => reports.overview(query, signal)), data = resource.data
  const from = draft?.from ?? params.get('from') ?? data?.from ?? '', to = draft?.to ?? params.get('to') ?? data?.to ?? ''
  function preset(days: number) {
    if (!data) return
    const today = new Date(Date.parse(data.generatedAt) + 7 * 3600000).toISOString().slice(0, 10)
    setDraft(null); setError(''); setParams({ from: shiftDate(today, 1 - days), to: today })
  }
  return <section className="page-section admin-report"><div className="catalog-heading"><div><span className="eyebrow">THỐNG KÊ CỬA HÀNG</span><h1>Tổng quan</h1></div><SubmitButton type="button" className="button-outline" onClick={resource.reload}>Làm mới</SubmitButton></div>
    <form className="report-filters" onSubmit={e => { e.preventDefault(); try { reportDate(from); reportDate(to); const days = (Date.parse(to) - Date.parse(from)) / 86400000 + 1; if (days < 1 || days > 366) throw new Error(); setError(''); setDraft(null); setParams({ from, to }) } catch { setError('Chọn khoảng ngày hợp lệ, tối đa 366 ngày.') } }}>
      <TextField label="Từ ngày" type="date" required value={from} onChange={e => setDraft({ from: e.target.value, to })} /><TextField label="Đến ngày" type="date" required value={to} onChange={e => setDraft({ from, to: e.target.value })} /><SubmitButton>Xem báo cáo</SubmitButton>
      <div className="catalog-actions">{[[1, 'Hôm nay'], [7, '7 ngày'], [30, '30 ngày']] .map(([days, label]) => <SubmitButton type="button" className="button-outline" key={days} disabled={!data} onClick={() => preset(Number(days))}>{label}</SubmitButton>)}</div>
    </form>{error && <p role="alert" className="field-error">{error}</p>}
    {!data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải thống kê…" /> : <>
      <p className="muted">Từ {dayLabel(data.from)} đến {dayLabel(data.to)} · Giờ Việt Nam · Cập nhật {formatVietnamTime(data.generatedAt)}</p>
      <div className="report-metrics">{[['Số đơn mới', data.orders.toLocaleString('vi-VN'), 'Theo ngày tạo đơn'], ['Giá trị đơn mới', formatVnd(data.orderValue), 'Gồm cả đơn đã hủy; chưa phải tiền đã thu'], ['Tiền đã nhận', formatVnd(data.received), 'Theo ngày xác nhận thanh toán'], ['Tiền đã hoàn', formatVnd(data.refunded), 'Chỉ giao dịch hoàn tiền đã hoàn tất'], ['Thu ròng', formatSignedVnd(data.netReceived), 'Tiền nhận trừ tiền hoàn trong kỳ; có thể âm']].map(([title, value, hint]) => <article className="catalog-panel" key={title}><h2>{title}</h2><strong>{value}</strong><p>{hint}</p></article>)}</div>
      <section className="catalog-panel"><h2>Tiền nhận và hoàn theo ngày</h2>{data.received === 0 && data.refunded === 0 ? <p>Chưa có giao dịch thu hoặc hoàn tiền trong kỳ.</p> : <CashChart report={data} />}</section>
      <div className="report-columns"><section className="catalog-panel"><h2>Trạng thái các đơn tạo trong kỳ</h2><ul className="report-statuses">{data.statuses.map(s => <li key={s.status}><span>{orderStatuses[s.status]}</span><strong>{s.count}</strong><div aria-hidden="true" style={{ width: `${data.orders ? s.count * 100 / data.orders : 0}%` }} /></li>)}</ul><p className="muted">Trạng thái hiện tại của các đơn được tạo trong khoảng ngày đã chọn.</p></section>
      <section className="catalog-panel"><h2>Cần xử lý hiện tại</h2><p className="muted">Toàn cửa hàng, không giới hạn theo khoảng ngày.</p><ul className="report-queue"><li><Link to="/admin/orders?status=Reviewing">Đang xem xét <strong>{data.queue.reviewing}</strong></Link></li><li><Link to="/admin/orders?status=Shipping">Đang giao <strong>{data.queue.shipping}</strong></Link></li><li><Link to="/admin/orders?status=Delivered">Đã giao, chờ hoàn tất <strong>{data.queue.delivered}</strong></Link></li><li><Link to="/admin/orders">Hoàn tiền chờ xử lý <strong>{data.queue.pendingRefunds}</strong></Link></li></ul></section></div>
      <section className="catalog-panel"><h2>Phiên bản bán chạy</h2><p className="muted">Top 10 theo số chiếc trên đơn hoàn tất trong kỳ, tính theo ngày hoàn tất. Gồm cả sản phẩm đã ẩn; chưa trừ số lượng hoàn trả.</p>{data.bestSellers.length === 0 ? <p>Chưa có đơn hoàn tất trong kỳ.</p> : <div className="report-table-wrap"><table><thead><tr><th>Điện thoại / phiên bản</th><th>Số chiếc</th><th>Số đơn</th></tr></thead><tbody>{data.bestSellers.map(b => <tr key={b.variantId}><td><strong>{b.productName}</strong><br /><small>{b.variant}</small></td><td>{b.quantity}</td><td>{b.orders}</td></tr>)}</tbody></table></div>}</section>
      <section className="catalog-panel"><h2>Tồn kho điện thoại hiện tại</h2><p>{data.stock.activeVariants} phiên bản đang bán · {data.stock.lowStockVariants} phiên bản còn từ 0 đến 5 chiếc khả dụng.</p>{data.stock.items.length === 0 ? <p>Chưa có phiên bản đang bán cần bổ sung hàng.</p> : <div className="report-table-wrap"><table><thead><tr><th>Điện thoại / SKU</th><th>Còn khả dụng</th><th>Kho</th></tr></thead><tbody>{data.stock.items.map(i => <tr key={i.variantId}><td><strong>{i.productName}</strong><br /><small>{i.sku}</small></td><td>{i.available}</td><td><Link to={`/admin/inventory/${i.variantId}`}>Xem kho</Link></td></tr>)}</tbody></table></div>}<p className="muted">Khả dụng = hàng trong kho trừ hàng đang giữ. Chỉ tính phiên bản và sản phẩm đang hiển thị.</p></section>
      <section className="catalog-panel"><h2>Số liệu từng ngày</h2><div className="report-table-wrap daily-report"><table><thead><tr><th>Ngày</th><th>Đơn mới</th><th>Tiền nhận</th><th>Tiền hoàn</th><th>Thu ròng</th></tr></thead><tbody>{data.daily.map(d => <tr key={d.date}><td>{dayLabel(d.date)}</td><td>{d.orders}</td><td>{formatVnd(d.received)}</td><td>{formatVnd(d.refunded)}</td><td>{formatSignedVnd(d.netReceived)}</td></tr>)}</tbody></table></div></section>
      <div className="catalog-actions"><Link className="button" to="/admin/orders">Quản lý đơn hàng</Link><Link className="button button-outline" to="/admin/inventory">Quản lý tồn kho</Link><Link className="button button-outline" to="/admin/products">Quản lý điện thoại</Link></div>
    </>}
  </section>
}
