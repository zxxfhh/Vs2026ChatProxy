using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.UI
{
    /// <summary>
    /// Provider 编辑/新增对话框。
    /// 类型下拉分组：原生协议 / OpenAI 兼容（国内预配置） / OpenAI 兼容（国外）。
    /// 支持从上游 API 获取真实模型列表。
    /// </summary>
    public partial class ProviderEditDialog : Form
    {
        public ProviderConfig Provider { get; private set; }

        private readonly bool _isNew;
        private readonly List<string> _existingNames;

        // ── dgvModels 列索引（按 Designer 中 Columns.AddRange 的顺序）──
        // 0: Enabled(bool)  1: ModelId(string)  2: ContextTag(combo)  3: AppendTag(bool)
        private const int iEnabled = 0;
        private const int iModelId = 1;
        private const int iContextTag = 2;
        private const int iAppendTag = 3;

        public ProviderEditDialog(ProviderConfig? existing, List<string> existingNames)
        {
            InitializeComponent();
            _existingNames = existingNames;
            Provider = existing != null
                ? new ProviderConfig
                {
                    Type = existing.Type,
                    Name = existing.Name,
                    BaseUrl = existing.BaseUrl,
                    ApiKey = existing.ApiKey,
                    Models = existing.Models.Select(m => new ModelConfig
                    {
                        Id = m.Id, Enabled = m.Enabled,
                        ContextTag = m.ContextTag, AppendContextTag = m.AppendContextTag,
                        MaxContextTokens = m.MaxContextTokens
                    }).ToList()
                }
                : new ProviderConfig { Type = "openai", Models = [] };
            _isNew = existing == null;
            Text = _isNew ? "添加供应商" : "编辑供应商";

            LoadToControls();
        }

        private void LoadToControls()
        {
            var targetType = Provider.Type.ToLowerInvariant();
            for (int i = 0; i < cmbType.Items.Count; i++)
            {
                if (cmbType.Items[i] is string item && item == targetType)
                {
                    cmbType.SelectedIndex = i;
                    break;
                }
            }
            if (cmbType.SelectedIndex < 0) cmbType.SelectedIndex = 0;

            txtName.Text = Provider.Name;
            txtBaseUrl.Text = Provider.BaseUrl;
            txtApiKey.Text = Provider.ApiKey ?? "";
            RefreshModelGrid();
        }

        private void RefreshModelGrid()
        {
            dgvModels.Rows.Clear();
            foreach (var m in Provider.Models)
            {
                dgvModels.Rows.Add(m.Enabled, m.Id, m.ContextTag, m.AppendContextTag);
            }
        }

        /// <summary>获取当前选中的类型名</summary>
        private string GetSelectedType()
        {
            return cmbType.SelectedItem?.ToString() ?? "openai";
        }

        /// <summary>切换类型时，如果是新建，自动填入预配置 BaseUrl</summary>
        private void cmbType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!_isNew) return;

            var selectedType = GetSelectedType();

            txtBaseUrl.Text = selectedType switch
            {
                "anthropic" => "https://api.anthropic.com",
                "gemini" => "https://generativelanguage.googleapis.com",
                "deepseek" => "https://api.deepseek.com",
                "ollama" => "http://localhost:11434",
                "openai" => "https://api.openai.com",
                _ => ""
            };
        }

        // ── 获取模型 ─────────────────────────────────────────────

        private async void btnFetchModels_Click(object sender, EventArgs e)
        {
            var selectedType = GetSelectedType();
            var baseUrl = txtBaseUrl.Text.Trim();
            var apiKey = txtApiKey.Text.Trim();

            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("请先填写 Base URL", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnFetchModels.Enabled = false;
            btnFetchModels.Text = "获取中...";
            Cursor = Cursors.WaitCursor;

            try
            {
                List<(string id, int? contextTokens)> fetchedModels;

                if (selectedType is "anthropic" or "claude")
                    fetchedModels = await FetchAnthropicModels(baseUrl, apiKey);
                else if (selectedType is "gemini" or "google")
                    fetchedModels = await FetchGeminiModels(baseUrl, apiKey);
                else
                    fetchedModels = await FetchOpenAiCompatibleModels(baseUrl, apiKey);

                if (fetchedModels.Count == 0)
                {
                    MessageBox.Show("未获取到任何模型，请检查 Base URL 和 API Key 是否正确。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 合并到现有列表（去重）
                var existingIds = new HashSet<string>();
                foreach (DataGridViewRow row in dgvModels.Rows)
                {
                    if (!row.IsNewRow)
                        existingIds.Add(row.Cells[iModelId].Value?.ToString()?.Trim() ?? "");
                }

                var added = 0;
                var autoContext = 0;
                foreach (var (id, ctxTokens) in fetchedModels.OrderBy(x => x.id))
                {
                    if (existingIds.Add(id))
                    {
                        // 自动匹配上下文标签
                        var (tag, append) = TokensToTag(ctxTokens);
                        dgvModels.Rows.Add(true, id, tag, append);
                        added++;
                        if (ctxTokens.HasValue && ctxTokens.Value > 0) autoContext++;
                    }
                }

                var msg = $"获取到 {fetchedModels.Count} 个模型，新增 {added} 个。";
                if (autoContext > 0)
                    msg += $"\n其中 {autoContext} 个自动识别了上下文大小。";
                MessageBox.Show(msg, "获取成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取模型失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnFetchModels.Enabled = true;
                btnFetchModels.Text = "🔄 获取模型";
                Cursor = Cursors.Default;
            }
        }

        /// <summary>OpenAI 兼容协议 GET {baseUrl}/v1/models（不返回上下文大小）</summary>
        private static async Task<List<(string id, int? contextTokens)>> FetchOpenAiCompatibleModels(string baseUrl, string apiKey)
        {
            var url = BuildModelsUrl(baseUrl, "/models");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var resp = await http.GetStringAsync(url);
            var jobj = JObject.Parse(resp);
            var data = jobj["data"] as JArray;
            if (data == null) return [];

            return data
                .Select(m => m["id"]?.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => (id!, (int?)null))
                .ToList();
        }

        /// <summary>Anthropic GET {baseUrl}/v1/models（返回 max_input_tokens）</summary>
        private static async Task<List<(string id, int? contextTokens)>> FetchAnthropicModels(string baseUrl, string apiKey)
        {
            var url = baseUrl.TrimEnd('/') + "/v1/models";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var resp = await http.GetStringAsync(url);
            var jobj = JObject.Parse(resp);
            var data = jobj["data"] as JArray;
            if (data == null) return [];

            return data
                .Where(m => !string.IsNullOrEmpty(m["id"]?.ToString()))
                .Select(m =>
                {
                    var id = m["id"]!.ToString();
                    var ctx = m["max_input_tokens"]?.Value<int>();
                    // Anthropic 对某些模型返回 0，视为无效
                    if (ctx.HasValue && ctx.Value <= 0) ctx = null;
                    return (id, ctx);
                })
                .ToList();
        }

        /// <summary>Google Gemini GET {baseUrl}/v1beta/models（返回 inputTokenLimit）</summary>
        private static async Task<List<(string id, int? contextTokens)>> FetchGeminiModels(string baseUrl, string apiKey)
        {
            var url = baseUrl.TrimEnd('/') + "/v1beta/models";
            if (!string.IsNullOrEmpty(apiKey))
                url += "?key=" + apiKey;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resp = await http.GetStringAsync(url);
            var jobj = JObject.Parse(resp);
            var models = jobj["models"] as JArray;
            if (models == null) return [];

            return models
                .Select(m =>
                {
                    var name = m["name"]?.ToString() ?? "";
                    var id = name.StartsWith("models/") ? name.Substring(7) : name;
                    var ctx = m["inputTokenLimit"]?.Value<int>();
                    return (id, ctx);
                })
                .Where(x => !string.IsNullOrEmpty(x.id))
                .ToList();
        }

        /// <summary>将 token 数自动匹配到最近的上下文标签</summary>
        private static (string tag, bool append) TokensToTag(int? contextTokens)
        {
            if (!contextTokens.HasValue || contextTokens.Value <= 0)
                return ("", false);

            var tokens = contextTokens.Value;
            // 从大到小匹配
            foreach (var kv in ModelConfig.ContextTagTokenMap.OrderByDescending(x => x.Value))
            {
                if (tokens >= kv.Value)
                    return (kv.Key, true);
            }
            return ("", false);
        }

        /// <summary>构建模型列表 URL，自动适配 BaseUrl 是否已含版本路径</summary>
        private static string BuildModelsUrl(string baseUrl, string relativePath)
        {
            var b = baseUrl.TrimEnd('/');
            if (System.Text.RegularExpressions.Regex.IsMatch(b, @"/v\d+(/.*)?$"))
                return b + relativePath;
            return b + "/v1" + relativePath;
        }

        // ── 保存 / 取消 / 添加 / 删除 ────────────────────────────

        private void btnSave_Click(object sender, EventArgs e)
        {
            var name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("名称不能为空", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_isNew && _existingNames.Contains(name))
            {
                MessageBox.Show($"名称 \"{name}\" 已存在", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(txtBaseUrl.Text.Trim()))
            {
                MessageBox.Show("Base URL 不能为空", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Provider.Type = GetSelectedType();
            Provider.Name = name;
            Provider.BaseUrl = txtBaseUrl.Text.Trim();
            Provider.ApiKey = string.IsNullOrEmpty(txtApiKey.Text) ? null : txtApiKey.Text;

            // 收集模型
            Provider.Models.Clear();
            foreach (DataGridViewRow row in dgvModels.Rows)
            {
                if (row.IsNewRow) continue;
                var modelId = row.Cells[iModelId].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(modelId)) continue;
                var mc = new ModelConfig
                {
                    Enabled = row.Cells[iEnabled].Value is bool b ? b : true,
                    Id = modelId,
                    ContextTag = row.Cells[iContextTag].Value?.ToString()?.Trim() ?? "",
                    AppendContextTag = row.Cells[iAppendTag].Value is bool a && a,
                };
                mc.SyncMaxContextFromTag();
                Provider.Models.Add(mc);
            }

            if (Provider.Models.Count == 0)
            {
                MessageBox.Show("至少需要一个模型", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnAddModel_Click(object sender, EventArgs e)
        {
            dgvModels.Rows.Add(true, "", "", false);
        }

        private void btnDelModel_Click(object sender, EventArgs e)
        {
            if (dgvModels.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvModels.SelectedRows)
                {
                    if (!row.IsNewRow) dgvModels.Rows.Remove(row);
                }
            }
        }

        private void chkShowKey_CheckedChanged(object sender, EventArgs e)
        {
            txtApiKey.UseSystemPasswordChar = !chkShowKey.Checked;
        }

        private void chkToggleAll_CheckedChanged(object? sender, EventArgs e)
        {
            var chk = sender as CheckBox;
            if (chk == null) return;
            var value = chk.Checked;
            foreach (DataGridViewRow row in dgvModels.Rows)
            {
                if (!row.IsNewRow)
                    row.Cells[iEnabled].Value = value;
            }
        }
    }
}
