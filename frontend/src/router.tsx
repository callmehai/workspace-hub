import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from './layouts/MainLayout';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { Login } from './pages/Login';
import { RegisterPage } from './pages/RegisterPage';
import { Inbox } from './pages/Inbox';
import { Projects } from './pages/Projects';
import { KanbanBoard } from './pages/KanbanBoard';
import { Integrations } from './pages/Integrations';
import { OAuthCallback } from './pages/connections/OAuthCallback';
import { ScheduledEmails } from './pages/ScheduledEmails';
import { GoogleCallback } from './pages/auth/GoogleCallback';
import { VerifyOtp } from './pages/auth/VerifyOtp';
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
            path: 'tasks',
            element: <Inbox />,
          },
          {
            path: 'files',
            element: <Inbox />,
          },
          {
            path: 'calendar',
            element: <Inbox />,
          },
          {
            path: 'projects',
            element: <Projects />,
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
            path: '*',
            element: <Navigate to="/" replace />,
          },
        ],
      },
    ],
  },
]);
