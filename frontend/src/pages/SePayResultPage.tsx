import { Link, useSearchParams } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { OrderPayments } from './OrderPayments'
import { orders } from '../services/orders'
import { readPendingCheckout } from '../checkout/pending'
import { useResource } from './adminCatalogHooks'
import { ResourceNotice } from './AdminCatalogShared'
export function SePayResultPage() {
  const [params] = useSearchParams(), auth = useAuth(), id = params.get('orderId') ?? '', result = params.get('result')
  const valid = /^[1-9][0-9]{0,18}$/.test(id)
  const receipt = useResource('sepay:' + id + ':' + (auth.user?.userId ?? 'guest'), async signal => {
    const operation = readPendingCheckout()
    if (!operation) return null
    const recovered = await orders.result(operation.key, signal)
    return recovered.order?.id === id ? operation.key : null
  })
  return <section className="page-section"><h1>Kết quả chuyển hướng SePay Sandbox</h1><p>{result === 'cancel' ? 'Bạn đã rời bước thanh toán. Đơn vẫn giữ nguyên cho đến hạn thanh toán.' : result === 'error' ? 'SePay trả về thông báo lỗi. Hãy kiểm tra trạng thái tiền trước khi thử lại.' : 'Bạn đã quay lại từ SePay. Cửa hàng cần nhận và xác minh thông báo thanh toán.'}</p><p>Trang chuyển hướng không xác nhận đã nhận tiền. Trạng thái dưới đây được lấy từ cửa hàng.</p>{valid && (auth.user ? <OrderPayments area="me" id={id} /> : receipt.data ? <OrderPayments area="checkout" id={receipt.data} /> : receipt.data === undefined && !receipt.error ? <ResourceNotice loadingText="Đang kiểm tra phiên checkout…" error={undefined} reload={receipt.reload} /> : <p>Hãy yêu cầu liên kết xem đơn qua email để kiểm tra và tiếp tục thanh toán.</p>)}<Link className="button button-outline" to="/guest/lookup">Tra cứu đơn qua email</Link><Link to="/account/orders">Đơn hàng của tôi</Link></section>
}
