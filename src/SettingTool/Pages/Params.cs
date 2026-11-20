// 本机加（2026-11-19）：动作调试台——把模型的几百个参数摆成滑杆，一拧就能在桌面上看见。
// 前置是桌宠的 set_freeze：不冻住的话她自己的待机、视线、眨眼每帧都会把值覆盖回去。
//
// 参数表从渲染器来：l2d-render --script "params <临时文件>" 会导出 id / 最小 / 最大 / 默认。
// 导一次要半秒到三秒，所以缓存在 cache\params-cache.txt，头部记着是哪个模型，模型换了才重导。

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;

namespace SettingTool;

/// <summary>一条参数：名字 + 滑杆 + 当前值。拖动时调 set_param，双击归位到默认值。</summary>
internal sealed class ParamRow : Control
{
    private static readonly Font NameFont = new("Consolas", 9.5f);
    private const int NameWidth = 170, ValueWidth = 100;

    private readonly string label;
    private readonly double min, max, def;
    private double value;
    private bool dragging;
    private DateTime lastSent;

    internal string ParamId { get; }
    internal event Action<string, double>? Moved;

    /// <summary>现在这条滑杆摆在哪（存快照用）。</summary>
    internal double Current => value;

    /// <summary>把滑杆挪到某个值，并照常通知桌宠（还原快照用）。</summary>
    internal void SetValue(double next)
    {
        value = Math.Clamp(next, min, max);
        Invalidate();
        Moved?.Invoke(ParamId, value);
    }

    /// <summary>拉回模型自带的默认值（重置用）。</summary>
    internal void ResetToDefault()
    {
        value = def;
        Invalidate();
        Moved?.Invoke(ParamId, value);
    }

    internal ParamRow(string id, string label, double min, double max, double def)
    {
        ParamId = id;
        this.label = label;
        this.min = min;
        this.max = max;
        this.def = def;
        value = def;
        Width = 600;
        Height = 38;
        BackColor = Theme.Card;
        Margin = new Padding(0, 1, 0, 1);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        if (min > max) (this.min, this.max) = (max, min);
    }

    private Rectangle Track => new(NameWidth, Height / 2 - 3, Math.Max(40, Width - NameWidth - ValueWidth - 20), 6);

    private double FromX(int x)
    {
        var t = Track;
        var f = Math.Clamp((x - t.Left) / (double)t.Width, 0, 1);
        return min + (max - min) * f;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2)
        {
            value = def;
            Invalidate();
            Moved?.Invoke(ParamId, value);
            return;
        }
        if (e.X < NameWidth - 10) return;   // 名字区留给双击归位
        dragging = true;
        value = FromX(e.X);
        Invalidate();
        Moved?.Invoke(ParamId, value);
        lastSent = DateTime.UtcNow;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        value = FromX(e.X);
        Invalidate();
        // 拖动时别每一像素发一次：本地 HTTP 再快也架不住一秒几百个请求
        if ((DateTime.UtcNow - lastSent).TotalMilliseconds < 40) return;
        lastSent = DateTime.UtcNow;
        Moved?.Invoke(ParamId, value);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = false;
        Moved?.Invoke(ParamId, value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        using var ink = new SolidBrush(Theme.InkSoft);
        g.DrawString(label.Length > 0 ? label : ParamId, NameFont, ink, 4, (Height - NameFont.Height) / 2f);
        if (label.Length > 0 && label != ParamId)
        {
            using var faint = new SolidBrush(Theme.InkFaint);
            g.DrawString(ParamId, NameFont, faint, 4, (Height - NameFont.Height) / 2f + 14);
        }

        var track = Track;
        using (var path = Theme.Rounded(track, 3))
        using (var back = new SolidBrush(Color.FromArgb(233, 240, 249)))
            g.FillPath(back, path);

        var fill = (float)Math.Clamp((value - min) / (max - min) * track.Width, 0, track.Width);
        if (fill > 0)
        {
            using var path = Theme.Rounded(new Rectangle(track.Left, track.Top, (int)Math.Round(fill), track.Height), 3);
            using var brush = Theme.Accent(new Rectangle(track.Left, track.Top, 1, 1));
            g.FillPath(brush, path);
        }

        var knobX = track.Left + fill;
        using (var knob = new SolidBrush(Color.White)) g.FillEllipse(knob, knobX - 6, track.Top - 4, 14, 14);
        using (var ring = new Pen(Theme.BlueDeep, 1.4f)) g.DrawEllipse(ring, knobX - 6, track.Top - 4, 14, 14);

        var text = value.ToString("0.###", CultureInfo.InvariantCulture);
        using var valueBrush = new SolidBrush(Theme.Ink);
        var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        g.DrawString(text, NameFont, valueBrush, new RectangleF(track.Right + 14, 0, ValueWidth - 14, Height), format);
    }
}

