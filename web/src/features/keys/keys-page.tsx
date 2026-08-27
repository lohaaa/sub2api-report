import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRoundIcon, RefreshCwIcon, TriangleAlertIcon } from "lucide-react";
import { useState } from "react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { Spinner } from "@/components/ui/spinner";
import { FormError } from "@/features/auth/form-error";
import {
  getApiKeyInventory,
  synchronizeSub2ApiKeys,
  type ApiKeyInventoryItem,
} from "@/lib/api-client";

export function KeysPage() {
  const [page, setPage] = useState(1);
  const [retiredOnly, setRetiredOnly] = useState(false);
  const queryClient = useQueryClient();
  const inventoryQuery = useQuery({
    queryKey: ["api-keys", page, retiredOnly],
    queryFn: ({ signal }) => getApiKeyInventory(page, retiredOnly, signal),
  });
  const syncMutation = useMutation({
    mutationFn: synchronizeSub2ApiKeys,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["api-keys"] });
    },
  });

  if (inventoryQuery.isPending) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Spinner />
        加载 Key 清单
      </div>
    );
  }
  if (inventoryQuery.isError) {
    return (
      <FormError message="无法读取 Sub2API Key 清单，请稍后重试。" />
    );
  }

  const inventory = inventoryQuery.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="API Keys"
        description="按 Sub2API 用户同步的 Key 快照；报告生成前会自动刷新"
      />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={inventory.total > 0 ? "secondary" : "outline"}>
            共 {inventory.total} 个 Key
          </Badge>
          {inventory.diagnostics.retiredKeys > 0 ? (
            <Badge variant="outline">
              上游已移除 {inventory.diagnostics.retiredKeys}
            </Badge>
          ) : null}
          {inventory.lastSynchronizedAt ? (
            <span className="text-xs text-muted-foreground">
              最近同步 {formatDateTime(inventory.lastSynchronizedAt)}
            </span>
          ) : null}
        </div>
        <div className="flex items-center gap-3">
          <Label
            htmlFor="keys-retired-only"
            className="text-sm text-muted-foreground"
            title="这些 Key 已不在 Sub2API 当前清单中，仅保留历史记录"
          >
            仅看历史 Key
          </Label>
          <Checkbox
            id="keys-retired-only"
            checked={retiredOnly}
            onCheckedChange={(checked) => {
              setRetiredOnly(checked === true);
              setPage(1);
            }}
          />
          <Button
            type="button"
            size="sm"
            disabled={syncMutation.isPending}
            onClick={() => syncMutation.mutate()}
          >
            {syncMutation.isPending ? (
              <Spinner data-icon="inline-start" />
            ) : (
              <RefreshCwIcon data-icon="inline-start" />
            )}
            同步 Key
          </Button>
        </div>
      </div>
      <FormError message={
        syncMutation.error instanceof Error ? syncMutation.error.message : null
      } />
      {syncMutation.isSuccess ? (
        <Alert>
          <KeyRoundIcon aria-hidden="true" />
          <AlertTitle>Key 同步完成</AlertTitle>
          <AlertDescription>
            新增 {syncMutation.data.added} 个、更新 {syncMutation.data.updated}
            个、上游移除 {syncMutation.data.retired} 个，当前共 {syncMutation.data.total} 个。
          </AlertDescription>
        </Alert>
      ) : null}
      {inventory.items.length === 0 ? (
        <Alert>
          <TriangleAlertIcon aria-hidden="true" />
          <AlertTitle>{retiredOnly ? "没有历史 Key" : "暂无 Key"}</AlertTitle>
          <AlertDescription>
            {retiredOnly
              ? "当前没有已从 Sub2API 移除的历史 Key。"
              : "保存 Sub2API 连接并选择统计用户后，生成报告或点击“同步 Key”即可自动读取。"}

          </AlertDescription>
        </Alert>
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Key</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>Group ID</TableHead>
                <TableHead>最后使用</TableHead>
                <TableHead className="text-right">最近发现</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {inventory.items.map((key: ApiKeyInventoryItem) => (
                <TableRow key={key.id}>
                  <TableCell>
                    <div className="flex min-w-40 flex-col">
                      <span className="font-medium">{key.name}</span>
                      <span className="text-xs text-muted-foreground">
                        ID {key.externalId}
                      </span>
                      {key.sourceUserEmail ? (
                        <span className="text-xs text-muted-foreground">
                          用户 {key.sourceUserEmail}
                        </span>
                      ) : null}
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={
                        key.retiredAt
                          ? "outline"
                          : key.status === "active"
                            ? "secondary"
                            : "outline"
                      }
                    >
                      {key.retiredAt ? "上游已移除" : key.status}
                    </Badge>
                  </TableCell>
                  <TableCell>{key.groupId ?? "全部"}</TableCell>
                  <TableCell>
                    {key.lastUsedAt ? formatDateTime(key.lastUsedAt) : "从未使用"}
                  </TableCell>
                  <TableCell className="text-right text-xs text-muted-foreground">
                    {formatDateTime(key.lastSeenAt)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {inventory.pages > 1 ? (
        <div className="flex items-center justify-end gap-2 text-sm">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={page <= 1}
            onClick={() => setPage((current) => Math.max(1, current - 1))}
          >
            上一页
          </Button>
          <span className="text-muted-foreground">
            第 {inventory.page} / {inventory.pages} 页
          </span>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={page >= inventory.pages}
            onClick={() => setPage((current) => Math.min(inventory.pages, current + 1))}
          >
            下一页
          </Button>
        </div>
      ) : null}
    </div>
  );
}
function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
