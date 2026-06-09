import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { tokenStore } from '../lib/api'

const navItems = [{ to: '/inbox', label: 'Inbox' }]

/** Layout chính sau đăng nhập: sidebar + nội dung. Thêm nav item khi có page mới. */
export default function AppLayout() {
  const navigate = useNavigate()

  const logout = () => {
    tokenStore.clear()
    navigate('/login', { replace: true })
  }

  return (
    <div className="flex h-full">
      <aside className="flex w-56 flex-col border-r border-gray-200 bg-gray-50 p-4">
        <div className="mb-6 text-lg font-bold text-brand-700">Workspace Hub</div>
        <nav className="flex flex-col gap-1">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `rounded px-3 py-2 text-sm ${
                  isActive ? 'bg-brand-100 text-brand-700' : 'text-gray-700 hover:bg-gray-100'
                }`
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <button
          onClick={logout}
          className="mt-auto rounded px-3 py-2 text-left text-sm text-gray-500 hover:bg-gray-100"
        >
          Đăng xuất
        </button>
      </aside>
      <main className="flex-1 overflow-auto p-6">
        <Outlet />
      </main>
    </div>
  )
}
