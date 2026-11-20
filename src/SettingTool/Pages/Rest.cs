// 重写版（2026-11-17）：行为页 + 模型页 + 其它页。

namespace SettingTool;

internal sealed partial class MainForm
{
    // ───────────── 行为
    private NeoField idleField = NumField(120m, 15m, 600m, 5m, 110);
    // 本机加（2026-11-18）：连点几次她就生气退出。0-99（0 = 关掉这个功能）。
    private NeoField quitTapsField = NumField(3m, 0m, 99m, 1m, 110);
    private NeoToggle coaxSleepToggle = new("00:30 起哄睡（四层递进）", 560);
    private NeoToggle coaxLunchToggle = new("11:30 提醒吃午饭", 560);
    private NeoToggle musicDanceToggle = new("听到音乐就跟着摇摆", 560);
    private NeoField musicAppsField = TextField("", 560);
    private NeoToggle checkUpdateToggle = new("启动时和每 12 小时看有没有新版本", 560);

    // ───────────── 羁绊（本机加 2026-11-19）

    // 和 Affection.cs 的 Levels 一致
    private static readonly (int From, string Name)[] BondLevels =
        [(0, "初识"), (100, "磨合"), (300, "同频"), (550, "默契"), (800, "羁绊")];

    private static string BondFile => System.IO.Path.Combine(Program.Root, "affection.json");

