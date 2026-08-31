import { describe, expect, it } from "vitest";
import { channelPresentations } from "@/features/channels/channel-presentation";

describe("channel presentation", () => {
  it("distinguishes attachments from revocable download links", () => {
    expect(channelPresentations.Email).toMatchObject({
      hasAttachment: true,
      attachmentLabel: "含 XLSX 附件",
    });
    expect(channelPresentations.DingTalk).toMatchObject({
      hasAttachment: false,
      contentLabel: "Markdown 摘要",
      attachmentLabel: "无直接附件",
    });
    expect(channelPresentations.Feishu).toMatchObject({
      hasAttachment: false,
      contentLabel: "富文本摘要",
      attachmentLabel: "无直接附件",
    });
    expect(channelPresentations.DingTalk.capability).toContain("限时 XLSX 下载链接");
    expect(channelPresentations.Feishu.capability).toContain("限时 XLSX 下载链接");
  });
});
