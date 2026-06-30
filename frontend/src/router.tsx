import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from './layouts/MainLayout';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { Login } from './pages/Login';
import { RegisterPage } from './pages/RegisterPage';
import { Inbox } from './pages/Inbox';
import { Projects } from './pages/Projects';
import { KanbanBoard } from './pages/KanbanBoard';
import { Integrations } from './pages/settings/Integrations';
import { OAuthCallback } from './pages/connections/OAuthCallback';
import { ScheduledEmails } from './pages/ScheduledEmails';

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
            path: 'kanban',
            element: <KanbanBoard />,
          },
          {
            path: 'settings/integrations',
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
