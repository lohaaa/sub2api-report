import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  CheckCircle2Icon,
  DatabaseIcon,
  PlugZapIcon,
  RefreshCwIcon,
  ServerIcon,
  ShieldCheckIcon,
  SlidersHorizontalIcon,
} from "lucide-react";
import { PageHeader } from "@/components/layout/page-header";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { useSystemVersion } from "@/hooks/use-system-version";
import { SecuritySettings } from "./security-settings";
import { Sub2ApiConnectionForm } from "./sub2api-connection-form";
import { Sub2ApiUserScope } from "./sub2api-user-scope";
import { SystemSettingsForm } from "./system-settings-form";

type SettingsTab = "runtime" | "configuration" | "security" | "sub2api";

const tabHashes: Record<SettingsTab, string> = {
  runtime: "#runtime",
  configuration: "#configuration",
  security: "#security",
  sub2api: "#sub2api",
};

export function SettingsPage() {
  const versionQuery = useSystemVersion();
  const location = useLocation();
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<SettingsTab>(() =>
    tabFromHash(location.hash),
  );
  const [focusStepUpOnOpen, setFocusStepUpOnOpen] = useState(false);

  useEffect(() => {
    setActiveTab(tabFromHash(location.hash));
  }, [location.hash]);

  useEffect(() => {
    if (activeTab !== "security" || !focusStepUpOnOpen) return;
    const field = document.getElementById("step-up-password");
    if (!field) return;
    if (typeof field.scrollIntoView === "function") {
      field.scrollIntoView({ behavior: "smooth", block: "center" });
    }
    field.focus({ preventScroll: true });
    setFocusStepUpOnOpen(false);
  }, [activeTab, focusStepUpOnOpen]);

  function changeTab(value: string) {
    if (!isSettingsTab(value)) return;
    setActiveTab(value);
    void navigate(
      {
        pathname: location.pathname,
        search: location.search,
        hash: tabHashes[value],
      },
      { replace: true },
    );
  }
  function openSecurityAuthorization() {
    setFocusStepUpOnOpen(true);
    setActiveTab("security");
    void navigate(
      {
        pathname: location.pathname,
        search: location.search,
        hash: tabHashes.security,
      },
      { replace: true },
    );
  }


  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-6">
      <PageHeader
        title="系统设置"
        description="运行参数、管理员安全和外部连接"
      />
      <Tabs value={activeTab} onValueChange={changeTab}>
        <TabsList
          variant="line"
          aria-label="系统设置分类"
          className="grid h-auto w-full grid-cols-2 sm:grid-cols-4"
        >
          <TabsTrigger value="runtime" className="min-h-9">
            <ServerIcon data-icon="inline-start" />
            运行信息
          </TabsTrigger>
          <TabsTrigger value="configuration" className="min-h-9">
            <SlidersHorizontalIcon data-icon="inline-start" />
            动态配置
          </TabsTrigger>
          <TabsTrigger value="security" className="min-h-9">
            <ShieldCheckIcon data-icon="inline-start" />
            管理员安全
          </TabsTrigger>
          <TabsTrigger value="sub2api" className="min-h-9">
            <PlugZapIcon data-icon="inline-start" />
            Sub2API 连接
          </TabsTrigger>
        </TabsList>

        <TabsContent value="runtime" className="pt-4">
          <section aria-labelledby="runtime-title" className="flex flex-col gap-4">
            <SectionHeading
              id="runtime-title"
              title="运行信息"
              description="当前应用实例状态"
            />
            <div className="divide-y rounded-md border">
              <SettingRow
                icon={ServerIcon}
                label="应用版本"
                value={versionQuery.data?.version ?? "未连接"}
              />
              <SettingRow
                icon={RefreshCwIcon}
                label="发布通道"
                value={versionQuery.data?.releaseChannel ?? "stable"}
              />
              <SettingRow icon={DatabaseIcon} label="数据库" value="SQLite" />
            </div>
          </section>
        </TabsContent>

        <TabsContent value="configuration" className="pt-4">
          <section
            aria-labelledby="system-settings-title"
            className="flex flex-col gap-5"
          >
            <SectionHeading
              id="system-settings-title"
              title="动态配置"
              description="设置保存到 SQLite 并在运行期生效"
            />
            <SystemSettingsForm />
          </section>
        </TabsContent>

        <TabsContent value="security" className="pt-4">
          <section aria-labelledby="security-title" className="flex flex-col gap-5">
            <SectionHeading
              id="security-title"
              title="管理员安全"
              description="密码和敏感操作授权"
            />
            <SecuritySettings />
          </section>
        </TabsContent>

        <TabsContent value="sub2api" className="pt-4">
          <section
            aria-labelledby="connection-title"
            className="flex flex-col gap-5"
          >
            <SectionHeading
              id="connection-title"
              title="Sub2API 连接"
              description="上游地址、机器凭据和 Codex 数据范围"
            />
            <Sub2ApiConnectionForm onRequestStepUp={openSecurityAuthorization} />
            <Sub2ApiUserScope />
          </section>
        </TabsContent>
      </Tabs>
    </div>
  );
}

function tabFromHash(hash: string): SettingsTab {
  if (hash === "#report-download-settings" || hash === "#configuration") {
    return "configuration";
  }
  if (hash === "#security") return "security";
  if (hash === "#sub2api") return "sub2api";
  return "runtime";
}

function isSettingsTab(value: string): value is SettingsTab {
  return value === "runtime" ||
    value === "configuration" ||
    value === "security" ||
    value === "sub2api";
}

function SectionHeading({
  id,
  title,
  description,
}: {
  id: string;
  title: string;
  description: string;
}) {
  return (
    <div>
      <h2 id={id} className="text-base font-semibold">
        {title}
      </h2>
      <p className="text-sm text-muted-foreground">{description}</p>
    </div>
  );
}

function SettingRow({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof ServerIcon;
  label: string;
  value: string;
}) {
  return (
    <div className="flex min-h-14 items-center gap-3 px-4 py-3">
      <Icon aria-hidden="true" />
      <span className="flex-1 text-sm text-muted-foreground">{label}</span>
      <span className="flex items-center gap-2 text-sm font-medium">
        <CheckCircle2Icon aria-hidden="true" />
        {value}
      </span>
    </div>
  );
}