internal sealed partial class MainForm
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // 常用参数放前面（清单里点名的那些），其余按字母
    private static readonly (string Id, string Label)[] CommonParams =
    [
        ("ParamAngleX", "头 左右"), ("ParamAngleY", "头 上下"), ("ParamAngleZ", "头 左右倾斜"),
        ("ParamEyeLOpen", "左眼 开"), ("ParamEyeROpen", "右眼 开"),
        ("ParamEyeBallX", "眼球 X"), ("ParamEyeBallY", "眼球 Y"),
        ("ParamBrowLY", "左眉 Y"), ("ParamBrowRY", "右眉 Y"),
        ("ParamMouthOpenY", "嘴 张开"), ("ParamMouthForm", "嘴 形状"),
        ("ParamBodyAngleX", "身体 X"), ("ParamBodyAngleY", "身体 Y"), ("ParamBodyAngleZ", "身体 Z"),
        ("ParamBreath", "呼吸"), ("ParamBodyAngleX2", "身体 X2"), ("ParamBodyAngleY2", "身体 Y2"),
        ("Movement1", "手 狗尾草"), ("Movement2", "手 抚胸"), ("Movement3", "手 竖食指"),
    ];

    private static readonly Dictionary<string, string> LabelOf =
        CommonParams.ToDictionary(one => one.Id, one => one.Label);

    private readonly List<ParamRow> paramRows = [];
    private FlowLayoutPanel? paramHost;
    private Label? paramStatus;
    private NeoToggle? freezeToggle;
    private Label? paramModelLabel;
    private string paramFilter = "";
    private bool showAllParams;

    // ───────────────────────────────────── 卡片

    private Card BuildParamsCard()
    {
        var card = new Card("动作调试台", CardWidth);

        card.AddControl(Hint(
            "先把「冻结」打开，再拖滑杆——不然她自己的待机、视线、眨眼每帧都在改同一批参数，拖了像没反应。\n" +
            "双击一条能把它归位；调试完记得解冻。"));

        freezeToggle = new NeoToggle("冻结她的动作（调参之前先打开）", CardWidth - 60);
        freezeToggle.CheckedChanged += (_, _) =>
        {
            var on = freezeToggle.Checked;
            CallPet("set_freeze", "{\"on\":" + (on ? "true" : "false") + "}");
            Note(on ? "已冻结：她现在只剩你拧出来的样子" : "已解冻：待机和视线回来了");
        };
        card.AddControl(freezeToggle);

        var search = new NeoField(new TextBox(), 280);
        // 本机改（2026-11-19）：原来每敲一个字就重建一遍控件（几百个滑杆），
        // 打字明显一顿一顿的。改成停手 250 毫秒再过滤。
        var searchDelay = new System.Windows.Forms.Timer { Interval = 250 };
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            paramFilter = search.Text.Trim();
            FillParams();
        };
        search.TextChanged += (_, _) =>
        {
            searchDelay.Stop();
            searchDelay.Start();
        };

        var all = new NeoToggle("显示全部", 130);
        all.CheckedChanged += (_, _) => { showAllParams = all.Checked; FillParams(); };

        var reload = new NeoButton("重新读参数");
        reload.Click += (_, _) =>
        {
            try { File.Delete(ParamCacheFile()); } catch { }
            FillParams(force: true);
        };

        var resetAll = new NeoButton("全部归位") { Width = 100 };
        resetAll.Click += (_, _) =>
        {
            foreach (var row in paramRows)
            {
                CallPet("set_param", "{\"name\":\"" + row.ParamId + "\",\"reset\":true}");
            }
            foreach (var row in paramRows) row.Invalidate();
            Note("已把摆着的这些参数统统松开");
        };

        // 本机加（2026-11-19）：重置——把所有滑杆拉回模型自带的默认值。
        // 和「全部归位」不一样：归位是松手让动画接管，重置是明明白白按住默认值。
        var resetDefault = new NeoButton("重置默认值") { Width = 110 };
        resetDefault.Click += (_, _) =>
        {
            if (paramRows.Count == 0) { Note("还没读参数，先点「重新读参数」"); return; }
            foreach (var row in paramRows) row.ResetToDefault();
            Note($"已把 {paramRows.Count} 条拉回默认值");
        };

        card.AddControl(Row(search, all));
        card.AddControl(Row(resetDefault, resetAll, reload));

        // 本机加：当前模型是谁，摆在这儿——换过模型以后一眼能看出来参数表是不是新的
        paramModelLabel = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 22,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 2, 0, 2),
        };
        card.AddControl(paramModelLabel);

        // 本机加（2026-11-19）：参数快照——把这会儿摆着的值存成一份，之后一键还原。
        // 调试动作的时候手一抖就回不去了，有快照能反复对比。
        var saveShot = new NeoButton("存快照") { Width = 96, Height = 34 };
        saveShot.Click += (_, _) =>
        {
            var values = paramRows.ToDictionary(row => row.ParamId, row => row.Current);
            if (values.Count == 0) { Note("还没读参数，先点「重新读参数」"); return; }
            var model = ModelFileForParams();
            var payload = new Dictionary<string, object?>
            {
                ["model"] = model == null ? "" : Path.GetFileName(model),
                ["savedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ["values"] = values,
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotFile())!);
                File.WriteAllText(SnapshotFile(), System.Text.Json.JsonSerializer.Serialize(payload,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                Note($"存了 {values.Count} 个参数");
            }
            catch (Exception problem) { Note("存不了快照：" + problem.Message); }
        };

        var loadShot = new NeoButton("还原快照") { Width = 100, Height = 34 };
        loadShot.Click += (_, _) => RestoreSnapshot();

        card.AddControl(Row(saveShot, loadShot));

        paramHost = new BufferedFlow
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            Width = CardWidth - 60,
            Margin = new Padding(0, 4, 0, 0),
        };
        card.AddControl(paramHost);

        paramStatus = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 24,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        card.AddControl(paramStatus);

        return card;
    }

    // ───────────────────────────────────── 参数表

    private static string ParamCacheFile() => Path.Combine(Program.Root, "cache", "params-cache.txt");

    /// <summary>当前设置的模型文件；模型选的是「默认」时按大肥鱼、再退到第一个找到的。</summary>
    private string? ModelFileForParams()
    {
        var folder = Path.Combine(Program.Root, "model");
        if (!Directory.Exists(folder)) return null;
        var chosen = store.Text("ModelPath", "");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var full = Path.Combine(folder, chosen);
            if (File.Exists(full)) return full;
        }
        var found = Directory.EnumerateFiles(folder, "*.model3.json", SearchOption.AllDirectories).ToList();
        return found.FirstOrDefault(one => Path.GetFileName(one).Equals("大肥鱼.model3.json", StringComparison.OrdinalIgnoreCase))
            ?? found.FirstOrDefault();
    }

    /// <summary>参数表：id → (最小, 最大, 默认)。缓存里存的模型对不上就重新导。</summary>
    private List<(string Id, double Min, double Max, double Def)> ReadParamTable(string modelFile, out string? error)
    {
        error = null;
        var cache = ParamCacheFile();
        var header = "# " + modelFile;

        if (File.Exists(cache))
        {
            try
            {
                var lines = File.ReadAllLines(cache);
                if (lines.Length > 1 && lines[0] == header)
                    return lines.Skip(1).Select(ParseParamLine).Where(one => one != null).Select(one => one!.Value).ToList();
            }
            catch { }
        }

        var exe = Path.Combine(Program.Root, "renderer", "build", "Release", "l2d-render.exe");
        if (!File.Exists(exe)) { error = "没找到渲染器：" + exe; return []; }

        var temp = Path.Combine(Path.GetTempPath(), "fishpet-params-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        var script = temp + ".script";
        try
        {
            File.WriteAllText(script, "params " + temp + "\n", new UTF8Encoding(false));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                }
            };
            foreach (var arg in new[] { "--model", modelFile, "--width", "200", "--height", "400",
                "--texscale", "0.25", "--masksize", "1024", "--cache", Path.Combine(Program.Root, "cache"), "--script", script })
                process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(30000)) { try { process.Kill(); } catch { } error = "渲染器 30 秒还没导完参数"; return []; }

            if (!File.Exists(temp)) { error = "渲染器没写出参数表（换角色以后先跑一次桌宠，确认模型能加载）"; return []; }

            var lines = File.ReadAllLines(temp);
            var list = lines.Select(ParseParamLine).Where(one => one != null).Select(one => one!.Value).ToList();
            if (list.Count == 0) { error = "参数表是空的"; return []; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                File.WriteAllLines(cache, new[] { header }.Concat(lines), new UTF8Encoding(false));
            }
            catch { }
            return list;
        }
        catch (Exception problem)
        {
            error = "导参数表失败：" + problem.Message;
            return [];
        }
        finally
        {
            try { File.Delete(script); } catch { }
            try { File.Delete(temp); } catch { }
        }
    }

    private static (string Id, double Min, double Max, double Def)? ParseParamLine(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 4) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)) return null;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var max)) return null;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var def)) return null;
        return (parts[0], min, max, def);
    }

    // 本机改（2026-11-19）：参数表要跑一次渲染器（0.5–3 秒），原来在界面线程里同步读，
    // 一进模型页就卡死。现在挪到后台线程，读完再回 UI 建滑杆。
    private bool filling;

    private void FillParams(bool force = false)
    {
        if (paramHost == null || filling) return;
        var modelFile = ModelFileForParams();
        if (modelFile == null)
        {
            paramStatus!.Text = "model 文件夹里还没有模型";
            return;
        }

        filling = true;
        paramStatus!.Text = "正在读参数表…（换模型以后第一次会久一点）";

        Task.Run(() =>
        {
            var table = ReadParamTable(modelFile, out var error);
            try
            {
                BeginInvoke(new Action(() =>
                {
                    filling = false;
                    ShowParamRows(modelFile, table, error);
                }));
            }
            catch (Exception)
            {
                filling = false;
            }
        });
    }

    private void ShowParamRows(string modelFile, List<(string Id, double Min, double Max, double Def)> table, string? error)
    {
        if (table.Count == 0)
        {
            paramStatus!.Text = error ?? "读不到参数表";
            return;
        }

        // 常用组先排，剩下的按字母
        var ordered = table
            .OrderBy(one => Array.FindIndex(CommonParams, common => common.Id == one.Id) is var at && at >= 0 ? at : 1000)
            .ThenBy(one => one.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = ordered.Where(one => paramFilter.Length == 0 || one.Id.Contains(paramFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        var limit = showAllParams ? 120 : 24;
        var shown = matched.Take(limit).ToList();

        if (paramModelLabel != null)
            paramModelLabel.Text = $"当前模型：{Path.GetFileName(modelFile)}　共 {table.Count} 个参数";

        if (paramHost == null) return;   // 页面还没建出来（懒加载时可能为空）
        paramHost.SuspendLayout();
        paramHost.Controls.Clear();
        paramRows.Clear();
        foreach (var (id, min, max, def) in shown)
        {
            var row = new ParamRow(id, LabelOf.TryGetValue(id, out var label) ? label : "", min, max, def);
            row.Moved += (param, value) => CallPet("set_param",
                "{\"name\":\"" + param + "\",\"value\":" + value.ToString("0.####", CultureInfo.InvariantCulture) + "}");
            paramRows.Add(row);
            paramHost.Controls.Add(row);
        }
        paramHost.ResumeLayout(true);

        var total = matched.Count;
        paramStatus!.Text = total <= shown.Count
            ? $"{Path.GetFileName(modelFile)} · {shown.Count} 条参数"
            : $"{Path.GetFileName(modelFile)} · 显示 {shown.Count} / 匹配 {total} 条，输入关键字缩小范围，或者打开「显示全部」";
    }

    // ───────────────────────────────────── 参数快照

    private static string SnapshotFile() => Path.Combine(Program.Root, "cache", "param-snapshot.json");

    /// <summary>把快照里的值挨个摆回去；滑杆上没摆着的参数也发一次 set_param。</summary>
    private void RestoreSnapshot()
    {
        var file = SnapshotFile();
        if (!File.Exists(file)) { Note("还没有存过快照"); return; }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("values", out var values)) { Note("快照文件读不懂"); return; }
            var applied = 0;
            foreach (var one in values.EnumerateObject())
            {
                if (one.Value.ValueKind != System.Text.Json.JsonValueKind.Number) continue;
                var next = one.Value.GetDouble();
                var row = paramRows.FirstOrDefault(one2 => one2.ParamId == one.Name);
                if (row != null) row.SetValue(next);
                else CallPet("set_param", "{\"name\":\"" + one.Name + "\",\"value\":" +
                    next.ToString("0.####", CultureInfo.InvariantCulture) + "}");
                applied++;
            }
            var stamp = doc.RootElement.TryGetProperty("savedAt", out var at) ? "（存于 " + at.GetString() + "）" : "";
            Note($"还原了 {applied} 个参数{stamp}");
        }
        catch (Exception problem) { Note("还原失败：" + problem.Message); }
    }

    // ───────────────────────────────────── 跟她说话

    /// <summary>走桌宠的 HTTP 接口调一个工具。她没开着就什么都不做，只留一句提示。</summary>
    private void CallPet(string tool, string json)
    {
        var apiFile = Path.Combine(Program.Root, "api.json");
        if (!PetHost.Running || !File.Exists(apiFile)) return;
        try
        {
            var api = System.Text.Json.JsonDocument.Parse(File.ReadAllText(apiFile)).RootElement;
            var url = api.GetProperty("url").GetString();
            var token = api.GetProperty("token").GetString();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token)) return;

            _ = Task.Run(() =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url + "/call/" + tool);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var answer = Http.Send(request);
                }
                catch { }
            });
        }
        catch { }
    }
}
