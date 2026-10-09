import type { ApiProblem } from '../types/contracts'

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  readonly errors: Record<string, string[]>

  constructor(status: number, code: string, traceId?: string, errors: Record<string, string[]> = {}) {
    super(errorMessage(status, code))
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.traceId = traceId
    this.errors = errors
  }
}

function errorMessage(status: number, code: string): string {
  if (code === 'CHECKOUT_STORAGE_UNAVAILABLE') return 'Không thể lưu hoặc đọc phiên đặt hàng. Kiểm tra quyền lưu trữ trình duyệt; nếu đã gửi yêu cầu, hãy tra cứu đơn qua email trước khi đặt lại.'
  if (code === 'CHECKOUT_NOT_COMPLETED') return 'Chưa có đơn được ghi nhận cho phiên này. Bạn có thể quay lại đặt hàng bằng cùng phiên.'
  if (code === 'CHECKOUT_EXPIRED') return 'Phiên đặt hàng chưa dùng đã hết hạn. Tính lại báo giá và xác nhận để đặt hàng.'
  if (code === 'CHECKOUT_NOT_FOUND') return 'Không tìm thấy phiên đặt hàng của bạn. Có thể bạn đã đổi tài khoản hoặc phiên guest hết hạn; hãy tra cứu đơn qua email.'
  if (code === 'CHECKOUT_BUSY') return 'Yêu cầu đang được xử lý. Kiểm tra kết quả hoặc thử lại với cùng yêu cầu.'
  if (code === 'IDEMPOTENCY_PAYLOAD_MISMATCH') return 'Yêu cầu trước đã tạo đơn với thông tin khác. Kiểm tra kết quả đặt hàng trước khi tiếp tục.'
  if (code === 'CHECKOUT_SESSION_LIMIT') return 'Bạn đang có nhiều phiên đặt hàng chưa dùng. Dùng lại phiên hiện tại hoặc chờ phiên hết hạn.'
  if (code === 'INVALID_ORDER_TOKEN') return 'Liên kết không hợp lệ, đã sử dụng hoặc hết hạn, hoặc tài khoản chưa khớp email đặt hàng. Hãy yêu cầu liên kết mới.'
  if (code === 'GUEST_ACCESS_REQUIRED') return 'Phiên xem đơn đã hết hạn hoặc đơn đã được nhận vào tài khoản. Mở liên kết email hoặc yêu cầu liên kết mới.'
  if (code === 'CONSENT_REQUIRED') return 'Đơn này chưa đồng ý nhận hướng dẫn tạo tài khoản. Bạn vẫn có thể chủ động đăng ký và xác minh để nhận quyền đơn.'
  if (code === 'CART_STORAGE_INVALID') return 'Không thể đọc hoặc lưu giỏ hàng. Kiểm tra quyền lưu trữ hoặc xóa giỏ để khôi phục.'
  if (code === 'PRICE_CHANGED') return 'Giá hoặc phí đã thay đổi. Vui lòng xem và xác nhận báo giá mới.'
  if (code === 'OUT_OF_STOCK') return 'Số lượng trong giỏ vượt lượng còn hàng. Giảm số lượng và tính lại báo giá.'
  if (code === 'VARIANT_UNAVAILABLE') return 'Có phiên bản không còn được bán. Xóa phiên bản đó và tính lại báo giá.'
  if (code === 'SHIPPING_UNAVAILABLE') return 'Chưa thể tính phí giao hàng. Vui lòng thử lại sau.'
  if (code === 'QUOTE_LIMIT') return 'Giá trị giỏ hàng vượt giới hạn số tiền được hỗ trợ.'
  if (code === 'OPERATION_KEY_CONFLICT') return 'Mã thao tác đã dùng với nội dung khác. Kiểm tra lịch sử trước khi tạo thao tác mới.'
  if (code === 'INSUFFICIENT_AVAILABLE') return 'Không thể giảm quá lượng khả dụng. Tải lại kho và kiểm tra lượng đã giữ.'
  if (code === 'INVENTORY_LIMIT') return 'Số lượng trong kho vượt giới hạn 2147483647.'
  if (code === 'INVENTORY_BUSY') return 'Kho đang được cập nhật. Thử lại với cùng mã thao tác.'
  if (code === 'OPERATION_STORAGE_UNAVAILABLE') return 'Không thể đọc hoặc lưu thao tác chờ trong phiên trình duyệt. Kiểm tra lưu trữ phiên và lịch sử kho trước khi gửi thao tác mới.'
  if (code === 'DUPLICATE_CATALOG') return 'Tên hãng, đường dẫn, SKU hoặc tổ hợp màu/dung lượng/RAM đã tồn tại. Vui lòng kiểm tra lại.'
  if (code === 'INACTIVE_PARENT') return 'Hãng, danh mục hoặc sản phẩm đang ẩn. Hãy bật mục cha hoặc lưu mục này ở trạng thái ẩn.'
  if (code === 'VERSION_MISMATCH') return 'Thông tin đã được người khác thay đổi. Tải phiên bản mới trước khi lưu lại.'
  if (code === 'CATALOG_BUSY') return 'Danh mục đang được cập nhật. Vui lòng thử lại sau.'
  if (code === 'IMAGE_UNAVAILABLE') return 'Chưa thể lưu ảnh trong môi trường này. Vui lòng thử lại sau.'
  if (code === 'INVALID_IMAGE') return 'Ảnh không hợp lệ. Dùng PNG RGB/RGBA 8 bit, không interlace, tối đa 2048 × 2048.'
  if (code === 'IMAGE_TOO_LARGE') return 'Ảnh vượt quá giới hạn 2 MiB.'
  if (code === 'INVALID_CREDENTIALS') return 'Email hoặc mật khẩu không hợp lệ, hoặc tài khoản đang tạm khóa.'
  if (code === 'EMAIL_NOT_VERIFIED') return 'Vui lòng xác minh email trước khi đăng nhập.'
  if (code === 'INVALID_TOKEN') return 'Liên kết không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu email mới.'
  if (code === 'EMAIL_UNAVAILABLE') return 'Chưa thể gửi email. Vui lòng thử lại sau.'
  if (code === 'CSRF_INVALID') return 'Phiên làm việc đã thay đổi. Vui lòng tải lại trang và thử lại.'
  if (code === 'TIMEOUT') return 'Kết nối mất quá nhiều thời gian. Vui lòng thử lại.'
  if (code === 'NETWORK_ERROR') return 'Không thể kết nối. Vui lòng thử lại.'
  if (code === 'INVALID_RESPONSE') return 'Phản hồi không hợp lệ. Vui lòng thử lại.'
  if (status === 400) return 'Vui lòng kiểm tra lại thông tin đã nhập.'
  if (status === 401) return 'Bạn cần đăng nhập để tiếp tục.'
  if (status === 403) return 'Bạn không có quyền thực hiện thao tác này.'
  if (status === 404) return 'Không tìm thấy dữ liệu bạn yêu cầu.'
  if (status === 412) return 'Thông tin đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa.'
  if (status === 428) return 'Vui lòng tải lại thông tin trước khi chỉnh sửa.'
  if (status === 409) return 'Thông tin bị xung đột. Vui lòng kiểm tra và thử lại.'
  if (status === 429) return 'Bạn thao tác quá nhanh. Vui lòng thử lại sau.'
  return 'Chưa thể hoàn tất yêu cầu. Vui lòng thử lại.'
}

export function parseProblem(value: unknown, status: number): ApiProblem {
  const problem = isRecord(value) ? value : {}
  const errors: Record<string, string[]> = {}
  if (isRecord(problem.errors)) {
    for (const [field, messages] of Object.entries(problem.errors)) {
      if (Array.isArray(messages) && messages.every(message => typeof message === 'string')) {
        // Own data properties, including untrusted field names such as __proto__.
        Object.defineProperty(errors, field, { value: messages, enumerable: true })
      }
    }
  }
  return {
    type: typeof problem.type === 'string' ? problem.type : 'about:blank',
    title: typeof problem.title === 'string' ? problem.title : 'Request failed',
    status,
    code: typeof problem.code === 'string' ? problem.code : 'HTTP_ERROR',
    traceId: typeof problem.traceId === 'string' ? problem.traceId : '',
    errors,
  }
}

export function fieldError(errors: Record<string, string[]>, field: string): string | undefined {
  return Object.entries(errors).find(([key]) => key.toLowerCase() === field.toLowerCase())?.[1].join(' ')
}
