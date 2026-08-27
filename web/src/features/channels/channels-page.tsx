import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MegaphoneIcon,
  PencilIcon,
  PlugZapIcon,
  PlusIcon,
  Trash2Icon,
} from "lucide-react";
import { useState } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Spinner } from "@/components/ui/spinner";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { FormError } from "@/features/auth/form-error";
import {
  ChannelEditorDialog,
  channelTypeLabels,
} from "@/features/channels/channel-editor-dialog";
import {
  ApiError,
  deleteChannel,
  getChannels,
  testChannel,
  type NotificationChannel,
  type NotificationChannelType,
} from "@/lib/api-client";

export function ChannelsPage() {
  const queryClient = useQueryClient();
  const [editorOpen, setEditorOpen] = useState(false);
  const [editorType, setEditorType] =
    useState<NotificationChannelType>("Email");
  const [editing, setEditing] = useState<NotificationChannel | null>(null);
  const [deleting, setDeleting] = useState<NotificationChannel | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const channelsQuery = useQuery({
    queryKey: ["channels"],
    queryFn: ({ signal }) => getChannels(signal),
  });
  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteChannel(id),
    onSuccess: async () => {
      setDeleting(null);
      await queryClient.invalidateQueries({ queryKey: ["channels"] });
    },
  });

  function openCreate() {
    setEditing(null);
    setEditorType("Email");
    setEditorOpen(true);
  }

  function openEdit(channel: NotificationChannel) {
    setEditing(channel);
    setEditorType(channel.type);
    setEditorOpen(true);
  }

  async function runTest(id: string) {
    setTestError(null);
    try {
      await testChannel(id);
      await queryClient.invalidateQueries({ queryKey: ["channels"] });
    } catch (error) {
      setTestError(
        error instanceof ApiError ? error.message : "测试发送失败。",
      );
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader
          title="发送渠道"
          description="配置邮件、钉钉和飞书投递，秘密加密保存并只显示掩码"
        />
        <Button onClick={openCreate}>
          <PlusIcon data-icon="inline-start" />
          新增渠道
        </Button>
      </div>

      <FormError
        message={channelsQuery.isError ? "无法读取发送渠道。" : testError}
      />
      {deleteMutation.error instanceof ApiError ? (
        <FormError message={deleteMutation.error.message} />
      ) : null}

      {channelsQuery.isPending ? (
        <div
          className="flex items-center gap-2 text-sm text-muted-foreground"
          aria-busy="true"
        >
          <Spinner />
          加载发送渠道
        </div>
      ) : (
        <Table>
          <TableCaption>已配置的通知渠道，测试消息使用合成数据</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">名称</TableHead>
              <TableHead scope="col">类型</TableHead>
              <TableHead scope="col">状态</TableHead>
              <TableHead scope="col">最近测试</TableHead>
              <TableHead scope="col">
                <span className="sr-only">操作</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {channelsQuery.data && channelsQuery.data.length > 0 ? (
              channelsQuery.data.map((channel) => (
                <TableRow key={channel.id}>
                  <TableCell>
                    <div className="flex flex-col">
                      <span className="font-medium">{channel.name}</span>
                      {channel.type === "Email" && channel.email ? (
                        <span className="text-xs text-muted-foreground">
                          {channel.email.toAddresses.length} 个收件人
                          {channel.email.hasPassword
                            ? ` · 密码 ${channel.email.passwordMask ?? ""}`
                            : ""}
                        </span>
                      ) : null}
                      {channel.type !== "Email" && channel.webhook ? (
                        <span className="text-xs text-muted-foreground">
                          Webhook {channel.webhook.webhookMask ?? ""} · 密钥{" "}
                          {channel.webhook.signSecretMask ?? ""}
                        </span>
                      ) : null}
                    </div>
                  </TableCell>
                  <TableCell>{channelTypeLabels[channel.type]}</TableCell>
                  <TableCell>
                    <Badge variant={channel.enabled ? "secondary" : "outline"}>
                      {channel.enabled ? "已启用" : "已停用"}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {channel.lastTestSucceeded === null ? (
                      <span className="text-muted-foreground">未测试</span>
                    ) : (
                      <div className="flex flex-col">
                        <Badge
                          variant={
                            channel.lastTestSucceeded ? "secondary" : "outline"
                          }
                        >
                          {channel.lastTestSucceeded
                            ? "测试通过"
                            : `测试失败 · ${channel.lastTestCode ?? "failed"}`}
                        </Badge>
                        {channel.lastTestedAt ? (
                          <span className="mt-1 text-xs text-muted-foreground">
                            {formatDateTime(channel.lastTestedAt)}
                          </span>
                        ) : null}
                      </div>
                    )}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-wrap justify-end gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={!channel.enabled}
                        onClick={() => runTest(channel.id)}
                      >
                        <PlugZapIcon data-icon="inline-start" />
                        测试
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => openEdit(channel)}
                      >
                        <PencilIcon data-icon="inline-start" />
                        编辑
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-destructive"
                        onClick={() => setDeleting(channel)}
                      >
                        <Trash2Icon data-icon="inline-start" />
                        删除
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell
                  colSpan={5}
                  className="h-24 text-center text-muted-foreground"
                >
                  <div className="flex flex-col items-center gap-2">
                    <MegaphoneIcon aria-hidden="true" className="size-5" />
                    暂无发送渠道，点击“新增渠道”开始配置
                  </div>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      )}

      <ChannelEditorDialog
        open={editorOpen}
        onOpenChange={setEditorOpen}
        channelType={editorType}
        existing={editing}
        onSaved={async () => {
          await queryClient.invalidateQueries({ queryKey: ["channels"] });
        }}
      />

      <Dialog
        open={deleting !== null}
        onOpenChange={(open) => (!open ? setDeleting(null) : undefined)}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>删除发送渠道</DialogTitle>
            <DialogDescription>
              只有从未投递过的渠道才能删除；已有投递记录时请改为停用。
            </DialogDescription>
          </DialogHeader>
          {deleting ? (
            <p className="text-sm">
              确认删除 <strong>{deleting.name}</strong>（
              {channelTypeLabels[deleting.type]}）？
            </p>
          ) : null}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setDeleting(null)}
            >
              取消
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={deleteMutation.isPending}
              onClick={() => deleting && deleteMutation.mutate(deleting.id)}
            >
              {deleteMutation.isPending ? (
                <Spinner data-icon="inline-start" />
              ) : null}
              确认删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {channelsQuery.data && channelsQuery.data.length === 0 ? (
        <Alert>
          <AlertTitle>下一步</AlertTitle>
          <AlertDescription>
            配置渠道后，可以在报告详情页把快照发送给已启用的渠道。
          </AlertDescription>
        </Alert>
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
