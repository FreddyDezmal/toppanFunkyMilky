import { BrowserRouter } from 'react-router'
import { AuthProvider } from '../features/auth/AuthProvider'
import { AppRoutes } from './AppRoutes'

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  )
}