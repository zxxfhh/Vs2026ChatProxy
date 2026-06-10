using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// 工具描述压缩与输出截断。
    /// 参考 CopilotProxy 的 token-optimizer.js，实现：
    /// 1. 工具 Schema 描述压缩（降低约 60-70% Token）
    /// 2. 工具输出截断（防止返回内容过长导致 Token 溢出）
    /// 3. 工具结果摘要（为旧的工具输出生成轻量摘要）
    /// </summary>
    public static class ToolOptimizer
    {
        // ── 工具描述压缩 ──────────────────────────────────────

        /// <summary>冗余前缀列表（参考 CopilotProxy 的 compressDescription）</summary>
        private static readonly string[] VerbosePrefixes =
        [
            "This tool allows you to",
            "Use this tool to",
            "This function is used to",
            "This tool is for",
            "Helps you to",
        ];

        /// <summary>术语替换表（长描述 → 短描述）</summary>
        private static readonly Dictionary<string, string> TermSubstitutions = new()
        {
            ["performs a search"] = "searches",
            ["performs an operation"] = "operates",
            ["allows the user to"] = "",
            ["in the specified"] = "in",
            ["within the given"] = "in",
            ["to the user"] = "",
            ["for the user"] = "",
        };

        /// <summary>
        /// 压缩工具描述文本。
        /// 去除冗余前缀，截取第一句话，最大 120 字符。
        /// </summary>
        public static string CompressDescription(string? description)
        {
            if (string.IsNullOrEmpty(description)) return "";

            var desc = description.Trim();

            // 去除冗余前缀
            foreach (var prefix in VerbosePrefixes)
            {
                if (desc.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    desc = desc.Substring(prefix.Length).TrimStart(' ', '.');
                    break;
                }
            }

            // 术语替换（忽略大小写）
            foreach (var kvp in TermSubstitutions)
            {
                var idx = desc.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    desc = desc.Substring(0, idx) + kvp.Value + desc.Substring(idx + kvp.Key.Length);
                    idx = desc.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                }
            }

            // 截取到第一个完整句子（句号结尾）
            var sentenceEnd = desc.IndexOf('.');
            if (sentenceEnd > 0 && sentenceEnd < desc.Length - 1)
            {
                desc = desc.Substring(0, sentenceEnd + 1);
            }

            // 最大 120 字符
            if (desc.Length > 120)
                desc = desc.Substring(0, 117) + "...";

            return desc.Trim();
        }

        /// <summary>
        /// 压缩工具 Schema（递归去除 property descriptions，只保留 type/enum/required）。
        /// 参考 CopilotProxy 的 compressToolSchema。
        /// </summary>
        public static JObject? CompressToolSchema(JToken? schema)
        {
            if (schema == null) return null;
            var obj = schema as JObject;
            if (obj == null) return null;

            var compressed = new JObject();

            // 只保留关键属性
            foreach (var key in new[] { "type", "enum", "required", "minimum", "maximum", "items", "properties" })
            {
                if (obj.TryGetValue(key, out var val))
                {
                    if (key == "properties" && val is JObject props)
                    {
                        var compressedProps = new JObject();
                        foreach (var prop in props)
                        {
                            if (prop.Value is JObject propObj)
                            {
                                var cp = new JObject();
                                // 保留 type 和 enum，移除冗长的 description
                                if (propObj.TryGetValue("type", out var t)) cp["type"] = t;
                                if (propObj.TryGetValue("enum", out var e)) cp["enum"] = e;
                                if (propObj.TryGetValue("items", out var i)) cp["items"] = CompressToolSchema(i);
                                if (propObj.TryGetValue("properties", out var p)) cp["properties"] = CompressToolSchema(p);
                                compressedProps[prop.Key] = cp;
                            }
                        }
                        compressed["properties"] = compressedProps;
                    }
                    else if (key == "items")
                    {
                        compressed["items"] = CompressToolSchema(val);
                    }
                    else
                    {
                        compressed[key] = val;
                    }
                }
            }

            return compressed;
        }

        /// <summary>
        /// 批量压缩工具定义列表（用于发送给模型前减少 Token）。
        /// 参考 CopilotProxy 的 compressToolDefinitions。
        /// </summary>
        public static JArray CompressToolDefinitions(JArray? tools)
        {
            if (tools == null || tools.Count == 0) return [];

            var result = new JArray();
            foreach (var tool in tools)
            {
                var func = tool["function"];
                if (func == null)
                {
                    result.Add(tool);
                    continue;
                }

                var compressed = new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = func["name"],
                        ["description"] = CompressDescription(func["description"]?.ToString()),
                        ["parameters"] = CompressToolSchema(func["parameters"]),
                    }
                };

                result.Add(compressed);
            }

            return result;
        }

        // ── 工具输出截断 ──────────────────────────────────────

        /// <summary>工具输出最大字符数（默认 8000）</summary>
        public const int MaxToolOutputChars = 8000;

        /// <summary>工具输出截断提示</summary>
        private const string TruncationNotice = "\n\n...[输出已截断，共 {0} 字符，保留前 {1} 字符]";

        /// <summary>
        /// 截断过长的工具输出。
        /// 参考 CopilotProxy 的工具输出截断逻辑。
        /// </summary>
        public static string TruncateToolOutput(string? output, int maxChars = MaxToolOutputChars)
        {
            if (string.IsNullOrEmpty(output)) return "";
            if (output.Length <= maxChars) return output;

            return output.Substring(0, maxChars) +
                string.Format(TruncationNotice, output.Length, maxChars);
        }

        // ── 工具结果摘要 ──────────────────────────────────────

        /// <summary>
        /// 为工具输出生成轻量摘要。
        /// 参考 CopilotProxy 的 _summarizeToolResult。
        /// </summary>
        public static string SummarizeToolResult(string? output, string? toolName)
        {
            if (string.IsNullOrEmpty(output)) return "(空输出)";

            return toolName switch
            {
                "grep_search" or "search_content" or "search_file" =>
                    SummarizeSearchResult(output),
                "get_file" or "read_file" =>
                    SummarizeFileRead(output),
                "list_dir" =>
                    SummarizeListDir(output),
                "run_command_in_terminal" or "execute_command" =>
                    SummarizeCommandOutput(output),
                _ => output.Length > 200
                    ? output.Substring(0, 197) + "..."
                    : output
            };
        }

        private static string SummarizeSearchResult(string output)
        {
            var lines = output.Split('\n');
            var fileCount = lines.Count(l => l.Contains(':') && !l.StartsWith(" "));
            return fileCount > 20
                ? $"搜索结果: {fileCount} 处匹配（显示前 20 行）\n{string.Join("\n", lines.Take(20))}"
                : output.Length > 500 ? output.Substring(0, 497) + "..." : output;
        }

        private static string SummarizeFileRead(string output)
        {
            var lines = output.Split('\n');
            return lines.Length > 50
                ? $"文件内容: {lines.Length} 行（显示前 50 行）\n{string.Join("\n", lines.Take(50))}"
                : output.Length > 500 ? output.Substring(0, 497) + "..." : output;
        }

        private static string SummarizeListDir(string output)
        {
            var lines = output.Split('\n');
            return lines.Length > 30
                ? $"目录列表: {lines.Length} 项（显示前 30 项）\n{string.Join("\n", lines.Take(30))}"
                : output;
        }

        private static string SummarizeCommandOutput(string output)
        {
            var lines = output.Split('\n');
            return lines.Length > 20
                ? $"命令输出: {lines.Length} 行（显示末尾 20 行）\n{string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 20)))}"
                : output.Length > 500 ? output.Substring(0, 497) + "..." : output;
        }

        // ── 工具动作摘要（用于上下文注入） ──────────────────

        /// <summary>
        /// 从消息历史中提取工具操作摘要。
        /// 参考 CopilotProxy 的 _extractToolSummary。
        /// </summary>
        public static string ExtractToolActionSummary(List<Providers.ChatMessage> messages)
        {
            var sb = new StringBuilder();
            var filesRead = new HashSet<string>();
            var filesEdited = new HashSet<string>();
            var filesCreated = new HashSet<string>();
            var searches = new List<string>();
            var commands = new List<string>();

            foreach (var msg in messages)
            {
                if (msg.Role != "assistant" || msg.ToolCalls == null) continue;

                foreach (var tc in msg.ToolCalls)
                {
                    switch (tc.Name)
                    {
                        case "get_file" or "read_file":
                            CollectPath(tc.Arguments, filesRead);
                            break;
                        case "replace_string_in_file" or "edit_file" or "search_and_replace":
                            CollectPath(tc.Arguments, filesEdited);
                            break;
                        case "create_file":
                            CollectPath(tc.Arguments, filesCreated);
                            break;
                        case "grep_search" or "search_content" or "semantic_search":
                            var q = ExtractArg(tc.Arguments, "query");
                            if (!string.IsNullOrEmpty(q)) searches.Add(q);
                            break;
                        case "run_command_in_terminal" or "execute_command":
                            var cmd = ExtractArg(tc.Arguments, "command");
                            if (!string.IsNullOrEmpty(cmd)) commands.Add(cmd);
                            break;
                    }
                }
            }

            if (filesRead.Count > 0) sb.AppendLine($"📖 读取: {string.Join(", ", filesRead.Take(10))}");
            if (filesEdited.Count > 0) sb.AppendLine($"✏️ 编辑: {string.Join(", ", filesEdited.Take(10))}");
            if (filesCreated.Count > 0) sb.AppendLine($"📝 创建: {string.Join(", ", filesCreated.Take(10))}");
            if (searches.Count > 0) sb.AppendLine($"🔍 搜索: {string.Join(", ", searches.Take(5))}");
            if (commands.Count > 0) sb.AppendLine($"⚡ 命令: {string.Join(", ", commands.Take(5))}");

            return sb.ToString();
        }

        private static void CollectPath(string? arguments, HashSet<string> collection)
        {
            try
            {
                var obj = JObject.Parse(arguments ?? "{}");
                var path = obj["filePath"]?.ToString() ?? obj["filename"]?.ToString() ?? obj["path"]?.ToString();
                if (!string.IsNullOrEmpty(path)) collection.Add(path);
            }
            catch { }
        }

        private static string? ExtractArg(string? arguments, string key)
        {
            try
            {
                var obj = JObject.Parse(arguments ?? "{}");
                return obj[key]?.ToString();
            }
            catch { return null; }
        }
    }
}
