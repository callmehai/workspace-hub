import { useQuery } from '@tanstack/react-query'
import api from '../lib/api'

interface HealthDto {
  status: string
  database: string
  userCount: number
  serverTimeUtc: string
}

/**
 * Placeholder Inbox. Hiện chỉ demo kết nối BE qua TanStack Query (gọi /api/health).
 * Kanban + danh sách Item sẽ làm ở SCRUM-19/20.
 */
export default function InboxPage() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['health'],
    queryFn: async () => {
      const { data } = await api.get<HealthDto>('/health')
      return data
    },
  })

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-bold text-gray-900">Inbox</h1>
      <p className="text-sm text-gray-500">Kanban &amp; danh sách Item sẽ có ở các sprint sau.</p>

      <div className="rounded-lg border border-gray-200 p-4">
        <div className="mb-2 text-sm font-medium text-gray-700">Trạng thái backend</div>
        {isLoading && <div className="text-sm text-gray-400">Đang kiểm tra...</div>}
        {isError && <div className="text-sm text-red-500">Không kết nối được backend</div>}
        {data && (
          <ul className="text-sm text-gray-600">
            <li>Status: <span className="font-medium">{data.status}</span></li>
            <li>Database: <span className="font-medium">{data.database}</span></li>
            <li>User count: <span className="font-medium">{data.userCount}</span></li>
          </ul>
        )}
      </div>
    </div>
  )
}
