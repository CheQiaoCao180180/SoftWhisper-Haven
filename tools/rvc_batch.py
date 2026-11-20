# 用贝壳 RVC 模型批量转换一个人设的语音。
# 用法：python rvc_batch.py 傲娇 zh
#
# 流程：算 key → 找到 voices 里的 mp3 → 备份到 voices-orig → RVC 转 wav → ffmpeg 转回 mp3 覆盖
# 模型只加载一次，之后每条半秒左右。
import hashlib
import os
import re
import shutil
import subprocess
import sys

ROOT = r"D:\大肥鱼插件"
VOICES = os.path.join(ROOT, "audio", "voices")
ORIG = os.path.join(ROOT, "audio", "voices-orig")
# 本机改（2026-11-19）：模型可以用环境变量换，默认还是贝壳。
MODEL = os.environ.get("RVC_MODEL") or r"E:\1404\Beike_RVC_48k\beike_48k.pth"
INDEX = os.environ.get("RVC_INDEX") or r"E:\1404\Beike_RVC_48k\beike.index"
# 本机加（2026-11-19）：变调（半音）。幼童用满穗 +4 更像小孩。
F0_KEY = int(os.environ.get("RVC_F0_KEY") or "0")
FFMPEG = r"E:\Live2D Cubism 4.1\tools\ffmpeg\ffmpeg.exe"
TMP = r"D:\软件缓存\rvc-tmp"

# 和 Personas.cs / gen_voices_all.py 一致：(名字, 音色, 语速, 音高, 音量)
PERSONAS = {
    "幼童": ("zh-CN-XiaoyiNeural", "-10%", "+26Hz", "+0%"),
    "傲娇": ("zh-CN-XiaoyiNeural", "+10%", "+5Hz", "+0%"),
    "病娇": ("zh-CN-XiaoxiaoNeural", "-13%", "-7Hz", "-8%"),
    "人妻": ("zh-CN-XiaoxiaoNeural", "-3%", "-1Hz", "+0%"),
    "甜憨": ("zh-CN-XiaoyiNeural", "-12%", "+9Hz", "+0%"),
    "清泠": ("zh-CN-XiaoxiaoNeural", "-15%", "+2Hz", "-6%"),
    "狡黠": ("zh-CN-XiaoyiNeural", "+6%", "+8Hz", "+0%"),
    "软恬": ("zh-CN-XiaoxiaoNeural", "-16%", "-4Hz", "-22%"),
}

# 日文音色（和 gen_voices_all.py 的 LOCALIZED 一致）
PERSONAS_JA = {
    "幼童": ("ja-JP-NanamiNeural", "-10%", "+20Hz"),
    "傲娇": ("ja-JP-NanamiNeural", "+8%", "+5Hz"),
    "病娇": ("ja-JP-NanamiNeural", "-12%", "-8Hz"),
    "人妻": ("ja-JP-NanamiNeural", "-3%", "+0Hz"),
    "甜憨": ("ja-JP-NanamiNeural", "-10%", "+12Hz"),
    "清泠": ("ja-JP-NanamiNeural", "-15%", "+2Hz"),
    "狡黠": ("ja-JP-NanamiNeural", "+5%", "+8Hz"),
    "软恬": ("ja-JP-NanamiNeural", "-16%", "-5Hz"),
}

CUE = re.compile(r"〔[^〕]*〕")


def clean(text):
    return CUE.sub("", text).replace("～", "~").replace("♪", "").replace("♬", "").strip()


def key(voice, rate, pitch, volume, text):
    return hashlib.sha1((voice + rate + pitch + volume + "|" + text).encode("utf-8")).hexdigest()[:16]


def collect(lang):
    base = os.path.join(ROOT, "lines", "zh" if lang == "zh" else lang)
    texts = []
    for name in ("touch.txt", "stroke.txt", "hover.txt", "greet.txt"):
        path = os.path.join(base, name)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line or line.startswith("//") or line.startswith("#") or line.startswith("@"):
                    continue
                parts = line.split("|")
                if len(parts) <= 1:
                    continue
                text = clean(parts[1])
                if len(text) >= 2 and re.search(r"[\w\u4e00-\u9fff\u3040-\u30ff]", text):
                    texts.append(text)
    return list(dict.fromkeys(texts))


