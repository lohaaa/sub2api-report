import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import { useEffect } from "react";
import { Controller, useForm } from "react-hook-form";
import { z } from "zod";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
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
  ApiError,
  createChannel,
  updateChannel,
  type CreateChannelInput,
  type EmailChannelDisplay,
  type NotificationChannel,
  type NotificationChannelType,
  type SmtpSecurityMode,
  type UpdateChannelInput,
} from "@/lib/api-client";

export const channelTypeLabels: Record<NotificationChannelType, string> = {
  Email: "邮件（SMTP）",
  DingTalk: "钉钉群机器人",
  Feishu: "飞书群机器人",
};

const smtpSecurityLabels: Record<SmtpSecurityMode, string> = {
  StartTls: "STARTTLS（推荐，如 587）",
  ImplicitTls: "隐式 TLS（如 465）",
  None: "不加密（仅内网中继）",
};

function splitAddresses(value: string): string[] {
  const addresses = value
    .split(/[,\n，;；\s]+/)
    .map((address) => address.trim())
    .filter((address) => address.length > 0);
  return [...new Set(addresses.map((address) => address.toLowerCase()))].map(
    (lower) =>
      addresses.find((address) => address.toLowerCase() === lower) ?? lower,
  );
}

const emailSchema = z.object({
  host: z.string().trim().min(1, "请填写 SMTP 主机").max(255),
  port: z.coerce
    .number<number>()
    .int("端口必须是整数")
    .min(1, "端口必须在 1 到 65535 之间")
    .max(65535),
  security: z.enum(["StartTls", "ImplicitTls", "None"]),
  username: z.string().trim().max(320),
  smtpPassword: z.string().max(1024),
  clearSmtpPassword: z.boolean(),
  fromAddress: z
    .string()
    .trim()
    .min(3, "请填写发件人地址")
    .max(320)
    .email("发件人地址格式不正确"),
  fromName: z.string().trim().max(200),
  toAddresses: z
    .string()
    .max(4096)
    .refine(
      (value) => splitAddresses(value).length > 0,
      "至少填写一个收件人地址",
    ),
  ccAddresses: z.string().max(4096),
});

const webhookSchema = z.object({
  webhookUrl: z
    .string()
    .trim()
    .max(2048)
    .refine(
      (value) =>
        value === "" ||
        /^https:\/\/oapi\.dingtalk\.com\//i.test(value) ||
        /^https:\/\/open\.feishu\.cn\//i.test(value),
      "Webhook 必须是钉钉或飞书的官方 HTTPS 地址",
    ),
  signSecret: z.string().max(512),
});

const schema = z.object({
  name: z.string().trim().min(1, "请填写渠道名称").max(100),
  enabled: z.boolean(),
  host: z.string().max(255),
  port: z.string().max(6),
  security: z.enum(["StartTls", "ImplicitTls", "None"]),
  username: z.string().max(320),
  smtpPassword: z.string().max(1024),
  clearSmtpPassword: z.boolean(),
  fromAddress: z.string().max(320),
  fromName: z.string().max(200),
  toAddresses: z.string().max(4096),
  ccAddresses: z.string().max(4096),
  webhookUrl: z.string().max(2048),
  signSecret: z.string().max(512),
});

type Values = z.infer<typeof schema>;

function toFormValues(
  type: NotificationChannelType,
  existing: NotificationChannel | null,
): Values {
  const email = type === "Email" ? (existing?.email ?? null) : null;
  return {
    name: existing?.name ?? "",
    enabled: existing?.enabled ?? true,
    host: email?.host ?? "",
    port: String(email?.port ?? 587),
    security: email?.security ?? "StartTls",
    username: email?.username ?? "",
    smtpPassword: "",
    clearSmtpPassword: false,
    fromAddress: email?.fromAddress ?? "",
    fromName: email?.fromName ?? "Sub2API Report",
    toAddresses: email ? email.toAddresses.join(", ") : "",
    ccAddresses:
      email && email.ccAddresses.length > 0 ? email.ccAddresses.join(", ") : "",
    webhookUrl: "",
    signSecret: "",
  };
}

