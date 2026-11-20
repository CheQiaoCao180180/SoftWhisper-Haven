# 重写版（2026-11-17）：按人设批量合成台词。
#
# 以前只支持一个音色、一个固定语速，四套人设靠桌宠现场合成凑。
# 现在十种人设各有音色/语速/音高，全预生成 —— 点她的时候不该等。
#
# 缓存 key 和桌宠保持一致：SHA1(voice + rate + pitch + "|" + text)[:16]
# 用法：python gen_voices_all.py
#       python gen_voices_all.py --only 毒舌 元气    只补这两个

import asyncio
import hashlib
import os
import re
import sys

import edge_tts

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "voices")

# 和 Personas.cs 对齐：(名字, 音色, 语速, 音高)
# 改这里之前先改 Personas.cs，两边必须一致
PERSONAS = [
    # (名字, 音色, 语速, 音高, 音量)  —— 必须和 Personas.cs 一字不差
    ("幼童", "zh-CN-XiaoyiNeural", "-10%", "+26Hz", "+0%"),
    ("傲娇", "zh-CN-XiaoyiNeural", "+10%", "+5Hz", "+0%"),
    ("病娇", "zh-CN-XiaoxiaoNeural", "-13%", "-7Hz", "-8%"),
    ("人妻", "zh-CN-XiaoxiaoNeural", "-3%", "-1Hz", "+0%"),
    ("甜憨", "zh-CN-XiaoyiNeural", "-12%", "+9Hz", "+0%"),
    ("清泠", "zh-CN-XiaoxiaoNeural", "-15%", "+2Hz", "-6%"),
    ("狡黠", "zh-CN-XiaoyiNeural", "+6%", "+8Hz", "+0%"),
    ("软恬", "zh-CN-XiaoxiaoNeural", "-16%", "-4Hz", "-22%"),
]

# 本机改（2026-11-17）：台词搬到 lines\zh 了，只有中文那套需要预生成语音
# 本机加（2026-11-18）：英文/日文的音色，和 Voice.cs 的 Localized() 一致。
# 各语言的女声少，靠音高和语速区分人设。
LOCALIZED = {
    "en": {
        "幼童": ("en-US-AnaNeural", "-10%", "+15Hz"),
        "傲娇": ("en-US-AriaNeural", "+8%", "+5Hz"),
        "病娇": ("en-US-AriaNeural", "-12%", "-8Hz"),
        "人妻": ("en-US-JennyNeural", "-3%", "+0Hz"),
        "甜憨": ("en-US-AnaNeural", "-12%", "+5Hz"),
        "清泠": ("en-US-JennyNeural", "-15%", "+2Hz"),
        "狡黠": ("en-US-AriaNeural", "+5%", "+8Hz"),
        "软恬": ("en-US-JennyNeural", "-16%", "-4Hz"),
    },
    "ja": {
        "幼童": ("ja-JP-NanamiNeural", "-10%", "+20Hz"),
        "傲娇": ("ja-JP-NanamiNeural", "+8%", "+5Hz"),
        "病娇": ("ja-JP-NanamiNeural", "-12%", "-8Hz"),
        "人妻": ("ja-JP-NanamiNeural", "-3%", "+0Hz"),
        "甜憨": ("ja-JP-NanamiNeural", "-10%", "+12Hz"),
        "清泠": ("ja-JP-NanamiNeural", "-15%", "+2Hz"),
        "狡黠": ("ja-JP-NanamiNeural", "+5%", "+8Hz"),
        "软恬": ("ja-JP-NanamiNeural", "-16%", "-5Hz"),
    },
}

SOURCES = [
    ("lines/zh/touch.txt", 1),
    ("lines/zh/stroke.txt", 1),
    ("lines/zh/hover.txt", 1),
    ("mutter.txt", 0),
    ("skits.txt", 0),
    ("lines/zh/greet.txt", 1),
    # 本机修（2026-11-19）：漏了 dizzy.txt——头晕那十句一直没生成过语音
    ("lines/zh/dizzy.txt", 1),
]

CUE = re.compile(r"〔[^〕]*〕")
LIMIT = 5


def clean(text: str) -> str:
    text = CUE.sub("", text)
    text = text.replace("～", "~").replace("♪", "").replace("♬", "")
    return text.strip()


def key(voice: str, rate: str, pitch: str, volume: str, text: str) -> str:
    # 必须和 Voice.cs 里的 Key(text, persona) 完全一致
    payload = voice + rate + pitch + volume + "|" + text
    return hashlib.sha1(payload.encode("utf-8")).hexdigest()[:16]


def collect():
    seen = {}
    for name, column in SOURCES:
        path = os.path.join(ROOT, name)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line or line.startswith("//") or line.startswith("#") or line.startswith("@"):
                    continue
                parts = line.split("|")
                if len(parts) <= column:
                    continue
                text = clean(parts[column])
                if len(text) < 2:
                    continue
                # 本机加（2026-11-18）：小剧场里有些「台词」是纯符号（……、♪～～♪），
                # 那是动作节奏不是话，合成不出来也没必要合成。
                if not re.search(r"[\w\u4e00-\u9fff]", text):
                    continue
                seen.setdefault(text, text)
    return list(seen.values())


async def one(sem, voice, rate, pitch, volume, k, text):
    path = os.path.join(OUT, k + ".mp3")
    if os.path.exists(path) and os.path.getsize(path) > 512:
        return "skip"
    async with sem:
        for attempt in range(3):
            try:
                await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch, volume=volume).save(path)
                return "ok"
            except Exception as error:
                if attempt == 2:
                    return "fail"
                await asyncio.sleep(1.5)


async def main():
    os.makedirs(OUT, exist_ok=True)
    want = None
    if "--only" in sys.argv:
        want = sys.argv[sys.argv.index("--only") + 1:]

    # 本机加（2026-11-18）：--lang en|ja 时换音色、换台词文件
    lang = "zh"
    if "--lang" in sys.argv:
        lang = sys.argv[sys.argv.index("--lang") + 1]

    if lang in LOCALIZED:
        global SOURCES
        SOURCES = [(n.replace("lines/zh/", f"lines/{lang}/"), c)
                   for n, c in SOURCES if n.startswith("lines/zh/")]

    lines = collect()
    print(f"[{lang}] 台词 {len(lines)} 条")

    sem = asyncio.Semaphore(LIMIT)
    for name, voice, rate, pitch, volume in PERSONAS:
        if want and name not in want:
            continue
        if lang in LOCALIZED:
            voice, rate, pitch = LOCALIZED[lang][name]

        jobs = [(key(voice, rate, pitch, volume, text), text) for text in lines]
        todo = [(k, t) for k, t in jobs if not (os.path.exists(os.path.join(OUT, k + ".mp3")) and os.path.getsize(os.path.join(OUT, k + ".mp3")) > 512)]
        print(f"[{name}] {voice} {rate}/{pitch}/{volume} —— 缺 {len(todo)} 条")

        stats = {"ok": 0, "skip": 0, "fail": 0}
        batch = 25
        for start in range(0, len(todo), batch):
            chunk = todo[start:start + batch]
            results = await asyncio.gather(*[one(sem, voice, rate, pitch, volume, k, t) for k, t in chunk])
            for r in results:
                stats[r] += 1
            if (start // batch) % 8 == 0:
                done = min(start + batch, len(todo))
                print(f"  {done}/{len(todo)}  ok={stats['ok']} fail={stats['fail']}", flush=True)
        print(f"[{name}] 完成 ok={stats['ok']} fail={stats['fail']}")


if __name__ == "__main__":
    asyncio.run(main())