def main():
    who = sys.argv[1] if len(sys.argv) > 1 else "傲娇"
    lang = sys.argv[2] if len(sys.argv) > 2 else "zh"

    if lang == "ja":
        voice, rate, pitch = PERSONAS_JA[who]
        volume = "+0%"
    else:
        voice, rate, pitch, volume = PERSONAS[who]

    texts = collect(lang)
    keys = [key(voice, rate, pitch, volume, t) for t in texts]
    existing = [k for k in keys if os.path.exists(os.path.join(VOICES, k + ".mp3"))]
    print(f"[{who}/{lang}] 台词 {len(texts)} 条，有语音 {len(existing)} 条", flush=True)

    os.makedirs(ORIG, exist_ok=True)
    os.makedirs(TMP, exist_ok=True)

    # 先备份（只备份一次，第二次跑不会覆盖掉最早的原声）
    backed = 0
    for k in existing:
        src = os.path.join(VOICES, k + ".mp3")
        dst = os.path.join(ORIG, k + ".mp3")
        if not os.path.exists(dst):
            shutil.copy2(src, dst)
            backed += 1
    print(f"  备份 {backed} 条到 voices-orig", flush=True)

    # 模型加载一次
    print("  加载 RVC…", flush=True)
    from rvc_python.infer import RVCInference
    import torch
    import numpy as np
    import soundfile as sf

    device = "cuda:0" if torch.cuda.is_available() else "cpu"
    rvc = RVCInference(device=device)
    rvc.load_model(MODEL, index_path=INDEX)
    rvc.set_params(f0method="rmvpe", f0up_key=F0_KEY, index_rate=0.35, filter_radius=3,
                   resample_sr=0, rms_mix_rate=0.75, protect=0.33)
    info = rvc.models[rvc.current_model]
    file_index = info.get("index", "")
    print("  模型就绪", flush=True)

    ok = fail = 0
    for n, k in enumerate(existing, 1):
        src = os.path.join(VOICES, k + ".mp3")
        wav = os.path.join(TMP, k + ".wav")
        try:
            result = rvc.vc.vc_single(
                sid=0, input_audio_path=src, f0_up_key=rvc.f0up_key, f0_method=rvc.f0method,
                file_index=file_index, index_rate=rvc.index_rate, filter_radius=rvc.filter_radius,
                resample_sr=rvc.resample_sr, rms_mix_rate=rvc.rms_mix_rate, protect=rvc.protect,
                f0_file="", file_index2="")
            if isinstance(result, tuple) and len(result) >= 2 and isinstance(result[1], tuple):
                audio, sr = result[1]
            elif isinstance(result, tuple) and len(result) >= 2 and not isinstance(result[0], str):
                audio, sr = result[0], result[1]
            else:
                audio, sr = result, rvc.vc.tgt_sr
            audio = np.asarray(audio)
            if audio.ndim == 1:
                audio = audio[:, None]
            if audio.dtype.kind in "iu":
                audio = audio.astype("float32") / 32768.0
            elif np.abs(audio).max() > 1.5:
                audio = audio / 32768.0
            sf.write(wav, audio, sr)

            # 转回 mp3，覆盖原文件（文件名不变，桌宠照样认）
            subprocess.run([FFMPEG, "-y", "-i", wav, "-codec:a", "libmp3lame", "-q:a", "3", src],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            ok += 1
        except Exception as error:
            fail += 1
            if fail <= 5:
                print(f"  失败 {k}: {error}", flush=True)
        finally:
            try:
                os.remove(wav)
            except Exception:
                pass
        if n % 50 == 0:
            print(f"  {n}/{len(existing)}  ok={ok} fail={fail}", flush=True)

    print(f"[{who}/{lang}] 完成 ok={ok} fail={fail}", flush=True)


if __name__ == "__main__":
    main()
