import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CheckCircle2Icon,
  PlugZapIcon,
  ShieldAlertIcon,
  BookOpenIcon,
} from "lucide-react";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Alert, AlertAction, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Spinner } from "@/components/ui/spinner";
import { FormError } from "@/features/auth/form-error";
import { PasswordField } from "@/features/auth/password-field";
import {
  ApiError,
  getCurrentAdministrator,
  getSub2ApiConnection,
  saveSub2ApiConnection,
  testSub2ApiConnection,
} from "@/lib/api-client";

const positiveId = /^[1-9][0-9]{0,18}$/;
const schema = z.object({
  baseUrl: z
    .url("请输入完整的 HTTP 或 HTTPS 地址")
    .max(2048)
    .regex(/^https?:\/\//i, "只允许 HTTP 或 HTTPS 地址"),
  adminApiKey: z.string().max(4096),
  codexGroupId: z
    .string()
    .refine(
      (value) => value === "" || positiveId.test(value),
      "请输入数字分组 ID（例如 123），不要填写组名",
    ),
});
type Values = z.infer<typeof schema>;

export function Sub2ApiConnectionForm() {
  const queryClient = useQueryClient();
  const [guideOpen, setGuideOpen] = useState(false);
  const connectionQuery = useQuery({
    queryKey: ["sub2api-connection"],
    queryFn: ({ signal }) => getSub2ApiConnection(signal),
  });
  const administratorQuery = useQuery({
    queryKey: ["current-administrator"],
    queryFn: ({ signal }) => getCurrentAdministrator(signal),
    staleTime: 30_000,
    retry: false,
  });
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      baseUrl: "",
      adminApiKey: "",
      codexGroupId: "",
    },
  });
  useEffect(() => {
    if (connectionQuery.data) {
      form.reset({
        baseUrl: connectionQuery.data.baseUrl ?? "",
        adminApiKey: "",
        codexGroupId: connectionQuery.data.codexGroupId ?? "",
      });
    }
  }, [connectionQuery.data, form]);
  const saveMutation = useMutation({
    mutationFn: (values: Values) =>
      saveSub2ApiConnection({
        baseUrl: values.baseUrl,
        adminApiKey: values.adminApiKey || null,
        clearAdminApiKey: false,
        codexGroupId: values.codexGroupId || null,
        revision: connectionQuery.data?.revision ?? 0,
      }),
    onSuccess: (connection) => {
      queryClient.setQueryData(["sub2api-connection"], connection);
      form.reset({
        baseUrl: connection.baseUrl ?? "",
        adminApiKey: "",
        codexGroupId: connection.codexGroupId ?? "",
      });
    },
  });
  const testMutation = useMutation({
    mutationFn: testSub2ApiConnection,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["sub2api-connection"] });
    },
  });
  const stepUpExpiresAt = administratorQuery.data?.stepUpExpiresAt ?? null;
  const [authorizationCheckedAt, setAuthorizationCheckedAt] = useState(() =>
    Date.now(),
  );
  useEffect(() => {
    if (!stepUpExpiresAt) {
      return;
    }
    const timer = window.setInterval(
      () => setAuthorizationCheckedAt(Date.now()),
      30_000,
    );
    return () => window.clearInterval(timer);
  }, [stepUpExpiresAt]);
  const stepUpValidUntil =
    stepUpExpiresAt !== null &&
    new Date(stepUpExpiresAt).getTime() > authorizationCheckedAt
      ? stepUpExpiresAt
      : null;

  if (connectionQuery.isPending) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Spinner />
        加载连接配置
      </div>
    );
  }
  if (connectionQuery.isError) {
    return <FormError message="无法读取 Sub2API 连接配置。" />;
  }

  const connection = connectionQuery.data;
  function focusStepUpPassword() {
    const field = document.getElementById("step-up-password");
    if (!field) {
      return;
    }
    field.scrollIntoView({ behavior: "smooth", block: "center" });
    field.focus({ preventScroll: true });
  }
  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={connection.configured ? "secondary" : "outline"}>
          {connection.configured ? "已配置" : "未配置"}
        </Badge>
        {connection.adminApiKeyMask ? (
          <Badge variant="outline">密钥 {connection.adminApiKeyMask}</Badge>
        ) : null}
        {connection.lastTestSucceeded !== null ? (
          <Badge
            variant={connection.lastTestSucceeded ? "secondary" : "outline"}
          >
            {connection.lastTestSucceeded
              ? "连接测试通过"
              : `连接测试失败 · ${connection.lastTestCode ?? "unknown"}`}
          </Badge>
        ) : null}
        {connection.lastSynchronizedAt ? (
          <span className="text-xs text-muted-foreground">
            最近同步 {formatDateTime(connection.lastSynchronizedAt)} ·{" "}
            {connection.lastSynchronizedKeyCount ?? 0} 个 Key
          </span>
        ) : null}
      </div>
      <div className="flex justify-end">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setGuideOpen(true)}
        >
          <BookOpenIcon data-icon="inline-start" />
          获取指南
        </Button>
      </div>
      <form
        className="flex flex-col gap-5"
        onChange={() => {
          if (saveMutation.isError) {
            saveMutation.reset();
          }
        }}
        onSubmit={form.handleSubmit((values) => {
          if (!connection.hasAdminApiKey && !values.adminApiKey) {
            form.setError("adminApiKey", {
              message: "首次保存必须填写 Admin API Key",
            });
            return;
          }
          saveMutation.mutate(values);
        })}
        noValidate
      >
        <FormError
          message={
            saveMutation.error instanceof ApiError &&
            saveMutation.error.status !== 403
              ? saveMutation.error.message
              : null
          }
        />
        {saveMutation.error instanceof ApiError &&
        saveMutation.error.status === 403 ? (
          <Alert variant="destructive">
            <ShieldAlertIcon aria-hidden="true" />
            <AlertTitle>需要敏感操作授权</AlertTitle>
            <AlertDescription>
              授权已过期或不存在，请在管理员安全中重新确认当前密码后再保存。
            </AlertDescription>
          </Alert>
        ) : null}
        <Alert>
          {stepUpValidUntil ? (
            <CheckCircle2Icon aria-hidden="true" />
          ) : (
            <ShieldAlertIcon aria-hidden="true" />
          )}
          <AlertTitle>保存连接配置需要敏感操作授权</AlertTitle>
          <AlertDescription>
            {stepUpValidUntil
              ? `授权有效至 ${formatTime(stepUpValidUntil)}，现在可以保存连接配置。`
              : "保存会写入 Admin API Key 等敏感数据，请先在管理员安全中确认当前密码，获得 10 分钟授权。"}
          </AlertDescription>
          {stepUpValidUntil ? null : (
            <AlertAction>
              <Button
                size="sm"
                variant="outline"
                type="button"
                onClick={focusStepUpPassword}
              >
                前往授权
              </Button>
            </AlertAction>
          )}
        </Alert>
        {saveMutation.isSuccess ? (
          <Alert>
            <CheckCircle2Icon aria-hidden="true" />
            <AlertTitle>连接配置已保存</AlertTitle>
            <AlertDescription>
              当前 revision 为 {saveMutation.data.revision}。
            </AlertDescription>
          </Alert>
        ) : null}
        <FieldGroup className="sm:grid sm:grid-cols-2">
          <Field
            className="sm:col-span-2"
            data-invalid={Boolean(form.formState.errors.baseUrl)}
          >
            <FieldLabel htmlFor="sub2api-base-url">Base URL</FieldLabel>
            <Input
              id="sub2api-base-url"
              type="url"
              inputMode="url"
              autoComplete="url"
              required
              aria-invalid={Boolean(form.formState.errors.baseUrl)}
              {...form.register("baseUrl")}
            />
            <FieldDescription>
              访问 Sub2API 的站点地址，只保留协议、域名和端口，不要加 /admin 或
              /api/v1。
            </FieldDescription>
            <FieldError errors={[form.formState.errors.baseUrl]} />
          </Field>
          <Field
            className="sm:col-span-2"
            data-invalid={Boolean(form.formState.errors.adminApiKey)}
          >
            <FieldLabel htmlFor="sub2api-admin-key">Admin API Key</FieldLabel>
            <PasswordField
              id="sub2api-admin-key"
              autoComplete="off"
              aria-invalid={Boolean(form.formState.errors.adminApiKey)}
              {...form.register("adminApiKey")}
            />
            <FieldDescription>
              {connection.hasAdminApiKey
                ? "留空会保留当前密钥；输入新值会替换密钥。"
                : "首次保存必须填写密钥。"}
            </FieldDescription>
            <FieldError errors={[form.formState.errors.adminApiKey]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.codexGroupId)}>
            <FieldLabel htmlFor="sub2api-group-id">
              Codex Group ID（选填）
            </FieldLabel>
            <Input
              id="sub2api-group-id"
              inputMode="numeric"
              autoComplete="off"
              placeholder="例如 123"
              pattern="[1-9][0-9]{0,18}"
              aria-invalid={Boolean(form.formState.errors.codexGroupId)}
              {...form.register("codexGroupId")}
            />
            <FieldDescription>
              填写 Sub2API Codex 分组的数字
              ID，不是组名；留空时同步所选用户的全部 Key。
            </FieldDescription>
            <FieldError errors={[form.formState.errors.codexGroupId]} />
          </Field>
        </FieldGroup>
        <div className="flex flex-wrap gap-2">
          <Button
            type="submit"
            disabled={saveMutation.isPending || !form.formState.isDirty}
          >
            {saveMutation.isPending ? (
              <Spinner data-icon="inline-start" />
            ) : null}
            保存连接配置
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={!connection.configured || testMutation.isPending}
            onClick={() => testMutation.mutate()}
          >
            {testMutation.isPending ? (
              <Spinner data-icon="inline-start" />
            ) : (
              <PlugZapIcon data-icon="inline-start" />
            )}
            测试连接
          </Button>
        </div>
      </form>
      {testMutation.data ? (
        <Alert
          variant={testMutation.data.succeeded ? "default" : "destructive"}
        >
          {testMutation.data.succeeded ? (
            <CheckCircle2Icon aria-hidden="true" />
          ) : (
            <ShieldAlertIcon aria-hidden="true" />
          )}
          <AlertTitle>
            {testMutation.data.succeeded ? "连接成功" : "连接失败"}
          </AlertTitle>
          <AlertDescription>
            {testMutation.data.message}
            {testMutation.data.availableUserCount === null
              ? ""
              : ` 上游当前有 ${testMutation.data.availableUserCount} 个用户。`}
          </AlertDescription>
        </Alert>
      ) : null}
      <FormError
        message={
          testMutation.error instanceof ApiError
            ? testMutation.error.message
            : null
        }
      />
      <Dialog open={guideOpen} onOpenChange={setGuideOpen}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>获取 Sub2API 连接配置</DialogTitle>
            <DialogDescription>
              以下信息都在 Sub2API 管理后台中；管理凭据只会被本系统加密保存。
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-4 text-sm">
            <GuideItem
              title="Base URL"
              description="复制 Sub2API 站点根地址，去掉 /admin 等管理页面路径；子路径部署时保留基础路径，不要附加 /api/v1。"
            />
            <GuideItem
              title="Admin API Key"
              description="管理后台 → 系统设置 → 安全与认证 → 管理员 API Key，点击“创建密钥”。密钥只完整显示一次；重新生成会使旧 Key 失效。"
            />
            <GuideItem
              title="敏感操作授权"
              description="首次保存或替换 Admin API Key 前，需要先在管理员安全中确认当前密码；授权有效期 10 分钟，过期后重新确认。"
            />
            <GuideItem
              title="统计用户范围"
              description="保存并测试连接后同步用户，再选择指定用户或显式启用全部有效用户。系统会按每个 Key 的所属用户自动携带 user_id 查询用量。"
            />
            <GuideItem
              title="Codex Group ID"
              description="管理后台 → 分组管理 → 列设置，开启 ID 列，找到承载 Codex 的分组。同一用户的 Key 还访问其他平台时填写；只用于 Codex 时可留空。"
            />
            <div className="rounded-md border p-3">
              <p className="font-medium">推荐顺序</p>
              <p className="mt-1 text-muted-foreground">
                保存连接 → 测试连接 → 同步用户 → 选择范围 → 同步 Key →
                完成人员归属。
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button type="button" onClick={() => setGuideOpen(false)}>
              知道了
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function GuideItem({
  title,
  description,
}: {
  title: string;
  description: string;
}) {
  return (
    <div>
      <p className="font-medium">{title}</p>
      <p className="mt-0.5 text-muted-foreground">{description}</p>
    </div>
  );
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}
