import { KeyRoundIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { ThemeMenu } from '@/components/layout/theme-menu'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

export function AuthLayout({
  title,
  description,
  children,
  footer,
}: {
  title: string
  description: string
  children: ReactNode
  footer?: ReactNode
}) {
  return (
    <main className="grid min-h-svh grid-rows-[auto_1fr] bg-muted/30">
      <header className="flex h-14 items-center justify-between border-b bg-background px-4 sm:px-6">
        <div className="flex min-w-0 items-center gap-2">
          <span className="grid size-8 shrink-0 place-items-center rounded-md bg-primary text-primary-foreground">
            <KeyRoundIcon aria-hidden="true" />
          </span>
          <span className="truncate text-sm font-semibold">Sub2API Report</span>
        </div>
        <ThemeMenu />
      </header>
      <div className="flex min-h-0 items-start justify-center overflow-y-auto px-4 py-8 sm:items-center sm:py-12">
        <Card className="w-full max-w-md">
          <CardHeader>
            <CardTitle className="text-xl"><h1>{title}</h1></CardTitle>
            <CardDescription>{description}</CardDescription>
          </CardHeader>
          <CardContent>{children}</CardContent>
          {footer ? <CardFooter className="justify-center">{footer}</CardFooter> : null}
        </Card>
      </div>
    </main>
  )
}
