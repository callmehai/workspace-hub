import { useState } from 'react';
<<<<<<< HEAD
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { Hexagon, LogIn } from 'lucide-react';
import toast from 'react-hot-toast';
=======
import { Link, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import toast from 'react-hot-toast';
import { Hexagon, LogIn } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import api from '../lib/api';
import type { AuthResponse } from '../types/auth';
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9

export const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
<<<<<<< HEAD
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
=======
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  const loginMutation = useMutation({
    mutationFn: async () =>
      (await api.post<AuthResponse>('/auth/login', { email, password })).data,
    onSuccess: (data) => {
      login(data.accessToken, data.user);
      toast.success('Đăng nhập thành công!');
      navigate('/', { replace: true });
    },
    onError: (error) => {
      const message = isAxiosError<{ message?: string }>(error)
        ? error.response?.data?.message
        : undefined;
      toast.error(message ?? 'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.');
    },
  });
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 text-gray-900 font-sans">
      <div className="w-full max-w-md p-8">
        <div className="flex flex-col items-center mb-8">
          <div className="w-16 h-16 bg-white rounded-xl flex items-center justify-center mb-6 shadow-lg border border-gray-200">
            <Hexagon className="w-8 h-8 text-brand-500" />
            <span className="font-bold text-sm ml-1 hidden">Workspace<br/>Hub</span>
          </div>
          <h1 className="text-3xl font-semibold mb-2">Sign in to your hub</h1>
          <p className="text-gray-500">Enter your details to access your workspace.</p>
        </div>

<<<<<<< HEAD
        <form onSubmit={handleLogin} className="space-y-5">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
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
              <label className="block text-sm font-medium text-gray-700">Password</label>
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
            className="w-full flex items-center justify-center py-2.5 px-4 rounded-md shadow-sm text-sm font-medium text-white bg-brand-500 hover:bg-brand-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-white focus:ring-brand-500 disabled:opacity-50 transition-colors"
          >
            {loading ? 'Signing in...' : 'Sign In'}
            {!loading && <LogIn className="w-4 h-4 ml-2" />}
=======
        <form
          onSubmit={(e) => {
            e.preventDefault();
            loginMutation.mutate();
          }}
          className="space-y-5"
        >
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full px-4 py-2.5 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-brand-500"
              required
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Password</label>
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
            disabled={loginMutation.isPending}
            className="w-full flex items-center justify-center py-2.5 px-4 rounded-md shadow-sm text-sm font-medium text-white bg-brand-500 hover:bg-brand-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-white focus:ring-brand-500 disabled:opacity-50 transition-colors"
          >
            {loginMutation.isPending ? 'Signing in...' : 'Sign In'}
            {!loginMutation.isPending && <LogIn className="w-4 h-4 ml-2" />}
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
          </button>
        </form>

        <div className="mt-6 flex items-center justify-center">
          <div className="border-t border-gray-200 flex-grow"></div>
          <span className="px-3 text-xs text-gray-500 font-semibold uppercase">OR</span>
          <div className="border-t border-gray-200 flex-grow"></div>
        </div>

        <div className="mt-6">
          <button className="w-full flex items-center justify-center py-2.5 px-4 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white border border-gray-200 hover:bg-gray-100 transition-colors focus:outline-none">
            <svg className="w-5 h-5 mr-2" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
              <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"/>
              <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/>
              <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"/>
              <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/>
            </svg>
            Continue with Google
          </button>
        </div>

        <p className="mt-8 text-center text-sm text-gray-500">
<<<<<<< HEAD
          Don't have an account? <a href="#" className="text-brand-500 hover:text-brand-400 font-medium">Create an account</a>
=======
          Don't have an account?{' '}
          <Link to="/register" className="text-brand-500 hover:text-brand-400 font-medium">
            Create an account
          </Link>
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
        </p>
      </div>
    </div>
  );
};
