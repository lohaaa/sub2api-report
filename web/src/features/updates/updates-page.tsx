import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangleIcon,
  CheckCircle2Icon,
  DownloadIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Field, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Progress } from "@/components/ui/progress";
import { Spinner } from "@/components/ui/spinner";
import { PasswordField } from "@/features/auth/password-field";
import { FormError } from "@/features/auth/form-error";
import { useSystemVersion } from "@/hooks/use-system-version";
import {
  ApiError,
  checkForUpdates,
  createStepUp,
  getUpdateOperation,
  getUpdatePlan,
  getUpdateStatus,
  installUpdate,
  type UpdateOperation,
} from "@/lib/api-client";

const activeStates = new Set([
  "queued",
  "preflight",
  "downloading_archive",
  "verifying_archive",
  "loading_image",
  "requesting_maintenance",
  "backing_up",
  "replacing_app",
  "verifying",
  "completing_maintenance",
  "rolling_back",
]);

const stageLabels: Record<string, string> = {
  queued: "等待执行",
  preflight: "升级前检查",
  downloading_archive: "下载镜像归档",
  verifying_archive: "校验归档",
  loading_image: "加载镜像",
  requesting_maintenance: "进入维护模式",
  backing_up: "备份数据库",
  replacing_app: "替换应用",
  verifying: "验证候选版本",
  completing_maintenance: "恢复业务服务",
  succeeded: "升级成功",
  rolling_back: "正在回滚",
  rolled_back: "已回滚",
  failed: "升级失败",
  failed_needs_operator: "需要主机处理",
};

const progressStages = [
  "queued",
  "preflight",
  "downloading_archive",
  "verifying_archive",
  "loading_image",
  "requesting_maintenance",
  "backing_up",
  "replacing_app",
  "verifying",
  "completing_maintenance",
  "succeeded",
];

