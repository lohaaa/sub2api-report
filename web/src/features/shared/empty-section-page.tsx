import type { LucideIcon } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { PageHeader } from '@/components/layout/page-header'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

type EmptySectionPageProps = {
  title: string
  description: string
  icon: LucideIcon
  columns: string[]
  emptyText: string
}

export function EmptySectionPage({
  title,
  description,
  icon: Icon,
  columns,
  emptyText,
}: EmptySectionPageProps) {
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader title={title} description={description} />
        <Badge variant="secondary">0 条记录</Badge>
      </div>
      <section aria-label={`${title}列表`} className="min-w-0">
        <div className="overflow-hidden rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                {columns.map((column) => <TableHead key={column}>{column}</TableHead>)}
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableCell colSpan={columns.length} className="h-48 text-center">
                  <div className="flex flex-col items-center gap-2 text-muted-foreground">
                    <Icon aria-hidden="true" />
                    <span>{emptyText}</span>
                  </div>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </section>
    </div>
  )
}
