import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from './layouts/MainLayout';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { Login } from './pages/Login';
import { RegisterPage } from './pages/RegisterPage';
import { Inbox } from './pages/Inbox';
import { KanbanBoard } from './pages/KanbanBoard';
import { Integrations } from './pages/Integrations';
import { OAuthCallback } from './pages/connections/OAuthCallback';
import { ScheduledEmails } from './pages/ScheduledEmails';
import { SendEmail } from './pages/SendEmail';
import { ProfilePage } from './pages/ProfilePage';
import { GoogleCallback } from './pages/auth/GoogleCallback';
import { VerifyOtp } from './pages/auth/VerifyOtp';
import { AdminRoute } from './components/auth/AdminRoute';
import { AdminDashboard } from './pages/AdminDashboard';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/oauth/callback',
    element: <OAuthCallback />,
  },
  {
    path: '/auth/google/callback',
    element: <GoogleCallback />,
  },
  {
    path: '/register',
    element: <RegisterPage />,
  },
  {
    path: '/verify-otp',
    element: <VerifyOtp />,
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
            // Bấm thông báo (linkUrl BE: /inbox?item=...) → mở panel chi tiết item trên Inbox.
            path: 'inbox',
            element: <Inbox />,
          },
          {
            path: 'kanban',
            element: <KanbanBoard />,
          },
          {
            path: 'integrations',
            element: <Integrations />,
          },
          {
            path: 'scheduled-emails',
            element: <ScheduledEmails />,
          },
          {
            path: 'send-email',
            element: <SendEmail />,
          },
          {
            path: 'profile',
            element: <ProfilePage />,
          },
          {
            path: '*',
            element: <Navigate to="/" replace />,
          },
        ],
      },
      {
        path: 'admin',
        element: <AdminRoute />,
        children: [
          {
            element: <MainLayout />,
            children: [
              {
                index: true,
                element: <AdminDashboard />,
              },
              {
                path: '*',
                element: <Navigate to="/admin" replace />,
              },
            ],
          },
        ],
      },
    ],
  },
]);
