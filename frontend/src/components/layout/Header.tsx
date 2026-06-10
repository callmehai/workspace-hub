import { Search, Bell, Settings } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

export const Header = () => {
  const { user } = useAuth();

  return (
    <header className="h-16 border-b border-gray-200 bg-gray-50 flex items-center justify-between px-6 shrink-0">
      <div className="flex-1 max-w-2xl">
        <div className="relative group">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 group-focus-within:text-brand-500" />
          <input
            type="text"
            placeholder="Search across tools..."
            className="w-full bg-white border border-gray-200 rounded-md py-1.5 pl-10 pr-4 text-sm text-gray-800 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 placeholder-gray-500 transition-colors"
          />
        </div>
      </div>
      
      <div className="flex items-center space-x-4 ml-4">
        <button className="text-gray-500 hover:text-white transition-colors relative">
          <Bell className="w-5 h-5" />
          <span className="absolute top-0 right-0 w-2 h-2 bg-red-500 rounded-full border-2 border-dark-900"></span>
        </button>
        <button className="text-gray-500 hover:text-white transition-colors">
          <Settings className="w-5 h-5" />
        </button>
        <div className="h-8 w-8 rounded-full bg-gradient-to-tr from-brand-500 to-purple-500 flex items-center justify-center text-sm font-medium text-white shadow-sm overflow-hidden border border-gray-200">
          {user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U'}
        </div>
      </div>
    </header>
  );
};
