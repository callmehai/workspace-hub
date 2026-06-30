import { NavLink } from 'react-router-dom';
import { 
  Inbox, 
  CheckCircle2, 
  FileText, 
  Calendar as CalendarIcon, 
  Code, 
  MessageSquare, 
  Briefcase, 
  Settings, 
  HelpCircle,
  Plus,
  LayoutGrid
} from 'lucide-react';

export const Sidebar = () => {
  return (
    <aside className="w-64 bg-gray-50 border-r border-gray-200 text-gray-700 flex flex-col h-full shrink-0">
      <div className="p-4 flex items-center space-x-3">
        <div className="w-10 h-10 bg-brand-500 rounded-lg flex items-center justify-center text-gray-900 shadow-sm">
          <LayoutGrid className="w-5 h-5" />
        </div>
        <div>
          <h2 className="text-gray-900 font-semibold leading-tight">Main Workspace</h2>
          <p className="text-xs text-gray-500">Productivity Hub</p>
        </div>
      </div>

      <div className="px-4 py-2">
        <button className="w-full flex items-center justify-center space-x-2 bg-brand-500 hover:bg-brand-600 text-gray-900 py-2 rounded-md font-medium transition-colors">
          <Plus className="w-4 h-4" />
          <span>New Project</span>
        </button>
      </div>

      <div className="flex-1 overflow-y-auto py-2">
        <nav className="px-3 mb-6 space-y-0.5">
          <NavLink
            to="/"
            className={({ isActive }) =>
              `flex items-center space-x-3 px-3 py-2 rounded-md transition-colors text-sm font-medium ${
                isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
              }`
            }
          >
            <Inbox className="w-4 h-4" />
            <span>Inbox</span>
          </NavLink>
          <NavLink
            to="/tasks"
            className={({ isActive }) =>
              `flex items-center space-x-3 px-3 py-2 rounded-md transition-colors text-sm font-medium ${
                isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
              }`
            }
          >
            <CheckCircle2 className="w-4 h-4" />
            <span>Tasks</span>
          </NavLink>
          <NavLink
            to="/kanban"
            className={({ isActive }) =>
              `flex items-center space-x-3 px-3 py-2 rounded-md transition-colors text-sm font-medium ${
                isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
              }`
            }
          >
            <LayoutGrid className="w-4 h-4" />
            <span>Kanban Board</span>
          </NavLink>
          <NavLink
            to="/files"
            className={({ isActive }) =>
              `flex items-center space-x-3 px-3 py-2 rounded-md transition-colors text-sm font-medium ${
                isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
              }`
            }
          >
            <FileText className="w-4 h-4" />
            <span>Files</span>
          </NavLink>
          <NavLink
            to="/calendar"
            className={({ isActive }) =>
              `flex items-center space-x-3 px-3 py-2 rounded-md transition-colors text-sm font-medium ${
                isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
              }`
            }
          >
            <CalendarIcon className="w-4 h-4" />
            <span>Calendar</span>
          </NavLink>
        </nav>

        <div className="px-3 mb-2">
          <h3 className="px-3 text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Integrations</h3>
          <div className="space-y-0.5">
            <button className="w-full flex items-center justify-between px-3 py-2 text-sm font-medium text-gray-600 hover:text-brand-600 hover:bg-brand-50 rounded-md transition-colors">
              <div className="flex items-center space-x-3">
                <Code className="w-4 h-4" />
                <span>GitHub</span>
              </div>
              <div className="w-2 h-2 rounded-full bg-green-500"></div>
            </button>
            <button className="w-full flex items-center justify-between px-3 py-2 text-sm font-medium text-gray-600 hover:text-brand-600 hover:bg-brand-50 rounded-md transition-colors">
              <div className="flex items-center space-x-3">
                <MessageSquare className="w-4 h-4" />
                <span>Slack</span>
              </div>
              <div className="w-2 h-2 rounded-full bg-green-500"></div>
            </button>
            <button className="w-full flex items-center justify-between px-3 py-2 text-sm font-medium text-gray-600 hover:text-brand-600 hover:bg-brand-50 rounded-md transition-colors">
              <div className="flex items-center space-x-3">
                <Briefcase className="w-4 h-4" />
                <span>Jira</span>
              </div>
              <div className="w-2 h-2 rounded-full bg-red-500"></div>
            </button>
          </div>
        </div>
      </div>

      <div className="p-3 border-t border-gray-200 space-y-0.5">
        <NavLink 
          to="/integrations"
          className={({ isActive }) =>
            `w-full flex items-center space-x-3 px-3 py-2 text-sm font-medium rounded-md transition-colors ${
              isActive ? 'bg-brand-50 text-brand-700' : 'text-gray-600 hover:text-brand-600 hover:bg-brand-50'
            }`
          }
        >
          <Settings className="w-4 h-4" />
          <span>Kết nối dịch vụ</span>
        </NavLink>
        <button className="w-full flex items-center space-x-3 px-3 py-2 text-sm font-medium text-gray-600 hover:text-brand-600 hover:bg-brand-50 rounded-md transition-colors">
          <HelpCircle className="w-4 h-4" />
          <span>Help</span>
        </button>
      </div>
    </aside>
  );
};
