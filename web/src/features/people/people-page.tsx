import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangleIcon,
  ArrowLeftIcon,
  ArrowRightIcon,
  KeyRoundIcon,
  PencilIcon,
  PlusIcon,
  RefreshCwIcon,
  Trash2Icon,
  UserPlusIcon,
  UsersIcon,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
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
import { PageHeader } from '@/components/layout/page-header'
import { FormError } from '@/features/auth/form-error'
import {
  ApiError,
  createApiKeyAssignment,
  createPerson,
  deactivatePerson,
  deleteApiKeyAssignment,
  getApiKeyInventory,
  getPeople,
  getSub2ApiConnection,
  synchronizeSub2ApiKeys,
  updateApiKeyAssignment,
  updatePerson,
  type ApiKeyAssignment,
  type ApiKeyInventoryItem,
  type Person,
} from '@/lib/api-client'

export function PeoplePage() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [unmappedOnly, setUnmappedOnly] = useState(false)
  const [personEditor, setPersonEditor] = useState<Person | 'create' | null>(null)
  const [assignmentEditor, setAssignmentEditor] = useState<{
    key: ApiKeyInventoryItem
    assignment?: ApiKeyAssignment
  } | null>(null)
  const peopleQuery = useQuery({ queryKey: ['people'], queryFn: ({ signal }) => getPeople(signal) })
  const connectionQuery = useQuery({
    queryKey: ['sub2api-connection'],
    queryFn: ({ signal }) => getSub2ApiConnection(signal),
  })
  const inventoryQuery = useQuery({
    queryKey: ['api-key-inventory', page, unmappedOnly],
    queryFn: ({ signal }) => getApiKeyInventory(page, unmappedOnly, signal),
  })
  const syncMutation = useMutation({
    mutationFn: synchronizeSub2ApiKeys,
    onSuccess: async () => {
      setPage(1)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['api-key-inventory'] }),
        queryClient.invalidateQueries({ queryKey: ['sub2api-connection'] }),
      ])
    },
  })

  const diagnostics = inventoryQuery.data?.diagnostics
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader title="人员与 Key" description="维护人员档案、Key 轮换有效期和未映射检查" />
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => setPersonEditor('create')}>
            <UserPlusIcon data-icon="inline-start" />
            新增人员
          </Button>
          <Button
            onClick={() => syncMutation.mutate()}
            disabled={!connectionQuery.data?.configured || syncMutation.isPending}
          >
            {syncMutation.isPending ? <Spinner data-icon="inline-start" /> : <RefreshCwIcon data-icon="inline-start" />}
            同步 Key
          </Button>
        </div>
      </div>

      {!connectionQuery.isPending && !connectionQuery.data?.configured ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>尚未配置 Sub2API</AlertTitle>
          <AlertDescription>请先在系统设置中保存连接和 Admin API Key。</AlertDescription>
        </Alert>
      ) : null}
      <FormError message={
        syncMutation.error instanceof ApiError
          ? syncMutation.error.message
          : peopleQuery.isError
            ? '无法读取人员列表。'
            : inventoryQuery.isError
              ? '无法读取 Key 清单。'
              : connectionQuery.isError
                ? '无法读取 Sub2API 连接状态。'
                : null
      } />
      {syncMutation.data ? (
        <Alert>
          <RefreshCwIcon aria-hidden="true" />
          <AlertTitle>Key 同步完成</AlertTitle>
          <AlertDescription>
            新增 {syncMutation.data.added}，更新 {syncMutation.data.updated}，退休 {syncMutation.data.retired}，当前共 {syncMutation.data.total} 个。
          </AlertDescription>
        </Alert>
      ) : null}
      {diagnostics && (diagnostics.unmappedKeys > 0 || diagnostics.overlappingAssignments > 0) ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>归属检查需要处理</AlertTitle>
          <AlertDescription>
            缺少必要归属 {diagnostics.unmappedKeys}，重叠归属 {diagnostics.overlappingAssignments}。
          </AlertDescription>
        </Alert>
      ) : null}
      {diagnostics && diagnostics.retiredKeys > 0 ? (
        <Alert>
          <KeyRoundIcon aria-hidden="true" />
          <AlertTitle>已保留退休 Key</AlertTitle>
          <AlertDescription>{diagnostics.retiredKeys} 个退休 Key 继续保留历史快照和归属。</AlertDescription>
        </Alert>
      ) : null}

      <section aria-labelledby="people-title" className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 id="people-title" className="text-base font-semibold">人员</h2>
            <p className="text-sm text-muted-foreground">停用人员会保留历史归属。</p>
          </div>
          <Badge variant="secondary">{peopleQuery.data?.length ?? 0} 人</Badge>
        </div>
        <div className="overflow-hidden rounded-md border">
          <Table>
            <TableCaption className="sr-only">人员档案列表</TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead>人员</TableHead>
                <TableHead>编码</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>当前 Key</TableHead>
                <TableHead>历史归属</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {peopleQuery.isPending ? <LoadingRow columns={6} /> : null}
              {peopleQuery.data?.length === 0 ? <EmptyRow columns={6} icon={UsersIcon} text="暂无人员记录" /> : null}
              {peopleQuery.data?.map((person) => (
                <TableRow key={person.id}>
                  <TableCell className="font-medium">{person.displayName}</TableCell>
                  <TableCell>{person.code}</TableCell>
                  <TableCell><Badge variant={person.isActive ? 'secondary' : 'outline'}>{person.isActive ? '启用' : '停用'}</Badge></TableCell>
                  <TableCell>{person.currentApiKeyCount}</TableCell>
                  <TableCell>{person.assignmentCount}</TableCell>
                  <TableCell className="text-right">
                    <Button size="sm" variant="ghost" onClick={() => setPersonEditor(person)}>
                      <PencilIcon data-icon="inline-start" />
                      编辑
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <section aria-labelledby="keys-title" className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 id="keys-title" className="text-base font-semibold">Sub2API Key 清单</h2>
            <p className="text-sm text-muted-foreground">
              {inventoryQuery.data?.lastSynchronizedAt
                ? `最近同步 ${formatDateTime(inventoryQuery.data.lastSynchronizedAt)}`
                : '尚未同步'}
            </p>
          </div>
          <Field orientation="horizontal" className="w-fit">
            <Checkbox
              id="unmapped-only"
              checked={unmappedOnly}
              onCheckedChange={(checked) => {
                setUnmappedOnly(checked)
                setPage(1)
              }}
            />
            <FieldLabel htmlFor="unmapped-only">仅看未映射</FieldLabel>
          </Field>
        </div>
        <div className="overflow-hidden rounded-md border">
          <Table>
            <TableCaption className="sr-only">已同步且不含业务密钥明文的 API Key 清单</TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead>Key</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>Group ID</TableHead>
                <TableHead>最后使用</TableHead>
                <TableHead>人员归属</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {inventoryQuery.isPending ? <LoadingRow columns={6} /> : null}
              {inventoryQuery.data?.items.length === 0 ? <EmptyRow columns={6} icon={KeyRoundIcon} text="暂无符合条件的 Key" /> : null}
              {inventoryQuery.data?.items.map((key) => {
                const latestAssignment = key.assignments.at(-1)
                return (
                  <TableRow key={key.id}>
                    <TableCell>
                      <div className="flex min-w-40 flex-col">
                        <span className="font-medium">{key.name}</span>
                        <span className="text-xs text-muted-foreground">ID {key.externalId}</span>
                      </div>
                    </TableCell>
                    <TableCell>
                      <Badge variant={key.retiredAt ? 'outline' : key.status === 'active' ? 'secondary' : 'outline'}>
                        {key.retiredAt ? '已退休' : key.status}
                      </Badge>
                    </TableCell>
                    <TableCell>{key.groupId ?? '全部'}</TableCell>
                    <TableCell>{key.lastUsedAt ? formatDateTime(key.lastUsedAt) : '从未使用'}</TableCell>
                    <TableCell>
                      {latestAssignment ? (
                        <div className="flex flex-col">
                          <span>{latestAssignment.personDisplayName}</span>
                          <span className="text-xs text-muted-foreground">
                            {formatRange(latestAssignment.validFrom, latestAssignment.validTo)}
                          </span>
                        </div>
                      ) : <Badge variant="outline">未映射</Badge>}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1">
                        {latestAssignment ? (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => setAssignmentEditor({ key, assignment: latestAssignment })}
                          >
                            <PencilIcon data-icon="inline-start" />
                            编辑
                          </Button>
                        ) : null}
                        <Button size="sm" variant="ghost" onClick={() => setAssignmentEditor({ key })}>
                          <PlusIcon data-icon="inline-start" />
                          分配
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
        <div className="flex items-center justify-between gap-3">
          <span className="text-sm text-muted-foreground">
            第 {inventoryQuery.data?.page ?? page} / {inventoryQuery.data?.pages ?? 1} 页，共 {inventoryQuery.data?.total ?? 0} 个
          </span>
          <div className="flex gap-1">
            <Button
              size="sm"
              variant="outline"
              disabled={page <= 1}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              <ArrowLeftIcon data-icon="inline-start" />
              上一页
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={page >= (inventoryQuery.data?.pages ?? 1)}
              onClick={() => setPage((current) => current + 1)}
            >
              下一页
              <ArrowRightIcon data-icon="inline-end" />
            </Button>
          </div>
        </div>
      </section>

      <PersonDialog
        value={personEditor}
        onOpenChange={(open) => { if (!open) setPersonEditor(null) }}
      />
      <AssignmentDialog
        value={assignmentEditor}
        people={peopleQuery.data ?? []}
        onOpenChange={(open) => { if (!open) setAssignmentEditor(null) }}
      />
    </div>
  )
}

const personSchema = z.object({
  code: z.string().trim().min(1, '请输入人员编码').max(64).regex(/^[A-Za-z0-9._-]+$/, '只能使用字母、数字、点、下划线和短横线'),
  displayName: z.string().trim().min(1, '请输入显示名称').max(200),
  isActive: z.boolean(),
})
type PersonValues = z.infer<typeof personSchema>

function PersonDialog({ value, onOpenChange }: {
  value: Person | 'create' | null
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const editing = value !== null && value !== 'create' ? value : null
  const form = useForm<PersonValues>({
    resolver: zodResolver(personSchema),
    defaultValues: { code: '', displayName: '', isActive: true },
  })
  useEffect(() => {
    form.reset(editing
      ? { code: editing.code, displayName: editing.displayName, isActive: editing.isActive }
      : { code: '', displayName: '', isActive: true })
  }, [editing, form, value])
  const saveMutation = useMutation({
    mutationFn: (values: PersonValues) => editing
      ? updatePerson(editing.id, { ...values, revision: editing.revision })
      : createPerson(values),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['people'] }),
        queryClient.invalidateQueries({ queryKey: ['api-key-inventory'] }),
      ])
      onOpenChange(false)
    },
  })
  const deactivateMutation = useMutation({
    mutationFn: () => deactivatePerson(editing?.id ?? ''),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['people'] })
      onOpenChange(false)
    },
  })

  return (
    <Dialog open={value !== null} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{editing ? '编辑人员' : '新增人员'}</DialogTitle>
          <DialogDescription>人员编码用于稳定识别，显示名称用于报告呈现。</DialogDescription>
        </DialogHeader>
        <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => saveMutation.mutate(values))} noValidate>
          <FormError message={
            saveMutation.error instanceof ApiError
              ? saveMutation.error.message
              : deactivateMutation.error instanceof ApiError
                ? deactivateMutation.error.message
                : null
          } />
          <FieldGroup>
            <Field data-invalid={Boolean(form.formState.errors.code)}>
              <FieldLabel htmlFor="person-code">人员编码</FieldLabel>
              <Input id="person-code" autoComplete="off" required aria-invalid={Boolean(form.formState.errors.code)} {...form.register('code')} />
              <FieldError errors={[form.formState.errors.code]} />
            </Field>
            <Field data-invalid={Boolean(form.formState.errors.displayName)}>
              <FieldLabel htmlFor="person-display-name">显示名称</FieldLabel>
              <Input id="person-display-name" autoComplete="name" required aria-invalid={Boolean(form.formState.errors.displayName)} {...form.register('displayName')} />
              <FieldError errors={[form.formState.errors.displayName]} />
            </Field>
            {editing ? (
              <Controller
                control={form.control}
                name="isActive"
                render={({ field }) => (
                  <Field orientation="horizontal">
                    <Checkbox id="person-active" checked={field.value} onCheckedChange={field.onChange} />
                    <FieldLabel htmlFor="person-active">启用人员</FieldLabel>
                  </Field>
                )}
              />
            ) : null}
          </FieldGroup>
          <DialogFooter>
            {editing?.isActive ? (
              <Button
                type="button"
                variant="destructive"
                disabled={deactivateMutation.isPending}
                onClick={() => deactivateMutation.mutate()}
              >
                <Trash2Icon data-icon="inline-start" />
                停用人员
              </Button>
            ) : null}
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>取消</Button>
            <Button type="submit" disabled={saveMutation.isPending}>
              {saveMutation.isPending ? <Spinner data-icon="inline-start" /> : null}
              {editing ? '保存人员' : '创建人员'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const assignmentSchema = z.object({
  personId: z.string().min(1, '请选择人员'),
  validFrom: z.iso.date('请选择开始日期'),
  validTo: z.union([z.literal(''), z.iso.date()]),
}).refine(
  (value) => value.validTo === '' || value.validTo >= value.validFrom,
  { path: ['validTo'], message: '结束日期不能早于开始日期' },
)
type AssignmentValues = z.infer<typeof assignmentSchema>

function AssignmentDialog({ value, people, onOpenChange }: {
  value: { key: ApiKeyInventoryItem; assignment?: ApiKeyAssignment } | null
  people: Person[]
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const assignment = value?.assignment
  const activePeople = people.filter((person) => person.isActive)
  const personOptions = activePeople.map((person) => ({ value: person.id, label: person.displayName }))
  const form = useForm<AssignmentValues>({
    resolver: zodResolver(assignmentSchema),
    defaultValues: { personId: '', validFrom: today(), validTo: '' },
  })
  useEffect(() => {
    form.reset({
      personId: assignment?.personId ?? '',
      validFrom: assignment?.validFrom ?? today(),
      validTo: assignment?.validTo ?? '',
    })
  }, [assignment, form, value])
  const saveMutation = useMutation({
    mutationFn: (values: AssignmentValues) => assignment
      ? updateApiKeyAssignment(assignment.id, {
          validFrom: values.validFrom,
          validTo: values.validTo || null,
          revision: assignment.revision,
        })
      : createApiKeyAssignment(values.personId, {
          externalApiKeyId: value?.key.id ?? '',
          validFrom: values.validFrom,
          validTo: values.validTo || null,
        }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['people'] }),
        queryClient.invalidateQueries({ queryKey: ['api-key-inventory'] }),
      ])
      onOpenChange(false)
    },
  })
  const deleteMutation = useMutation({
    mutationFn: () => deleteApiKeyAssignment(assignment?.id ?? ''),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['people'] }),
        queryClient.invalidateQueries({ queryKey: ['api-key-inventory'] }),
      ])
      onOpenChange(false)
    },
  })

  return (
    <Dialog open={value !== null} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{assignment ? '编辑 Key 归属' : '分配 Key'}</DialogTitle>
          <DialogDescription>
            {value ? `${value.key.name} · ID ${value.key.externalId}` : '选择人员和有效日期。'}
          </DialogDescription>
        </DialogHeader>
        <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => saveMutation.mutate(values))} noValidate>
          <FormError message={
            saveMutation.error instanceof ApiError
              ? saveMutation.error.message
              : deleteMutation.error instanceof ApiError
                ? deleteMutation.error.message
                : null
          } />
          <FieldGroup>
            <Controller
              control={form.control}
              name="personId"
              render={({ field, fieldState }) => (
                <Field data-invalid={Boolean(fieldState.error)}>
                  <FieldLabel htmlFor="assignment-person">人员</FieldLabel>
                  {assignment ? (
                    <Input id="assignment-person" value={assignment.personDisplayName} disabled />
                  ) : (
                    <Select items={personOptions} value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="assignment-person" className="w-full" aria-invalid={Boolean(fieldState.error)}>
                        <SelectValue placeholder="选择人员" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          {personOptions.map((person) => (
                            <SelectItem key={person.value} value={person.value}>{person.label}</SelectItem>
                          ))}
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  )}
                  <FieldError errors={[fieldState.error]} />
                </Field>
              )}
            />
            <Field data-invalid={Boolean(form.formState.errors.validFrom)}>
              <FieldLabel htmlFor="assignment-valid-from">生效日期</FieldLabel>
              <Input id="assignment-valid-from" type="date" required aria-invalid={Boolean(form.formState.errors.validFrom)} {...form.register('validFrom')} />
              <FieldError errors={[form.formState.errors.validFrom]} />
            </Field>
            <Field data-invalid={Boolean(form.formState.errors.validTo)}>
              <FieldLabel htmlFor="assignment-valid-to">结束日期</FieldLabel>
              <Input id="assignment-valid-to" type="date" aria-invalid={Boolean(form.formState.errors.validTo)} {...form.register('validTo')} />
              <FieldDescription>留空表示持续有效；起止日期均包含在归属期内。</FieldDescription>
              <FieldError errors={[form.formState.errors.validTo]} />
            </Field>
          </FieldGroup>
          <DialogFooter>
            {assignment ? (
              <Button type="button" variant="destructive" disabled={deleteMutation.isPending} onClick={() => deleteMutation.mutate()}>
                <Trash2Icon data-icon="inline-start" />
                删除归属
              </Button>
            ) : null}
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>取消</Button>
            <Button type="submit" disabled={saveMutation.isPending || (!assignment && activePeople.length === 0)}>
              {saveMutation.isPending ? <Spinner data-icon="inline-start" /> : null}
              {assignment ? '保存归属' : '创建归属'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function LoadingRow({ columns }: { columns: number }) {
  return <TableRow><TableCell colSpan={columns} className="h-24 text-center"><Spinner />加载中</TableCell></TableRow>
}

function EmptyRow({ columns, icon: Icon, text }: { columns: number; icon: typeof UsersIcon; text: string }) {
  return (
    <TableRow>
      <TableCell colSpan={columns} className="h-32 text-center">
        <span className="inline-flex items-center gap-2 text-muted-foreground"><Icon aria-hidden="true" />{text}</span>
      </TableCell>
    </TableRow>
  )
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

function formatRange(validFrom: string, validTo: string | null) {
  return validTo ? `${validFrom} 至 ${validTo}` : `${validFrom} 起`
}

function today() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}