export function UpdatesPage() {
  const queryClient = useQueryClient();
  const systemVersion = useSystemVersion();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [password, setPassword] = useState("");
  const [submittedOperationId, setSubmittedOperationId] = useState<string | null>(null);
  const previousOperationStateRef = useRef<string | null>(null);

  const statusQuery = useQuery({
    queryKey: ["updates", "status"],
    queryFn: ({ signal }) => getUpdateStatus(signal),
    refetchInterval: (query) =>
      query.state.data?.lastOperationState && activeStates.has(query.state.data.lastOperationState)
        ? 5_000
        : false,
  });
  const operationId = submittedOperationId ?? statusQuery.data?.lastOperationId ?? null;
  const operationQuery = useQuery({
    queryKey: ["updates", "operation", operationId],
    queryFn: ({ signal }) => getUpdateOperation(operationId!, signal),
    enabled: Boolean(operationId),
    refetchInterval: (query) =>
      query.state.data && activeStates.has(query.state.data.state) ? 3_000 : false,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 8,
  });
  const planQuery = useQuery({
    queryKey: ["updates", "plan"],
    queryFn: ({ signal }) => getUpdatePlan(signal),
    enabled: statusQuery.data?.state === "update_available",
    retry: false,
  });
  const checkMutation = useMutation({
    mutationFn: checkForUpdates,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["updates", "status"] }),
        queryClient.invalidateQueries({ queryKey: ["updates", "plan"] }),
      ]);
    },
  });
  const installMutation = useMutation({
    mutationFn: async () => {
      await createStepUp({ password });
      return installUpdate(planQuery.data?.targetVersion ?? null);
    },
    onSuccess: async (operation) => {
      setPassword("");
      setDialogOpen(false);
      setSubmittedOperationId(operation.operationId);
      await queryClient.invalidateQueries({ queryKey: ["updates", "status"] });
    },
  });

  const operation = operationQuery.data;

  // Refresh version and plan data exactly once when a running installation reaches a terminal
  // state. Never reload during the operation, never reload automatically, and guard against
  // re-runs from repeated polls or remounts (state only transitions once per operation).
  const operationState = operation?.state ?? null;
  useEffect(() => {
    const previousState = previousOperationStateRef.current;
    previousOperationStateRef.current = operationState;
    if (!operationState || !previousState) {
      return;
    }
    if (!activeStates.has(previousState) || activeStates.has(operationState)) {
      return;
    }
    void queryClient.invalidateQueries({ queryKey: ["updates", "status"] });
    void queryClient.invalidateQueries({ queryKey: ["updates", "plan"] });
    void queryClient.invalidateQueries({ queryKey: ["system", "version"] });
  }, [operationState, queryClient]);

  const progress = useMemo(() => operationProgress(operation), [operation]);
  const error = checkMutation.error ?? installMutation.error ?? statusQuery.error ?? operationQuery.error;
  const errorMessage = error instanceof ApiError ? error.message : error ? "无法读取更新状态。" : null;
  const updateAvailable = statusQuery.data?.state === "update_available";
  const manualUpgrade = planQuery.data?.manualUpgradeRequired ?? false;
  const canInstall = Boolean(
    updateAvailable &&
      planQuery.data?.installationEnabled &&
      !manualUpgrade &&
      !operationIsActive(operation),
  );

  return (
    <div className="flex min-w-0 flex-col gap-6">
      <PageHeader title="系统更新" description="检查签名 Release，并查看安装、验证和回滚状态。" />

      {errorMessage ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>更新服务不可用</AlertTitle>
          <AlertDescription>{errorMessage}</AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>版本状态</CardTitle>
            <CardDescription>当前应用与最新稳定版本</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-5">
            <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-3 text-sm">
              <dt className="text-muted-foreground">当前版本</dt>
              <dd className="font-mono">v{systemVersion.data?.version ?? "--"}</dd>
              <dt className="text-muted-foreground">可用版本</dt>
              <dd className="font-mono">{statusQuery.data?.availableVersion ? `v${statusQuery.data.availableVersion}` : "--"}</dd>
              <dt className="text-muted-foreground">Updater</dt>
              <dd className="font-mono">v{statusQuery.data?.version ?? "--"}</dd>
              <dt className="text-muted-foreground">安装能力</dt>
              <dd>
                <Badge variant={statusQuery.data?.installationEnabled ? "default" : "outline"}>
                  {statusQuery.data?.installationEnabled ? "已启用" : "未启用"}
                </Badge>
              </dd>
            </dl>
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                onClick={() => checkMutation.mutate()}
                disabled={checkMutation.isPending || operationIsActive(operation)}
              >
                {checkMutation.isPending ? <Spinner data-icon="inline-start" /> : <RefreshCwIcon data-icon="inline-start" />}
                检查更新
              </Button>
              {canInstall ? (
                <Button onClick={() => setDialogOpen(true)}>
                  <DownloadIcon data-icon="inline-start" />
                  安装更新
                </Button>
              ) : null}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>升级计划</CardTitle>
            <CardDescription>签名校验、备份、替换和健康验证</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {planQuery.data ? (
              <ol className="flex flex-col gap-3">
                {planQuery.data.steps.map((step) => (
                  <li key={step.order} className="grid grid-cols-[1.75rem_minmax(0,1fr)] gap-2 text-sm">
                    <span className="flex size-7 items-center justify-center rounded-md border text-xs font-medium">
                      {step.order}
                    </span>
                    <span className="min-w-0">
                      <span className="block font-medium">{stageLabels[step.name] ?? step.name}</span>
                      <span className="block text-muted-foreground">{step.description}</span>
                    </span>
                  </li>
                ))}
              </ol>
            ) : (
              <p className="text-sm text-muted-foreground">检查更新后显示目标版本的安装计划。</p>
            )}
          </CardContent>
        </Card>
      </div>

      {manualUpgrade ? (
        <Alert>
          <ShieldCheckIcon aria-hidden="true" />
          <AlertTitle>需要主机升级</AlertTitle>
          <AlertDescription>该版本修改了 Updater 或部署契约，请下载完整 Release bundle 并执行 update.sh。</AlertDescription>
        </Alert>
      ) : null}

      {!statusQuery.data?.installationEnabled && updateAvailable ? (
        <Alert>
          <ShieldCheckIcon aria-hidden="true" />
          <AlertTitle>在线安装尚未启用</AlertTitle>
          <AlertDescription>可以查看签名版本信息，但安装按钮将在安全验收完成后开放。</AlertDescription>
        </Alert>
      ) : null}

      {operation ? (
        <Card>
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <CardTitle>最近安装操作</CardTitle>
                <CardDescription>{operation.currentVersion} → {operation.targetVersion}</CardDescription>
              </div>
              <OperationBadge operation={operation} />
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <div className="flex justify-between gap-4 text-sm">
                <span>{stageLabels[operation.stage ?? operation.state] ?? operation.state}</span>
                <span className="text-muted-foreground">{progress}%</span>
              </div>
              <Progress value={progress} aria-label="升级进度" />
            </div>
            {operation.lastError ? (
              <Alert variant="destructive">
                <AlertTriangleIcon aria-hidden="true" />
                <AlertTitle>{operation.state === "failed_needs_operator" ? "需要主机处理" : "操作失败"}</AlertTitle>
                <AlertDescription>{operation.lastError}</AlertDescription>
              </Alert>
            ) : null}
            <ol className="grid gap-2 text-sm sm:grid-cols-2">
              {operation.stages.map((stage, index) => (
                <li key={`${stage.stage}-${index}`} className="flex min-w-0 items-center gap-2">
                  {stage.completedAt ? <CheckCircle2Icon className="shrink-0 text-muted-foreground" aria-hidden="true" /> : <Spinner className="shrink-0" />}
                  <span className="truncate">{stageLabels[stage.stage] ?? stage.stage}</span>
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>
      ) : null}

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>确认安装更新</DialogTitle>
            <DialogDescription>
              安装期间服务会短暂进入维护模式。系统会先备份数据库，验证失败时自动回滚。
            </DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="update-step-up-password">当前密码</FieldLabel>
              <PasswordField
                id="update-step-up-password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </Field>
          </FieldGroup>
          <FormError message={installMutation.error instanceof ApiError ? installMutation.error.message : null} />
          <DialogFooter>
            <Button variant="outline" onClick={() => setDialogOpen(false)} disabled={installMutation.isPending}>
              取消
            </Button>
            <Button onClick={() => installMutation.mutate()} disabled={!password || installMutation.isPending}>
              {installMutation.isPending ? <Spinner data-icon="inline-start" /> : <DownloadIcon data-icon="inline-start" />}
              确认安装
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function operationIsActive(operation: UpdateOperation | undefined) {
  return operation ? activeStates.has(operation.state) : false;
}

function operationProgress(operation: UpdateOperation | undefined) {
  if (!operation) return 0;
  if (operation.state === "rolled_back" || operation.state === "failed" || operation.state === "failed_needs_operator") return 100;
  const index = progressStages.indexOf(operation.stage ?? operation.state);
  return index < 0 ? 0 : Math.round((index / (progressStages.length - 1)) * 100);
}

function OperationBadge({ operation }: { operation: UpdateOperation }) {
  const terminalSuccess = operation.state === "succeeded";
  const terminalFailure = ["failed", "failed_needs_operator"].includes(operation.state);
  return (
    <Badge variant={terminalFailure ? "destructive" : terminalSuccess ? "default" : "outline"}>
      {stageLabels[operation.state] ?? operation.state}
    </Badge>
  );
}
