import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2Icon, RefreshCwIcon, UsersIcon } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { FormError } from "@/features/auth/form-error";
import {
  getSub2ApiConnection,
  getSub2ApiUsers,
  synchronizeSub2ApiUsers,
  updateSub2ApiUserScope,
} from "@/lib/api-client";

export function Sub2ApiUserScope() {
  const queryClient = useQueryClient();
  const connectionQuery = useQuery({
    queryKey: ["sub2api-connection"],
    queryFn: ({ signal }) => getSub2ApiConnection(signal),
  });
  const usersQuery = useQuery({
    queryKey: ["sub2api-users"],
    queryFn: ({ signal }) => getSub2ApiUsers(signal),
    enabled: Boolean(connectionQuery.data?.configured),
  });
  const [mode, setMode] = useState<"SelectedUsers" | "AllActiveUsers">(
    "SelectedUsers",
  );
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  useEffect(() => {
    if (usersQuery.data) {
      setMode(usersQuery.data.scopeMode);
      setSelectedIds(
        usersQuery.data.users
          .filter((user) => user.isSelected)
          .map((user) => user.id),
      );
    }
  }, [usersQuery.data]);

  const syncMutation = useMutation({
    mutationFn: synchronizeSub2ApiUsers,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["sub2api-users"] }),
        queryClient.invalidateQueries({ queryKey: ["sub2api-connection"] }),
      ]);
    },
  });
  const saveMutation = useMutation({
    mutationFn: () =>
      updateSub2ApiUserScope({
        mode,
        selectedUserIds: selectedIds,
        revision: usersQuery.data?.connectionRevision ?? 0,
      }),
    onSuccess: (result) => {
      queryClient.setQueryData(["sub2api-users"], result);
    },
  });

  if (!connectionQuery.data?.configured) {
    return null;
  }

  const users =
    usersQuery.data?.users.filter((user) => user.retiredAt === null) ?? [];
  return (
    <div className="flex flex-col gap-4 border-t pt-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">统计用户范围</h3>
          <p className="text-sm text-muted-foreground">
            同步 Sub2API 用户后，选择指定用户或全部有效用户。
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={syncMutation.isPending}
          onClick={() => syncMutation.mutate()}
        >
          {syncMutation.isPending ? (
            <Spinner data-icon="inline-start" />
          ) : (
            <RefreshCwIcon data-icon="inline-start" />
          )}
          同步用户
        </Button>
      </div>
      <FormError message={errorMessage(syncMutation.error)} />
      <FormError message={errorMessage(saveMutation.error)} />
      <FormError message={errorMessage(usersQuery.error)} />
      {syncMutation.isSuccess ? (
        <Alert>
          <CheckCircle2Icon aria-hidden="true" />
          <AlertTitle>用户同步完成</AlertTitle>
          <AlertDescription>
            共读取 {syncMutation.data.total} 个用户，新增{" "}
            {syncMutation.data.added}
            个、更新 {syncMutation.data.updated} 个、退休{" "}
            {syncMutation.data.retired} 个。
          </AlertDescription>
        </Alert>
      ) : null}
      {saveMutation.isSuccess ? (
        <Alert>
          <CheckCircle2Icon aria-hidden="true" />
          <AlertTitle>统计范围已保存</AlertTitle>
          <AlertDescription>
            {saveMutation.data.scopeMode === "AllActiveUsers"
              ? "报告将统计全部有效用户的 Key。"
              : `报告将统计 ${saveMutation.data.users.filter((user) => user.isSelected).length} 个指定用户。`}
            当前配置 revision 为 {saveMutation.data.connectionRevision}。
          </AlertDescription>
        </Alert>
      ) : null}
      {usersQuery.isPending ? (
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Spinner />
          加载用户
        </div>
      ) : null}
      {users.length === 0 && !usersQuery.isPending ? (
        <Alert>
          <UsersIcon aria-hidden="true" />
          <AlertTitle>尚未同步用户</AlertTitle>
          <AlertDescription>
            点击“同步用户”读取 Sub2API 用户目录，再设置报告范围。
          </AlertDescription>
        </Alert>
      ) : null}
      {users.length > 0 ? (
        <>
          <Select
            value={mode}
            onValueChange={(value) => {
              setMode(value as typeof mode);
              saveMutation.reset();
            }}
          >
            <SelectTrigger aria-label="统计范围模式">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="SelectedUsers">指定用户</SelectItem>
                <SelectItem value="AllActiveUsers">全部有效用户</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          {mode === "SelectedUsers" ? (
            <div className="divide-y rounded-md border">
              {users.map((user) => (
                <label
                  key={user.id}
                  className="flex min-h-12 items-center gap-3 px-3 py-2 text-sm"
                >
                  <Checkbox
                    checked={selectedIds.includes(user.id)}
                    onCheckedChange={(checked) => {
                      setSelectedIds((current) =>
                        checked === true
                          ? [...new Set([...current, user.id])]
                          : current.filter((id) => id !== user.id),
                      );
                      saveMutation.reset();
                    }}
                  />
                  <span className="min-w-0 flex-1 truncate">{user.email}</span>
                  <span className="font-mono text-xs text-muted-foreground">
                    ID {user.externalId}
                  </span>
                  <Badge variant="outline">{user.status}</Badge>
                </label>
              ))}
            </div>
          ) : (
            <Alert>
              <UsersIcon aria-hidden="true" />
              <AlertTitle>全部有效用户</AlertTitle>
              <AlertDescription>
                当前会同步并统计{" "}
                {users.filter((user) => user.status === "active").length}{" "}
                个有效用户的 Key。
              </AlertDescription>
            </Alert>
          )}
          <div>
            <Button
              type="button"
              disabled={
                saveMutation.isPending ||
                (mode === "SelectedUsers" && selectedIds.length === 0)
              }
              onClick={() => saveMutation.mutate()}
            >
              {saveMutation.isPending ? (
                <Spinner data-icon="inline-start" />
              ) : null}
              保存统计范围
            </Button>
          </div>
        </>
      ) : null}
    </div>
  );
}

function errorMessage(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }
  return error ? "操作失败，请稍后重试。" : null;
}
