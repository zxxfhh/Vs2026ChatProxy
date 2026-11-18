using Newtonsoft.Json.Linq;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// VS Copilot 工具 Schema 定义与验证。
    /// 参考 CopilotProxy 的 tool-schemas.js，定义 VS 2026 / VS Code Copilot 使用的工具参数 Schema。
    /// 用于验证和补全 AI 模型输出的 Tool Call 参数。
    /// </summary>
    public static class ToolSchemas
    {
        /// <summary>工具参数定义</summary>
        private class SchemaDef
        {
            public HashSet<string> Required { get; set; } = [];
            public HashSet<string> Optional { get; set; } = [];
        }

        // ── 工具参数 Schema 表 ──────────────────────────────────
        // 从 VS 2026 / VS Code Copilot 实际请求中提取的工具定义

        private static readonly Dictionary<string, SchemaDef> Schemas = new()
        {
            // ── 文件与代码操作 ────────────────────────────
            ["get_file"] = new()
            {
                Required = ["filename", "startLine", "endLine"],
                Optional = ["includeLineNumbers"]
            },
            ["read_file"] = new()
            {
                Required = ["filename"],
                Optional = ["startLine", "endLine", "includeLineNumbers"]
            },
            ["create_file"] = new()
            {
                Required = ["filePath", "content"],
                Optional = []
            },
            ["edit_file"] = new()
            {
                Required = ["filePath", "edits"],
                Optional = []
            },
            ["replace_string_in_file"] = new()
            {
                Required = ["filePath", "oldString", "newString"],
                Optional = []
            },
            ["insert_content"] = new()
            {
                Required = ["filePath", "lineNumber", "content"],
                Optional = []
            },
            ["search_and_replace"] = new()
            {
                Required = ["filePath", "search", "replace"],
                Optional = ["useRegex", "isCaseSensitive"]
            },

            // ── 搜索 ──────────────────────────────────────
            ["grep_search"] = new()
            {
                Required = ["query", "isRegexp", "includePattern", "maxResults"],
                Optional = []
            },
            ["search_content"] = new()
            {
                Required = ["query"],
                Optional = ["isRegexp", "includePattern", "maxResults"]
            },
            ["search_file"] = new()
            {
                Required = ["query"],
                Optional = ["isRegexp", "includePattern", "maxResults"]
            },
            ["semantic_search"] = new()
            {
                Required = ["query"],
                Optional = []
            },
            ["list_dir"] = new()
            {
                Required = ["path"],
                Optional = ["recursive"]
            },
            ["file_search"] = new()
            {
                Required = ["query"],
                Optional = ["maxResults"]
            },

            // ── 终端 ──────────────────────────────────────
            ["run_command_in_terminal"] = new()
            {
                Required = ["command", "summary", "background"],
                Optional = ["id", "explanation"]
            },
            ["execute_command"] = new()
            {
                Required = ["command"],
                Optional = ["summary", "background", "explanation"]
            },
            ["list_commands"] = new()
            {
                Required = [],
                Optional = []
            },

            // ── Web ───────────────────────────────────────
            ["fetch_webpage"] = new()
            {
                Required = ["urls", "query"],
                Optional = []
            },
            ["web_search"] = new()
            {
                Required = ["query"],
                Optional = ["maxResults"]
            },

            // ── 规划/任务 ─────────────────────────────────
            ["task_complete"] = new()
            {
                Required = [],
                Optional = []
            },
            ["manage_todo_list"] = new()
            {
                Required = ["todoList"],
                Optional = []
            },

            // ── GitHub ────────────────────────────────────
            ["github_issue"] = new()
            {
                Required = ["owner", "repo", "body"],
                Optional = ["title", "labels", "assignees"]
            },
            ["github_pr"] = new()
            {
                Required = ["owner", "repo", "title", "body", "head", "base"],
                Optional = ["draft"]
            },
            ["github_search"] = new()
            {
                Required = ["query"],
                Optional = ["type", "maxResults"]
            },

            // ── VS Code 专用工具 ──────────────────────────
            ["task"] = new()
            {
                Required = ["input"],
                Optional = []
            },
            ["explore"] = new()
            {
                Required = ["input"],
                Optional = []
            },
            ["research"] = new()
            {
                Required = ["input"],
                Optional = []
            },
            ["code_review"] = new()
            {
                Required = ["input"],
                Optional = []
            },
            ["configure"] = new()
            {
                Required = ["input"],
                Optional = []
            },

            // ── 符号查找 ──────────────────────────────────
            ["find_references"] = new()
            {
                Required = ["fullName"],
                Optional = ["includeDeclaration"]
            },
            ["go_to_definition"] = new()
            {
                Required = ["fullName"],
                Optional = []
            },
            ["list_symbols"] = new()
            {
                Required = ["query"],
                Optional = ["maxResults"]
            },
        };

        /// <summary>VS 自定义工具名称集合</summary>
        private static readonly HashSet<string> VsCustomTools = ["task", "explore", "research", "code_review", "configure"];

        /// <summary>判断工具名是否已知</summary>
        public static bool IsKnownTool(string toolName)
            => Schemas.ContainsKey(toolName);

        /// <summary>判断工具名是否为 VS 自定义类型</summary>
        public static bool IsVsCustomTool(string toolName)
            => VsCustomTools.Contains(toolName);

        /// <summary>
        /// 为已知工具填充缺失的参数默认值。
        /// 参考 CopilotProxy 的 applyToolDefaults。
        /// </summary>
        public static JObject ApplyDefaults(string toolName, JObject args)
        {
            if (!Schemas.TryGetValue(toolName, out var schema)) return args;

            // 工具特定的默认值
            switch (toolName)
            {
                case "grep_search":
                case "search_content":
                case "search_file":
                    SetDefaultIfMissing(args, "isRegexp", false);
                    SetDefaultIfMissing(args, "includePattern", "*");
                    SetDefaultIfMissing(args, "maxResults", 20);
                    break;

                case "get_file":
                case "read_file":
                    SetDefaultIfMissing(args, "startLine", 1);
                    SetDefaultIfMissing(args, "endLine", 0);
                    SetDefaultIfMissing(args, "includeLineNumbers", false);
                    // VS 2026 要求 endLine >= startLine，endLine=0 表示"读全文"，改为大值
                    {
                        var sl = args["startLine"]?.Value<int>() ?? 1;
                        var el = args["endLine"]?.Value<int>() ?? 0;
                        if (el > 0 && el < sl) args["endLine"] = sl;
                        else if (el == 0 && sl > 0) args["endLine"] = 999999;
                    }
                    break;

                case "run_command_in_terminal":
                case "execute_command":
                    SetDefaultIfMissing(args, "background", false);
                    SetDefaultIfMissing(args, "summary", args["command"]?.ToString() ?? "");
                    break;

                case "fetch_webpage":
                    // urls 如果是字符串而非数组，包装为数组
                    if (args["urls"] is JValue v && v.Value != null)
                    {
                        args["urls"] = new JArray(v.ToString());
                    }
                    break;

                case "list_dir":
                    SetDefaultIfMissing(args, "recursive", false);
                    break;

                case "file_search":
                    SetDefaultIfMissing(args, "maxResults", 10);
                    break;
            }

            return args;
        }

        /// <summary>
        /// 规范化 VS 自定义工具格式。
        /// 将 VS 的 type:"custom" 格式转为标准 type:"function" 格式。
        /// 参考 CopilotProxy 的 normalizeToolType。
        /// </summary>
        public static JObject? NormalizeVsCustomTool(JObject toolCall)
        {
            var type = toolCall["type"]?.ToString();
            if (type != "custom") return null;

            var custom = toolCall["custom"];
            if (custom == null) return null;

            var name = custom["name"]?.ToString();
            var input = custom["input"];

            if (string.IsNullOrEmpty(name)) return null;

            return new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = name,
                    ["arguments"] = input?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}"
                }
            };
        }

        private static void SetDefaultIfMissing(JObject args, string key, object defaultValue)
        {
            if (!args.ContainsKey(key) || args[key]?.Type == JTokenType.Null)
            {
                args[key] = JToken.FromObject(defaultValue);
            }
        }
    }
}
