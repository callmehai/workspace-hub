import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from './layouts/MainLayout';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { Login } from './pages/Login';
<<<<<<< HEAD
import { Inbox } from './pages/Inbox';
import { Projects } from './pages/Projects';
=======
import { RegisterPage } from './pages/RegisterPage';
import { Inbox } from './pages/Inbox';
import { Projects } from './pages/Projects';
import { KanbanBoard } from './pages/KanbanBoard';
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <Login />,
  },
  {
<<<<<<< HEAD
=======
    path: '/register',
    element: <RegisterPage />,
  },
  {
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
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
<<<<<<< HEAD
=======
            path: 'kanban',
            element: <KanbanBoard />,
          },
          {
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
            path: '*',
            element: <Navigate to="/" replace />,
          },
        ],
      },
    ],
  },
]);
