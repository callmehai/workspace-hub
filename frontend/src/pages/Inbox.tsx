import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Filter, Loader2, AlertCircle, Calendar, FileText, Mail, MessageSquare, StickyNote, Star } from 'lucide-react';
import { itemsApi } from '../lib/itemsApi';
import { type ItemResponse } from '../types/items';
import { ItemDetail } from '../components/ItemDetail';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';

export const Inbox = () => {
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);
  const [selectedFilter, setSelectedFilter] = useState<'all' | 'unread' | 'important'>('all');
  const [search, setSearch] = useState('');

  // Fetch Items
  const { data: pagedResult, isLoading, isError, refetch } = useQuery({
    queryKey: ['items', search],
    queryFn: () => itemsApi.getItems({ 
      search: search.trim() || undefined,
      limit: 100 
    })
  });

  const items = pagedResult?.items || [];

  // Filter items based on selected category
  const filteredItems = items.filter(item => {
    if (selectedFilter === 'important') {
      return item.isImportant;
    }
    if (selectedFilter === 'unread') {
      if (item.type === 'Email' && item.metadataJson) {
        try {
          const meta = JSON.parse(item.metadataJson);
          return meta.isUnread;
        } catch {
          return false;
        }
      }
      return false;
    }
    return true;
  });

  const getItemIcon = (type: string) => {
    switch (type) {
      case 'Email':
        return <Mail className="w-4 h-4 text-blue-500" />;
      case 'Event':
        return <Calendar className="w-4 h-4 text-emerald-500" />;
      case 'File':
        return <FileText className="w-4 h-4 text-amber-500" />;
      case 'Ticket':
        return <MessageSquare className="w-4 h-4 text-purple-500" />;
      default:
        return <StickyNote className="w-4 h-4 text-gray-500" />;
    }
  };

  const getSourceLabel = (item: ItemResponse) => {
    if (item.type === 'Ticket' && item.metadataJson) {
      try {
        const meta = JSON.parse(item.metadataJson);
        return meta.issueKey || 'Jira';
      } catch {
        return 'Jira';
      }
    }
    return item.type;
  };

  return (
    <div className="h-full flex flex-col md:flex-row overflow-hidden bg-gray-50">
      {/* Left Column - List */}
      <div className="w-full md:w-5/12 lg:w-4/12 flex flex-col border-r border-gray-200 bg-white shrink-0">
        
        {/* Search & Filter Bar */}
        <div className="p-4 border-b border-gray-200 space-y-3 shrink-0">
          <input 
            type="text"
            placeholder="Tìm kiếm tiêu đề, nội dung..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full bg-gray-50 border border-gray-200 rounded-lg py-1.5 px-3.5 text-sm text-gray-800 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 transition-colors placeholder-gray-400"
          />
          
          <div className="flex items-center space-x-2 overflow-x-auto hide-scrollbar">
            <button className="flex items-center space-x-1.5 px-2.5 py-1.5 bg-gray-50 hover:bg-gray-100 text-gray-600 rounded-md text-xs border border-gray-200 transition-colors shrink-0">
              <Filter className="w-3.5 h-3.5" />
              <span>Lọc</span>
            </button>
            <div className="h-4 border-l border-gray-200 mx-1 shrink-0"></div>
            
            <button 
              onClick={() => setSelectedFilter('all')}
              className={`px-3 py-1.5 rounded-md text-xs font-semibold whitespace-nowrap transition-colors ${
                selectedFilter === 'all' 
                  ? 'bg-brand-500/10 text-brand-700 border border-brand-500/20' 
                  : 'text-gray-500 hover:text-gray-800 hover:bg-gray-50 border border-transparent'
              }`}
            >
              Tất cả
            </button>
            
            <button 
              onClick={() => setSelectedFilter('unread')}
              className={`px-3 py-1.5 rounded-md text-xs font-semibold whitespace-nowrap transition-colors ${
                selectedFilter === 'unread' 
                  ? 'bg-brand-500/10 text-brand-700 border border-brand-500/20' 
                  : 'text-gray-500 hover:text-gray-800 hover:bg-gray-50 border border-transparent'
              }`}
            >
              Chưa đọc
            </button>
            
            <button 
              onClick={() => setSelectedFilter('important')}
              className={`px-3 py-1.5 flex items-center space-x-1.5 rounded-md text-xs font-semibold whitespace-nowrap transition-colors ${
                selectedFilter === 'important' 
                  ? 'bg-brand-500/10 text-brand-700 border border-brand-500/20' 
                  : 'text-gray-500 hover:text-gray-800 hover:bg-gray-50 border border-transparent'
              }`}
            >
              <span className="w-1.5 h-1.5 rounded-full bg-red-500"></span>
              <span>Quan trọng</span>
            </button>
          </div>
        </div>

        {/* Items List */}
        <div className="flex-1 overflow-y-auto">
          {isLoading ? (
            <div className="flex flex-col items-center justify-center py-20 text-gray-400 space-y-2">
              <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
              <span className="text-xs">Đang tải dữ liệu...</span>
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center justify-center p-6 py-20 text-center text-gray-400">
              <AlertCircle className="w-10 h-10 text-red-500 mb-2" />
              <h4 className="text-sm font-semibold text-gray-800">Lỗi tải danh sách</h4>
              <button 
                onClick={() => refetch()} 
                className="mt-3 text-xs bg-brand-500 text-white px-3 py-1.5 rounded-md"
              >
                Thử lại
              </button>
            </div>
          ) : filteredItems.length === 0 ? (
            <div className="flex flex-col items-center justify-center p-6 py-20 text-center text-gray-400">
              <span className="text-xs">Không tìm thấy mục nào phù hợp.</span>
            </div>
          ) : (
            <div className="divide-y divide-gray-100">
              {filteredItems.map(item => {
                const isSelected = item.id === selectedItemId;
                let isStarred = false;
                let isUnread = false;

                if (item.type === 'Email' && item.metadataJson) {
                  try {
                    const meta = JSON.parse(item.metadataJson);
                    isStarred = meta.isStarred;
                    isUnread = meta.isUnread;
                  } catch {
                    // Ignore parsing error
                  }
                }

                return (
                  <div 
                    key={item.id}
                    onClick={() => setSelectedItemId(item.id)}
                    className={`p-4 transition-colors cursor-pointer group flex gap-3 border-l-2 ${
                      isSelected 
                        ? 'bg-brand-500/5 border-brand-500' 
                        : isUnread 
                          ? 'bg-gray-50/50 border-transparent font-medium' 
                          : 'hover:bg-gray-50 border-transparent'
                    }`}
                  >
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center justify-between mb-1.5">
                        <div className="flex items-center space-x-2 text-xs text-gray-500">
                          <span className="inline-flex items-center gap-1 font-semibold text-gray-700 bg-gray-100 px-1.5 py-0.5 rounded text-[10px]">
                            {getItemIcon(item.type)}
                            <span>{getSourceLabel(item)}</span>
                          </span>
                          <span>•</span>
                          <span>{formatDistanceToNow(new Date(item.occurredAt), { addSuffix: true, locale: vi })}</span>
                        </div>
                        <div className="flex items-center space-x-1.5">
                          {isStarred && <Star className="w-3.5 h-3.5 text-amber-400 fill-current" />}
                          {isUnread && <span className="w-2 h-2 rounded-full bg-brand-500" />}
                        </div>
                      </div>
                      <h4 className={`text-sm font-semibold mb-1 truncate transition-colors ${
                        isSelected ? 'text-brand-600' : 'text-gray-900 group-hover:text-brand-600'
                      }`}>
                        {item.title}
                      </h4>
                      <p className="text-xs text-gray-500 line-clamp-2 mb-2 leading-relaxed">
                        {item.snippet}
                      </p>
                      <div className="flex items-center space-x-2">
                        {item.isImportant && (
                          <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[9px] font-bold bg-red-50 text-red-600 border border-red-100">
                            Quan trọng
                          </span>
                        )}
                        <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[9px] font-semibold ${
                          item.status === 'Done' 
                            ? 'bg-green-50 text-green-700 border border-green-100' 
                            : item.status === 'Doing'
                              ? 'bg-blue-50 text-blue-700 border border-blue-100'
                              : 'bg-gray-100 text-gray-600 border border-gray-200'
                        }`}>
                          {item.status}
                        </span>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Right Column - Detail */}
      <div className="flex-1 flex flex-col bg-gray-50 min-w-0">
        {selectedItemId ? (
          <ItemDetail 
            itemId={selectedItemId}
            onClose={() => setSelectedItemId(null)}
            onDeleted={() => setSelectedItemId(null)}
          />
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center text-gray-400 p-8">
            <div className="w-16 h-16 bg-gray-100 rounded-2xl flex items-center justify-center mb-4">
              <Mail className="w-8 h-8 text-gray-400" />
            </div>
            <h3 className="text-base font-semibold text-gray-800 mb-1">Chưa chọn mục nào</h3>
            <p className="text-xs text-gray-500 text-center max-w-xs">
              Vui lòng chọn một email, sự kiện, tệp hoặc ticket từ danh sách bên trái để xem nội dung chi tiết và thực hiện các thao tác ghi dữ liệu.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};
