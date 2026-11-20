# 大肥鱼桌宠 · DSH 接入

装好这一条，DSH 就会自己去启动大肥鱼的 MCP 接口，她也就有了说话、表情、换装、专注这些工具——不用再手动敲 `claude mcp add` 那种命令。

## 怎么装

这个包就在大肥鱼仓库的 `dsh-plugin/` 目录里。两种装法任选：

**用界面装**：DSH 的「插件」页 → 安装本地目录 → 选 `dsh-plugin` 这个文件夹。

**让 AI 装**：跟你的 Agent 说「把 `dsh-plugin` 装成插件」，它会用 `plugin_manager` 的 `install_bundle` 装。

装完在「插件」里会出现「大肥鱼桌宠」这一条，可以随时开关。

> 如果之前手动配过 `dafeiyu` 这个 MCP，会报 `serverName "dafeiyu" is already in use`——两条做的是同一件事，把手动那条删掉就行（它一般在 profile 的 `cordis.patch.yml` 里，`- id: mcp-dafeiyu` 那一段）。

## 装之前

桌宠本体得先在。按 [README](https://github.com/CheQiaoCao180180/SoftWhisper-Haven) 的做法装好，也就是这个位置有文件：

```
%USERPROFILE%\大肥鱼插件\bin\FishPet.Mcp.exe
```

装在别的地方也行，装完插件后到「插件」里找到 `dafeiyu` 那一行，把 `command` 改成实际路径。

## 装好之后

1. 开一个新会话
2. 跟 AI 说句话，看她是不是在桌面上回你

能用的工具（工具名前缀 `mcp__dafeiyu__`）：

- `say` —— 让她在桌面上说话，`〔〕` 里写动作
- `show` / `hide` / `quit` —— 叫她出来、让她退下、收起身体
- `dress` —— 换衣服和配饰
- `look_in_mirror` / `look_back` —— 照镜子、回头看自己刚才的样子
- `set_activity` —— 干活时戴上眼镜
- `focus` —— 番茄钟
- `remember` / `recall` —— 记忆小本本
- `get_state` / `recent_events` —— 看她现在的状态、刚才被怎么碰了

## 限制

- **桌宠没开也能装**：插件设了 `failOnStartupError: false`，找不到可执行文件时只是没有这些工具，不会让 DSH 起不来。
- **只管接口，不管渲染**：她还是在独立的桌面窗口里画，不会出现在 DSH 网页里。想在网页里看见她，那得换 Web 渲染，是另一件事。
- 路径按 README 推荐的位置算。装在别处的要手动改。
