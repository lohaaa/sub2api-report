import type { NotificationChannelType } from "@/lib/api-client";

export type ChannelPresentation = {
  shortLabel: string;
  fullLabel: string;
  contentLabel: string;
  capability: string;
  attachmentLabel: string;
  hasAttachment: boolean;
};

export const channelPresentations = {
  Email: {
    shortLabel: "邮件",
    fullLabel: "邮件（SMTP）",
    contentLabel: "HTML 汇总 + XLSX 完整明细",
    capability: "邮件正文展示各窗口汇总，并附完整 XLSX 工作簿",
    attachmentLabel: "含 XLSX 附件",
    hasAttachment: true,
  },
  DingTalk: {
    shortLabel: "钉钉",
    fullLabel: "钉钉群机器人",
    contentLabel: "Markdown 摘要",
    capability: "分段展示摘要；配置外部地址后附限时 XLSX 下载链接",
    attachmentLabel: "无直接附件",
    hasAttachment: false,
  },
  Feishu: {
    shortLabel: "飞书",
    fullLabel: "飞书群机器人",
    contentLabel: "富文本摘要",
    capability: "分段展示摘要；配置外部地址后附限时 XLSX 下载链接",
    attachmentLabel: "无直接附件",
    hasAttachment: false,
  },
} satisfies Record<NotificationChannelType, ChannelPresentation>;
