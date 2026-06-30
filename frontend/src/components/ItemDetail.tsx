import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { 
  Star, Mail, MailOpen, Trash2, Edit3, Save, X, Calendar, MapPin, 
  Users, FileText, ExternalLink, MessageSquare, Plus, Tag, Loader2, 
  AlertCircle, ChevronRight, CheckCircle
} from 'lucide-react';
import { itemsApi, jiraApi, type JiraPriority, type JiraTransition } from '../lib/itemsApi';
import { type PatchItemRequest } from '../types/items';
import { handleApiError } from '../lib/api';
import toast from 'react-hot-toast';

interface ItemDetailProps {
  itemId: string;
  onClose?: () => void;
  onDeleted?: () => void;
}

export const ItemDetail: React.FC<ItemDetailProps> = ({ itemId, onClose, onDeleted }) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  
  const [isEditing, setIsEditing] = useState(false);
  const [newLabelName, setNewLabelName] = useState('');
  const [isAddingLabel, setIsAddingLabel] = useState(false);
  const [commentText, setCommentText] = useState('');

  // Event form edit state
  const [eventForm, setEventForm] = useState({
    title: '',
    start: '',
    end: '',
    location: '',
    attendees: ''
  });

  // File edit state
  const [fileName, setFileName] = useState('');
  const [isRenamingFile, setIsRenamingFile] = useState(false);

  // Jira Ticket edit state
  const [jiraForm, setJiraForm] = useState({
    summary: '',
    description: '',
    assignee: '',
    priority: '',
    labels: '',
    statusTransition: ''
  });

  // Fetch item by ID
  const { data: item, isLoading, isError, refetch } = useQuery({
    queryKey: ['item', itemId],
    queryFn: () => itemsApi.getItemById(itemId),
    enabled: !!itemId,
  });

  // Mutate item (writeback PATCH)
  const patchMutation = useMutation({
    mutationFn: (payload: PatchItemRequest) => itemsApi.patchItem(itemId, payload),
    onSuccess: () => {
      toast.success('Đã lưu thay đổi thành công');
      setIsEditing(false);
      setIsRenamingFile(false);
      setNewLabelName('');
      setIsAddingLabel(false);
      setCommentText('');
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => {
      handleApiError(err, 'Lỗi cập nhật dữ liệu', {
        onConflict: () => {
          refetch();
          queryClient.invalidateQueries({ queryKey: ['items'] });
        },
        navigate
      });
    }
  });

  // Delete item mutation
  const deleteMutation = useMutation({
    mutationFn: () => itemsApi.deleteItem(itemId),
    onSuccess: () => {
      toast.success('Đã xóa item thành công');
      queryClient.invalidateQueries({ queryKey: ['items'] });
      if (onDeleted) onDeleted();
      if (onClose) onClose();
    },
    onError: (err) => {
      handleApiError(err, 'Không thể xóa dữ liệu', {
        onConflict: () => {
          refetch();
          queryClient.invalidateQueries({ queryKey: ['items'] });
        },
        navigate
      });
    }
  });

  // Fetch Jira Transitions helper if Jira Ticket
  const isJira = item?.type === 'Ticket';
  const connectionId = item?.metadataJson ? JSON.parse(item.metadataJson).connectionId || (item as unknown as { connectionId?: string }).connectionId : undefined;
  
  const { data: transitions = [] } = useQuery({
    queryKey: ['jiraTransitions', itemId, connectionId],
    queryFn: () => jiraApi.getTransitions(connectionId, itemId),
    enabled: isJira && !!connectionId && !!itemId,
  });

  const { data: priorities = [] } = useQuery({
    queryKey: ['jiraPriorities', connectionId],
    queryFn: () => jiraApi.getPriorities(connectionId),
    enabled: isJira && !!connectionId,
  });

  if (isLoading) {
    return (
      <div className="h-full flex items-center justify-center bg-[#13141f] text-gray-400">
        <div className="flex flex-col items-center space-y-3">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500" />
          <span className="text-sm font-medium">Đang tải chi tiết...</span>
        </div>
      </div>
    );
  }

  if (isError || !item) {
    return (
      <div className="h-full flex flex-col items-center justify-center p-6 bg-[#13141f] text-gray-400">
        <AlertCircle className="w-12 h-12 text-red-500 mb-3" />
        <h3 className="text-base font-semibold text-white mb-1">Không thể tải thông tin chi tiết</h3>
        <p className="text-xs text-gray-500 text-center max-w-xs mb-4">Vui lòng thử lại sau hoặc tải lại trang.</p>
        <button 
          onClick={() => refetch()}
          className="px-4 py-2 bg-brand-500 text-white rounded-lg text-sm font-medium hover:bg-brand-600 transition-colors"
        >
          Tải lại
        </button>
      </div>
    );
  }

  const metadata = item.metadataJson ? JSON.parse(item.metadataJson) : {};

  // Setup Event initial form values
  const startEditingEvent = () => {
    let startVal = '';
    let endVal = '';
    if (metadata.start) {
      startVal = new Date(metadata.start).toISOString().slice(0, 16);
    }
    if (metadata.end) {
      endVal = new Date(metadata.end).toISOString().slice(0, 16);
    }
    setEventForm({
      title: item.title,
      start: startVal,
      end: endVal,
      location: metadata.location || '',
      attendees: metadata.attendees ? metadata.attendees.join(', ') : ''
    });
    setIsEditing(true);
  };

  const handleSaveEvent = () => {
    if (!eventForm.title || !eventForm.start || !eventForm.end) {
      toast.error('Vui lòng điền đầy đủ tiêu đề, thời gian bắt đầu và kết thúc');
      return;
    }
    const startIso = new Date(eventForm.start).toISOString();
    const endIso = new Date(eventForm.end).toISOString();
    
    if (new Date(startIso) >= new Date(endIso)) {
      toast.error('Thời gian bắt đầu phải trước thời gian kết thúc');
      return;
    }

    const attendeesArray = eventForm.attendees
      ? eventForm.attendees.split(',').map(email => email.trim()).filter(email => email.length > 0)
      : [];

    patchMutation.mutate({
      title: eventForm.title,
      start: startIso,
      end: endIso,
      location: eventForm.location || null,
      attendees: attendeesArray
    });
  };

  // Setup File edit values
  const startRenamingFile = () => {
    setFileName(item.title);
    setIsRenamingFile(true);
  };

  const handleRenameFile = () => {
    if (!fileName.trim()) {
      toast.error('Tên tệp không được để trống');
      return;
    }
    if (fileName === item.title) {
      setIsRenamingFile(false);
      return;
    }
    patchMutation.mutate({
      name: fileName.trim()
    });
  };

  // Setup Jira initial edit values
  const startEditingJira = () => {
    setJiraForm({
      summary: item.title,
      description: item.snippet,
      assignee: metadata.assignee || '',
      priority: metadata.priority || '',
      labels: metadata.labels ? metadata.labels.join(', ') : '',
      statusTransition: ''
    });
    setIsEditing(true);
  };

  const handleSaveJira = () => {
    if (!jiraForm.summary.trim()) {
      toast.error('Tiêu đề không được để trống');
      return;
    }
    
    const labelsArray = jiraForm.labels
      ? jiraForm.labels.split(',').map(l => l.trim()).filter(l => l.length > 0)
      : [];

    patchMutation.mutate({
      summary: jiraForm.summary,
      description: jiraForm.description || null,
      assignee: jiraForm.assignee || null,
      priority: jiraForm.priority || null,
      labels: labelsArray
    });
  };

  // Add Comment for Jira
  const handleAddComment = () => {
    if (!commentText.trim()) return;
    patchMutation.mutate({
      comment: commentText.trim()
    });
  };

  // Transition Jira status
  const handleTransitionStatus = (transitionNameOrId: string) => {
    if (!transitionNameOrId) return;
    patchMutation.mutate({
      statusTransition: transitionNameOrId
    });
  };

  // Labels Add/Remove for Email
  const handleAddLabel = () => {
    if (!newLabelName.trim()) return;
    patchMutation.mutate({
      addLabels: [newLabelName.trim().toUpperCase()]
    });
  };

  const handleRemoveLabel = (labelName: string) => {
    patchMutation.mutate({
      removeLabels: [labelName]
    });
  };

  // Formatter for file size
  const formatBytes = (bytes?: number) => {
    if (!bytes) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  return (
    <div className="h-full flex flex-col bg-[#13141f] text-gray-200">
      {/* Toolbar */}
      <div className="h-14 border-b border-gray-800 flex items-center justify-between px-6 shrink-0 bg-[#0f1019]">
        <div className="flex items-center space-x-4">
          {item.type === 'Email' && (
            <>
              {/* Star/Unstar */}
              <button 
                onClick={() => patchMutation.mutate({ isStarred: !metadata.isStarred })}
                disabled={patchMutation.isPending}
                className={`p-1.5 rounded-lg transition-colors hover:bg-gray-800 ${metadata.isStarred ? 'text-amber-400' : 'text-gray-500 hover:text-white'}`}
                title={metadata.isStarred ? "Bỏ gắn sao" : "Gắn sao"}
              >
                <Star className="w-5 h-5 fill-current" />
              </button>

              {/* Read/Unread */}
              <button 
                onClick={() => patchMutation.mutate({ isUnread: !metadata.isUnread })}
                disabled={patchMutation.isPending}
                className="p-1.5 rounded-lg text-gray-500 hover:text-white transition-colors hover:bg-gray-800"
                title={metadata.isUnread ? "Đánh dấu đã đọc" : "Đánh dấu chưa đọc"}
              >
                {metadata.isUnread ? <MailOpen className="w-5 h-5" /> : <Mail className="w-5 h-5" />}
              </button>

              {/* Trash/Restore Email */}
              <button 
                onClick={() => patchMutation.mutate({ isTrashed: !metadata.isTrashed })}
                disabled={patchMutation.isPending}
                className={`p-1.5 rounded-lg transition-colors hover:bg-gray-800 ${metadata.isTrashed ? 'text-red-400' : 'text-gray-500 hover:text-white'}`}
                title={metadata.isTrashed ? "Khôi phục từ Thùng rác" : "Cho vào Thùng rác"}
              >
                <Trash2 className="w-5 h-5" />
              </button>
            </>
          )}

          {item.type === 'File' && (
            <>
              {/* Rename File */}
              <button 
                onClick={startRenamingFile}
                disabled={patchMutation.isPending}
                className="p-1.5 rounded-lg text-gray-500 hover:text-white transition-colors hover:bg-gray-800"
                title="Đổi tên tệp"
              >
                <Edit3 className="w-5 h-5" />
              </button>

              {/* Trash/Restore File */}
              <button 
                onClick={() => patchMutation.mutate({ isTrashed: !metadata.isTrashed })}
                disabled={patchMutation.isPending}
                className={`p-1.5 rounded-lg transition-colors hover:bg-gray-800 ${metadata.isTrashed ? 'text-red-400' : 'text-gray-500 hover:text-white'}`}
                title={metadata.isTrashed ? "Khôi phục từ Thùng rác" : "Đưa vào Thùng rác"}
              >
                <Trash2 className="w-5 h-5" />
              </button>
            </>
          )}

          {item.type === 'Event' && !isEditing && (
            <>
              {/* Edit Event */}
              <button 
                onClick={startEditingEvent}
                className="p-1.5 rounded-lg text-gray-500 hover:text-white transition-colors hover:bg-gray-800"
                title="Sửa sự kiện"
              >
                <Edit3 className="w-5 h-5" />
              </button>
            </>
          )}

          {item.type === 'Ticket' && !isEditing && (
            <>
              {/* Edit Ticket */}
              <button 
                onClick={startEditingJira}
                className="p-1.5 rounded-lg text-gray-500 hover:text-white transition-colors hover:bg-gray-800"
                title="Sửa Ticket"
              >
                <Edit3 className="w-5 h-5" />
              </button>
            </>
          )}

          {/* Delete Permanently (all types except Note) */}
          {item.type !== 'Note' && (
            <button 
              onClick={() => {
                if (window.confirm('Bạn có chắc chắn muốn xóa vĩnh viễn mục này khỏi máy chủ nhà cung cấp? Thao tác này không thể hoàn tác.')) {
                  deleteMutation.mutate();
                }
              }}
              disabled={deleteMutation.isPending}
              className="p-1.5 rounded-lg text-gray-500 hover:text-red-500 transition-colors hover:bg-red-950/20"
              title="Xóa vĩnh viễn"
            >
              {deleteMutation.isPending ? <Loader2 className="w-5 h-5 animate-spin" /> : <Trash2 className="w-5 h-5 text-red-500" />}
            </button>
          )}
        </div>

        <div className="flex items-center space-x-2">
          {onClose && (
            <button 
              onClick={onClose}
              className="p-1.5 rounded-lg text-gray-500 hover:text-white transition-colors hover:bg-gray-800"
            >
              <X className="w-5 h-5" />
            </button>
          )}
        </div>
      </div>

      {/* Main Details Area */}
      <div className="flex-1 overflow-y-auto p-6 md:p-8 space-y-6">
        
        {/* Render Title/Header */}
        {isEditing ? (
          <div className="space-y-4 bg-[#1c1d2c] border border-gray-800 p-5 rounded-2xl">
            <h3 className="text-sm font-semibold text-gray-400 uppercase tracking-wider mb-2">Chỉnh sửa thông tin</h3>
            
            {item.type === 'Event' && (
              <div className="space-y-4">
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Tiêu đề sự kiện</label>
                  <input 
                    type="text" 
                    value={eventForm.title}
                    onChange={e => setEventForm({...eventForm, title: e.target.value})}
                    className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                  />
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Thời gian bắt đầu (Local)</label>
                    <input 
                      type="datetime-local" 
                      value={eventForm.start}
                      onChange={e => setEventForm({...eventForm, start: e.target.value})}
                      className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Thời gian kết thúc (Local)</label>
                    <input 
                      type="datetime-local" 
                      value={eventForm.end}
                      onChange={e => setEventForm({...eventForm, end: e.target.value})}
                      className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Địa điểm</label>
                  <input 
                    type="text" 
                    value={eventForm.location}
                    onChange={e => setEventForm({...eventForm, location: e.target.value})}
                    className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    placeholder="VD: Phòng họp số 3"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Người tham gia (Phân cách bởi dấu phẩy)</label>
                  <input 
                    type="text" 
                    value={eventForm.attendees}
                    onChange={e => setEventForm({...eventForm, attendees: e.target.value})}
                    className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    placeholder="vd1@gmail.com, vd2@gmail.com"
                  />
                </div>
                
                <div className="flex justify-end space-x-3 pt-2">
                  <button 
                    onClick={() => setIsEditing(false)}
                    className="px-4 py-2 border border-gray-800 text-gray-400 hover:text-white rounded-lg text-sm transition-colors"
                  >
                    Hủy
                  </button>
                  <button 
                    onClick={handleSaveEvent}
                    disabled={patchMutation.isPending}
                    className="px-5 py-2 bg-brand-500 text-white rounded-lg text-sm font-medium hover:bg-brand-600 transition-colors flex items-center space-x-1.5"
                  >
                    {patchMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                    <span>Lưu</span>
                  </button>
                </div>
              </div>
            )}

            {item.type === 'Ticket' && (
              <div className="space-y-4">
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Summary (Tóm tắt)</label>
                  <input 
                    type="text" 
                    value={jiraForm.summary}
                    onChange={e => setJiraForm({...jiraForm, summary: e.target.value})}
                    className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Mô tả (Description)</label>
                  <textarea 
                    value={jiraForm.description}
                    onChange={e => setJiraForm({...jiraForm, description: e.target.value})}
                    className="w-full h-32 bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm resize-none font-sans"
                  />
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Priority (Độ ưu tiên)</label>
                    <select
                      value={jiraForm.priority}
                      onChange={e => setJiraForm({...jiraForm, priority: e.target.value})}
                      className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    >
                      <option value="">Chọn độ ưu tiên...</option>
                      {priorities.map((p: JiraPriority) => (
                        <option key={p.id} value={p.name}>{p.name}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Assignee ID (AccountId)</label>
                    <input 
                      type="text" 
                      value={jiraForm.assignee}
                      onChange={e => setJiraForm({...jiraForm, assignee: e.target.value})}
                      placeholder="VD: 5f1c2438abc123"
                      className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-500 mb-1">Labels (Cách nhau bởi dấu phẩy)</label>
                  <input 
                    type="text" 
                    value={jiraForm.labels}
                    onChange={e => setJiraForm({...jiraForm, labels: e.target.value})}
                    placeholder="label1, label2"
                    className="w-full bg-[#0f1019] border border-gray-800 rounded-lg px-3.5 py-2.5 text-white focus:outline-none focus:border-brand-500 transition-colors text-sm"
                  />
                </div>
                
                <div className="flex justify-end space-x-3 pt-2">
                  <button 
                    onClick={() => setIsEditing(false)}
                    className="px-4 py-2 border border-gray-800 text-gray-400 hover:text-white rounded-lg text-sm transition-colors"
                  >
                    Hủy
                  </button>
                  <button 
                    onClick={handleSaveJira}
                    disabled={patchMutation.isPending}
                    className="px-5 py-2 bg-brand-500 text-white rounded-lg text-sm font-medium hover:bg-brand-600 transition-colors flex items-center space-x-1.5"
                  >
                    {patchMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                    <span>Lưu</span>
                  </button>
                </div>
              </div>
            )}
          </div>
        ) : (
          <div className="space-y-4">
            <div className="flex items-center space-x-2">
              <span className="text-[10px] uppercase font-extrabold tracking-wider bg-brand-500/10 text-brand-400 border border-brand-500/20 px-2 py-0.5 rounded">
                {item.type}
              </span>
              {item.isImportant && (
                <span className="text-[10px] uppercase font-extrabold tracking-wider bg-red-500/10 text-red-400 border border-red-500/20 px-2 py-0.5 rounded">
                  Quan trọng
                </span>
              )}
            </div>
            
            {/* File title editing inline */}
            {isRenamingFile ? (
              <div className="flex items-center space-x-2">
                <input 
                  type="text" 
                  value={fileName}
                  onChange={e => setFileName(e.target.value)}
                  className="bg-[#0f1019] border border-brand-500 rounded-lg py-2 px-3 text-lg font-bold text-white focus:outline-none flex-1"
                  autoFocus
                  onKeyDown={e => {
                    if (e.key === 'Enter') handleRenameFile();
                    else if (e.key === 'Escape') setIsRenamingFile(false);
                  }}
                  onBlur={handleRenameFile}
                />
                <button onClick={handleRenameFile} className="p-2 bg-brand-500 text-white rounded-lg hover:bg-brand-600">
                  <CheckCircle className="w-5 h-5" />
                </button>
              </div>
            ) : (
              <h1 className="text-xl md:text-2xl font-bold text-white leading-tight">
                {item.title}
              </h1>
            )}

            {/* Email meta information */}
            {item.type === 'Email' && (
              <div className="bg-[#1c1d2c] border border-gray-800 rounded-xl p-4 text-xs space-y-2 text-gray-400">
                <div className="flex"><span className="w-16 font-semibold text-gray-500">Từ:</span> <span className="text-gray-200">{metadata.from}</span></div>
                {metadata.to && <div className="flex"><span className="w-16 font-semibold text-gray-500">Tới:</span> <span className="text-gray-200">{metadata.to}</span></div>}
                <div className="flex"><span className="w-16 font-semibold text-gray-500">Ngày gửi:</span> <span className="text-gray-200">{new Date(item.occurredAt).toLocaleString()}</span></div>
              </div>
            )}

            {/* Event information */}
            {item.type === 'Event' && (
              <div className="bg-[#1c1d2c] border border-gray-800 rounded-xl p-5 space-y-3.5 text-sm">
                <div className="flex items-center space-x-3 text-gray-400">
                  <Calendar className="w-4 h-4 text-brand-500" />
                  <div className="flex-1">
                    <div className="font-semibold text-gray-300">Thời gian</div>
                    <div className="text-xs">Bắt đầu: {metadata.start ? new Date(metadata.start).toLocaleString() : 'N/A'}</div>
                    <div className="text-xs">Kết thúc: {metadata.end ? new Date(metadata.end).toLocaleString() : 'N/A'}</div>
                  </div>
                </div>
                {metadata.location && (
                  <div className="flex items-center space-x-3 text-gray-400">
                    <MapPin className="w-4 h-4 text-brand-500" />
                    <div>
                      <div className="font-semibold text-gray-300">Địa điểm</div>
                      <div className="text-xs">{metadata.location}</div>
                    </div>
                  </div>
                )}
                {metadata.attendees && metadata.attendees.length > 0 && (
                  <div className="flex items-start space-x-3 text-gray-400">
                    <Users className="w-4 h-4 text-brand-500 mt-1" />
                    <div>
                      <div className="font-semibold text-gray-300">Người tham gia ({metadata.attendees.length})</div>
                      <div className="flex flex-wrap gap-1.5 mt-2">
                        {metadata.attendees.map((email: string) => (
                          <span key={email} className="text-xs bg-[#0f1019] px-2.5 py-1 rounded-full border border-gray-800 text-gray-300">
                            {email}
                          </span>
                        ))}
                      </div>
                    </div>
                  </div>
                )}
                {metadata.meetUrl && (
                  <div className="pt-2">
                    <a 
                      href={metadata.meetUrl} 
                      target="_blank" 
                      rel="noopener noreferrer" 
                      className="inline-flex items-center space-x-2 bg-emerald-600 hover:bg-emerald-700 text-white px-4 py-2 rounded-lg font-medium text-xs transition-colors"
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      <span>Tham gia Google Meet</span>
                    </a>
                  </div>
                )}
              </div>
            )}

            {/* File information */}
            {item.type === 'File' && (
              <div className="bg-[#1c1d2c] border border-gray-800 rounded-xl p-5 space-y-3 text-sm">
                <div className="flex items-center space-x-3 text-gray-400">
                  <FileText className="w-5 h-5 text-brand-500" />
                  <div>
                    <div className="font-semibold text-gray-300">Chi tiết tệp tin</div>
                    {metadata.size && <div className="text-xs mt-0.5">Dung lượng: {formatBytes(metadata.size)}</div>}
                    {metadata.mimeType && <div className="text-xs text-gray-500 font-mono mt-0.5">{metadata.mimeType}</div>}
                  </div>
                </div>
                {metadata.webViewLink && (
                  <div className="pt-2">
                    <a 
                      href={metadata.webViewLink} 
                      target="_blank" 
                      rel="noopener noreferrer" 
                      className="inline-flex items-center space-x-2 bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-lg font-medium text-xs transition-colors"
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      <span>Mở trong Google Drive</span>
                    </a>
                  </div>
                )}
              </div>
            )}

            {/* Jira Ticket details */}
            {item.type === 'Ticket' && (
              <div className="bg-[#1c1d2c] border border-gray-800 rounded-xl p-5 space-y-4 text-sm">
                <div className="grid grid-cols-2 gap-4 text-xs">
                  <div><span className="block text-gray-500 font-semibold mb-0.5">Mã Ticket</span> <a href={metadata.issueUrl} target="_blank" rel="noopener noreferrer" className="text-brand-400 hover:underline inline-flex items-center space-x-1 font-mono font-bold text-sm">{metadata.issueKey || 'N/A'} <ExternalLink className="w-3 h-3 ml-0.5 inline" /></a></div>
                  <div><span className="block text-gray-500 font-semibold mb-0.5">Dự án</span> <span className="text-gray-200 font-medium text-sm">{metadata.projectKey || 'N/A'}</span></div>
                  <div><span className="block text-gray-500 font-semibold mb-0.5">Người giải quyết (Assignee)</span> <span className="text-gray-200 text-sm font-medium">{metadata.assignee || 'Unassigned'}</span></div>
                  <div><span className="block text-gray-500 font-semibold mb-0.5">Độ ưu tiên (Priority)</span> <span className="text-gray-200 text-sm font-medium">{metadata.priority || 'Medium'}</span></div>
                </div>

                <div className="border-t border-gray-800 pt-3">
                  <span className="block text-xs text-gray-500 font-semibold mb-2">Trạng thái hiện tại: <span className="text-brand-400 font-bold ml-1">{metadata.status || 'To Do'}</span></span>
                  
                  {transitions.length > 0 ? (
                    <div className="space-y-1.5">
                      <span className="block text-xs text-gray-400">Đổi trạng thái:</span>
                      <div className="flex flex-wrap gap-2">
                        {transitions.map((t: JiraTransition) => (
                          <button
                            key={t.id}
                            onClick={() => handleTransitionStatus(t.id)}
                            disabled={patchMutation.isPending}
                            className="text-xs bg-[#0f1019] hover:bg-brand-500/10 hover:border-brand-500/30 text-gray-300 border border-gray-800 rounded-lg px-2.5 py-1.5 font-medium transition-colors"
                          >
                            {t.name} <ChevronRight className="w-3.5 h-3.5 inline text-gray-500" /> <span className="text-gray-500 text-[10px]">{t.toStatusName}</span>
                          </button>
                        ))}
                      </div>
                    </div>
                  ) : (
                    <span className="text-xs text-gray-600 italic">Không có chuyển đổi trạng thái nào khả dụng từ Jira.</span>
                  )}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Labels tag area for Email */}
        {item.type === 'Email' && (
          <div className="border-t border-gray-800 pt-4">
            <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2 flex items-center space-x-1">
              <Tag className="w-3.5 h-3.5" />
              <span>Nhãn Gmail (Labels)</span>
            </h4>
            
            <div className="flex flex-wrap gap-1.5 items-center">
              {metadata.labels && metadata.labels.map((label: string) => (
                <span key={label} className="inline-flex items-center space-x-1 text-xs bg-brand-500/10 text-brand-400 border border-brand-500/20 px-2.5 py-1 rounded-full">
                  <span>{label}</span>
                  <button 
                    onClick={() => handleRemoveLabel(label)}
                    disabled={patchMutation.isPending}
                    className="hover:text-red-500 rounded-full font-bold ml-1 w-3.5 h-3.5 flex items-center justify-center bg-brand-500/20 hover:bg-red-500/10 transition-colors"
                  >
                    &times;
                  </button>
                </span>
              ))}

              {isAddingLabel ? (
                <div className="flex items-center space-x-1.5">
                  <input 
                    type="text" 
                    value={newLabelName}
                    onChange={e => setNewLabelName(e.target.value)}
                    placeholder="NHÃN_MỚI"
                    className="bg-[#0f1019] border border-gray-800 rounded px-2 py-0.5 text-xs focus:outline-none focus:border-brand-500 uppercase"
                    onKeyDown={e => {
                      if (e.key === 'Enter') handleAddLabel();
                    }}
                    autoFocus
                  />
                  <button onClick={handleAddLabel} className="text-xs text-brand-500 hover:text-brand-400 font-semibold">Thêm</button>
                  <button onClick={() => setIsAddingLabel(false)} className="text-xs text-gray-500 hover:text-white font-semibold">Hủy</button>
                </div>
              ) : (
                <button 
                  onClick={() => setIsAddingLabel(true)}
                  className="inline-flex items-center space-x-1 text-xs text-gray-500 hover:text-white border border-dashed border-gray-800 px-2.5 py-1 rounded-full transition-colors hover:border-gray-500"
                >
                  <Plus className="w-3 h-3" />
                  <span>Thêm nhãn</span>
                </button>
              )}
            </div>
          </div>
        )}

        {/* Content Snippet / Description Box */}
        {(!isEditing || item.type !== 'Ticket') && (
          <div className="border-t border-gray-800 pt-5 space-y-2 flex-1">
            <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Nội dung</h4>
            <div className="bg-[#1c1d2c] border border-gray-800 rounded-xl p-5 text-sm font-sans leading-relaxed text-gray-300 max-h-[300px] overflow-y-auto whitespace-pre-wrap">
              {item.type === 'Note' ? (
                // Note has markdown stored in metadata
                metadata.contentMarkdown || item.snippet
              ) : (
                item.snippet || <span className="text-gray-500 italic">Không có nội dung mô tả.</span>
              )}
            </div>
          </div>
        )}

        {/* Jira Comments area */}
        {item.type === 'Ticket' && !isEditing && (
          <div className="border-t border-gray-800 pt-5 space-y-4">
            <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2 flex items-center space-x-1.5">
              <MessageSquare className="w-3.5 h-3.5" />
              <span>Bình luận Jira</span>
            </h4>

            {/* Comments list if comments are in metadata or mock */}
            <div className="space-y-3">
              {/* Add Comment Input */}
              <div className="flex flex-col space-y-2 bg-[#1c1d2c] border border-gray-800 p-3 rounded-xl">
                <textarea 
                  value={commentText}
                  onChange={e => setCommentText(e.target.value)}
                  placeholder="Thêm bình luận lên Jira..."
                  className="w-full h-16 bg-[#0f1019] border border-gray-800 rounded-lg p-2.5 text-xs focus:outline-none focus:border-brand-500 resize-none font-sans text-gray-300 leading-normal"
                />
                <div className="flex justify-end">
                  <button 
                    onClick={handleAddComment}
                    disabled={!commentText.trim() || patchMutation.isPending}
                    className="px-3.5 py-1.5 bg-brand-500 disabled:bg-gray-800 disabled:text-gray-600 text-white rounded-lg text-xs font-medium hover:bg-brand-600 transition-colors flex items-center space-x-1"
                  >
                    {patchMutation.isPending && patchMutation.variables?.comment ? <Loader2 className="w-3 h-3 animate-spin" /> : null}
                    <span>Gửi bình luận</span>
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

      </div>
    </div>
  );
};
