using Newtonsoft.Json.Linq;

namespace Vs2026ChatProxy.Providers
{
    // ── 角色常量 ─────────────────────────────────────────────

    public static class ChatRole
    {
        public const string System = "system";
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string Tool = "tool";
    }

    // ── 单条消息 ─────────────────────────────────────────────

    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string ToolCallId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ToolCall> ToolCalls { get; set; } = [];
    }

    // ── 工具调用 ─────────────────────────────────────────────

    public class ToolCall
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }

    // ── 对话选项 ─────────────────────────────────────────────

    public class ChatOptions
    {
        public string Model { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        public double TopP { get; set; } = 1.0;
        public int MaxTokens { get; set; } = 4096;

        /// <summary>工具定义（OpenAI 原始 JSON 格式，由各 Provider 自行转换）</summary>
        public JArray? Tools { get; set; }

        /// <summary>工具选择策略：OpenAI 原始 JSON（字符串 "auto" 或对象）</summary>
        public JToken? ToolChoice { get; set; }
    }

    // ── 完整响应（非流式） ───────────────────────────────────

    public class ChatResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";
        public string Content { get; set; } = "";
        public string FinishReason { get; set; } = "";
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public List<ToolCall> ToolCalls { get; set; } = [];
    }

    // ── 流式片段 ─────────────────────────────────────────────

    public class ChatChunk
    {
        public string Content { get; set; } = "";
        public string FinishReason { get; set; } = "";
        public List<ToolCall> ToolCalls { get; set; } = [];
    }
}
