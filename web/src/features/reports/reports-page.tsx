import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  DownloadIcon,
  EyeIcon,
  FileChartColumnIcon,
  PlayIcon,
} from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/layout/page-header'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
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
  generateReport,
  getReports,
  type ReportStatus,
} from '@/lib/api-client'
import { formatCost, formatDate, formatTimestamp } from './report-format'

export function ReportsPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [cutoffDate, setCutoffDate] = useState('')
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)
  const reportsQuery = useQuery({
    queryKey: ['reports', page],
    queryFn: ({ signal }) => getReports(page, signal),
  })
  const generateMutation = useMutation({
    mutationFn: () => generateReport(cutoffDate || null),
    onSuccess: async (report) => {
      await queryClient.invalidateQueries({ queryKey: ['reports'] })
      navigate(`/reports/${report.reportId}`)
    },
  })

  function handleGenerate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    generateMutation.mutate()
  }

  async function handleDownload(id: string) {
    setDownloadError(null)
    setDownloadingId(id)
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
      setDownloadingId(null)
    }
  }

  const errorMessage = generateMutation.error instanceof ApiError
    ? generateMutation.error.message
    : reportsQuery.isError
      ? '无法读取报告列表。'
      : downloadError

  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader title="报告记录" description="生成并核对不可变的 7/30 日用量快照" />
        <form className="flex flex-wrap items-end gap-2" onSubmit={handleGenerate}>
          <FieldGroup className="w-44">
            <Field>
              <FieldLabel htmlFor="report-cutoff-date">统计截止日</FieldLabel>
              <Input
                id="report-cutoff-date"
                name="cutoffDate"
                type="date"
                value={cutoffDate}
                onChange={(event) => setCutoffDate(event.target.value)}
              />
            </Field>
          </FieldGroup>
          <Button type="submit" disabled={generateMutation.isPending}>
            {generateMutation.isPending
              ? <Spinner data-icon="inline-start" />
              : <PlayIcon data-icon="inline-start" />}
            生成报告
          </Button>
        </form>
      </div>

      <FormError message={errorMessage} />

      <section aria-labelledby="report-list-title" className="border-y">
        <h2 id="report-list-title" className="sr-only">报告快照列表</h2>
        <Table>
          <TableCaption>按生成时间倒序排列的报告快照</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">截止日</TableHead>
              <TableHead scope="col">状态</TableHead>
              <TableHead scope="col" className="text-right">人员 / Key</TableHead>
              <TableHead scope="col" className="text-right">7 日费用</TableHead>
              <TableHead scope="col" className="text-right">30 日费用</TableHead>
              <TableHead scope="col">生成时间</TableHead>
              <TableHead scope="col" className="text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {reportsQuery.isPending
              ? Array.from({ length: 4 }, (_, index) => (
                  <TableRow key={index}>
                    <TableCell colSpan={7}><Skeleton className="h-8 w-full" /></TableCell>
                  </TableRow>
                ))
              : reportsQuery.data?.items.length
                ? reportsQuery.data.items.map((report) => (
                    <TableRow key={report.id}>
                      <TableCell className="font-medium">{formatDate(report.cutoffDate)}</TableCell>
                      <TableCell><ReportStatusBadge status={report.status} /></TableCell>
                      <TableCell className="text-right tabular-nums">
                        {report.personCount} / {report.keyCount}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{formatCost(report.sevenDayActualCost)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatCost(report.thirtyDayActualCost)}</TableCell>
                      <TableCell>{formatTimestamp(report.generatedAt)}</TableCell>
                      <TableCell>
                        <div className="flex justify-end gap-1">
                          <Link className={buttonVariants({ variant: 'ghost', size: 'sm' })} to={`/reports/${report.id}`}>
                            <EyeIcon data-icon="inline-start" />
                            查看
                          </Link>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            title="下载 CSV"
                            aria-label={`下载 ${report.cutoffDate} 报告 CSV`}
                            disabled={downloadingId === report.id}
                            onClick={() => handleDownload(report.id)}
                          >
                            {downloadingId === report.id ? <Spinner /> : <DownloadIcon />}
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                : (
                    <TableRow>
                      <TableCell colSpan={7} className="h-24 text-center text-muted-foreground">
                        <FileChartColumnIcon className="mx-auto mb-2" aria-hidden="true" />
                        暂无报告快照
                      </TableCell>
                    </TableRow>
                  )}
          </TableBody>
        </Table>
      </section>

      {reportsQuery.data && reportsQuery.data.pages > 1 ? (
        <nav className="flex items-center justify-end gap-2" aria-label="报告分页">
          <span className="text-sm text-muted-foreground">
            第 {reportsQuery.data.page} / {reportsQuery.data.pages} 页
          </span>
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="上一页"
            disabled={page <= 1}
            onClick={() => setPage((value) => Math.max(1, value - 1))}
          >
            <ArrowLeftIcon />
          </Button>
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="下一页"
            disabled={page >= reportsQuery.data.pages}
            onClick={() => setPage((value) => value + 1)}
          >
            <ArrowRightIcon />
          </Button>
        </nav>
      ) : null}
    </div>
  )
}

export function ReportStatusBadge({ status }: { status: ReportStatus }) {
  return status === 'Complete'
    ? <Badge variant="secondary">完整</Badge>
    : <Badge variant="outline">部分完成</Badge>
}
