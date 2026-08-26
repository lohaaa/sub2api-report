type PageHeaderProps = {
  title: string
  description?: string
}

export function PageHeader({ title, description }: PageHeaderProps) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <h1 data-page-title tabIndex={-1} className="text-xl font-semibold outline-none">
        {title}
      </h1>
      {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
    </div>
  )
}