export function ChannelEditorDialog({
  open,
  onOpenChange,
  channelType,
  existing,
  onSaved,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  channelType: NotificationChannelType;
  existing: NotificationChannel | null;
  onSaved: () => void;
}) {
  const isEmail = channelType === "Email";
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: toFormValues(channelType, existing),
  });
  useEffect(() => {
    if (open) {
      form.reset(toFormValues(channelType, existing));
    }
  }, [open, channelType, existing, form]);
  const saveMutation = useMutation({
    mutationFn: async (values: Values) => {
      if (existing === null) {
        return createChannel(buildCreateInput(channelType, values));
      }

      return updateChannel(
        existing.id,
        buildUpdateInput(channelType, existing, values),
      );
    },
    onSuccess: () => {
      onOpenChange(false);
      onSaved();
    },
  });

  function parseEmailValues(values: Values) {
    const parsed = emailSchema.safeParse({
      host: values.host,
      port: values.port,
      security: values.security,
      username: values.username,
      smtpPassword: values.smtpPassword,
      clearSmtpPassword: values.clearSmtpPassword,
      fromAddress: values.fromAddress,
      fromName: values.fromName,
      toAddresses: values.toAddresses,
      ccAddresses: values.ccAddresses,
    });
    if (!parsed.success) {
      throw new Error(parsed.error.issues[0]?.message ?? "邮件配置不完整。");
    }

    return parsed.data;
  }

  function parseWebhookValues(values: Values, requireAll: boolean) {
    const parsed = webhookSchema.safeParse({
      webhookUrl: values.webhookUrl,
      signSecret: values.signSecret,
    });
    if (!parsed.success) {
      throw new Error(
        parsed.error.issues[0]?.message ?? "Webhook 配置不完整。",
      );
    }

    if (
      requireAll &&
      (parsed.data.webhookUrl === "" || parsed.data.signSecret === "")
    ) {
      throw new Error("Webhook 地址和加签密钥都是必填项。");
    }

    return parsed.data;
  }

  function buildCreateInput(
    type: NotificationChannelType,
    values: Values,
  ): CreateChannelInput {
    if (type === "Email") {
      const parsed = parseEmailValues(values);
      return {
        type: "Email",
        name: values.name,
        enabled: values.enabled,
        email: {
          host: parsed.host,
          port: parsed.port,
          security: parsed.security,
          username: parsed.username || null,
          fromAddress: parsed.fromAddress,
          fromName: parsed.fromName || null,
          toAddresses: splitAddresses(parsed.toAddresses),
          ccAddresses: splitAddresses(parsed.ccAddresses),
        },
        smtpPassword: parsed.smtpPassword || null,
        webhookUrl: null,
        signSecret: null,
      };
    }

    const parsed = parseWebhookValues(values, true);
    return {
      type,
      name: values.name,
      enabled: values.enabled,
      email: null,
      smtpPassword: null,
      webhookUrl: parsed.webhookUrl,
      signSecret: parsed.signSecret,
    };
  }

  function buildUpdateInput(
    type: NotificationChannelType,
    current: NotificationChannel,
    values: Values,
  ): UpdateChannelInput {
    if (type === "Email") {
      const parsed = parseEmailValues(values);
      if (parsed.clearSmtpPassword && parsed.smtpPassword !== "") {
        throw new Error("清除密码和替换密码不能同时执行。");
      }

      return {
        name: values.name,
        enabled: values.enabled,
        email: {
          host: parsed.host,
          port: parsed.port,
          security: parsed.security,
          username: parsed.username || null,
          fromAddress: parsed.fromAddress,
          fromName: parsed.fromName || null,
          toAddresses: splitAddresses(parsed.toAddresses),
          ccAddresses: splitAddresses(parsed.ccAddresses),
        },
        removeStoredPassword: parsed.clearSmtpPassword,
        newSmtpPassword: parsed.smtpPassword || null,
        webhookUrl: null,
        signSecret: null,
        revision: current.revision,
      };
    }

    const parsed = parseWebhookValues(values, false);
    return {
      name: values.name,
      enabled: values.enabled,
      email: null,
      removeStoredPassword: false,
      newSmtpPassword: null,
      webhookUrl: parsed.webhookUrl || null,
      signSecret: parsed.signSecret || null,
      revision: current.revision,
    };
  }

  const email = existing?.email ?? null;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>
            {existing === null ? "新增发送渠道" : "编辑发送渠道"}
          </DialogTitle>
          <DialogDescription>
            {channelTypeLabels[channelType]}
            。秘密在保存时加密存储，读取接口只返回掩码。
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={form.handleSubmit((values) => saveMutation.mutate(values))}
          noValidate
        >
          <FormError
            message={
              saveMutation.error instanceof ApiError
                ? saveMutation.error.message
                : null
            }
          />
          {saveMutation.error instanceof Error &&
          !(saveMutation.error instanceof ApiError) ? (
            <FormError message={saveMutation.error.message} />
          ) : null}
          <FieldGroup>
            <Field data-invalid={Boolean(form.formState.errors.name)}>
              <FieldLabel htmlFor="channel-name">渠道名称</FieldLabel>
              <Input
                id="channel-name"
                required
                autoComplete="off"
                aria-invalid={Boolean(form.formState.errors.name)}
                {...form.register("name")}
              />
              <FieldError errors={[form.formState.errors.name]} />
            </Field>
            {isEmail ? (
              <EmailFields form={form} email={email} />
            ) : (
              <WebhookFields form={form} />
            )}
            <Field>
              <div className="flex items-center gap-2">
                <Controller
                  control={form.control}
                  name="enabled"
                  render={({ field }) => (
                    <Checkbox
                      id="channel-enabled"
                      checked={field.value}
                      onCheckedChange={(checked) =>
                        field.onChange(checked === true)
                      }
                    />
                  )}
                />
                <FieldLabel htmlFor="channel-enabled" className="font-normal">
                  启用该渠道（未启用时不参与投递）
                </FieldLabel>
              </div>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              取消
            </Button>
            <Button type="submit" disabled={saveMutation.isPending}>
              {saveMutation.isPending ? (
                <Spinner data-icon="inline-start" />
              ) : null}
              保存渠道
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

