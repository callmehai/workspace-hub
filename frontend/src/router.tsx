import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from './layouts/MainLayout';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { Login } from './pages/Login';
import { RegisterPage } from './pages/RegisterPage';
import { Inbox } from './pages/Inbox';
import { Projects } from './pages/Projects';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/register',
    element: <RegisterPage />,
  },
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      {
        element: <MainLayout />,
        children: [
          {
            index: true,
            element: <Inbox />,
          },
          {
            path: 'projects',
            element: <Projects />,
          },
          {
            path: '*',
            element: <Navigate to="/" replace />,
          },
        ],
      },
    ],
  },
]);
