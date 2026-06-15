import { Filter, Check, Clock, Link2, Copy, MoreHorizontal, User } from 'lucide-react';

export const Inbox = () => {
  return (
    <div className="h-full flex flex-col md:flex-row overflow-hidden bg-gray-50">
      {/* Left Column - List */}
      <div className="w-full md:w-5/12 lg:w-4/12 flex flex-col border-r border-gray-200 bg-white shrink-0">
        <div className="p-4 border-b border-gray-200 flex items-center space-x-2 overflow-x-auto hide-scrollbar">
          <button className="flex items-center space-x-1.5 px-3 py-1.5 bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-md text-sm border border-gray-200 transition-colors">
            <Filter className="w-4 h-4" />
            <span>Filter</span>
          </button>
          <div className="h-4 border-l border-gray-200 mx-1"></div>
          <button className="px-3 py-1.5 text-brand-500 bg-brand-500/10 border border-brand-500/20 rounded-md text-sm font-medium whitespace-nowrap">
            All Sources
          </button>
          <button className="px-3 py-1.5 text-gray-500 hover:text-gray-800 hover:bg-gray-100 rounded-md text-sm font-medium transition-colors whitespace-nowrap">
            Unread
          </button>
          <button className="px-3 py-1.5 flex items-center space-x-1.5 text-gray-500 hover:text-gray-800 hover:bg-gray-100 rounded-md text-sm font-medium transition-colors whitespace-nowrap">
            <span className="w-2 h-2 rounded-full bg-red-500"></span>
            <span>High Priority</span>
          </button>
        </div>

        <div className="flex-1 overflow-y-auto">
          <div className="px-4 py-2 text-xs font-semibold text-gray-500 uppercase tracking-wider bg-white/50 backdrop-blur sticky top-0">
            Today
          </div>
          
          <div className="divide-y divide-dark-600">
            {/* Item 1 - Active */}
            <div className="p-4 bg-brand-500/5 hover:bg-gray-100 transition-colors cursor-pointer group flex gap-3 border-l-2 border-brand-500">
              <div className="mt-1">
                <input type="checkbox" className="w-4 h-4 rounded border-gray-300 bg-white checked:bg-brand-500 focus:ring-brand-500/30" />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center space-x-2 text-xs text-gray-500">
                    <span className="font-medium text-gray-700 bg-gray-200 px-1.5 py-0.5 rounded text-[10px]">GH-104</span>
                    <span>•</span>
                    <span>api/auth-service</span>
                  </div>
                  <span className="text-xs text-gray-500">10m ago</span>
                </div>
                <h4 className="text-sm font-semibold text-white mb-1 truncate group-hover:text-brand-400 transition-colors">Fix OAuth token refresh loop in production</h4>
                <p className="text-sm text-gray-500 line-clamp-2 mb-2 leading-relaxed">
                  Users are reporting being logged out repeatedly. Looks like the refresh interceptor...
                </p>
                <div className="flex items-center space-x-2">
                  <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-red-500/10 text-red-400 border border-red-500/20">
                    ! High
                  </span>
                  <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-gray-200 text-gray-700 border border-gray-300">
                    bug
                  </span>
                </div>
              </div>
            </div>

            {/* Item 2 */}
            <div className="p-4 hover:bg-gray-100 transition-colors cursor-pointer group flex gap-3 border-l-2 border-transparent">
              <div className="mt-1">
                <input type="checkbox" className="w-4 h-4 rounded border-gray-300 bg-white checked:bg-brand-500 focus:ring-brand-500/30" />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center space-x-2 text-xs text-gray-500">
                    <span className="font-medium text-gray-700 bg-gray-200 px-1.5 py-0.5 rounded text-[10px]">SL-Msg</span>
                    <span>•</span>
                    <span>#design-system</span>
                  </div>
                  <span className="text-xs text-gray-500">1h ago</span>
                </div>
                <h4 className="text-sm font-semibold text-gray-800 mb-1 truncate group-hover:text-brand-400 transition-colors">New token architecture review</h4>
                <p className="text-sm text-gray-500 line-clamp-2 mb-2 leading-relaxed">
                  Hey team, I've pushed the proposed semantic token structure to Figma....
                </p>
                <div className="flex items-center space-x-2">
                  <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-brand-500/10 text-brand-400 border border-brand-500/20">
                    @mention
                  </span>
                </div>
              </div>
            </div>

            {/* Item 3 */}
            <div className="p-4 hover:bg-gray-100 transition-colors cursor-pointer group flex gap-3 border-l-2 border-transparent">
              <div className="mt-1">
                <input type="checkbox" className="w-4 h-4 rounded border-gray-300 bg-white checked:bg-brand-500 focus:ring-brand-500/30" />
              </div>
              <div className="flex-1 min-w-0 opacity-60 hover:opacity-100 transition-opacity">
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center space-x-2 text-xs text-gray-500">
                    <span className="font-medium text-gray-700 bg-gray-200 px-1.5 py-0.5 rounded text-[10px]">JIR-892</span>
                    <span>•</span>
                    <span>Frontend Team</span>
                  </div>
                  <span className="text-xs text-gray-500">2h ago</span>
                </div>
                <h4 className="text-sm font-semibold text-gray-500 line-through mb-1 truncate">Implement dark mode toggle in navbar</h4>
                <p className="text-sm text-gray-500 line-clamp-2 mb-2 leading-relaxed">
                  Completed and merged to staging. Will monitor for visual regressions on...
                </p>
                <div className="flex items-center space-x-2">
                  <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/10 text-green-400 border border-green-500/20">
                    ✓ Done
                  </span>
                </div>
              </div>
            </div>

          </div>
        </div>
      </div>

      {/* Right Column - Detail */}
      <div className="flex-1 flex flex-col bg-gray-50 min-w-0">
        {/* Toolbar */}
        <div className="h-14 border-b border-gray-200 flex items-center justify-between px-6 shrink-0">
          <div className="flex items-center space-x-4">
            <button className="text-gray-500 hover:text-white transition-colors" title="Mark as done">
              <Check className="w-5 h-5" />
            </button>
            <button className="text-gray-500 hover:text-white transition-colors" title="Snooze">
              <Clock className="w-5 h-5" />
            </button>
            <div className="h-4 border-l border-gray-200"></div>
            <div className="flex items-center space-x-1.5 text-sm text-gray-500 bg-white px-2 py-1 rounded cursor-pointer hover:bg-gray-100 transition-colors">
              <span className="font-mono text-xs">GH-104</span>
            </div>
          </div>
          <div className="flex items-center space-x-4">
            <button className="text-gray-500 hover:text-white transition-colors">
              <Link2 className="w-5 h-5" />
            </button>
            <button className="text-gray-500 hover:text-white transition-colors">
              <Copy className="w-5 h-5" />
            </button>
            <button className="text-gray-500 hover:text-white transition-colors">
              <MoreHorizontal className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Content Scrollable Area */}
        <div className="flex-1 overflow-y-auto">
          <div className="max-w-4xl mx-auto p-8">
            <h1 className="text-3xl font-bold text-white mb-4 leading-tight">Fix OAuth token refresh loop in production</h1>
            
            <div className="flex items-center space-x-3 mb-8 text-sm text-gray-500">
              <div className="w-6 h-6 rounded-full bg-emerald-600 flex items-center justify-center text-white text-xs font-medium">
                SC
              </div>
              <span className="font-medium text-gray-800">Sarah Chen</span>
              <span>opened 10 minutes ago</span>
            </div>

            <div className="flex gap-8 border-b border-gray-200 pb-6 mb-6 text-sm">
              <div>
                <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Status</span>
                <div className="flex items-center space-x-1.5 text-gray-800">
                  <span className="w-2 h-2 rounded-full bg-red-500"></span>
                  <span>Open</span>
                </div>
              </div>
              <div>
                <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Priority</span>
                <div className="flex items-center space-x-1.5 text-red-400 font-medium">
                  <span>! High</span>
                </div>
              </div>
              <div>
                <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Assignee</span>
                <button className="flex items-center space-x-1.5 text-gray-500 hover:text-gray-800 transition-colors">
                  <User className="w-4 h-4" />
                  <span>Unassigned</span>
                </button>
              </div>
            </div>

            <div className="prose prose-invert max-w-none">
              <p className="text-gray-700 leading-relaxed mb-6">
                We are seeing an influx of reports where users are being booted back to the login screen mid-session. Logs indicate that the <code className="text-brand-300 bg-brand-500/10 px-1 py-0.5 rounded">401 Unauthorized</code> interceptor is catching the expired token, attempting the refresh, but failing to persist the new token to local storage before retrying the original request.
              </p>

              <div className="rounded-lg overflow-hidden border border-gray-200 bg-white font-mono text-sm shadow-xl">
                <div className="flex items-center px-4 py-2 border-b border-gray-200 bg-white">
                  <span className="text-xs text-gray-500">interceptors.js</span>
                </div>
                <div className="p-4 overflow-x-auto">
                  <pre className="text-gray-700 leading-snug">
<span className="text-gray-500 italic">// interceptors.js</span>
<span className="text-blue-400">instance</span>.<span className="text-yellow-200">interceptors</span>.<span className="text-yellow-200">response</span>.<span className="text-blue-400">use</span>(
  (<span className="text-orange-300">res</span>) <span className="text-blue-400">=&gt;</span> res,
  <span className="text-purple-400">async</span> (<span className="text-orange-300">err</span>) <span className="text-blue-400">=&gt;</span> {'{'}
    <span className="text-purple-400">const</span> originalConfig = err.config;
    <span className="text-purple-400">if</span> (err.response.status === <span className="text-green-400">401</span> && !originalConfig._retry) {'{'}
      originalConfig._retry = <span className="text-purple-400">true</span>;
      <span className="text-purple-400">try</span> {'{'}
        <span className="text-purple-400">const</span> rs = <span className="text-purple-400">await</span> <span className="text-blue-400">instance</span>.<span className="text-blue-400">post</span>(<span className="text-green-300">"/auth/refresh"</span>, {'{'}
          refreshToken: <span className="text-blue-400">getLocalRefreshToken</span>(),
        {'}'});
        <span className="text-purple-400">const</span> {'{'} accessToken {'}'} = rs.data;
        
        <span className="text-red-400 italic font-bold">// BUG: Missed await on storage set?</span>
        <span className="text-blue-400">window</span>.<span className="text-blue-400">localStorage</span>.<span className="text-blue-400">setItem</span>(<span className="text-green-300">"token"</span>, accessToken);
        
        <span className="text-purple-400">return</span> <span className="text-blue-400">instance</span>(originalConfig);
      {'}'} <span className="text-purple-400">catch</span> (_error) {'{'}
        <span className="text-purple-400">return</span> <span className="text-blue-400">Promise</span>.<span className="text-blue-400">reject</span>(_error);
      {'}'}
    {'}'}
    <span className="text-purple-400">return</span> <span className="text-blue-400">Promise</span>.<span className="text-blue-400">reject</span>(err);
  {'}'}
);
                  </pre>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
