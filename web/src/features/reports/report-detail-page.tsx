import { useQuery } from '@tanstack/react-query'
import {
  AlertTriangleIcon,
  ArrowLeftIcon,
  DownloadIcon,
  KeyRoundIcon,
} from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/layout/page-header'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { FormError } from '@/features/auth/form-error'
import {
  ApiError,
  downloadReportCsv,
  getReport,
  type ReportKeyUsage,
  type ReportUsageMetrics,
} from '@/lib/api-client'
import { formatCost, formatCount, formatDate, formatTimestamp } from './report-format'
import { cn } from '@/lib/utils'
import { ReportStatusBadge } from './reports-page'

export function ReportDetailPage() {
  const { id = '' } = useParams()
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [isDownloading, setIsDownloading] = useState(false)
  const reportQuery = useQuery({
    queryKey: ['report', id],
    queryFn: ({ signal }) => getReport(id, signal),
    enabled: Boolean(id),
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 1,
  })

  async function handleDownload() {
    setDownloadError(null)
    setIsDownloading(true)
    try {
      const file = await downloadReportCsv(id)
      const url = URL.createObjectURL(file.blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = file.fileName
      anchor.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 0)
    }
    catch (error) {
      setDownloadError(error instanceof ApiError ? error.message : 'CSV 下载失败。')
    }
    finally {
      setIsDownloading(false)
    }
  }

  if (reportQuery.isPending) {
    return <ReportDetailLoading />
  }
  if (reportQuery.isError) {
    const notFound = reportQuery.error instanceof ApiError && reportQuery.error.status === 404
    return (
      <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
        <Link className={cn(buttonVariants({ variant: 'ghost' }), 'w-fit')} to="/reports">
          <ArrowLeftIcon data-icon="inline-start" />
          返回报告列表
        </Link>
        <FormError message={notFound ? '报告不存在或已被清理。' : '无法读取报告详情。'} />
      </div>
    )
  }

  const report = reportQuery.data
  const diagnostics = report.diagnostics
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-2">
          <Link className={cn(buttonVariants({ variant: 'ghost' }), 'w-fit px-0')} to="/reports">
            <ArrowLeftIcon data-icon="inline-start" />
            返回报告列表
          </Link>
          <div className="flex flex-wrap items-center gap-2">
            <PageHeader
              title={`${formatDate(report.thirtyDayWindow.endDate)} 报告`}
              description={`${report.thirtyDayWindow.startDate} 至 ${report.thirtyDayWindow.endDate} · ${report.timezone}`}
            />
            <ReportStatusBadge status={report.status} />
          </div>
        </div>
        <Button variant="outline" disabled={isDownloading} onClick={handleDownload}>
          {isDownloading ? <Spinner data-icon="inline-start" /> : <DownloadIcon data-icon="inline-start" />}
          下载 CSV
        </Button>
      </div>

      <FormError message={downloadError} />
      {report.status === 'Partial' ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>报告数据不完整</AlertTitle>
          <AlertDescription>
            失败区间 {diagnostics.failedSegments.length}，未归属区间 {diagnostics.unassignedSegments.length}，冲突区间 {diagnostics.conflictingSegments.length}。
          </AlertDescription>
        </Alert>
      ) : null}
      {diagnostics.zeroUsageKeyIds.length > 0 ? (
        <Alert>
          <KeyRoundIcon aria-hidden="true" />
          <AlertTitle>存在零用量 Key</AlertTitle>
          <AlertDescription>{diagnostics.zeroUsageKeyIds.length} 个 Key 在 30 日窗口内没有用量。</AlertDescription>
        </Alert>
      ) : null}

      <div className="grid border-y sm:grid-cols-2 sm:divide-x">
        <MetricsSummary
          title="最近 7 日"
          range={`${formatDate(report.sevenDayWindow.startDate)} 至 ${formatDate(report.sevenDayWindow.endDate)}`}
          metrics={report.sevenDayTotal}
        />
        <MetricsSummary
          title="最近 30 日"
          range={`${formatDate(report.thirtyDayWindow.startDate)} 至 ${formatDate(report.thirtyDayWindow.endDate)}`}
          metrics={report.thirtyDayTotal}
        />
      </div>

      <section aria-labelledby="person-summary-title">
        <h2 id="person-summary-title" className="mb-3 text-base font-semibold">人员汇总</h2>
        <Table>
          <TableCaption>按人员编码排列的 7/30 日归属用量</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">人员</TableHead>
              <TableHead scope="col" className="text-right">Key</TableHead>
              <TableHead scope="col" className="text-right">7 日请求</TableHead>
              <TableHead scope="col" className="text-right">7 日 Token</TableHead>
              <TableHead scope="col" className="text-right">7 日费用</TableHead>
              <TableHead scope="col" className="text-right">30 日请求</TableHead>
              <TableHead scope="col" className="text-right">30 日 Token</TableHead>
              <TableHead scope="col" className="text-right">30 日费用</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {report.people.length > 0
              ? report.people.map((person) => (
                  <TableRow key={person.personId}>
                    <TableCell>
                      <div className="flex flex-col">
                        <span className="font-medium">{person.displayName}</span>
                        <span className="font-mono text-xs text-muted-foreground">{person.code}</span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{person.keyCount}</TableCell>
                    <MetricCells metrics={person.sevenDay} />
                    <MetricCells metrics={person.thirtyDay} />
                  </TableRow>
                ))
              : (
                  <TableRow>
                    <TableCell colSpan={8} className="h-20 text-center text-muted-foreground">
                      当前快照没有可归属的人员用量
                    </TableCell>
                  </TableRow>
                )}
          </TableBody>
        </Table>
      </section>

      <section aria-labelledby="key-detail-title">
        <h2 id="key-detail-title" className="mb-3 text-base font-semibold">Key 明细</h2>
        <Table>
          <TableCaption>每个 Key 的归属区间、采集状态和 30 日用量</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">Key</TableHead>
              <TableHead scope="col">归属区间</TableHead>
              <TableHead scope="col" className="text-right">请求</TableHead>
              <TableHead scope="col" className="text-right">Token</TableHead>
              <TableHead scope="col" className="text-right">实际费用</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {report.keys.map((key) => <KeyRow key={key.keyId} item={key} />)}
          </TableBody>
        </Table>
      </section>

      <p className="text-xs text-muted-foreground">
        快照 v{report.schemaVersion} · 连接配置 revision {report.connectionRevision} · {formatTimestamp(report.generatedAt)}
      </p>
    </div>
  )
}

