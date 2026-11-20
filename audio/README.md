# audio\ —— 大肥鱼的耳朵

她听到电脑在放歌，就会跟着拍子跳。这个文件夹是"耳朵"：一个常驻的 PowerShell 采集器，
把系统正在播放的声音算成音量和 16 段频谱，每 50ms 往 stdout 打一行 JSON：

```
{"ready":true,"device":"耳机 (Realtek(R) Audio) + 扬声器 (Realtek(R) Audio)"}
{"peak":0.0578,"rms":0.0181,"bands":[0.074,0.028,...]}   ← 16 段
```

- 采集方式是 **WASAPI loopback**（和 OBS 抓桌面音频同一套），会同时抓所有活动的输出设备（耳机、扬声器、主声音驱动程序）；只读正在播的声音；
  **不录音、不落盘、不上传**，只算数字。
- 会被 `bin\FishPet.exe` 当子进程拉起来：`probe.ps1 -IntervalMs 50 -ParentPid <桌宠 PID>`。
  桌宠退出时它自己发现父进程没了就退出，不会留孤儿进程。
- 桌宠关掉"听音乐跳舞"（`music_dance`）或者重启时，它会跟着退出。

## 文件

| 文件 | 是什么 |
| --- | --- |
| `probe.ps1` | 采集器：Windows PowerShell 5.1 + `Add-Type` 现场编译一小段 C#，用 NAudio 抓 loopback、做 FFT、分频段 |
| `NAudio.dll` | NAudio 1.10.0（MIT，Copyright © Mark Heath & Contributors），随包分发。中文路径下 `LoadFrom` 会报 0x80131515，`probe.ps1` 会先复制到 `%TEMP%` 再用 `LoadFile` 加载。 |

## 哪来的

采集器改自 **dsh-whale-pulse**（"鱼的耳机"，MIT）的 `runtime/probe.ps1`：
<https://github.com/XiaTian-606/dsh-whale-pulse>

改动：

1. 去掉"按应用抓取"（那要用额外的 `ApplicationLoopback.dll`），改成同时抓所有活动输出设备，网易云的 DirectSound/主声音驱动程序也能触发；
2. NAudio 直接从本文件夹读，不再复制到 `%TEMP%`；
3. 输出全部 ASCII（设备名里的非 ASCII 换成 `?`），数字用 `InvariantCulture` 格式化——
   原作者那版在"小数点用逗号"的系统上会输出非法 JSON，整条链路静默失效；
4. 父进程 PID 由桌宠传进来，不再用 WMI 查。

"跟着拍子跳"的部分（`src\FishPet\Music.cs`、`Dance.cs`、`PetWindow.Dance.cs`）是这个插件自己写的。

## 单独调试

```powershell
# 跑 5 秒看看数值（Ctrl+C 停）
powershell -NoProfile -ExecutionPolicy Bypass -File audio\probe.ps1 -IntervalMs 50

# 系统音量很小的时候 rms 大约 0.002~0.02；放歌时 0.02~0.2
```

`probe.ps1` 必须保持 ASCII（PowerShell 5.1 读无 BOM 的 UTF-8 中文脚本会乱码）。

## 语音（她念台词）

`audio\voices\` 里那些 mp3 是她说话用的缓存，**默认不进仓库**（`.gitignore` 里有 `audio/voices/`）。
用户第一次跑的时候两种走法：

1. 跑 `gen_voices_all.py` 一次性生成（edge-tts，几十分钟）；
2. 什么都不跑，说话时现生成，第一次慢一点。

**授权**：两种生成方式各有说法，见 `docs\开源授权.md`。简单说——

- **edge-tts**：微软没授权把这些音频拿去商用或转卖；自己用没事，别随仓库发。
- **Ming 克隆**（已弃用，脚本和样本都删了）：模型是 MIT，但**参考音频**得自己有授权。试过，端侧太慢（每句十几秒），最后没用。
  `jimu-samples\` 里那些是第三方站点的试听样本，只当本地实验用，不要分发样本或克隆结果。

