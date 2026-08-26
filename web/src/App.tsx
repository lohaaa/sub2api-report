import {
  CalendarClockIcon,
  FileChartColumnIcon,
  MegaphoneIcon,
  ScrollTextIcon,
} from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from '@/components/layout/app-shell'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { AuthLayout } from '@/features/auth/auth-layout'
import { EmptySectionPage } from '@/features/shared/empty-section-page'
import { ApiError, getCurrentAdministrator, getSetupStatus } from '@/lib/api-client'

const PeoplePage = lazy(() => import('@/features/people/people-page').then((module) => ({ default: module.PeoplePage })))
const LoginPage = lazy(() => import('@/features/auth/login-page').then((module) => ({ default: module.LoginPage })))
const RecoveryPage = lazy(() => import('@/features/auth/recovery-page').then((module) => ({ default: module.RecoveryPage })))
const SetupPage = lazy(() => import('@/features/auth/setup-page').then((module) => ({ default: module.SetupPage })))
const SettingsPage = lazy(() => import('@/features/settings/settings-page').then((module) => ({ default: module.SettingsPage })))

export default function App() {
  return (
    <Suspense fallback={<ApplicationLoading />}>
      <Application />
    </Suspense>
  )
}

function Application() {
  const setupQuery = useQuery({
    queryKey: ['setup-status'],
    queryFn: ({ signal }) => getSetupStatus(signal),
    staleTime: 0,
  })
  const administratorQuery = useQuery({
    queryKey: ['current-administrator'],
    queryFn: ({ signal }) => getCurrentAdministrator(signal),
    enabled: setupQuery.isSuccess && !setupQuery.data.setupRequired,
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 401) && failureCount < 1,
    staleTime: 30_000,
  })

  if (setupQuery.isPending || (administratorQuery.isPending && administratorQuery.fetchStatus !== 'idle')) {
    return <ApplicationLoading />
  }
  if (setupQuery.isError) {
    return <ApplicationUnavailable onRetry={() => setupQuery.refetch()} />
  }
  if (setupQuery.data.setupRequired) {
    return (
      <Routes>
        <Route path="/setup" element={<SetupPage status={setupQuery.data} />} />
        <Route path="*" element={<Navigate to="/setup" replace />} />
      </Routes>
    )
  }

  const unauthenticated = administratorQuery.error instanceof ApiError
    && administratorQuery.error.status === 401
  if (administratorQuery.isError && !unauthenticated) {
    return <ApplicationUnavailable onRetry={() => administratorQuery.refetch()} />
  }
  if (unauthenticated) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/recover" element={<RecoveryPage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  return <AuthenticatedRoutes />
}

function AuthenticatedRoutes() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<DashboardPage />} />
        <Route path="people" element={<PeoplePage />} />
        <Route
          path="reports"
          element={
            <EmptySectionPage
              title="报告记录"
              description="报告快照、统计窗口和发送状态"
              icon={FileChartColumnIcon}
              columns={['统计截止日', '触发方式', '状态', '生成时间']}
              emptyText="暂无报告记录"
            />
          }
        />
        <Route
          path="channels"
          element={
            <EmptySectionPage
              title="发送渠道"
              description="邮件、钉钉和飞书投递配置"
              icon={MegaphoneIcon}
              columns={['名称', '类型', '状态', '最近测试']}
              emptyText="暂无发送渠道"
            />
          }
        />
        <Route
          path="schedule"
          element={
            <EmptySectionPage
              title="计划任务"
              description="月报运行时间和最近执行结果"
              icon={CalendarClockIcon}
              columns={['任务', '计划', '时区', '状态']}
              emptyText="暂无计划任务"
            />
          }
        />
        <Route path="settings" element={<SettingsPage />} />
        <Route
          path="audit"
          element={
            <EmptySectionPage
              title="审计日志"
              description="管理操作和安全事件"
              icon={ScrollTextIcon}
              columns={['时间', '操作', '目标', '结果']}
              emptyText="暂无审计事件"
            />
          }
        />
      </Route>
      <Route path="/login" element={<Navigate to="/" replace />} />
      <Route path="/recover" element={<Navigate to="/" replace />} />
      <Route path="/setup" element={<Navigate to="/" replace />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

function ApplicationLoading() {
  return (
    <AuthLayout title="加载中" description="正在读取实例状态。">
      <div className="flex flex-col gap-3" aria-busy="true">
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-8 w-28" />
      </div>
    </AuthLayout>
  )
}

function ApplicationUnavailable({ onRetry }: { onRetry: () => void }) {
  return (
    <AuthLayout title="无法连接服务" description="后端服务当前不可用。">
      <Alert variant="destructive">
        <AlertTitle>连接失败</AlertTitle>
        <AlertDescription>后端服务当前不可用，请稍后重试。</AlertDescription>
      </Alert>
      <Button className="mt-4" onClick={onRetry}>重试</Button>
    </AuthLayout>
  )
}
