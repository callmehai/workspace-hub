import axios from 'axios';

// Lấy base URL từ biến môi trường, hoặc dùng /api (sẽ được proxy bởi Vite trong dev)
const API_URL = import.meta.env.VITE_API_URL || '/api';

export const api = axios.create({
  baseURL: API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request Interceptor: Tự động đính kèm Token
api.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// Response Interceptor: Xử lý lỗi globally (VD: 401 Unauthorized)
api.interceptors.response.use(
  (response) => {
    return response;
  },
  (error) => {
    // Nếu API trả về 401 Unauthorized (token hết hạn hoặc không hợp lệ)
    if (error.response && error.response.status === 401) {
      // Xóa token
      localStorage.removeItem('token');
      // Chuyển hướng về trang login nếu chưa ở trang login
      if (window.location.pathname !== '/login') {
        window.location.href = '/login';
      }
    }
    return Promise.reject(error);
  }
);
