import { useQuery } from '@tanstack/react-query'
import {
  AlertCircleIcon,
  CalendarClockIcon,
  CheckCircle2Icon,
  CircleDollarSignIcon,
  FileClockIcon,
  KeyRoundIcon,
  PlugZapIcon,
  UsersIcon,
} from 'lucide-react'
import { Link } from 'react-router-dom'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { PageHeader } from '@/components/layout/page-header'
import { useSystemVersion } from '@/hooks/use-system-version'
import {
  getApiKeyInventory,
  getChannels,
  getReportGenerationRuns,
  getReports,
  getSub2ApiConnection,
  type ReportStatus,
} from '@/lib/api-client'
import { formatCost, formatDate, formatTimestamp } from '@/features/reports/report-format'

export function DashboardPage() {
  const versionQuery = useSystemVersion()
  const connectionQuery = useQuery({
    queryKey: ['sub2api-connection'],
    queryFn: ({ signal }) => getSub2ApiConnection(signal),
  })
  const keysQuery = useQuery({
    queryKey: ['api-keys', 1, false],
    queryFn: ({ signal }) => getApiKeyInventory(1, false, signal),
    enabled: connectionQuery.data?.configured === true,
  })
  const reportsQuery = useQuery({
    queryKey: ['reports', 1],
    queryFn: ({ signal }) => getReports(1, signal),
  })
  const generationsQuery = useQuery({
    queryKey: ['report-generations', 1],
    queryFn: ({ signal }) => getReportGenerationRuns(1, signal),
  })
  const channelsQuery = useQuery({
    queryKey: ['channels'],
    queryFn: ({ signal }) => getChannels(signal),
  })

  const connection = connectionQuery.data
  const latestReport = reportsQuery.data?.items[0] ?? null
  const latestGeneration = generationsQuery.data?.items[0] ?? null
  const keyCount = keysQuery.data?.total ?? connection?.lastSynchronizedKeyCount ?? 0
  const synchronizedUserCount = connection?.lastSynchronizedUserCount ?? 0
  const enabledChannelCount = channelsQuery.data?.filter((channel) => channel.enabled).length ?? 0
  const hasOperationalError = connectionQuery.isError
    || keysQuery.isError
    || reportsQuery.isError
    || generationsQuery.isError
    || channelsQuery.isError

  const metrics = [
    {
      label: '计划任务',
      value: '未启用',
      detail: '当前版本尚未实现自动调度',
      icon: CalendarClockIcon,
    },
    {
      label: '最近报告',
      value: latestReport ? formatDate(latestReport.cutoffDate) : '尚未生成',
      detail: latestReport
        ? `${latestReport.status === 'Complete' ? '完整' : '部分完成'} · ${latestReport.keyCount} 个 Key`
        : '等待首次手工运行',
      icon: FileClockIcon,
    },
    {
      label: '30 天实际费用',
      value: latestReport ? `¥${formatCost(latestReport.thirtyDayActualCost)}` : '¥0.00',
      detail: latestReport ? '来自最近不可变快照' : '暂无统计快照',
      icon: CircleDollarSignIcon,
    },
    {
      label: 'API Keys',
      value: String(keyCount),
      detail: connection?.lastSynchronizedAt
        ? `最近同步 ${formatTimestamp(connection.lastSynchronizedAt)}`
        : '报告生成前自动刷新',
      icon: KeyRoundIcon,
    },
  ] as const

  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader title="工作台" description="报告运行状态、待处理事项和最近结果" />
        <Button nativeButton={false} render={<Link to="/reports" />}>
          <FileClockIcon data-icon="inline-start" />
          查看报告
        </Button>
      </div>

      {!versionQuery.isSuccess ? (
        <Alert variant="destructive">
          <AlertCircleIcon />
          <AlertTitle>后端服务未连接</AlertTitle>
          <AlertDescription>当前无法读取系统版本。前端开发时请同时启动 ASP.NET Core API。</AlertDescription>
        </Alert>
      ) : null}

      {hasOperationalError ? (
        <Alert variant="destructive">
          <AlertCircleIcon />
          <AlertTitle>部分运行状态读取失败</AlertTitle>
          <AlertDescription>请刷新页面；持续失败时检查 API 日志和数据库迁移状态。</AlertDescription>
        </Alert>
      ) : null}

      {connection?.configured && connection.lastTestSucceeded === false ? (
        <Alert>
          <AlertCircleIcon />
          <AlertTitle>连接已配置，但最近测试失败</AlertTitle>
          <AlertDescription>请在系统设置重新执行连接测试；报告生成前仍会重新刷新用户与 Key。</AlertDescription>
        </Alert>
      ) : null}

      {latestGeneration?.status === 'Failed' ? (
        <Alert variant="destructive">
          <AlertCircleIcon />
          <AlertTitle>最近报告生成失败</AlertTitle>
          <AlertDescription>
            阶段 {latestGeneration.stage ?? 'unknown'}：{latestGeneration.errorMessage ?? latestGeneration.errorCode ?? '未知错误'}
          </AlertDescription>
        </Alert>
      ) : null}

      <section aria-labelledby="overview-title">
        <h2 id="overview-title" className="sr-only">运行概览</h2>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {metrics.map((metric) => (
            <Card key={metric.label} size="sm">
              <CardHeader>
                <CardDescription className="flex items-center gap-2">
                  <metric.icon aria-hidden="true" />
                  {metric.label}
                </CardDescription>
                <CardTitle className="text-lg">{metric.value}</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-xs text-muted-foreground">{metric.detail}</p>
              </CardContent>
            </Card>
          ))}
        </div>
      </section>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.5fr)_minmax(18rem,0.7fr)]">
        <section className="min-w-0" aria-labelledby="recent-title">
          <div className="mb-3 flex items-center justify-between gap-3">
            <div>
              <h2 id="recent-title" className="text-base font-semibold">最近报告</h2>
              <p className="text-sm text-muted-foreground">已生成的不可变报告快照</p>
            </div>
            <Badge variant="secondary">{reportsQuery.data?.total ?? 0} 条</Badge>
          </div>
          <div className="overflow-hidden rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>统计截止日</TableHead>
                  <TableHead>触发方式</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead className="text-right">30 天费用</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {reportsQuery.data?.items.length ? (
                  reportsQuery.data.items.slice(0, 5).map((report) => (
                    <TableRow key={report.id}>
                      <TableCell>
                        <Link className="font-medium hover:underline" to={`/reports/${report.id}`}>
                          {formatDate(report.cutoffDate)}
                        </Link>
                      </TableCell>
                      <TableCell>手工</TableCell>
                      <TableCell><ReportStatusBadge status={report.status} /></TableCell>
                      <TableCell className="text-right tabular-nums">
                        ¥{formatCost(report.thirtyDayActualCost)}
                      </TableCell>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <TableCell colSpan={4} className="h-28 text-center text-muted-foreground">
                      暂无报告记录
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </section>

        <section aria-labelledby="setup-title">
          <div className="mb-3">
            <h2 id="setup-title" className="text-base font-semibold">配置状态</h2>
            <p className="text-sm text-muted-foreground">生成首份报告前需要完成的项目</p>
          </div>
          <div className="flex flex-col divide-y rounded-md border">
            <SetupRow
              icon={PlugZapIcon}
              label="Sub2API 连接"
              status={connectionQuery.isPending ? '读取中' : connection?.configured ? '已配置' : '未配置'}
              to="/settings"
            />
            <SetupRow
              icon={UsersIcon}
              label="统计用户"
              status={synchronizedUserCount > 0 ? `${synchronizedUserCount} 个用户` : '尚未同步'}
              to="/settings"
            />
            <SetupRow
              icon={KeyRoundIcon}
              label="API Keys"
              status={keysQuery.isPending && connection?.configured ? '读取中' : `${keyCount} 个 Key`}
              to="/keys"
            />
            <SetupRow
              icon={CheckCircle2Icon}
              label="发送渠道"
              status={channelsQuery.isPending ? '读取中' : `${enabledChannelCount} 个启用`}
              to="/channels"
            />
          </div>
        </section>
      </div>
    </div>
  )
}

function ReportStatusBadge({ status }: { status: ReportStatus }) {
  return status === 'Complete'
    ? <Badge variant="secondary">完整</Badge>
    : <Badge variant="outline">部分完成</Badge>
}

function SetupRow({
  icon: Icon,
  label,
  status,
  to,
}: {
  icon: typeof PlugZapIcon
  label: string
  status: string
  to: string
}) {
  return (
    <Button
      variant="ghost"
      nativeButton={false}
      render={<Link to={to} />}
      className="h-auto justify-start rounded-none px-3 py-3 first:rounded-t-md last:rounded-b-md"
    >
      <Icon data-icon="inline-start" />
      <span className="flex-1 text-left">{label}</span>
      <span className="text-xs text-muted-foreground">{status}</span>
    </Button>
  )
}
