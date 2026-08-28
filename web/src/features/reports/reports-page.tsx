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
import { Checkbox } from '@/components/ui/checkbox'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field'
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
  createDefaultReportWindowSpecs,
  downloadReportCsv,
  generateReport,
  getReportGenerationRuns,
  getReports,
  reportWindowKeys,
  type ReportGenerationStatus,
  type ReportListItem,
  type ReportStatus,
  type ReportWindowSpec,
} from '@/lib/api-client'
import { formatCost, formatDate, formatTimestamp } from './report-format'

const builtinWindowOptions = [
  { key: reportWindowKeys.rollingSevenDays, label: '滚动 7 天' },
  { key: reportWindowKeys.rollingThirtyDays, label: '滚动 30 天' },
  { key: reportWindowKeys.previousCalendarWeek, label: '上一完整自然周' },
  { key: reportWindowKeys.previousCalendarMonth, label: '上一完整自然月' },
] as const

function validateWindowSelection(input: {
  selectedWindowKeys: string[]
  customStartDate: string
  customEndDate: string
  cutoffDate: string
}) {
  const hasCustomStart = input.customStartDate !== ''
  const hasCustomEnd = input.customEndDate !== ''
  if (hasCustomStart !== hasCustomEnd) {
    return '自定义区间需要同时填写开始日与结束日'
  }
  if (hasCustomStart && input.customStartDate > input.customEndDate) {
    return '自定义区间开始日不能晚于结束日'
  }
  if (
    hasCustomStart &&
    input.cutoffDate !== '' &&
    input.customEndDate > input.cutoffDate
  ) {
    return '自定义区间结束日不能晚于统计截止日'
  }
  if (
    input.selectedWindowKeys.length === 0 &&
    !(hasCustomStart && hasCustomEnd)
  ) {
    return '至少选择一个统计窗口'
  }
  return null
}

function buildReportWindowSpecs(input: {
  selectedWindowKeys: string[]
  customStartDate: string
  customEndDate: string
}): ReportWindowSpec[] {
  const specs = createDefaultReportWindowSpecs().filter((spec) =>
    input.selectedWindowKeys.includes(spec.key),
  )
  if (input.customStartDate && input.customEndDate) {
    specs.push({
      key: reportWindowKeys.customRange,
      kind: 'CustomRange',
      rollingDays: null,
      weekStartsOn: null,
      customStartDate: input.customStartDate,
      customEndDate: input.customEndDate,
    })
  }
  return specs
}