function MetricsSummary({
  title,
  range,
  metrics,
}: {
  title: string
  range: string
  metrics: ReportUsageMetrics
}) {
  return (
    <section className="flex flex-col gap-3 p-4" aria-label={title}>
      <div>
        <h2 className="text-sm font-semibold">{title}</h2>
        <p className="text-xs text-muted-foreground">{range}</p>
      </div>
      <dl className="grid grid-cols-3 gap-4">
        <div>
          <dt className="text-xs text-muted-foreground">请求</dt>
          <dd className="mt-1 font-semibold tabular-nums">{formatCount(metrics.totalRequests)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Token</dt>
          <dd className="mt-1 font-semibold tabular-nums">{formatCount(metrics.totalTokens)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">实际费用</dt>
          <dd className="mt-1 font-semibold tabular-nums">{formatCost(metrics.totalActualCost)}</dd>
        </div>
      </dl>
    </section>
  )
}

function MetricCells({ metrics }: { metrics: ReportUsageMetrics }) {
  return (
    <>
      <TableCell className="text-right tabular-nums">{formatCount(metrics.totalRequests)}</TableCell>
      <TableCell className="text-right tabular-nums">{formatCount(metrics.totalTokens)}</TableCell>
      <TableCell className="text-right tabular-nums">{formatCost(metrics.totalActualCost)}</TableCell>
    </>
  )
}

function KeyRow({ item }: { item: ReportKeyUsage }) {
  return (
    <TableRow>
      <TableCell>
        <div className="flex flex-col gap-1">
          <span className="font-medium">{item.name}</span>
          <span className="font-mono text-xs text-muted-foreground">ID {item.externalId}</span>
          <div className="flex gap-1">
            <Badge variant="outline">{item.status}</Badge>
            {item.retiredAt ? <Badge variant="secondary">已退休</Badge> : null}
          </div>
        </div>
      </TableCell>
      <TableCell>
        <ul className="flex min-w-56 flex-col gap-1">
          {item.segments.map((segment) => (
            <li key={`${segment.startDate}-${segment.endDate}`} className="text-xs">
              <span>{segment.startDate} 至 {segment.endDate}</span>
              <span className="ml-2 text-muted-foreground">
                {segment.failureKind
                  ? `采集失败 · ${segment.failureKind}`
                  : segment.personDisplayName ?? (segment.diagnosticCode === 'assignment_conflict' ? '归属冲突' : '未归属')}
              </span>
            </li>
          ))}
        </ul>
      </TableCell>
      <TableCell className="text-right tabular-nums">{formatCount(item.thirtyDay.totalRequests)}</TableCell>
      <TableCell className="text-right tabular-nums">{formatCount(item.thirtyDay.totalTokens)}</TableCell>
      <TableCell className="text-right tabular-nums">{formatCost(item.thirtyDay.totalActualCost)}</TableCell>
    </TableRow>
  )
}

function ReportDetailLoading() {
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6" aria-busy="true">
      <Skeleton className="h-8 w-48" />
      <Skeleton className="h-28 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  )
}
