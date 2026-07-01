import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { connectionsApi } from '../lib/connectionsApi';
import { handleApiError } from '../lib/errorUtils';
import toast from 'react-hot-toast';
import { Loader2, Lock, Plus, RefreshCw, AlertCircle } from 'lucide-react';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';

const SERVICES = [
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Gmail',
    name: 'Google Gmail',
    description: 'Đồng bộ hộp thư trực tiếp vào không gian làm việc.',
    icon: '/icons/gmail.svg',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'GCal',
    name: 'Google Calendar',
    description: 'Quản lý sự kiện và lịch trình một cách liền mạch.',
    icon: '/icons/gcal.svg',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Drive',
    name: 'Google Drive',
    description: 'Truy cập và sắp xếp tệp tin từ Drive.',
    icon: '/icons/drive.svg',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'jira',
    provider: 'atlassian',
    serviceType: 'Jira',
    name: 'Atlassian Jira',
    description: 'Nhập ticket, theo dõi sprint và cập nhật tiến độ.',
    icon: '/icons/jira.svg',
    bgColor: 'bg-gray-50',
  },
];

export const Integrations = () => {
  const queryClient = useQueryClient();

  const { data: connections = [], isLoading: loading, isError, refetch, isFetching } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
    retry: false,
  });

  const disconnectMutation = useMutation({
    mutationFn: connectionsApi.disconnect,
    onSuccess: () => {
      toast.success('Đã ngắt kết nối thành công');
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
    onError: (err) => handleApiError(err, 'Không thể ngắt kết nối'),
  });

  const connectMutation = useMutation({
    mutationFn: (params: { integrationKey: string; serviceType: string; redirectUri: string }) =>
      connectionsApi.startOAuth(params),
    onSuccess: (res) => {
      window.location.assign(res.authorizationUrl);
    },
    onError: (err) => handleApiError(err, 'Không thể bắt đầu kết nối'),
  });

  const syncMutation = useMutation({
    mutationFn: connectionsApi.syncConnection,
    onSuccess: () => {
      toast.success('Đã gửi yêu cầu đồng bộ');
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
    onError: (err) => handleApiError(err, 'Đồng bộ thất bại'),
  });

  const handleConnect = (integrationKey: string, serviceType: string) => {
    const redirectUri = `${window.location.origin}/oauth/callback`;
    connectMutation.mutate({ integrationKey, serviceType, redirectUri });
  };


  return (
    <div className="p-5 md:p-8 max-w-5xl mx-auto text-gray-800">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 mb-1">
          Kết nối dịch vụ
          {isFetching && !loading && <Loader2 className="w-4 h-4 animate-spin inline-block ml-2 text-gray-400" />}
        </h1>
        <p className="text-sm text-gray-500">
          Cấp quyền để Workspace Hub đọc và ghi dữ liệu của bạn.
        </p>
      </div>

      <div className="flex items-start gap-3 bg-brand-50 border border-brand-100/50 rounded-xl p-3 mb-6 text-sm">
        <Lock className="w-5 h-5 text-brand-600 flex-shrink-0 mt-0.5" />
        <div className="text-brand-800/80 leading-relaxed">
          <strong className="font-semibold text-brand-900">Đăng nhập bằng Google ≠ Kết nối dịch vụ.</strong>{' '}
          Đăng nhập chỉ xác thực tài khoản của bạn. Để đồng bộ hai chiều, bạn cần cấp quyền (scope) riêng cho từng dịch vụ bên dưới.
        </div>
      </div>

      {loading ? (
        <div className="flex justify-center items-center py-20">
          <Loader2 className="w-8 h-8 animate-spin text-brand-600" />
        </div>
      ) : isError ? (
        <div className="bg-white rounded-xl border border-gray-100 shadow-sm p-12 flex flex-col items-center justify-center text-center">
          <div className="w-12 h-12 bg-red-50 text-red-500 rounded-2xl flex items-center justify-center mb-4">
            <AlertCircle className="w-6 h-6" />
          </div>
          <h3 className="text-base font-semibold text-gray-900 mb-1">Không tải được kết nối</h3>
          <p className="text-sm text-gray-500 mb-6">Vui lòng thử lại sau giây lát.</p>
          <button
            onClick={() => refetch()}
            className="h-9 px-6 bg-brand-600 hover:bg-brand-700 text-white text-[13px] font-medium rounded-lg transition-colors shadow-sm"
          >
            Thử lại
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {SERVICES.map((service) => {
            const connection = connections.find(
              (c) => c.provider.toLowerCase() === service.provider.toLowerCase() &&
                c.serviceType.toLowerCase() === service.serviceType.toLowerCase()
            );

            const isConnected = !!connection;
            const status = connection?.status || 'Disconnected';
            const isActive = status.toLowerCase() === 'active';
            const isConnectionError = status.toLowerCase() === 'error';

            let statusBg = 'bg-gray-100';
            let statusFg = 'text-gray-600';
            let statusDot = 'bg-gray-400';
            let statusLabel = 'Chưa kết nối';

            if (isConnected) {
              if (isActive) {
                statusBg = 'bg-green-100';
                statusFg = 'text-green-700';
                statusDot = 'bg-green-500';
                statusLabel = 'Đang hoạt động';
              } else if (isConnectionError) {
                statusBg = 'bg-red-100';
                statusFg = 'text-red-700';
                statusDot = 'bg-red-500';
                statusLabel = 'Lỗi đồng bộ';
              } else {
                statusBg = 'bg-yellow-100';
                statusFg = 'text-yellow-700';
                statusDot = 'bg-yellow-500';
                statusLabel = status;
              }
            }

            const lastSyncedText = connection?.lastSyncedAt
              ? `Đồng bộ ${formatDistanceToNow(new Date(connection.lastSyncedAt), { addSuffix: true, locale: vi })}`
              : 'Chưa đồng bộ';

            const isLoadingAction =
              (disconnectMutation.isPending && disconnectMutation.variables === connection?.id) ||
              (syncMutation.isPending && syncMutation.variables === connection?.id) ||
              (connectMutation.isPending && connectMutation.variables?.serviceType === service.serviceType);

            return (
              <div key={`${service.integrationKey}-${service.serviceType}`} className={`bg-white rounded-xl border flex flex-col p-5 shadow-sm transition-shadow hover:shadow-md ${isConnectionError ? 'border-red-200' : 'border-gray-200'}`}>

                <div className="flex items-start gap-3.5 mb-3.5">
                  <div className={`w-11 h-11 rounded-xl ${service.bgColor} flex items-center justify-center border border-gray-100 flex-shrink-0`}>
                    <img src={service.icon} alt={service.name} className="w-6 h-6 object-contain drop-shadow-sm" />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="text-[15px] font-semibold text-gray-900">{service.name}</div>
                    <div className="text-[12.5px] text-gray-500 truncate">
                      {isConnected && connection.providerAccountId ? connection.providerAccountId : 'Chưa có tài khoản'}
                    </div>
                  </div>

                  <div className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${statusBg} ${statusFg} flex-shrink-0`}>
                    <span className={`w-1.5 h-1.5 rounded-full ${statusDot}`}></span>
                    {statusLabel}
                  </div>
                </div>

                <div className="text-[13px] text-gray-600 leading-relaxed mb-4">
                  {service.description}
                </div>

                <div className="mt-auto pt-2 flex items-center justify-between gap-3">
                  <span className="text-xs text-gray-400 truncate min-w-0">
                    {isConnected ? lastSyncedText : ''}
                  </span>

                  <div className="flex items-center gap-2 flex-shrink-0">
                    {isConnected ? (
                      <>
                        {isActive && (
                          <button
                            onClick={() => syncMutation.mutate(connection.id)}
                            disabled={isLoadingAction}
                            className="inline-flex items-center gap-1.5 h-8 px-3 border border-gray-200 rounded-lg bg-white text-gray-600 text-xs font-medium hover:bg-gray-50 hover:border-gray-300 transition-colors disabled:opacity-50"
                          >
                            <RefreshCw className={`w-3.5 h-3.5 ${syncMutation.isPending && syncMutation.variables === connection.id ? 'animate-spin' : ''}`} />
                            <span>Đồng bộ</span>
                          </button>
                        )}
                        {(isActive || isConnectionError) && (
                          <button
                            onClick={() => disconnectMutation.mutate(connection.id)}
                            disabled={isLoadingAction}
                            className="inline-flex items-center gap-1.5 h-8 px-3 border border-gray-200 rounded-lg bg-white text-gray-600 text-xs font-medium hover:border-red-500 hover:text-red-600 hover:bg-red-50 transition-colors disabled:opacity-50"
                          >
                            Ngắt
                          </button>
                        )}
                        {isConnectionError && (
                          <button
                            onClick={() => handleConnect(service.integrationKey, service.serviceType)}
                            disabled={isLoadingAction}
                            className="inline-flex items-center gap-1.5 h-8 px-3 border border-transparent rounded-lg bg-brand-600 text-white text-xs font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
                          >
                            Kết nối lại
                          </button>
                        )}
                      </>
                    ) : (
                      <button
                        onClick={() => handleConnect(service.integrationKey, service.serviceType)}
                        disabled={isLoadingAction}
                        className="inline-flex items-center gap-1.5 h-8 px-3 border border-transparent rounded-lg bg-brand-600 text-white text-xs font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
                      >
                        <Plus className="w-3.5 h-3.5" />
                        <span>Kết nối</span>
                      </button>
                    )}
                  </div>
                </div>

              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

