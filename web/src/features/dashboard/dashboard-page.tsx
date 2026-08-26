import {
  AlertCircleIcon,
  CalendarClockIcon,
  CheckCircle2Icon,
  CircleDollarSignIcon,
  FileClockIcon,
  KeyRoundIcon,
  PlugZapIcon,
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

const metrics = [
  { label: '下次计划运行', value: '9月1日 09:00', detail: 'Asia/Shanghai', icon: CalendarClockIcon },
  { label: '最近报告', value: '尚未生成', detail: '等待首次手工运行', icon: FileClockIcon },
  { label: '30 天实际费用', value: '¥0.00', detail: '暂无统计快照', icon: CircleDollarSignIcon },
  { label: '未映射 Key', value: '0', detail: '同步后自动检查', icon: KeyRoundIcon },
] as const

export function DashboardPage() {
  const versionQuery = useSystemVersion()

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
              <p className="text-sm text-muted-foreground">已生成的报告运行和发送结果</p>
            </div>
            <Badge variant="secondary">0 条</Badge>
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
                <TableRow>
                  <TableCell colSpan={4} className="h-28 text-center text-muted-foreground">
                    暂无报告记录
                  </TableCell>
                </TableRow>
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
            <SetupRow icon={PlugZapIcon} label="Sub2API 连接" status="未配置" to="/settings" />
            <SetupRow icon={KeyRoundIcon} label="人员与 Key" status="0 人" to="/people" />
            <SetupRow icon={CheckCircle2Icon} label="发送渠道" status="0 个" to="/channels" />
          </div>
        </section>
      </div>
    </div>
  )
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
