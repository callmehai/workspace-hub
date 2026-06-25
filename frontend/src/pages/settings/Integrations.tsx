import { useEffect, useState } from 'react';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import toast from 'react-hot-toast';
import { Loader2, Mail, Calendar, HardDrive, Kanban, AlertCircle, CheckCircle2, XCircle } from 'lucide-react';

const SERVICES = [
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Gmail',
    name: 'Google Gmail',
    description: 'Sync your inbox directly into your workspace.',
    icon: Mail,
    color: 'text-red-500',
    bgColor: 'bg-red-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'GCal',
    name: 'Google Calendar',
    description: 'Manage your events and schedule seamlessly.',
    icon: Calendar,
    color: 'text-blue-500',
    bgColor: 'bg-blue-50',
  },
  {
    integrationKey: 'google',
    provider: 'google',
    serviceType: 'Drive',
    name: 'Google Drive',
    description: 'Access and organize your files from Drive.',
    icon: HardDrive,
    color: 'text-green-500',
    bgColor: 'bg-green-50',
  },
  {
    integrationKey: 'jira',
    provider: 'atlassian',
    serviceType: 'Jira',
    name: 'Atlassian Jira',
    description: 'Import issues, track sprints, and link commits.',
    icon: Kanban, // Using Kanban icon as a placeholder for Jira
    color: 'text-blue-600',
    bgColor: 'bg-blue-50',
  },
];

export const Integrations = () => {
  const [connections, setConnections] = useState<ConnectionDto[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchConnections = async () => {
    try {
      const data = await connectionsApi.getConnections();
      setConnections(data);
    } catch {
      toast.error('Failed to load connections');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchConnections();
  }, []);

  const handleConnect = async (integrationKey: string, serviceType: string) => {
    try {
      const redirectUri = `${window.location.origin}/oauth/callback`;
      const res = await connectionsApi.startOAuth({ integrationKey, serviceType, redirectUri });

      // eslint-disable-next-line react-hooks/immutability
      window.location.href = res.authorizationUrl;
    } catch (err) {
      const e = err as { response?: { data?: { message?: string } } };
      toast.error(e.response?.data?.message || 'Failed to start connection');
    }
  };

  const handleDisconnect = async (id: string) => {
    try {
      await connectionsApi.disconnect(id);
      toast.success('Disconnected successfully');
      fetchConnections();
    } catch {
      toast.error('Failed to disconnect');
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
          Manage third-party tools linked to your workspace. Syncing occurs automatically every 5 minutes for connected services.
        </p>
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

          const Icon = service.icon;

          return (
            <div key={`${service.integrationKey}-${service.serviceType}`} className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 flex flex-col transition-shadow hover:shadow-md">
              <div className="flex justify-between items-start mb-4">
                <div className={`w-12 h-12 rounded-lg ${service.bgColor} flex items-center justify-center`}>
                  <Icon className={`w-6 h-6 ${service.color}`} />
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
                {/* Warning for Readonly scope reconnect reminder */}
                {isConnected && service.integrationKey === 'google' && (
                  <div className="mt-3 flex items-start space-x-2 text-xs text-amber-600 bg-amber-50 p-2 rounded border border-amber-100">
                    <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
                    <p>If migrated from older version with read-only access, reconnect to enable write permissions.</p>
                  </div>
                )}
              </div>

              <div className="mt-auto pt-4 border-t border-gray-100">
                {isConnected ? (
                  <button
                    onClick={() => handleDisconnect(connection.id)}
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
