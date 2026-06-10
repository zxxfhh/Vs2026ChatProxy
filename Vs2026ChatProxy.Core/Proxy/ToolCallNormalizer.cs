using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Providers;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// Tool Call 参数规范化管道。
    /// 参考 CopilotProxy 的 tool-schemas 和 tool-extractor，
    /// 修复 AI 模型输出的 Tool Call 参数中常见的问题：
    /// 1. 无效的 JSON 转义序列
    /// 2. 缺失必要的参数（使用 ToolSchemas 填充默认值）
    /// 3. 路径格式不一致
    /// 4. 畸形 JSON 修复
    /// 5. XML 格式工具调用提取（MiMo/DeepSeek 文本输出）
    /// 6. VS 自定义工具格式转换
    /// 7. Markdown 文件创建块提取
    /// </summary>
    public static class ToolCallNormalizer
    {
        /// <summary>规范化单个 Tool Call 的参数。返回 null 表示丢弃无效的 tool call。</summary>
        public static ToolCall? Normalize(ToolCall tc)
        {
            if (tc == null) return null;

            var name = tc.Name ?? "";
            var rawArgs = tc.Arguments ?? "{}";

            // 空名字 → 丢弃
            if (string.IsNullOrEmpty(name)) return null;

            // 0. 预处理：在 JSON 解析之前，暴力清理路径字段中的多重反斜杠
            //    GLM 模型经常在 JSON 字符串值中输出 ZhjngkModel\\\\\\MEntity\\\\\\...
            //    这些在 JSON 字面量中是合法的（\\\\ = \\），但解析后变成多重反斜杠。
            //    直接在原始 JSON 文本层面替换：将 3 个及以上连续反斜杠替换为单个正斜杠。
            rawArgs = Regex.Replace(rawArgs, @"\\{3,}", "/");

            // 1. 修复无效的 JSON 转义序列（如 \c \n 在非预期位置）
            rawArgs = FixEscapeSequences(rawArgs);

            // 2. 尝试解析 JSON
            JObject? parsed = null;
            try
            {
                parsed = JObject.Parse(rawArgs);
            }
            catch
            {
                // 3. 畸形 JSON 尝试修复
                parsed = TryFixMalformedJson(rawArgs);
                if (parsed == null)
                    return SalvageToolCall(name, rawArgs); // 无法修复，尝试正则提取；失败返回 null
            }

            if (parsed == null) return null;

            // 4. 工具特定参数别名映射（参考 CopilotProxy tool-extractor.js normalizeToolCall）
            //    AI 模型经常输出非标准参数名（如 filePath→filename, cmd→command），
            //    必须映射为 VS 期望的标准字段名。
            NormalizeToolParams(name, parsed);

            // 5. 路径格式规范化（常见于文件操作工具）
            NormalizePathFields(parsed);

            // 6. 为已知工具填充默认值（参考 CopilotProxy 的 applyToolDefaults）
            if (ToolSchemas.IsKnownTool(name))
            {
                parsed = ToolSchemas.ApplyDefaults(name, parsed);
            }

            return new ToolCall
            {
                Id = string.IsNullOrEmpty(tc.Id) ? $"call_{Guid.NewGuid():N}" : tc.Id,
                Name = name,
                Arguments = parsed.ToString(Formatting.None)
            };
        }

        /// <summary>批量规范化 Tool Call 列表（过滤掉返回 null 的无效项）</summary>
        public static List<ToolCall> NormalizeAll(List<ToolCall> toolCalls)
        {
            if (toolCalls == null || toolCalls.Count == 0) return toolCalls!;
            return toolCalls.Select(Normalize).Where(tc => tc != null).ToList()!;
        }

        /// <summary>修复无效的 JSON 转义序列</summary>
        private static string FixEscapeSequences(string json)
        {
            // 修复 Windows 路径中的反斜杠：未转义的 \C \J \P 等
            // 但排除合法的 JSON 转义：\" \\ \/ \b \f \n \r \t \uXXXX
            return Regex.Replace(json, @"\\([^""\\\/bfnrtu])", @"\\$1");
        }

        /// <summary>
        /// 清理路径值中的多余反斜杠。
        /// AI 模型（特别是 GLM/DeepSeek）经常输出损坏的路径，如：
        ///   "ZhjngkModel\\\\\\MEntity\\\\\\Inspection\\\\\\xxx.cs"
        /// JSON 解析后变成多重反斜杠，必须清理为单斜杠。
        /// </summary>
        private static string CleanPathValue(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            // 将连续的反斜杠替换为单个正斜杠
            var cleaned = Regex.Replace(path, @"\\{2,}", "/");
            // 再将单个反斜杠也替换为正斜杠
            cleaned = cleaned.Replace('\\', '/');
            // 去除连续重复的正斜杠
            while (cleaned.Contains("//"))
                cleaned = cleaned.Replace("//", "/");
            return cleaned;
        }

        /// <summary>尝试修复畸形 JSON</summary>
        private static JObject? TryFixMalformedJson(string json)
        {
            try
            {
                var trimmed = json.Trim();
                if (trimmed.StartsWith("{") && !trimmed.EndsWith("}"))
                    trimmed += "}";
                if (trimmed.StartsWith("[") && !trimmed.EndsWith("]"))
                    trimmed += "]";
                return JObject.Parse(trimmed);
            }
            catch { }

            try
            {
                var fixed1 = Regex.Replace(json, @",\s*}", "}");
                var fixed2 = Regex.Replace(fixed1, @",\s*]", "]");
                return JObject.Parse(fixed2);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 正则提取：当 JSON 完全无法解析时，尝试从原始文本中提取关键字段。
        /// 参考 CopilotProxy 的 salvageToolCall。
        /// </summary>
        private static ToolCall? SalvageToolCall(string name, string raw)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                var result = new JObject();

                // 根据工具名提取不同的关键字段
                switch (name)
                {
                    case "create_file":
                        var filePath = ExtractQuotedValue(raw, "filePath") ?? ExtractQuotedValue(raw, "path");
                        var content = ExtractQuotedValue(raw, "content");
                        if (filePath != null)
                        {
                            result["filePath"] = filePath;
                            result["content"] = content ?? "";
                            return new ToolCall { Name = name, Arguments = result.ToString(Formatting.None) };
                        }
                        break;

                    case "get_file" or "read_file":
                        var filename = ExtractQuotedValue(raw, "filename") ?? ExtractQuotedValue(raw, "filePath");
                        if (filename != null)
                        {
                            result["filename"] = filename;
                            result["startLine"] = ExtractIntValue(raw, "startLine") ?? 1;
                            result["endLine"] = ExtractIntValue(raw, "endLine") ?? 0;
                            return new ToolCall { Name = name, Arguments = result.ToString(Formatting.None) };
                        }
                        break;

                    case "replace_string_in_file":
                        var rPath = ExtractQuotedValue(raw, "filePath");
                        var oldStr = ExtractQuotedValue(raw, "oldString");
                        var newStr = ExtractQuotedValue(raw, "newString");
                        if (rPath != null && oldStr != null)
                        {
                            result["filePath"] = rPath;
                            result["oldString"] = oldStr;
                            result["newString"] = newStr ?? "";
                            return new ToolCall { Name = name, Arguments = result.ToString(Formatting.None) };
                        }
                        break;

                    case "run_command_in_terminal" or "execute_command":
                        var cmd = ExtractQuotedValue(raw, "command");
                        if (cmd != null)
                        {
                            result["command"] = cmd;
                            result["summary"] = ExtractQuotedValue(raw, "summary") ?? cmd;
                            result["background"] = false;
                            return new ToolCall { Name = name, Arguments = result.ToString(Formatting.None) };
                        }
                        break;
                }
            }
            catch { }

            return null;
        }

        /// <summary>规范化路径类型字段（清理多重反斜杠，统一为正斜杠）</summary>
        private static void NormalizePathFields(JObject parsed)
        {
            var pathFields = new[] { "path", "filePath", "filename", "dir", "directory", "folder" };
            foreach (var field in pathFields)
            {
                var token = parsed[field];
                if (token?.Type == JTokenType.String)
                {
                    parsed[field] = CleanPathValue(token.ToString());
                }
            }
        }

        /// <summary>
        /// 工具特定参数别名映射。
        /// 参考 CopilotProxy tool-extractor.js normalizeToolCall 的 per-tool 分支。
        /// AI 模型经常用不同的参数名输出，必须映射为 VS 期望的标准字段名。
        /// 例如：get_file 期望 "filename"，但模型可能输出 "filePath"/"path"/"uri"。
        /// </summary>
        private static void NormalizeToolParams(string name, JObject args)
        {
            // 辅助：从多个候选 key 中取第一个存在的值
            static string? PickFirst(JObject o, params string[] keys)
            {
                foreach (var k in keys)
                {
                    var v = o[k];
                    if (v != null && v.Type != JTokenType.Null)
                        return v.ToString();
                }
                return null;
            }

            // 辅助：如果目标 key 不存在但候选 key 有值，将其赋给目标 key
            static void AliasTo(JObject o, string target, params string[] candidates)
            {
                if (o[target]?.Type != JTokenType.Null && o[target] != null) return;
                var val = PickFirst(o, candidates);
                if (val != null) o[target] = val;
            }

            switch (name)
            {
                // ── get_file：VS 期望 filename ──
                case "get_file":
                    AliasTo(args, "filename", "filePath", "path", "uri", "resource");
                    args["filename"] = CleanPathValue(PickFirst(args, "filename") ?? "");
                    AliasTo(args, "startLine", "start_line", "fromLine", "line");
                    AliasTo(args, "endLine", "end_line", "toLine");
                    break;

                // ── read_file：VS 期望 filePath ──
                case "read_file":
                    AliasTo(args, "filePath", "filename", "path", "uri");
                    args["filePath"] = CleanPathValue(PickFirst(args, "filePath") ?? "");
                    AliasTo(args, "startLine", "start_line", "fromLine", "line");
                    AliasTo(args, "endLine", "end_line", "toLine");
                    break;

                // ── create_file ──
                case "create_file":
                    AliasTo(args, "filePath", "file_path", "path", "filename");
                    AliasTo(args, "content", "contents", "text", "code");
                    break;

                // ── replace_string_in_file ──
                case "replace_string_in_file":
                    AliasTo(args, "filePath", "path", "filename", "file");
                    AliasTo(args, "oldString", "old_string", "old_str", "search", "old_text");
                    AliasTo(args, "newString", "new_string", "new_str", "replace", "new_text");
                    break;

                // ── edit_file ──
                case "edit_file":
                    AliasTo(args, "filePath", "path", "filename", "file");
                    break;

                // ── search 工具 ──
                case "grep_search":
                case "search_content":
                case "search_file":
                    AliasTo(args, "query", "pattern", "search", "searchTerm");
                    AliasTo(args, "includePattern", "include", "fileTypes", "glob");
                    break;

                // ── 终端工具 ──
                case "run_command_in_terminal":
                case "execute_command":
                    AliasTo(args, "command", "cmd");
                    AliasTo(args, "summary", "description");
                    break;

                // ── fetch_webpage ──
                case "fetch_webpage":
                    AliasTo(args, "urls", "url");
                    // urls 如果是字符串而非数组，包装为数组
                    if (args["urls"] is JValue v && v.Value != null)
                    {
                        args["urls"] = new JArray(v.ToString());
                    }
                    break;

                // ── list_dir ──
                case "list_dir":
                    AliasTo(args, "path", "dir", "directory", "folder");
                    break;

                // ── VS Code 自定义工具 ──
                case "task":
                case "explore":
                case "research":
                case "code_review":
                case "configure":
                    AliasTo(args, "input", "prompt", "query", "task");
                    break;
            }
        }

        /// <summary>
        /// 修复 JSON 解析后的路径转义损坏。
        /// 参考 CopilotProxy tool-extractor.js _fixPathEscapes：
        /// JSON.parse 会将 \n 解析为换行符、\t 为制表符，但文件路径中的这些是字面值。
        /// </summary>
        private static string FixPathEscapes(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\r", "\\r");
        }

        // ── 多格式 Tool Call 提取 ────────────────────────────

        /// <summary>
        /// 从文本内容中提取可能被模型嵌入到文本中的 Tool Call。
        /// 支持三种格式：```tool 代码块、XML 标签、Markdown 文件创建块。
        /// 参考 CopilotProxy 的 extractToolCalls。
        /// </summary>
        public static (string cleanContent, List<ToolCall> toolCalls) ExtractFromText(string content)
        {
            if (string.IsNullOrEmpty(content)) return (content, []);
            var results = new List<ToolCall>();
            var remaining = content;

            // 格式 1: ```tool\n{...}``` 代码块
            var toolBlockPattern = new Regex(@"```tool\s*\n(.*?)```", RegexOptions.Singleline);
            foreach (Match m in toolBlockPattern.Matches(remaining))
            {
                var extracted = ParseToolCallJson(m.Groups[1].Value.Trim());
                if (extracted != null) results.Add(extracted);
            }
            remaining = toolBlockPattern.Replace(remaining, "");

            // 格式 2: XML 工具调用（MiMo/DeepSeek 风格）
            // <tool_call&gt;...&lt;/tool_call&gt; 或 <function_calls&gt;...
            var xmlResults = ParseXmlToolCalls(remaining);
            if (xmlResults.Count > 0)
            {
                results.AddRange(xmlResults);
                // 从文本中移除 XML 块
                remaining = Regex.Replace(remaining,
                    @"<(?:tool_call|function_calls)[\s\S]*?(?:</(?:tool_call|function_calls)>)",
                    "", RegexOptions.IgnoreCase);
            }

            // 格式 3: ```json\n{ "name": "...", "arguments": {...} }\n```
            var jsonBlockPattern = new Regex(@"```json\s*\n(\s*\{[^`]*?\})\s*```", RegexOptions.Singleline);
            foreach (Match m in jsonBlockPattern.Matches(remaining))
            {
                var extracted = ParseToolCallJson(m.Groups[1].Value.Trim());
                if (extracted != null && ToolSchemas.IsKnownTool(extracted.Name))
                    results.Add(extracted);
            }

            // 格式 4: Markdown 文件创建块 → 转为 create_file 工具调用
            // ## `path`\n```lang\ncontent\n```
            var mdFilePattern = new Regex(
                @"##\s*`([^`]+)`\s*\n```(\w*)\n([\s\S]*?)```",
                RegexOptions.Multiline);
            foreach (Match m in mdFilePattern.Matches(remaining))
            {
                results.Add(new ToolCall
                {
                    Id = $"tc_{Guid.NewGuid():N}",
                    Name = "create_file",
                    Arguments = new JObject
                    {
                        ["filePath"] = m.Groups[1].Value.Trim(),
                        ["content"] = m.Groups[3].Value
                    }.ToString(Formatting.None)
                });
            }

            return (remaining.Trim(), results);
        }

        /// <summary>
        /// 解析 XML 格式的工具调用。
        /// 参考 CopilotProxy 的 parseXMLToolCalls，支持两种 XML 格式：
        /// 1. MiMo/DeepSeek: &lt;tool_call&gt;&lt;function=name&gt;&lt;parameter=key&gt;value&lt;/parameter&gt;&lt;/function&gt;&lt;/tool_call&gt;
        /// 2. 标准: &lt;function_calls&gt;&lt;invoke name="..."&gt;&lt;parameter name="..."&gt;value&lt;/parameter&gt;&lt;/invoke&gt;&lt;/function_calls&gt;
        /// </summary>
        private static List<ToolCall> ParseXmlToolCalls(string text)
        {
            var results = new List<ToolCall>();

            // 格式 1: MiMo/DeepSeek XML
            var mimoPattern = new Regex(
                @"<tool_call&gt;\s*<function\s*=\s*""?(\w+)""?&gt;(.*?)</function&gt;\s*</tool_call&gt;",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in mimoPattern.Matches(text))
            {
                var name = m.Groups[1].Value;
                var paramText = m.Groups[2].Value;
                var args = ParseXmlParams(paramText);
                results.Add(new ToolCall
                {
                    Id = $"tc_{Guid.NewGuid():N}",
                    Name = name,
                    Arguments = args.ToString(Formatting.None)
                });
            }

            // 格式 2: 标准 function_calls XML
            var stdPattern = new Regex(
                @"<function_calls&gt;(.*?)</function_calls&gt;",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in stdPattern.Matches(text))
            {
                var invokePattern = new Regex(
                    @"<invoke\s+name=""([^""]+)""&gt;(.*?)</invoke&gt;",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match inv in invokePattern.Matches(m.Groups[1].Value))
                {
                    var name = inv.Groups[1].Value;
                    var args = ParseXmlParams(inv.Groups[2].Value);
                    results.Add(new ToolCall
                    {
                        Id = $"tc_{Guid.NewGuid():N}",
                        Name = name,
                        Arguments = args.ToString(Formatting.None)
                    });
                }
            }

            return results;
        }

        private static JObject ParseXmlParams(string paramText)
        {
            var args = new JObject();
            var paramPattern = new Regex(
                @"<parameter\s+(?:name=""([^""]+)""|key=""([^""]+)"")&gt;(.*?)</parameter&gt;",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match p in paramPattern.Matches(paramText))
            {
                var key = p.Groups[1].Value;
                if (string.IsNullOrEmpty(key)) key = p.Groups[2].Value;
                var val = p.Groups[3].Value.Trim();

                // 尝试解析为 JSON 值，否则保持字符串
                try { args[key] = JToken.Parse(val); }
                catch { args[key] = val; }
            }
            return args;
        }

        private static ToolCall? ParseToolCallJson(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var name = obj["name"]?.ToString() ?? obj["function"]?["name"]?.ToString() ?? "";
                var args = obj["arguments"]?.ToString()
                    ?? obj["function"]?["arguments"]?.ToString()
                    ?? obj["parameters"]?.ToString(Formatting.None)
                    ?? "{}";
                var id = obj["id"]?.ToString() ?? $"tc_{Guid.NewGuid():N}";
                if (!string.IsNullOrEmpty(name))
                {
                    return new ToolCall { Id = id, Name = name, Arguments = args };
                }
            }
            catch { }
            return null;
        }

        // ── VS 上下文提取 ────────────────────────────────────

        /// <summary>
        /// 从消息中提取 VS 工作区上下文。
        /// 参考 CopilotProxy 的 extractVSContext。
        /// </summary>
        public static (string? workspaceRoot, string? activeFile, string? selectedCode) ExtractVSContext(List<ChatMessage> messages)
        {
            string? workspaceRoot = null;
            string? activeFile = null;
            string? selectedCode = null;

            foreach (var msg in messages)
            {
                var content = msg.Content ?? "";
                if (string.IsNullOrEmpty(content)) continue;

                // 提取 workspace root
                if (workspaceRoot == null)
                {
                    var wsMatch = Regex.Match(content, @"(?:workspace[_-]?root|workspaceRoot|project[_-]?path)[:\s]+([^\s\n]+)",
                        RegexOptions.IgnoreCase);
                    if (wsMatch.Success) workspaceRoot = wsMatch.Groups[1].Value.Trim('"', '\'', '`');
                }

                // 提取 active file
                if (activeFile == null)
                {
                    var afMatch = Regex.Match(content, @"(?:active[_-]?file|currentFile|open[_-]?file)[:\s]+([^\s\n]+\.\w+)",
                        RegexOptions.IgnoreCase);
                    if (afMatch.Success) activeFile = afMatch.Groups[1].Value.Trim('"', '\'', '`');
                }

                // 提取 selected code
                if (selectedCode == null)
                {
                    var scMatch = Regex.Match(content, @"(?:selected[_-]?code|selection|highlighted[_-]?code)[:\s]+```\w*\n([\s\S]*?)```",
                        RegexOptions.IgnoreCase);
                    if (scMatch.Success) selectedCode = scMatch.Groups[1].Value;
                }
            }

            return (workspaceRoot, activeFile, selectedCode);
        }

        // ── 正则提取辅助 ────────────────────────────────────

        private static string? ExtractQuotedValue(string raw, string key)
        {
            var match = Regex.Match(raw, $@"""{key}""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int? ExtractIntValue(string raw, string key)
        {
            var match = Regex.Match(raw, $@"""{key}""\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
    }
}
