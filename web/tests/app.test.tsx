import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { TooltipProvider } from '@/components/ui/tooltip'
import { ThemeProvider } from '@/app/theme-provider'
import App from '@/App'
import { server } from './setup'

function renderApp(route = '/') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <TooltipProvider>
          <MemoryRouter initialEntries={[route]}>
            <App />
          </MemoryRouter>
        </TooltipProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )
}

function useAuthenticatedHandlers() {
  server.use(
    http.get('/api/v1/setup/status', () => HttpResponse.json({ setupRequired: false })),
    http.get('/api/v1/auth/me', () => HttpResponse.json({
      username: 'synthetic-admin',
      sessionStartedAt: '2026-08-26T10:00:00Z',
      stepUpExpiresAt: null,
    })),
    http.get('/api/v1/system/version', () =>
      HttpResponse.json({ version: '0.3.0', environment: 'Test', releaseChannel: 'stable' }),
    ),
  )
}

describe('application authentication gate', () => {
  it('renders setup before an administrator exists', async () => {
    server.use(
      http.get('/api/v1/setup/status', () => HttpResponse.json({
        setupRequired: true,
        challengeExpiresAt: '2026-08-26T10:30:00Z',
      })),
    )

    renderApp('/')

    expect(await screen.findByRole('heading', { level: 1, name: '初始化管理员' })).toBeInTheDocument()
    expect(screen.getByLabelText('初始化码')).toHaveAttribute('autocomplete', 'one-time-code')
    expect(screen.getByLabelText('管理员密码')).toHaveAttribute('autocomplete', 'new-password')
  })

  it('renders login for an unauthenticated initialized instance', async () => {
    server.use(
      http.get('/api/v1/setup/status', () => HttpResponse.json({ setupRequired: false })),
      http.get('/api/v1/auth/me', () => HttpResponse.json(
        { title: 'Unauthorized', status: 401 },
        { status: 401 },
      )),
    )

    renderApp('/')

    expect(await screen.findByRole('heading', { level: 1, name: '管理员登录' })).toBeInTheDocument()
    expect(screen.getByLabelText('用户名')).toHaveAttribute('autocomplete', 'username')
    expect(screen.getByLabelText('密码')).toHaveAttribute('autocomplete', 'current-password')
  })

  it('renders the dashboard and service version after authentication', async () => {
    useAuthenticatedHandlers()

    renderApp()

    expect(await screen.findByRole('heading', { level: 1, name: '工作台' })).toBeInTheDocument()
    expect(await screen.findByText('v0.3.0')).toBeInTheDocument()
  })

  it('renders synchronized people and Key diagnostics', async () => {
    useAuthenticatedHandlers()
    server.use(
      http.get('/api/v1/sub2api/connection', () => HttpResponse.json({
        configured: true,
        hasAdminApiKey: true,
        revision: 1,
      })),
      http.get('/api/v1/people', () => HttpResponse.json([{
        id: '00000000-0000-0000-0000-000000000001',
        code: 'person-a',
        displayName: '合成人员 A',
        isActive: true,
        currentApiKeyCount: 1,
        assignmentCount: 1,
        revision: 1,
        updatedAt: '2026-08-26T10:00:00Z',
      }])),
      http.get('/api/v1/sub2api/keys', () => HttpResponse.json({
        items: [],
        total: 0,
        page: 1,
        pageSize: 50,
        pages: 1,
        diagnostics: {
          unmappedKeys: 0,
          overlappingAssignments: 0,
          retiredKeys: 0,
        },
        lastSynchronizedAt: '2026-08-26T10:00:00Z',
      })),
    )

    renderApp('/people')

    expect(await screen.findByRole('heading', { level: 1, name: '人员与 Key' })).toBeInTheDocument()
    expect(await screen.findByText('合成人员 A')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: '仅看未映射' })).toBeInTheDocument()
  })

  it('renders database and security settings for the administrator', async () => {
    useAuthenticatedHandlers()
    server.use(
      http.get('/api/v1/system/settings', () => HttpResponse.json({
        timezone: 'Asia/Shanghai',
        releaseChannel: 'stable',
        logLevel: 'Information',
        reportRetentionMonths: 12,
        backupRetentionCount: 10,
        revision: 1,
        updatedAt: null,
      })),
      http.get('/api/v1/sub2api/connection', () => HttpResponse.json({
        configured: true,
        baseUrl: 'https://sub2api.example.com',
        hasAdminApiKey: true,
        adminApiKeyMask: '****1234',
        userId: '42',
        codexGroupId: '7',
        revision: 1,
        updatedAt: '2026-08-26T10:00:00Z',
        lastTestedAt: null,
        lastTestSucceeded: null,
        lastTestCode: null,
        lastSynchronizedAt: null,
        lastSynchronizedKeyCount: null,
      })),
    )

    renderApp('/settings')

    expect(await screen.findByRole('heading', { level: 1, name: '系统设置' })).toBeInTheDocument()
    expect(await screen.findByLabelText('默认时区')).toHaveValue('Asia/Shanghai')
    expect(screen.getByRole('heading', { level: 2, name: '管理员安全' })).toBeInTheDocument()
    expect(screen.getByLabelText('当前密码', { selector: '#change-current-password' })).toBeInTheDocument()
  })
})