type FormHandle = ReturnType<typeof useForm<Values>>;

function EmailFields({
  form,
  email,
}: {
  form: FormHandle;
  email: EmailChannelDisplay | null;
}) {
  return (
    <>
      <Field data-invalid={Boolean(form.formState.errors.host)}>
        <FieldLabel htmlFor="channel-smtp-host">SMTP 主机</FieldLabel>
        <Input
          id="channel-smtp-host"
          required
          autoComplete="off"
          aria-invalid={Boolean(form.formState.errors.host)}
          {...form.register("host")}
        />
        <FieldError errors={[form.formState.errors.host]} />
      </Field>
      <div className="sm:grid sm:grid-cols-2 sm:gap-3">
        <Field data-invalid={Boolean(form.formState.errors.port)}>
          <FieldLabel htmlFor="channel-smtp-port">SMTP 端口</FieldLabel>
          <Input
            id="channel-smtp-port"
            inputMode="numeric"
            required
            aria-invalid={Boolean(form.formState.errors.port)}
            {...form.register("port")}
          />
          <FieldError errors={[form.formState.errors.port]} />
        </Field>
        <Field>
          <FieldLabel htmlFor="channel-smtp-security">传输加密</FieldLabel>
          <Controller
            control={form.control}
            name="security"
            render={({ field }) => (
              <Select
                value={field.value}
                onValueChange={(value) =>
                  field.onChange(value as SmtpSecurityMode)
                }
              >
                <SelectTrigger id="channel-smtp-security">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {(
                      Object.keys(smtpSecurityLabels) as SmtpSecurityMode[]
                    ).map((mode) => (
                      <SelectItem key={mode} value={mode}>
                        {smtpSecurityLabels[mode]}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            )}
          />
        </Field>
      </div>
      <Field data-invalid={Boolean(form.formState.errors.username)}>
        <FieldLabel htmlFor="channel-smtp-username">SMTP 用户名</FieldLabel>
        <Input
          id="channel-smtp-username"
          autoComplete="off"
          aria-invalid={Boolean(form.formState.errors.username)}
          {...form.register("username")}
        />
        <FieldDescription>
          无认证中继可留空；填写密码时必须提供用户名。
        </FieldDescription>
        <FieldError errors={[form.formState.errors.username]} />
      </Field>
      <Field data-invalid={Boolean(form.formState.errors.smtpPassword)}>
        <FieldLabel htmlFor="channel-smtp-password">SMTP 密码</FieldLabel>
        <Input
          id="channel-smtp-password"
          type="password"
          autoComplete="new-password"
          aria-invalid={Boolean(form.formState.errors.smtpPassword)}
          {...form.register("smtpPassword")}
        />
        <FieldDescription>
          {email?.hasPassword
            ? `留空保留当前密码（${email.passwordMask ?? "已配置"}）。`
            : "无认证中继可留空。"}
        </FieldDescription>
        <FieldError errors={[form.formState.errors.smtpPassword]} />
      </Field>
      {email?.hasPassword ? (
        <Field>
          <div className="flex items-center gap-2">
            <Checkbox
              id="channel-clear-password"
              checked={form.watch("clearSmtpPassword")}
              onCheckedChange={(checked) =>
                form.setValue("clearSmtpPassword", checked === true, {
                  shouldDirty: true,
                })
              }
            />
            <FieldLabel
              htmlFor="channel-clear-password"
              className="font-normal"
            >
              清除已保存的密码（切换为无认证发送）
            </FieldLabel>
          </div>
        </Field>
      ) : null}
      <Field data-invalid={Boolean(form.formState.errors.fromAddress)}>
        <FieldLabel htmlFor="channel-from-address">发件人地址</FieldLabel>
        <Input
          id="channel-from-address"
          type="email"
          required
          autoComplete="email"
          aria-invalid={Boolean(form.formState.errors.fromAddress)}
          {...form.register("fromAddress")}
        />
        <FieldError errors={[form.formState.errors.fromAddress]} />
      </Field>
      <Field>
        <FieldLabel htmlFor="channel-from-name">发件人显示名</FieldLabel>
        <Input
          id="channel-from-name"
          autoComplete="off"
          {...form.register("fromName")}
        />
      </Field>
      <Field data-invalid={Boolean(form.formState.errors.toAddresses)}>
        <FieldLabel htmlFor="channel-to">收件人（逗号或换行分隔）</FieldLabel>
        <textarea
          id="channel-to"
          className="border-input bg-transparent flex min-h-20 w-full rounded-md border px-3 py-2 text-sm shadow-xs"
          aria-invalid={Boolean(form.formState.errors.toAddresses)}
          {...form.register("toAddresses")}
        />
        <FieldError errors={[form.formState.errors.toAddresses]} />
      </Field>
      <Field>
        <FieldLabel htmlFor="channel-cc">
          抄送（可选，逗号或换行分隔）
        </FieldLabel>
        <textarea
          id="channel-cc"
          className="border-input bg-transparent flex min-h-16 w-full rounded-md border px-3 py-2 text-sm shadow-xs"
          {...form.register("ccAddresses")}
        />
      </Field>
      <Alert>
        <AlertTitle>邮件说明</AlertTitle>
        <AlertDescription>
          邮件正文为 HTML 汇总表，并附带 UTF-8 BOM 的 CSV 完整明细。
        </AlertDescription>
      </Alert>
    </>
  );
}

function WebhookFields({ form }: { form: FormHandle }) {
  return (
    <>
      <Field data-invalid={Boolean(form.formState.errors.webhookUrl)}>
        <FieldLabel htmlFor="channel-webhook-url">Webhook 地址</FieldLabel>
        <Input
          id="channel-webhook-url"
          type="url"
          required
          autoComplete="off"
          aria-invalid={Boolean(form.formState.errors.webhookUrl)}
          {...form.register("webhookUrl")}
        />
        <FieldDescription>
          钉钉必须以 oapi.dingtalk.com 开头；飞书必须以 open.feishu.cn 开头。
        </FieldDescription>
        <FieldError errors={[form.formState.errors.webhookUrl]} />
      </Field>
      <Field data-invalid={Boolean(form.formState.errors.signSecret)}>
        <FieldLabel htmlFor="channel-sign-secret">加签密钥</FieldLabel>
        <Input
          id="channel-sign-secret"
          type="password"
          required
          autoComplete="new-password"
          aria-invalid={Boolean(form.formState.errors.signSecret)}
          {...form.register("signSecret")}
        />
        <FieldDescription>
          平台提供的加签密钥，长度 8 到 512 个字符。
        </FieldDescription>
        <FieldError errors={[form.formState.errors.signSecret]} />
      </Field>
    </>
  );
}
