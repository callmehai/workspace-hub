import { useQuery, useMutation } from '@tanstack/react-query';
import { connectionsApi } from '../../lib/connectionsApi';
import toast from 'react-hot-toast';
import { Loader2, AlertCircle, CheckCircle2, XCircle } from 'lucide-react';

const SERVICES = [
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Gmail',
    name: 'Google Gmail',
    description: 'Sync your inbox directly into your workspace.',
    icon: '/icons/gmail.svg',
    color: '',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'GCal',
    name: 'Google Calendar',
    description: 'Manage your events and schedule seamlessly.',
    icon: '/icons/gcal.svg',
    color: '',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Drive',
    name: 'Google Drive',
    description: 'Access and organize your files from Drive.',
    icon: '/icons/drive.svg',
    color: '',
    bgColor: 'bg-gray-50',
  },
  {
    integrationKey: 'jira',
    provider: 'atlassian',
    serviceType: 'Jira',
    name: 'Atlassian Jira',
    description: 'Import issues, track sprints, and link commits.',
    icon: '/icons/jira.svg',
    color: '',
    bgColor: 'bg-gray-50',
  },
];

export const Integrations = () => {
  const { data: connections = [], isLoading: loading, refetch } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const disconnectMutation = useMutation({
    mutationFn: connectionsApi.disconnect,
    onSuccess: () => {
      toast.success('Disconnected successfully');
      refetch();
    },
    onError: () => toast.error('Failed to disconnect'),
  });

  const handleConnect = async (integrationKey: string, serviceType: string) => {
    try {
      const redirectUri = `${window.location.origin}/oauth/callback`;
      const res = await connectionsApi.startOAuth({ integrationKey, serviceType, redirectUri });

      window.location.href = res.authorizationUrl;
    } catch (err) {
      const e = err as { response?: { data?: { message?: string } } };
      toast.error(e.response?.data?.message || 'Failed to start connection');
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="w-8 h-8 animate-spin text-brand-600" />
      </div>
    );
  }

  return (
    <div className="p-8 max-w-5xl mx-auto text-gray-800">
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-gray-900 mb-2">Connected Services</h1>
        <p className="text-gray-600">
          Manage third-party tools linked to your workspace
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {SERVICES.map((service) => {
          const connection = connections.find(
            (c) => c.provider.toLowerCase() === service.provider.toLowerCase() &&
              c.serviceType.toLowerCase() === service.serviceType.toLowerCase()
          );

          const isConnected = !!connection;
          const status = connection?.status || 'Disconnected';
          const isActive = status.toLowerCase() === 'active';
          const isError = status.toLowerCase() === 'error';

          return (
            <div key={`${service.integrationKey}-${service.serviceType}`} className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 flex flex-col transition-shadow hover:shadow-md">
              <div className="flex justify-between items-start mb-4">
                <div className={`w-12 h-12 rounded-lg ${service.bgColor} flex items-center justify-center border border-gray-100`}>
                  <img src={service.icon} alt={service.name} className="w-6 h-6 object-contain drop-shadow-sm" />
                </div>

                {/* Status Badge */}
                {isConnected ? (
                  isActive ? (
                    <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-medium bg-green-100 text-green-800">
                      <CheckCircle2 className="w-3.5 h-3.5" />
                      <span>CONNECTED</span>
                    </span>
                  ) : isError ? (
                    <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800">
                      <AlertCircle className="w-3.5 h-3.5" />
                      <span>ERROR</span>
                    </span>
                  ) : (
                    <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
                      <span>{status.toUpperCase()}</span>
                    </span>
                  )
                ) : (
                  <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
                    <XCircle className="w-3.5 h-3.5" />
                    <span>DISCONNECTED</span>
                  </span>
                )}
              </div>

              <div className="mb-4 flex-1">
                <h3 className="text-lg font-semibold text-gray-900 mb-1">{service.name}</h3>
                <p className="text-sm text-gray-500 leading-relaxed">{service.description}</p>
                {isConnected && connection.providerAccountId && (
                  <p className="text-xs text-gray-400 mt-2 font-mono">Account: {connection.providerAccountId}</p>
                )}

              </div>

              <div className="mt-auto pt-4 border-t border-gray-100">
                {isConnected ? (
                  <button
                    onClick={() => disconnectMutation.mutate(connection.id)}
                    className="w-full py-2 px-4 rounded-md text-sm font-medium border border-gray-300 text-gray-700 hover:bg-gray-50 transition-colors"
                  >
                    Disconnect
                  </button>
                ) : (
                  <button
                    onClick={() => handleConnect(service.integrationKey, service.serviceType)}
                    className="w-full py-2 px-4 rounded-md text-sm font-medium bg-brand-600 text-white hover:bg-brand-700 transition-colors"
                  >
                    Connect
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
