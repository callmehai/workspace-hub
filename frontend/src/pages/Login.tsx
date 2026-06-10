import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { Hexagon, LogIn } from 'lucide-react';
import toast from 'react-hot-toast';

export const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [loading, setLoading] = useState(false);
  const [email, setEmail] = useState('user@example.com');
  const [password, setPassword] = useState('password');

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);

    try {
      // Connect to real backend API
      const response = await api.post('/auth/login', { email, password });
      
      const { accessToken, user } = response.data;
      login(accessToken, user);
      
      toast.success('Đăng nhập thành công!');
      navigate('/', { replace: true });
    } catch (error: any) {
      console.error('Login failed:', error);
      toast.error(error.response?.data?.message || 'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-dark-900 text-white font-sans">
      <div className="w-full max-w-md p-8">
        <div className="flex flex-col items-center mb-8">
          <div className="w-16 h-16 bg-dark-800 rounded-xl flex items-center justify-center mb-6 shadow-lg border border-dark-600">
            <Hexagon className="w-8 h-8 text-brand-500" />
            <span className="font-bold text-sm ml-1 hidden">Workspace<br/>Hub</span>
          </div>
          <h1 className="text-3xl font-semibold mb-2">Sign in to your hub</h1>
          <p className="text-gray-400">Enter your details to access your workspace.</p>
        </div>

        <form onSubmit={handleLogin} className="space-y-5">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Email</label>
            <input 
              type="email" 
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full px-4 py-2.5 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-brand-500" 
              required 
            />
          </div>
          
          <div>
            <div className="flex justify-between items-center mb-1">
              <label className="block text-sm font-medium text-gray-300">Password</label>
              <a href="#" className="text-sm text-brand-500 hover:text-brand-400">Forgot password?</a>
            </div>
            <input 
              type="password" 
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-4 py-2.5 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-brand-500" 
              required 
            />
          </div>

          <button 
            type="submit" 
            disabled={loading}
            className="w-full flex items-center justify-center py-2.5 px-4 rounded-md shadow-sm text-sm font-medium text-white bg-brand-500 hover:bg-brand-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-dark-900 focus:ring-brand-500 disabled:opacity-50 transition-colors"
          >
            {loading ? 'Signing in...' : 'Sign In'}
            {!loading && <LogIn className="w-4 h-4 ml-2" />}
          </button>
        </form>

        <div className="mt-6 flex items-center justify-center">
          <div className="border-t border-dark-600 flex-grow"></div>
          <span className="px-3 text-xs text-gray-400 font-semibold uppercase">OR</span>
          <div className="border-t border-dark-600 flex-grow"></div>
        </div>

        <div className="mt-6">
          <button className="w-full flex items-center justify-center py-2.5 px-4 rounded-md shadow-sm text-sm font-medium text-gray-300 bg-dark-800 border border-dark-600 hover:bg-dark-700 transition-colors focus:outline-none">
            <svg className="w-5 h-5 mr-2" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
              <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"/>
              <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/>
              <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"/>
              <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/>
            </svg>
            Continue with Google
          </button>
        </div>

        <p className="mt-8 text-center text-sm text-gray-400">
          Don't have an account? <a href="#" className="text-brand-500 hover:text-brand-400 font-medium">Create an account</a>
        </p>
      </div>
    </div>
  );
};
