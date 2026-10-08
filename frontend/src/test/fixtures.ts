// Synthetic contract fixtures for tests only. Never imported by production pages.
import type { PagedResponse, ApiProblem } from '../types/contracts'
export const idPageFixture: PagedResponse<{ id: string }> = {
  items: [{ id: '9223372036854775807' }], page: 1, pageSize: 20, totalCount: 1,
}
export const fieldProblemFixture: ApiProblem = {
  type: 'about:blank', title: 'Dữ liệu không hợp lệ', status: 400, code: 'VALIDATION_ERROR', traceId: 'test-trace',
  errors: { Email: ['Email không hợp lệ.'] },
}