    private static (int Points, int Gain, int Loss) ReadBond()
    {
        try
        {
            if (!File.Exists(BondFile)) return (0, 0, 0);
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(BondFile));
            var root = doc.RootElement;
            int Get(string name) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
            return (Get("Points"), Get("TodayGain"), Get("TodayLoss"));
        }
        catch { return (0, 0, 0); }
    }

    private Card BuildBondCard()
    {
        var card = new Card("羁绊", CardWidth);
        var (points, gain, loss) = ReadBond();

        // 宽度给得保守一点：CardWidth 是随窗口算的，减多了才不会被 body 裁掉右边缘
        bondBar = new BondBar(points, Math.Max(360, CardWidth - 140));
        card.AddControl(bondBar);

        bondToday = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 24,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        card.AddControl(bondToday);
        UpdateBondLabels(points, gain, loss);

        card.AddControl(Hint("摸头、聊天、番茄钟、按时睡会加；戳裙子、挨打、熬夜、整天不理会掉。\n每天最多涨 50、最多掉 20；分涨过线会解锁对应台词。"));

        var reset = new NeoButton("重新开始", ButtonKind.Outline) { Width = 120 };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "把羁绊清零、从头开始？\n\n这一下不可撤销，但记忆小本本不会被清。", "重新开始",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            CallPet("reset_affection", "{}");
            var (p, g, l) = ReadBond();
            bondBar!.Points = p;
            bondBar.Invalidate();
            UpdateBondLabels(p, g, l);
            Note("羁绊已清零");
        };
        card.AddControl(Row(reset));

        return card;
    }

    private BondBar? bondBar;
    private Label? bondToday;

    private void UpdateBondLabels(int points, int gain, int loss)
    {
        var level = 1;
        for (var i = 0; i < BondLevels.Length; i++) if (points >= BondLevels[i].From) level = i + 1;
        var name = BondLevels[level - 1].Name;
        var next = level < BondLevels.Length ? BondLevels[level].From : 1000;
        var left = Math.Max(0, next - points);
        var where = level < BondLevels.Length ? $"距离「{BondLevels[level].Name}」还差 {left} 分" : "已经满级";
        if (bondToday != null)
            bondToday.Text = $"当前 {points} / 1000 · {name}　{where}　　今天 +{gain} / −{loss}（上限 +50 / −20）";
    }

    private FlowLayoutPanel BuildBehaviorPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("行为", "她闲着的时候干什么、什么时候主动开口。", CardWidth));

        // 本机加（2026-11-19）：羁绊可视化 + 重新开始
        Mount(page, BuildBondCard());

        var idle = new Card("待机", CardWidth);
        idle.AddControl(Row(Tag("间隔", 60), idleField));
        idle.AddControl(Hint("单位秒，15–600：没人理她的时候多久自己动一下。"));
        Mount(page, idle);

        var alarm = new Card("到点提醒", CardWidth);
        alarm.AddControl(coaxSleepToggle);
        alarm.AddControl(coaxLunchToggle);
        Mount(page, alarm);

        var music = new Card("跟着音乐动", CardWidth);
        music.AddControl(musicDanceToggle);
        music.AddControl(Row(Tag("白名单", 60), musicAppsField));
        music.AddControl(Hint("只在这些播放器出声时才跳，逗号分隔。说话、播客不会让她乱蹦。"));
        Mount(page, music);

        // 本机加（2026-11-18）：点几次就强制退出
        var taps = new Card("点几次就退出", CardWidth);
        taps.AddControl(Row(Tag("次数", 60), quitTapsField));
        taps.AddControl(Hint("上半身和下半身各数各的，各自满这个数——她会黑脸说一句，然后自己关掉。\n范围 0–99，默认 3；填 0 就是关掉这个功能，怎么点都不会退。隔 8 秒没点就重新数。"));
        Mount(page, taps);

        var update = new Card("更新", CardWidth);
        update.AddControl(checkUpdateToggle);
        Mount(page, update);

        return page;
    }

    // ───────────── 模型
    // 本机加（2026-11-18）：模型清单，给别人加模型时能看见每个模型完不完整
    private NeoTable? modelTable;
    private Label? modelSummary;
    private NeoField modelField = ComboField(["（默认：自动找）"], 460);
    // 本机改：衣服改用一组开关，风格和别处一致（原来是系统 CheckedListBox，方框+滚动条很突兀）
    private readonly List<NeoToggle> outfitToggles = [];

    private FlowLayoutPanel BuildModelPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("模型", "换角色、换衣服。换成别的 Live2D 模型也在这儿。", CardWidth));

        // ── 现有模型
        var who = new Card("换角色", CardWidth);
        who.AddControl(Row(Tag("当前", 60), modelField));
        var rescan = new NeoButton("重新扫描");
        rescan.Click += (_, _) => { FillModels(); RefreshModelTable(); Note("已重新扫描模型"); };

        // 本机加（2026-11-18）：别人下载的模型常常缺表情引用、表情还平铺在根目录。
        // 点这里就地整理：搬进 Exp3\ / Animation\，补 model3.json，猜口型眨眼。
        var fix = new NeoButton("一键整理") { Width = 100 };
        fix.Click += (_, _) => FixModelFolders();
        var openDir = new NeoButton("打开模型文件夹");
        openDir.Click += (_, _) =>
        {
            try
            {
                var folder = System.IO.Path.Combine(Program.Root, "model");
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception error) { Note("打不开：" + error.Message); }
        };
        who.AddControl(Row(rescan, fix, openDir));
        Mount(page, who);

        // 本机改（2026-11-19）：调试台原来挂在页面最下面，藏在「换衣服」后面没人找得到。
        // 现在紧跟在「换角色」后面——换完模型第一眼就能看见参数表。
        Mount(page, BuildParamsCard());
        FillParams();

        // ── 模型清单（给别人加模型时看得见东西）
        var list = new Card("模型清单", CardWidth);
        modelTable = new NeoTable(["模型", "文件", "状态"], CardWidth - 60, 180) { Tag = "wide" };
        list.AddControl(modelTable);
        modelSummary = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 26,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 0),
        };
        list.AddControl(modelSummary);
        Mount(page, list);

        // ── 加模型说明
        var add = new Card("加一个新模型", CardWidth);
        add.AddControl(Hint("你自己的 Live2D 模型也可以放进来——只要它有 model3.json，桌宠就认得。"));
        add.AddControl(Hint(
            "1. 把整个模型文件夹放进：\n" +
            "     " + System.IO.Path.Combine(Program.Root, "model") + "\n" +
            "2. 文件夹里至少要有：\n" +
            "     <名字>.model3.json     ← 桌宠靠它找模型\n" +
            "     <名字>.moc3            ← 模型本体\n" +
            "     <名字>.physics3.json   ← 可以不删，动起来更自然\n" +
            "     <贴图目录>/texture_*.png\n" +
            "3. 回到这儿点「重新扫描」，在「当前」里选它，保存后重启桌宠。"));
        add.AddControl(Hint(
            "注意：换装那几张卡是给「大肥鱼」做的，用的是她的自定义表情 ID。\n" +
            "换别的模型以后，勾了没反应是正常的（模型里没有对应表情）。"));
        Mount(page, add);

        // 本机改（2026-11-18）：按当前模型的表情列表，没有的就禁用 —— 别让人勾了个没反应
        var modelExps = CurrentModelExpressions();
        var clothes = new Card("换衣服", CardWidth);
        clothes.AddControl(Hint(modelExps.Count == 0
            ? "当前模型没有表情文件夹（Exp3），换装全部不可用。"
            : "勾上就是穿上，取消就是脱掉。灰色的开关表示当前模型没有这个表情。"));
        foreach (var label in new[] { "长裙", "胸口衣服", "脱鞋", "收起尾巴", "黑框眼镜", "单片眼镜", "钢盆", "兔牙" })
        {
            var toggle = new NeoToggle(label, 560);
            outfitToggles.Add(toggle);
            clothes.AddControl(toggle);
        }

        var presets = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            BackColor = Theme.Card, Width = 600, Margin = new Padding(0, 6, 0, 0),
        };
        foreach (var (label, items) in new[]
        {
            ("日常", new[] { "长裙" }),
            ("便装", new[] { "脱鞋" }),
            ("完全体", new[] { "长裙", "黑框眼镜" }),
            ("睡衣", new[] { "收起尾巴", "脱鞋" }),
            ("全脱", Array.Empty<string>()),
        })
        {
            var button = new NeoButton(label) { Width = 82, Height = 32 };
            var picked = items;
            button.Click += (_, _) => ApplyOutfit(picked);
            presets.Controls.Add(button);
        }
        clothes.AddControl(presets);
        // 本机改（2026-11-19）：当前模型一个换装表情都没有时，整张卡收起来。
        // 灰着的开关比没有更让人困惑——换了别人的模型，这栏本来就没什么可勾的。
        var anyOutfit = outfitToggles.Any(toggle => Mapped(toggle.Text) is { } expression && modelExps.Contains(expression));
        if (anyOutfit)
        {
            Mount(page, clothes);
        }
        else
        {
            var none = new Card("换衣服", CardWidth);
            none.AddControl(Hint("当前这个模型没有换装表情（长裙、眼镜、钢盆那些），这张卡就先收起来了。"));
            none.AddControl(Hint("想换装就把角色换回「大肥鱼」——她的 Exp3 里才有「裙子切换」「黑框眼镜」这些表情。"));
            Mount(page, none);
        }

        // 本机加（2026-11-18）：进页面就把清单刷出来
        RefreshModelTable();

        return page;
    }

    /// <summary>把 model 文件夹里的每个模型列出来，标出完整不完整。</summary>
    /// <summary>把 model\ 下每个模型目录都整理一遍。</summary>
    private void FixModelFolders()
    {
        var modelsRoot = Path.Combine(Program.Root, "model");
        if (!Directory.Exists(modelsRoot))
        {
            Note("还没有 model 目录");
            return;
        }

        var folders = Directory.GetDirectories(modelsRoot);
        if (folders.Length == 0)
        {
            Note("model 目录里还没有模型");
            return;
        }

        var lines = new List<string>();
        var changed = 0;
        foreach (var folder in folders)
        {
            var result = ModelFixer.Fix(folder, dryRun: false);
            if (!result.Ok) continue;
            var name = Path.GetFileName(folder);
            if (result.MovedExpressions > 0 || result.MovedMotions > 0
                || result.Expressions > 0 || result.Motions > 0)
                changed++;
            lines.Add($"{name}：表情 {result.Expressions}、动作 {result.Motions}");
            if (!string.IsNullOrEmpty(result.Lipsync))
                lines.Add("    口型 " + result.Lipsync);
            if (!string.IsNullOrEmpty(result.Blink))
                lines.Add("    眨眼 " + result.Blink);
        }

        FillModels();
        RefreshModelTable();
        Note(changed > 0 ? $"整理完 {changed} 个模型，重新扫描过了" : "都是整理过的，没改动");
        ShowFixDetail(lines);
    }

    /// <summary>整理完弹一个小窗，把每个模型的结果列出来。</summary>
    private void ShowFixDetail(List<string> lines)
    {
        if (lines.Count == 0) return;
        using var box = new Form
        {
            Text = "整理结果",
            Width = 620,
            Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White,
            Font = Theme.Body,
            MinimizeBox = false,
        };
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.Mono,
            BackColor = Color.White,
            ForeColor = Theme.Ink,
            Text = string.Join(Environment.NewLine, lines),
            Padding = new Padding(12),
        };
        box.Controls.Add(text);
        box.ShowDialog(this);
    }

    /// <summary>当前模型有哪些表情（读 Exp3 里的 *.exp3.json 名字）。</summary>
    private static HashSet<string> CurrentModelExpressions()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var settings = Path.Combine(Program.Root, "settings.json");
            if (!File.Exists(settings)) return names;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings));
            var model = doc.RootElement.TryGetProperty("ModelPath", out var mv) ? mv.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(model)) return names;
            // ModelPath 是相对 model\ 的（比如 "DeepSeek\\大肥鱼.model3.json"）
            var full = Path.Combine(Program.Root, "model", model.Replace('/', Path.DirectorySeparatorChar));
            var folder = Path.GetDirectoryName(full);
            if (folder == null) return names;
            var expDir = Path.Combine(folder, "Exp3");
            if (!Directory.Exists(expDir)) return names;
            foreach (var path in Directory.GetFiles(expDir, "*.exp3.json"))
            {
                var name = Path.GetFileName(path);
                names.Add(name[..^13]);
            }
        }
        catch { }
        return names;
    }

    /// <summary>换装标签 → 表情名（和 FishPet 的 Outfit 表一致）。</summary>
    private static string? Mapped(string label) => label switch
    {
        "长裙" => "裙子切换",
        "胸口衣服" => "胸口衣服",
        "脱鞋" => "脱鞋",
        "收起尾巴" => "尾巴开关",
        "黑框眼镜" => "黑框眼镜",
        "单片眼镜" => "单片眼镜",
        "钢盆" => "钢盆",
        "兔牙" => "兔牙",
        _ => null,
    };

    /// <summary>把换装开关按当前模型的表情启用 / 禁用。</summary>
    private void SyncOutfitToggles()
    {
        var exps = CurrentModelExpressions();
        foreach (var toggle in outfitToggles)
        {
            // SettingTool 里没有 FishPet 的 Outfit 类，这里照搬那张表
            var expression = Mapped(toggle.Text);
            var ok = expression != null && exps.Contains(expression);
            toggle.Enabled = ok;
            toggle.ForeColor = ok ? Theme.Ink : Theme.InkSoft;
        }
    }

    private void RefreshModelTable()
    {
        SyncOutfitToggles();
        if (modelTable == null) return;
        modelTable.ClearRows();

        var root = System.IO.Path.Combine(Program.Root, "model");
        var dirs = new List<string>();
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var json in Directory.EnumerateFiles(root, "*.model3.json", SearchOption.AllDirectories))
                    dirs.Add(System.IO.Path.GetDirectoryName(json)!);
            }
        }
        catch { }
        dirs = dirs.Distinct().OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

        var complete = 0;
        foreach (var dir in dirs)
        {
            var name = System.IO.Path.GetFileName(dir);
            int files = 0; long bytes = 0;
            try
            {
                foreach (var one in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    files++; bytes += new FileInfo(one).Length;
                }
            }
            catch { }

            var hasMoc = Directory.EnumerateFiles(dir, "*.moc3", SearchOption.AllDirectories).Any();
            string state;
            if (!hasMoc) state = "缺 .moc3";
            else
            {
                var pngs = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories).Count();
                state = pngs == 0 ? "缺贴图" : "完整";
                if (pngs > 0) complete++;
            }
            modelTable.AddRow(name, files + " 个 / " + (bytes / 1048576.0).ToString("0.0") + " MB", state);
        }

        if (modelSummary != null)
            modelSummary.Text = dirs.Count == 0
                ? "还没放模型 —— 把模型文件夹丢进 model 文件夹再点「重新扫描」"
                : $"{dirs.Count} 个模型，{complete} 个完整";
    }

    private void FillModels()
    {
        var folder = System.IO.Path.Combine(Program.Root, "model");
        var found = new List<string>();
        try
        {
            if (Directory.Exists(folder))
                found.AddRange(Directory.EnumerateFiles(folder, "*.model3.json", SearchOption.AllDirectories)
                    .Select(path => System.IO.Path.GetRelativePath(folder, path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        }
        catch { }

        var keep = modelField.Text;
        modelField.Items.Clear();
        modelField.Items.Add("（默认：自动找）");
        foreach (var one in found) modelField.Items.Add(one);

        var at = string.IsNullOrWhiteSpace(keep) ? 0 : modelField.Items.IndexOf(keep);
        modelField.SelectedIndex = at >= 0 ? at : 0;
    }

    private void ApplyOutfit(string[] items)
    {
        foreach (var toggle in outfitToggles)
            toggle.Checked = items.Contains(toggle.Text);
        Note("预设已套上 · 记得保存");
    }

    // ───────────── 其它
    private NeoField birthdayField = TextField("", 140);

    private FlowLayoutPanel BuildOtherPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("其它", "零碎的东西都在这儿。", CardWidth));

        var birthday = new Card("生日", CardWidth);
        birthday.AddControl(Row(Tag("日期", 60), birthdayField));
        birthday.AddControl(Hint("写成 09-26 这样；那天她会说生日快乐。留空就不记。"));
        Mount(page, birthday);

        // 本机加（2026-11-18）：给开发者看的接口信息。
        // 想自己写程序跟她说话的人，需要的就这三样：地址、口令、MCP 怎么接。
        var dev = new Card("开发者接口", CardWidth);
        dev.AddControl(Hint("想自己写程序让她说话、或者把桌宠接进别的 AI，用这几样。"));
        // 本机加（2026-11-18）：换模型之后表情名全变了，得提前说
        dev.AddControl(Hint("换角色以后表情 ID 会变：exp <表情名> 1 里那个名字，要照新模型的 Exp3\\ 里有什么填。\n"
            + "拿不准就先跑一次 l2d-render --model <你的模型> 看它报的「表情 N」，再列一遍 Exp3\\ 的文件名。"));

        var api = ReadApiFile();
        var state = api == null ? "没找到 api.json —— 桌宠还没启动过？" : "地址  " + api.Value.Url;
        dev.AddControl(Row(Tag("HTTP", 60), Soft(state)));
        if (api != null)
        {
            dev.AddControl(Row(Tag("口令", 60), Soft(api.Value.Token)));
            dev.AddControl(Hint("请求头带 Authorization: Bearer <口令>。例子：\n" +
                "POST " + api.Value.Url + "/call/say\n" +
                "{\"text\": \"〔眯眼笑〕晚上好\"}"));
            var copy = new NeoButton("复制口令", ButtonKind.Outline) { Tint = Theme.Blue, Width = 96 };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(api.Value.Token); Note("口令已复制"); }
                catch { Note("复制失败，手动选中吧"); }
            };
            dev.AddControl(Row(copy));
        }

        dev.AddControl(new Label
        {
            Text = "MCP（stdio，无参数）：",
            AutoSize = true, Font = Theme.Body, ForeColor = Theme.Ink,
            BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 2),
        });
        dev.AddControl(Soft(Path.Combine(Program.Root, "bin", "FishPet.Mcp.exe")));
        dev.AddControl(Hint("把它填进 Agent 的 MCP 配置里就能接上。接口文档见 prompt\\身体说明.md。"));
        Mount(page, dev);

        var about = new Card("关于", CardWidth);
        about.AddControl(Hint("大肥鱼设置 · 重写版（2026-11-17）\n配置文件就在插件根目录的 settings.json，改坏了随时能手工修。"));
        Mount(page, about);

        return page;
    }

    /// <summary>读 api.json，拿地址和口令；没有就返回 null。</summary>
    private static (string Url, string Token)? ReadApiFile()
    {
        try
        {
            var path = Path.Combine(Program.Root, "api.json");
            if (!File.Exists(path)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
            return url.Length == 0 ? null : (url, token);
        }
        catch { return null; }
    }

    /// <summary>只读的等宽小字，用来显示路径和口令。</summary>
    private static Label Soft(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Mono,
        ForeColor = Theme.InkSoft,
        BackColor = Theme.Card,
        Margin = new Padding(0, 6, 0, 6),
    };
}