export function ReportsPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [cutoffDate, setCutoffDate] = useState('')
  const [selectedWindowKeys, setSelectedWindowKeys] = useState<string[]>(
    builtinWindowOptions.map((option) => option.key),
  )
  const [customStartDate, setCustomStartDate] = useState('')
  const [customEndDate, setCustomEndDate] = useState('')
  const [windowFormError, setWindowFormError] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)
  const reportsQuery = useQuery({
    queryKey: ['reports', page],
    queryFn: ({ signal }) => getReports(page, signal),
  })
  const generateMutation = useMutation({
    mutationFn: (windows: ReportWindowSpec[]) =>
      generateReport(cutoffDate || null, windows),
    onSuccess: async (report) => {
      await queryClient.invalidateQueries({ queryKey: ['reports'] })
      await queryClient.invalidateQueries({ queryKey: ['report-generations'] })
      navigate(`/reports/${report.reportId}`)
    },
  })
  const generationsQuery = useQuery({
    queryKey: ['report-generations'],
    queryFn: ({ signal }) => getReportGenerationRuns(1, signal),
  })

  function toggleWindowKey(key: string, checked: boolean) {
    setWindowFormError(null)
    setSelectedWindowKeys((current) =>
      checked
        ? [...new Set([...current, key])]
        : current.filter((value) => value !== key),
    )
  }

  function handleGenerate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const validationError = validateWindowSelection({
      selectedWindowKeys,
      customStartDate,
      customEndDate,
      cutoffDate,
    })
    if (validationError) {
      setWindowFormError(validationError)
      return
    }
    setWindowFormError(null)
    generateMutation.mutate(
      buildReportWindowSpecs({ selectedWindowKeys, customStartDate, customEndDate }),
    )
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
        <PageHeader title="报告记录" description="按可配置统计窗口生成并核对不可变用量快照" />
      </div>

      <form className="border-y py-5" noValidate onSubmit={handleGenerate}>
        <FieldGroup className="max-w-3xl">
          <Field className="sm:max-w-64">
            <FieldLabel htmlFor="report-cutoff-date">统计截止日（选填）</FieldLabel>
            <Input
              id="report-cutoff-date"
              name="cutoffDate"
              type="date"
              value={cutoffDate}
              onChange={(event) => setCutoffDate(event.target.value)}
            />
            <FieldDescription>留空时使用配置时区的昨天</FieldDescription>
          </Field>
          <FieldSet>
            <FieldLegend>统计窗口</FieldLegend>
            <FieldGroup className="sm:grid sm:grid-cols-2 sm:gap-x-6 sm:gap-y-2">
              {builtinWindowOptions.map((option) => {
                const id = `report-window-${option.key}`
                return (
                  <Field key={option.key} orientation="horizontal">
                    <Checkbox
                      id={id}
                      checked={selectedWindowKeys.includes(option.key)}
                      onCheckedChange={(checked) => toggleWindowKey(option.key, checked === true)}
                    />
                    <FieldLabel htmlFor={id} className="font-normal">{option.label}</FieldLabel>
                  </Field>
                )
              })}
            </FieldGroup>
            <FieldGroup className="sm:grid sm:grid-cols-2 sm:gap-4">
              <Field>
                <FieldLabel htmlFor="report-custom-start">自定义开始日（选填）</FieldLabel>
                <Input
                  id="report-custom-start"
                  name="customStartDate"
                  type="date"
                  value={customStartDate}
                  onChange={(event) => {
                    setWindowFormError(null)
                    setCustomStartDate(event.target.value)
                  }}
                />
              </Field>
              <Field>
                <FieldLabel htmlFor="report-custom-end">自定义结束日（选填）</FieldLabel>
                <Input
                  id="report-custom-end"
                  name="customEndDate"
                  type="date"
                  value={customEndDate}
                  onChange={(event) => {
                    setWindowFormError(null)
                    setCustomEndDate(event.target.value)
                  }}
                />
              </Field>
            </FieldGroup>
            {windowFormError ? <FieldError>{windowFormError}</FieldError> : null}
          </FieldSet>
          <div>
            <Button type="submit" disabled={generateMutation.isPending}>
              {generateMutation.isPending
                ? <Spinner data-icon="inline-start" />
                : <PlayIcon data-icon="inline-start" />}
              生成报告
            </Button>
          </div>
        </FieldGroup>
      </form>

      <FormError message={errorMessage} />

      <section aria-labelledby="report-list-title" className="border-y">
        <h2 id="report-list-title" className="sr-only">报告快照列表</h2>
        <Table>
          <TableCaption>按生成时间倒序排列的报告快照</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">截止日</TableHead>
              <TableHead scope="col">状态</TableHead>
              <TableHead scope="col" className="text-right">用户数（个） / Key 数（个）</TableHead>
              <TableHead scope="col" className="text-right">窗口费用（USD）</TableHead>
              <TableHead scope="col">生成时间</TableHead>
              <TableHead scope="col" className="text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {reportsQuery.isPending
              ? Array.from({ length: 4 }, (_, index) => (
                  <TableRow key={index}>
                    <TableCell colSpan={6}><Skeleton className="h-8 w-full" /></TableCell>
                  </TableRow>
                ))
              : reportsQuery.data?.items.length
                ? reportsQuery.data.items.map((report) => (
                    <TableRow key={report.id}>
                      <TableCell className="font-medium">{formatDate(report.cutoffDate)}</TableCell>
                      <TableCell><ReportStatusBadge status={report.status} /></TableCell>
                      <TableCell className="text-right tabular-nums">
                        {report.userCount} / {report.keyCount}
                      </TableCell>
                      <TableCell className="text-right">
                        <ReportWindowCosts report={report} />
                      </TableCell>
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
                      <TableCell colSpan={6} className="h-24 text-center text-muted-foreground">
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

      <section aria-labelledby="generation-runs-title" className="border-y">
        <h2 id="generation-runs-title" className="py-3 text-sm font-semibold">最近生成记录</h2>
        <Table>
          <TableCaption>包含自动刷新失败的阶段与错误信息</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">开始时间</TableHead>
              <TableHead scope="col">状态</TableHead>
              <TableHead scope="col">失败阶段</TableHead>
              <TableHead scope="col">错误信息</TableHead>
              <TableHead scope="col" className="text-right">报告</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {generationsQuery.isPending
              ? (
                  <TableRow>
                    <TableCell colSpan={5}><Skeleton className="h-8 w-full" /></TableCell>
                  </TableRow>
                )
              : generationsQuery.data?.items?.length
                ? generationsQuery.data.items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>{formatTimestamp(item.startedAt)}</TableCell>
                      <TableCell><GenerationStatusBadge status={item.status} /></TableCell>
                      <TableCell>{item.stage ?? '—'}</TableCell>
                      <TableCell>{item.errorMessage ?? '—'}</TableCell>
                      <TableCell>
                        {item.reportSnapshotId
                          ? (
                              <Link
                                className={buttonVariants({ variant: 'ghost', size: 'sm' })}
                                to={`/reports/${item.reportSnapshotId}`}
                              >
                                <EyeIcon data-icon="inline-start" />
                                查看
                              </Link>
                            )
                            : '—'}
                      </TableCell>
                    </TableRow>
                  ))
                : (
                    <TableRow>
                      <TableCell colSpan={5} className="h-16 text-center text-muted-foreground">
                        暂无生成记录
                      </TableCell>
                    </TableRow>
                  )}
          </TableBody>
        </Table>
      </section>
    </div>
  )
}

function GenerationStatusBadge({ status }: { status: ReportGenerationStatus }) {
  return status === 'Succeeded'
    ? <Badge variant="secondary">成功</Badge>
    : status === 'Failed'
      ? <Badge variant="outline">失败</Badge>
      : <Badge variant="outline">运行中</Badge>
}

function ReportWindowCosts({ report }: { report: ReportListItem }) {
  const windows = report.windows ?? [];
  if (windows.length > 0) {
    return (
      <div className="flex flex-col text-xs tabular-nums">
        {windows.map((window) => (
          <span key={window.key}>
            {window.label}：{formatCost(window.totalActualCost)}
          </span>
        ))}
      </div>
    )
  }
  return (
    <div className="flex flex-col text-xs tabular-nums">
      <span>最近 7 天：{formatCost(report.sevenDayActualCost)}</span>
      <span>最近 30 天：{formatCost(report.thirtyDayActualCost)}</span>
    </div>
  )
}

export function ReportStatusBadge({ status }: { status: ReportStatus }) {
  return status === 'Complete'
    ? <Badge variant="secondary">完整</Badge>
    : <Badge variant="outline">部分完成</Badge>
}
