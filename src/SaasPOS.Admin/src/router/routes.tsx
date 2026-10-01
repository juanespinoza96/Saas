import { type RouteObject } from 'react-router-dom'
import { ProtectedRoute } from './ProtectedRoute'
import { AdminLayout } from '../components/layout/AdminLayout'
import { LoginPage } from '../pages/LoginPage'
import { DashboardPage } from '../pages/DashboardPage'
import { ComerciosPage } from '../pages/ComerciosPage'
import { ComercioDetallePage } from '../pages/ComercioDetallePage'
import { PlanesPage } from '../pages/PlanesPage'
import { LogsPage } from '../pages/LogsPage'
import { TrialsPage } from '../pages/TrialsPage'

export const routes: RouteObject[] = [
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AdminLayout />,
        children: [
          {
            path: '/',
            element: <DashboardPage />,
          },
          {
            path: '/comercios',
            element: <ComerciosPage />,
          },
          {
            path: '/comercios/:id',
            element: <ComercioDetallePage />,
          },
          {
            path: '/planes',
            element: <PlanesPage />,
          },
          {
            path: '/trials',
            element: <TrialsPage />,
          },
          {
            path: '/logs',
            element: <LogsPage />,
          },
        ],
      },
    ],
  },
]
