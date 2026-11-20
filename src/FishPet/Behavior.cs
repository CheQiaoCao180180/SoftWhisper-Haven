using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FishPet;

// A face made of the model's switches and face parameters, held for a few seconds, and the body moving with it.
// Motion is what her body does meanwhile: a parameter or a View* track of her whole figure, then seconds:value
// keys that start and end at 0; EyeOpen closes her eyes as far as it goes below 0. Pushed marks a movement that
// turns with a side: away from where she was touched, or either way at random when she moves by herself.
internal sealed record Mood(string[] Expressions, (string Id, double Value)[] Parameters, double Seconds = 3,
    (string Id, string Keys)[]? Motion = null, (string On, string Off)? Gesture = null, bool Pushed = false);

// Her reflexes on the Live2D display: what she does by herself when clicked, dragged, left alone or busy.
// Built only for this display, nothing here reads the clip rules. It gives the renderer commands and
// knows nothing of windows, so all of it is checked in FishPet.Tests.
internal sealed class Behavior
{
    private const string Glasses = "黑框眼镜", Monocle = "单片眼镜";
    private static readonly TimeSpan ClickSpan = TimeSpan.FromSeconds(2.5);
    private const int ClicksForBasin = 5;

    internal static readonly IReadOnlyDictionary<string, Mood> Moods = new Dictionary<string, Mood>
    {
        ["问候"] = new([], [("EyeSmile", 1), ("ParamMouthForm", 1)], 4.4, [("FaceAngleZ", "0:0 .5:7 1:5 1.5:0 3.4:0 3.8:7 4.4:0"), ("LeanInput", "0:0 1.1:0 1.7:-11 2.6:-11 3.3:0"), ("FaceAngleY", "0:0 1.1:0 1.7:-19 2.2:-17 2.6:-19 3.3:0"), ("ViewSquash", "0:0 1.1:0 1.7:.04 2.6:.04 3.3:0"), ("EyeOpen", "0:0 1.2:0 1.6:-1 2.7:-1 3.1:0 4.4:0")]), // 抚胸交给 Body（PetWindow 出场时 Apply("抚胸")）：手留到后面的话说完，不在 4.4 秒时自己放下（本机改，2026-11-06）
        ["告别"] = new([], [("EyeSmile", 1), ("ParamMouthForm", 1)], 3, [("FaceAngleZ", "0:0 .4:6 .8:0"), ("LeanInput", "0:0 .7:0 1.3:-12 2.6:-12 3:0"), ("FaceAngleY", "0:0 .7:0 1.3:-19 2.6:-19 3:0"), ("ViewSquash", "0:0 .7:0 1.3:.04 2.6:.04 3:0"), ("EyeOpen", "0:0 .8:0 1.2:-1 2.6:-1 3:0")], ("默认手切举手", "举手切默认手")),
        ["落地"] = new(["圈圈眼"], [("BrowInnerUp", .6), ("ParamMouthOpenY", .2)], 1.25, [("ViewSquash", "0:0 .1:.06 .3:-.02 .5:.015 .75:0"), ("FaceAngleY", "0:0 .1:-10 .75:0"), ("FaceAngleX", "0:0 .5:0 .65:-8 .8:8 .95:-6 1.1:4 1.3:0"), ("EyeOpen", "0:0 1.15:0 1.25:-1 1.4:0")]),
        ["害羞"] = new(["脸红"], [("FaceAngleY", -12), ("FaceAngleX", 10), ("FaceAngleZ", 9), ("ParamEyeBallX", -.6), ("ParamEyeBallY", -.5), ("MouthPressLipOpen", .6)], 3, [("FaceAngleZ", "0:0 .5:2 1:-1 1.6:2 2.2:-1 3:0"), ("ViewSquash", "0:0 .4:.02 2.4:.02 3:0")]),
        // 害羞又往前凑（本机加，2026-11-15）：点脚时用，脸红低头同时整个人往前倾一点
        ["害羞前倾"] = new(["脸红"], [("FaceAngleY", -12), ("FaceAngleX", 10), ("FaceAngleZ", 9), ("ParamEyeBallX", -.6), ("ParamEyeBallY", -.5), ("MouthPressLipOpen", .6)], 3.6, [("FaceAngleZ", "0:0 .5:2 1:-1 1.6:2 2.2:-1 3:0"), ("LeanInput", "0:0 .45:-11 2.9:-10 3.6:0"), ("ViewSquash", "0:0 .4:.02 2.4:.02 3.6:0")]),
        ["生气"] = new(["黑脸"], [("CheeckPuff", 1), ("ParamBrowLY", -.8), ("ParamBrowRY", -.8), ("ParamBrowLAngle", -1), ("ParamBrowRAngle", -1), ("FaceAngleX", -14), ("FaceAngleY", -6)], 3, [("FaceAngleX", "0:0 .15:-8 .3:6 .45:-5 .6:3 .8:0"), ("ViewSquash", "0:0 .08:.04 .25:0 1:0"), ("LeanInput", "0:0 .2:8 1.6:6 2.2:0")]),
        ["得意"] = new([], [("REyeSquint", .8), ("LEyeSquint", .8), ("ParamMouthForm", 1), ("MouthX", .5), ("FaceAngleY", 10), ("FaceAngleZ", 12)], 3, [("FaceAngleX", "0:0 .5:3 1.2:-2 1.9:3 3:0"), ("LeanInput", "0:0 .35:6 2.4:5 3:0")]),
        ["委屈"] = new(["流泪"], [("BrowInnerUp", 1), ("Mouthpucker", 1), ("FaceAngleY", -10), ("FaceAngleZ", -6)], 3, [("ViewSquash", "0:0 .4:.03 2.4:.025 3:0"), ("FaceAngleX", "0:0 .4:-3 .9:2 1.4:-3 1.9:2 3:0")]),
        ["开心"] = new([], [("EyeSmile", 1), ("EyeOpen", 0), ("ParamMouthForm", 1), ("ParamMouthOpenY", .5), ("FaceAngleY", 6)], 3, [("FaceAngleZ", "0:0 .3:-10 .7:10 1.1:-8 1.5:6 2:0"), ("FaceAngleY", "0:0 .3:6 .7:2 1.1:6 1.5:2 2:0")], ("默认手切举手", "举手切默认手")),
        ["心虚"] = new(["流冷汗"], [("ParamEyeBallX", .8), ("ParamEyeBallY", .3), ("MouthX", .6), ("ParamMouthForm", -.5), ("FaceAngleX", 14), ("FaceAngleY", -8)], 3, [("FaceAngleZ", "0:0 .3:-4 .6:2 .9:-3 1.2:2 1.5:-2 2:0"), ("ViewSquash", "0:0 .3:.03 2:.02 2.6:0"), ("LeanInput", "0:0 .3:6 2:5 2.6:0")], Pushed: true),
        ["震惊"] = new(["圈圈眼"], [("ParamMouthOpenY", .8), ("JawOpen", .5), ("FaceAngleX", -16), ("FaceAngleY", -8), ("ParamEyeBallX", -.6), ("ParamEyeBallY", -.5)], 3, [("ViewSquash", "0:0 .06:.06 .18:-.05 .4:.05 .6:0"), ("ViewY", "0:0 .06:0 .2:.06 .4:0"), ("FaceAngleY", "0:0 .2:16 .5:4 1.4:0")], Pushed: true),
        ["思考"] = new([], [("ParamEyeBallY", .7), ("ParamEyeBallX", .4), ("FaceAngleZ", 12), ("FaceAngleY", 6), ("BrowInnerUp", .6)], 3.4, [("FaceAngleX", "0:0 .6:4 1.4:2 2.2:5 3:0")], ("竖手指切出来动画", "竖手指切回默认")),
        ["摸头"] = new(["脸红"], [("EyeSmile", .9), ("ParamMouthForm", 1)], 3, [("FaceAngleY", "0:0 .25:-18 .7:-12 1.1:-19 1.5:-12 1.9:-16 2.4:0"), ("FaceAngleZ", "0:0 .5:14 1.1:-14 1.7:14 2.4:0"), ("ViewRoll", "0:0 .5:2 1.1:-2 1.7:2 2.4:0"), ("ViewSquash", "0:0 .25:.05 1.9:.04 2.4:0")]),
        ["戳脸"] = new(["＞＜"], [("CheeckPuff", .8), ("FaceAngleX", 14), ("FaceAngleZ", 8)], 3, [("FaceAngleX", "0:0 .08:12 .3:6 1.2:0"), ("FaceAngleY", "0:0 .08:-6 .4:-2 1.2:0"), ("ViewSquash", "0:0 .06:.04 .25:0 1.2:0")], Pushed: true),
        ["挨打"] = new(["钢盆", "流冷汗"], [("ParamMouthForm", -.6), ("FaceAngleY", -12)], 4, [("ViewSquash", "0:0 .09:.11 .3:.02 .5:.05 .75:.01 1.4:0"), ("FaceAngleZ", "0:0 .15:10 .4:-8 .65:6 .9:-4 1.2:2 1.6:0"), ("FaceAngleY", "0:0 .1:-12 .5:-4 1.6:0")]),
        // 彩蛋（本机加，2026-11-16）：同一个地方被点第七下，她就不装了
        ["彩蛋"] = new(["脸红", "爱心眼"], [("EyeSmile", .6), ("ParamMouthForm", 1), ("FaceAngleY", 8), ("BrowInnerUp", .7)], 4,
            [("LeanInput", "0:0 .5:-10 3:-8 4:0"), ("FaceAngleZ", "0:0 .6:-6 1.6:6 2.6:-4 3.6:0"), ("ViewSquash", "0:0 .4:.04 1:.02 4:0")], ("默认手切举手", "举手切默认手")),        // 被踩／被碰脚（本机加，2026-11-15）：脚上原来没有专属反应，点脚跟点身子一个样
        ["被踩"] = new(["脸红"], [("EyeSmile", .2), ("BrowInnerUp", .8), ("MouthPressLipOpen", .5), ("FaceAngleY", -8)], 3,
            [("ViewSquash", "0:0 .08:.06 .3:.02 .6:.03 1.2:0"), ("FaceAngleZ", "0:0 .12:8 .45:-6 .8:4 1.3:0"), ("LEGAngleZ2", "0:0 .06:-5 .22:4 .4:-3 .65:2 1.2:0")], Pushed: true),
        // 跺脚（本机加，2026-11-15）：腿左右蹬几下，像在原地生气
        ["跺脚"] = new(["鼓腮"], [("FaceAngleY", -6), ("ParamMouthForm", -.5)], 2.6,
            [("LEGAngleZ2", "0:0 .08:-9 .2:9 .32:-9 .44:9 .58:-4 .75:0"), ("ViewSquash", "0:0 .08:.035 .16:0 .28:.035 .4:0 .58:0"), ("HipAngleZ", "0:0 .1:-2.5 .3:2.5 .5:-1.5 .75:0")], Pushed: true),        // 亲吻（本机加，2026-11-15）：红着脸往前凑，像悄悄亲一下
        ["亲吻"] = new(["脸红"], [("EyeSmile", .25), ("ParamMouthForm", .8), ("BrowInnerUp", .6), ("FaceAngleY", 9), ("FaceAngleZ", -4)], 3.5,
            [("LeanInput", "0:0 .4:-14 3:-12 3.5:0"), ("ViewSquash", "0:0 .4:.03 3:.03 3.5:0"), ("FaceAngleZ", "0:0 .5:-5 1.2:-2 1.9:-5 2.6:-2 3.5:0")]),
        // 满足（本机加，2026-11-15）：笑眯了眼，轻轻晃
        ["满足"] = new([], [("EyeSmile", 1), ("EyeOpen", 0), ("ParamMouthForm", .9), ("FaceAngleY", 6), ("BrowInnerUp", .3)], 4,
            [("FaceAngleZ", "0:0 .6:-4 1.4:4 2.2:-3 3:2 4:0")], ("默认手切举手", "举手切默认手")),
        // 爱心（本机加，2026-11-15）：点手／牵着的时候偶尔冒出来的爱心眼，红着脸按着胸口
        ["爱心"] = new(["爱心眼", "脸红"], [("EyeSmile", .6), ("ParamMouthForm", .8), ("BrowInnerUp", .5), ("FaceAngleY", -6), ("FaceAngleZ", -5)], 4,
            [("LeanInput", "0:0 .5:-6 3.4:-5 4:0"), ("FaceAngleZ", "0:0 .6:-7 1.2:-4 1.8:-7 2.4:-4 4:0")], ("默认手切举手", "举手切默认手")),
        ["护裙"] = new(["黑脸", "脸红"], [("CheeckPuff", .8), ("ParamBrowLY", -.6), ("ParamBrowRY", -.6), ("FaceAngleX", -16), ("FaceAngleY", -8), ("ParamEyeBallX", .5)], 3, [("FaceAngleZ", "0:0 .2:-12 .5:-7 1.6:-8 2.8:0"), ("ViewSquash", "0:0 .12:.05 .35:.01 2.8:0"), ("LeanInput", "0:0 .3:5 2.2:4 2.8:0")], ("默认手切举手", "举手切默认手")),
        // 病娇（本机加，2026-11-15）：笑着往前凑、眼睛半睁、头微微歪，手抬起来 —— 语气甜但有点危险，只做效果不吓人
        ["病娇"] = new(["屑屑眼"], [("EyeSmile", .55), ("ParamMouthForm", .8), ("BrowInnerUp", .3), ("FaceAngleZ", 9), ("FaceAngleY", 6), ("ParamEyeBallX", .3)], 4.2,
            [("FaceAngleZ", "0:0 .5:11 1.2:8 1.9:11 2.6:8 3.3:10 4.2:0"), ("LeanInput", "0:0 .5:-9 3.4:-8 4.2:0"), ("FaceAngleY", "0:0 .5:7 3.4:6 4.2:0"), ("ViewSquash", "0:0 .4:.02 3.6:.02 4.2:0")]),
        // 亲密度满档（第 5 级）时戳裙子：不凶了，脸红、眼冒爱心，低着头扭扭捏捏地左右晃，手按在胸口（本机加，2026-11-08）
        ["娇羞"] = new(["脸红"], [("EyeSmile", .7), ("MouthPressLipOpen", .5), ("ParamMouthForm", .6), ("FaceAngleY", -10), ("ParamEyeBallX", -.5), ("ParamEyeBallY", -.4), ("BrowInnerUp", .7)], 4.2,
            [("FaceAngleZ", "0:0 .4:-9 .9:6 1.4:-8 1.9:5 2.4:-7 2.9:4 3.5:-3 4.2:0"), ("FaceAngleX", "0:0 .4:-6 .9:4 1.4:-6 1.9:4 2.4:-5 2.9:3 4.2:0"), ("LeanInput", "0:0 .3:-4 .9:3 1.4:-4 1.9:3 2.4:-3 3:2 4.2:0"), ("ViewSquash", "0:0 .3:.03 3.6:.025 4.2:0")]),
        // 满档戳裙子的另外两种（和“娇羞”三选一，概率平分）：红着脸往前凑；大大地往前倾、手按在胸口、笑眼（和 Body 的「笑眼」同一组参数）（用户定的，2026-11-08）
        ["娇羞前倾"] = new(["脸红"], [("EyeSmile", .6), ("MouthPressLipOpen", .4), ("ParamMouthForm", .6), ("BrowInnerUp", .5), ("FaceAngleZ", 7)], 4,
            [("LeanInput", "0:0 .45:-14 3.4:-13 4:0"), ("FaceAngleY", "0:0 .45:-7 3.4:-6 4:0"), ("FaceAngleZ", "0:0 .8:3 1.6:-2 2.4:3 3.2:-1 4:0"), ("ViewSquash", "0:0 .45:.02 3.4:.02 4:0")]),
        ["娇羞抚胸"] = new(["脸红"], [("EyeOpen", .45), ("EyeSmile", 1), ("ParamMouthForm", .8), ("BrowInnerUp", .6), ("FaceAngleZ", -6)], 4.2,
            [("LeanInput", "0:0 .5:-21 3.5:-20 4.2:0"), ("FaceAngleY", "0:0 .5:-10 3.5:-9 4.2:0"), ("ViewSquash", "0:0 .5:.03 3.5:.03 4.2:0"), ("FaceAngleZ", "0:0 1:-2 2:2 3:-2 4.2:0")], ("默认手切举手", "举手切默认手")),
        ["张望"] = new([], [], 3.4, [("FaceAngleX", "0:0 .5:24 .8:22 1.1:25 1.3:24 1.9:-22 2.2:-20 2.5:-23 2.7:-22 3.3:0"), ("FaceAngleY", "0:0 .5:4 1.3:3 1.9:5 2.7:3 3.3:0")]),
        ["哼歌"] = new([], [("EyeSmile", 1), ("EyeOpen", 0), ("ParamMouthForm", 1)], 3.4, [("FaceAngleZ", "0:0 .6:14 1.4:-14 2.2:14 3.2:0"), ("FaceAngleX", "0:0 .6:4 1.4:-4 2.2:4 3.2:0"), ("FaceAngleY", "0:0 .3:3 .6:0 1:3 1.4:0 1.8:3 2.2:0 3.2:0")]),
        ["打哈欠"] = new([], [], 3.6, [("EyeOpen", "0:0 .5:-.5 .9:-1 2.3:-1 2.7:-.45 3.1:-.4 3.6:0"), ("ParamMouthOpenY", "0:0 .5:.4 1:1 2.1:1 2.6:0"), ("JawOpen", "0:0 .5:.3 1:1 2.1:1 2.6:0"), ("BrowInnerUp", "0:0 .8:.9 2.1:.9 2.7:0"), ("FaceAngleY", "0:0 .9:12 2.1:14 2.8:-3 3.6:0"), ("FaceAngleZ", "0:0 .9:7 2.1:9 3.2:0"), ("LeanInput", "0:0 .9:7 2.1:8 3:0"), ("ViewSquash", "0:0 .9:-.025 2.1:-.03 2.6:.015 3.1:0")]),
        ["打瞌睡"] = new([], [], 5.2, [("EyeOpen", "0:0 .8:-.45 1.6:-.7 2.4:-.9 3:-1 3.15:0 3.9:0 4:-1 4.1:0 4.25:-1 4.35:0 5.2:0"), ("FaceAngleY", "0:0 1:-4 2:-9 2.8:-15 3:-17 3.2:5 3.6:0 5.2:0"), ("FaceAngleZ", "0:0 1.5:5 3:9 3.25:0 5.2:0"), ("ViewSquash", "0:0 1.5:.01 3:.025 3.15:-.02 3.4:.01 3.7:0"), ("ViewY", "0:0 3.05:0 3.2:.02 3.45:0"), ("BrowInnerUp", "0:0 3.05:0 3.25:.9 4.3:.6 5.2:0"), ("FaceAngleX", "0:0 3.4:0 3.8:12 4.4:-10 5:0")], Pushed: true),
        ["歪头"] = new([], [("FaceAngleZ", 18), ("FaceAngleX", 5), ("BrowInnerUp", .6), ("ParamBrowLY", .4), ("ParamBrowRY", .4), ("ParamEyeBallY", .2)], 3.2, [("FaceAngleZ", "0:0 .25:4 .5:0"), ("EyeOpen", "0:0 1.1:0 1.2:-1 1.32:0 1.48:-1 1.6:0 3.2:0")], Pushed: true),
        ["发呆"] = new([], [], 4.6, [("EyeOpen", "0:0 .9:-.45 3.1:-.5 3.25:-1 3.4:0 4.6:0"), ("ParamEyeBallY", "0:0 .9:.45 3.1:.45 3.4:0"), ("ParamMouthOpenY", "0:0 1:.3 3.1:.3 3.3:0"), ("JawOpen", "0:0 1:.18 3.1:.18 3.3:0"), ("FaceAngleY", "0:0 1:-3 3.1:-4 3.35:3 3.8:0"), ("FaceAngleX", "0:0 3.2:0 3.35:-7 3.55:6 3.75:-3 4:0"), ("ViewSquash", "0:0 1:.015 3.1:.015 3.35:0")]),
        ["叹气"] = new([], [], 3.6, [("LeanInput", "0:0 .8:5 1.8:-7 2.8:-6 3.5:0"), ("FaceAngleY", "0:0 .8:7 1.8:-9 2.8:-8 3.5:0"), ("ViewSquash", "0:0 .8:-.02 1.2:-.02 1.9:.03 2.8:.025 3.5:0"), ("EyeOpen", "0:0 .8:-.2 1.8:-.6 2.8:-.55 3.5:0"), ("ParamMouthOpenY", "0:0 1.1:0 1.4:.3 2.2:.12 2.6:0"), ("MouthFunnel", "0:0 1.1:0 1.4:.9 2.2:.6 2.6:0"), ("BrowInnerUp", "0:0 1:.6 2.8:.6 3.5:0")]),
        // 鼠标在她身上停了约 2 秒时的小反应（本机加，2026-11-10）：幅度都很轻，对应 hover.txt 的台词。
        // 摸头结束（右键松开）后按摸了多久分三档：脸都会红一下；越摸得久越舍不得、越腻歪（本机加，2026-11-10，对应 stroke.txt 的「抚摸头1/2/3」）。
        ["摸后失落"] = new(["脸红"], [("BrowInnerUp", .7), ("MouthPressLipOpen", .3), ("ParamMouthForm", -.1), ("ParamEyeBallY", -.4), ("FaceAngleY", -5), ("FaceAngleZ", 4)], 3.4,
            [("FaceAngleZ", "0:0 .5:3 1.3:-2 2.1:3 3.4:0"), ("ViewSquash", "0:0 .3:.01 2.8:.01 3.4:0")]),
        ["摸后不舍"] = new(["脸红"], [("EyeSmile", .45), ("BrowInnerUp", .9), ("MouthPressLipOpen", .5), ("ParamMouthForm", .2), ("ParamEyeBallY", -.45), ("FaceAngleY", -8), ("FaceAngleZ", 7)], 4,
            [("LeanInput", "0:0 .5:-9 3.3:-8 4:0"), ("FaceAngleZ", "0:0 .7:3 1.5:-3 2.3:3 3.1:-2 4:0"), ("ViewSquash", "0:0 .4:.02 3.4:.02 4:0")]),
        ["摸后腻歪"] = new(["脸红"], [("EyeSmile", .6), ("ParamMouthForm", 1), ("MouthPressLipOpen", .4), ("BrowInnerUp", .6), ("FaceAngleY", -6), ("FaceAngleZ", 9)], 4.8,
            [("LeanInput", "0:0 .5:-15 3.9:-14 4.8:0"), ("FaceAngleZ", "0:0 .8:3 1.6:-3 2.4:3 3.2:-3 4:2 4.8:0"), ("FaceAngleX", "0:0 .8:-4 1.8:4 2.8:-4 3.8:3 4.8:0"), ("ViewSquash", "0:0 .5:.03 4.1:.03 4.8:0")], ("默认手切举手", "举手切默认手")),
        // 摸头彩蛋（本机加，2026-11-11）：有效抚摸满 20 秒、融化后松手。脸红 + 手抬到胸口 + 快眨两下，6 秒。
        // 前倾：松手时按住 -17，表情里用动画加 0→+1 慢慢回到 -16，结束再交回给 clear 淡出，不会和旧值叠起来弹一下。
        ["摸后融化"] = new(["脸红"], [("EyeSmile", .55), ("ParamMouthForm", .9), ("MouthPressLipOpen", .35), ("BrowInnerUp", .5), ("FaceAngleY", -4), ("FaceAngleZ", 4), ("LeanInput", -17)], 6,
            [("LeanInput", "0:0 .8:1 4.8:1 6:0"), ("FaceAngleZ", "0:0 .8:5 2:-2 3.6:3 6:0"), ("FaceAngleX", "0:0 .7:-3 2.2:2 3.8:-2 6:0"),
             ("EyeOpen", "0:0 .2:-1 .28:-.2 .45:0 .95:-1 1.03:-.2 1.2:0 6:0"), ("ViewSquash", "0:0 .6:.02 5.2:.02 6:0")], ("默认手切举手", "举手切默认手")),
        ["悬停头"] = new([], [("FaceAngleZ", 11), ("FaceAngleX", -3), ("ParamEyeBallY", .3), ("BrowInnerUp", .4), ("EyeSmile", .35)], 2.8, [("FaceAngleZ", "0:0 .3:3 .8:-2 1.4:2 2.8:0")], Pushed: true),
        ["悬停脸"] = new(["脸红"], [("ParamMouthForm", .5), ("ParamEyeBallX", .4), ("ParamEyeBallY", -.2), ("FaceAngleY", -4)], 2.6, [("EyeOpen", "0:0 .15:-1 .3:0 1.1:0 1.25:-1 1.4:0 2.6:0"), ("ViewSquash", "0:0 .3:.012 2.2:.01 2.6:0")], Pushed: true),
        ["悬停手"] = new([], [("EyeSmile", .8), ("ParamMouthForm", 1), ("FaceAngleY", 4), ("FaceAngleZ", -4)], 2.8, [("FaceAngleZ", "0:0 .4:-5 1:-2 1.6:-5 2.8:0")], ("默认手切举手", "举手切默认手")),
        ["悬停裙"] = new([], [("ParamEyeBallX", .7), ("ParamEyeBallY", -.3), ("ParamMouthForm", -.3), ("ParamBrowLY", -.3), ("ParamBrowRY", -.3), ("FaceAngleX", -8)], 2.6, [("ViewSquash", "0:0 .12:.025 .5:.012 2.2:.01 2.6:0")], Pushed: true),
        ["悬停尾"] = new(["流冷汗"], [("ParamEyeBallX", .7), ("ParamEyeBallY", -.3), ("ParamMouthForm", -.4), ("FaceAngleX", -10), ("BrowInnerUp", .4)], 2.6, [("ViewSquash", "0:0 .08:.03 .4:.01 2.2:.01 2.6:0"), ("FaceAngleY", "0:0 .1:-5 .5:0 2.6:0")], Pushed: true),
        ["悬停身"] = new([], [("FaceAngleZ", 9), ("ParamEyeBallY", .3), ("BrowInnerUp", .3)], 2.6, [("FaceAngleX", "0:0 .5:3 1.1:-2 1.8:2 2.6:0")], Pushed: true),
        // 本机加（2026-11-16）：鼠标停在腿上——收一点点、脸偏过去，不好意思但没躲
        ["悬停腿"] = new(["脸红"], [("ParamEyeBallX", .5), ("ParamEyeBallY", -.4), ("ParamMouthForm", -.25), ("FaceAngleX", -6), ("FaceAngleZ", 4), ("BrowInnerUp", .35)], 2.6, [("ViewSquash", "0:0 .1:.022 .5:.01 2.2:.008 2.6:0"), ("FaceAngleY", "0:0 .15:-4 .6:0 2.6:0")], Pushed: true),
        ["玩狗尾草"] = new([], [("ParamEyeBallX", -.7), ("ParamEyeBallY", -.3), ("EyeOpen", .75), ("EyeSmile", 1), ("ParamMouthForm", 1)], 5.4, [("FaceAngleX", "0:0 .5:0 1.2:-10 4.7:-10 5.4:0"), ("FaceAngleZ", "0:0 .5:0 1.2:8 1.9:3 2.6:9 3.3:3 4:9 4.7:6 5.4:0"), ("FaceAngleY", "0:0 .5:0 1.2:-5 4.7:-5 5.4:0")], ("狗尾草切换出来", "狗尾草切回默认手")),
    };
    // What she does by herself when left alone. A hand only comes up as part of one of these: a gesture on
    // its own for no reason looked odd (the owner, 2026-11-02). Late at night she gets sleepy.
    // 本机加（2026-11-18）：原来 11 个，加 5 个适合一个人待着时做的。
    // 挑的都是不依赖「被摸」语境的：满足、开心、委屈、害羞前倾、娇羞。
    private static readonly string[] IdleMoods =
    [
        "张望", "哼歌", "害羞", "得意", "思考", "打哈欠", "打瞌睡", "歪头", "发呆", "叹气", "玩狗尾草",
        "满足", "开心", "委屈", "害羞前倾", "娇羞",
    ];
    private static readonly HashSet<string> Sleepy = ["打哈欠", "打瞌睡"];
    internal static int IdleWeight(string name, int hour) => Sleepy.Contains(name) && hour is >= 23 or < 6 ? 4 : 1;
    // 拎着时：嘴张成“啊”、眉毛抬起、圈圈眼；晃动由 PetWindow.Touch 实时驱动。落地见 DragEnd。
    private static readonly (string Id, double Value)[] Hanging = [("ParamMouthOpenY", .35), ("JawOpen", .2), ("MouthFunnel", .3), ("BrowInnerUp", .8)];

    private readonly Action<string> send;
    private readonly Func<IReadOnlyCollection<string>> worn;
    private readonly Random random;
    private readonly HashSet<string> activities = [];
    private readonly List<DateTime> clicks = [];
    private Mood? mood;
    private DateTime moodUntil, nextIdle;
    private string? lastIdle;
    private bool dragging, wearingGlasses;
    private DateTime motionUntil;
    private (string On, string Off)? moodGesture;
    internal string? Showing { get; private set; }
    // Her own body language (Body) under the reflexes: a hand she has up stays, and what a
    // reflex held and let go of is hers again once it ends.
    // 用户正按着右键抚摸、或她头晕着：待机的抽签和小动作都别插进来（本机加，2026-11-10）。
    internal bool Touching { get; set; }
    // 把正演着的小反应收掉（抚摸开始、头晕开始时用）。
    internal void Interrupt() => EndMood();
    internal static string HoverMood(string zone) => zone switch
    {
        "head" => "悬停头", "face" => "悬停脸", "hand" => "悬停手", "skirt" => "悬停裙", "chest" => "悬停身", "foot" => "悬停身", "leg" => "悬停腿", "tail" => "悬停尾", _ => "悬停身",
    };
    // 头部范围（占窗口的比例）：中心 (X, Y)、半宽 Rx、半高 Ry。右键摸头时那只手按它来倾斜，鼠标在这个椭圆里就算摸头。
    internal static (double X, double Y, double Rx, double Ry) HeadBox(string shape) => shape switch
    {
        "chibi" => (.5, .31, .26, .22),
        "full" => (.5, .19, .12, .085),
        _ => (.5, .225, .19, .095),
    };
    internal Func<bool>? HandBusy { get; init; }
    internal Action? Ended { get; init; }
    // 每次待机小动作开始时，把动作名交出去（PetWindow 用来触发自言自语）。
    internal Action<string>? IdleStarted { get; set; }
    // 用户写好的固定嘀咕文本（与待机小动作、小剧场三类按 35:35:30 分概率，类内按条件加权）。
    internal string[] TextIdleMoods { get; set; } = [];
    internal Action<string>? TextIdle { get; set; }
    // 待机小剧场（skits.txt）：几句话加动作、临时道具组成的一小段，和待机小动作、固定嘀咕三类按 35:35:30 分概率（见 Tick）。
    internal string[] SkitIdleMoods { get; set; } = [];
    internal Action<string>? SkitIdle { get; set; }
    // 她现在是不是因为在读、看、写而戴着眼镜（和主人用 dress 戴的不同）。
    internal bool GlassesOnForWork => wearingGlasses;

    internal Behavior(Action<string> send, Func<IReadOnlyCollection<string>> worn, Random random)
    {
        this.send = send; this.worn = worn; this.random = random;
    }

    // A new renderer knows nothing of what the one before showed.
    internal void Resume(DateTime now, int intervalSeconds)
    {
        mood = null; Showing = null; moodGesture = null; dragging = false; wearingGlasses = false; motionUntil = default;
        nextIdle = now.AddSeconds(intervalSeconds);
        UpdateGlasses();
    }

    // side: which way she was pushed, 1 to the viewer's right. Clicked again in the middle of a movement the
    // face changes at once, but a second movement on top of the first would throw her about.
    internal void Show(string name, DateTime now, int side = 1, bool force = false)
    {
        EndMood();
        var next = Moods[name];
        foreach (var expression in next.Expressions) send($"exp {expression} 1");
        foreach (var (id, value) in next.Parameters)
            send(FormattableString.Invariant($"set {id} {(next.Pushed && side < 0 && Sided.Contains(id) ? 0 - value : value)}"));
        mood = next; Showing = name; moodUntil = now.AddSeconds(next.Seconds);
        if (next.Motion == null || (now < motionUntil && !force)) return;
        // Only what turns with the side flips: squashing, leaning, nodding and her eyelids stay as they are.
        foreach (var (id, keys) in next.Motion) send($"anim {id} {(next.Pushed && side < 0 && Sided.Contains(id) ? Mirrored(keys) : keys)}");
        motionUntil = now.AddSeconds(next.Motion.Max(track => Seconds(track.Keys)) * .6);
        if (next.Gesture is { } hand && HandBusy?.Invoke() != true) { moodGesture = hand; send($"motion {hand.On}"); }
    }

    // What turns with the side she was touched on, held as well as moved.
    private static readonly HashSet<string> Sided = ["FaceAngleX", "FaceAngleZ", "ParamEyeBallX", "MouthX"];

    private static double Seconds(string keys) => double.Parse(keys[(keys.LastIndexOf(' ') + 1)..].Split(':')[0], CultureInfo.InvariantCulture);
    private static string Mirrored(string keys) => string.Join(' ', keys.Split(' ').Select(key =>
    {
        var parts = key.Split(':');
        return FormattableString.Invariant($"{parts[0]}:{0 - double.Parse(parts[1], CultureInfo.InvariantCulture)}");
    }));

    private void EndMood()
    {
        if (mood == null) return;
        var keep = worn();
        // What she wears by the owner's choice stays on when a mood that also used it ends.
        foreach (var expression in mood.Expressions) if (!keep.Contains(expression)) send($"exp {expression} 0");
        foreach (var (id, _) in mood.Parameters) send($"clear {id}");
        if (moodGesture is { } hand) { send($"motion {hand.Off}"); moodGesture = null; }
        mood = null; Showing = null;
        Ended?.Invoke();
    }

    // What she wears changes what is under the cursor: with her tail put away the same spot holds only
    // the ends of her sash, and a long skirt covers her legs.
    internal static string Zone(string shape, double x, double y, IReadOnlyCollection<string> worn)
    {
        string zone = BareZone(shape, x, y);
        if (zone == "tail" && worn.Contains("尾巴开关")) return "skirt";
        if (zone == "body" && y > .5 && worn.Contains("裙子切换")) return "skirt";
        return zone;
    }

    private static string BareZone(string shape, double x, double y) => shape switch
    {
        "chibi" => y < .33 ? "head" : y < .53 ? (x is > .27 and < .73 ? "face" : "head")
            : y < .66 && x is < .4 or > .6 ? "hand" : y > .92 ? "foot" : y > .80 ? "leg" : y < .76 ? "skirt" : "body",
        "full" => y < .17 ? "head" : y < .275 ? (x is > .30 and < .70 ? "face" : "head")
            : y is > .46 and < .56 && x is < .26 or > .74 ? "hand" : y is > .30 and < .42 ? "chest"
            : y is > .42 and < .57 ? "skirt"
            : y > .865 ? "foot"
            : y > .70 ? "leg"
            : y >= .57 && x is < .36 or > .64 ? "tail" : "body",
        _ => SmallZone(x, (y * 620 - 60) / 560),
    };

    // Measured on her without the headroom the window keeps above her for jumping.
    // 脸的下边界 .215 → .245（用户 2026-11-14：点嘴巴、下巴会判成身体，右键也摸不到）：量过，下巴尖约在 .24，.245 刚好压在衣领上沿。
    private static string SmallZone(double x, double y) => y < .125 ? "head" : y < .26 ? (x is > .28 and < .72 ? "face" : "head")
        : y is > .5 and < .6 && x is < .28 or > .74 ? "hand" : y > .90 ? "foot" : y > .78 ? "leg"
        : y is > .32 and < .43 ? "chest"
        : y is > .43 and < .62 ? "skirt"
        : y >= .62 && x is < .36 or > .62 ? "tail" : "body";

    // 点上半身/裙子时的随机反应（本机加，2026-11-15）：护裙为主，偶尔让步，很少数会病娇
    private string PrickMood(int every)
    {
        // 本机补（2026-11-20）：Smitten 这个开关从初始提交起就没人调用——「满级了三种娇羞各三分之一」
        // 一直没生效（测试里是照设计写的，功能没接上）。这里接上。
        if (Smitten?.Invoke() == true) return SmittenMoods[random.Next(SmittenMoods.Length)];
        // 本机修（2026-11-20）：原来 Next(every) 只出 0/1/2，三个分支把 0/1/2 占满了，
        // 默认那支（护裙）永远走不到——护裙这个反应其实一次都没出现过。
        // 按注释的本意改成：护裙为主（一半），偶尔让步（三分之一），很少数病娇（六分之一）。
        int roll = random.Next(every * 2);
        return roll switch { 0 => "病娇", <= 2 => "娇羞", _ => "护裙" };
    }

    // 点上半身（本机加，2026-11-15）：多让她往前凑一下 —— 前倾、抚胸各占一份
    private string ChestMood()
    {
        // 亲密度高的时候，胸口那边也更容易软下来（本机加，2026-11-15）
        int love = Bond >= 4 ? 2 : Bond >= 3 ? 1 : 0;
        return random.Next(10) switch
        {
            0 => "病娇",
            1 or 2 or 3 => "娇羞前倾",
            4 or 5 => "娇羞抚胸",
            6 => "娇羞",
            7 => "爱心",
            8 => love > 0 && random.Next(3) == 0 ? "爱心" : "娇羞",
            _ => random.Next(2) == 0 ? "心虚" : "护裙",
        };
    }

    // 点脸（本机加，2026-11-15）：多半戳脸，偶尔委屈，很少数会凑过来亲一下
    private string FaceMood() => random.Next(10) switch
    {
        0 => "亲吻",
        1 => "委屈",
        _ => "戳脸",
    };

    // 点手（本机加，2026-11-15）：亲密度越高，冒出爱心的机会越大 —— 初识 1/5，磨合 1/4，同频 1/3，默契 1/2，羁绊必定
    private string HandMood()
    {
        // 本机加（2026-11-18）：四成走「摸手」，专门给手写的那一组
        if (random.Next(5) < 2) return "摸手";
        if (Bond >= 3 && random.Next(6) == 0) return "满足";
        return Bond switch
        {
            >= 4 => random.Next(2) == 0 ? "爱心" : "开心",
            3 => random.Next(3) == 0 ? "爱心" : "开心",
            2 => random.Next(4) == 0 ? "爱心" : "开心",
            _ => random.Next(5) == 0 ? "爱心" : "开心",
        };
    }

    // 点她时按人设给反应（本机加，2026-11-15）：人妻偏温软、童真偏一惊一乍、病娇偏黏、傲娇还是原来那套别扭
    // 生物钟（本机加，2026-11-16）：深夜到凌晨她犯困，清晨刚醒也迷糊 —— 点她的反应会先走这两种
    private string ClockMood(DateTime now)
    {
        // now 传进来是 UTC，必须换成这台电脑的本地时间，否则白天也犯困（本机修，2026-11-16）
        // 本机修（2026-11-20）：非 UTC 分支原来写的是 DateTime.Now.Hour —— 把传进来的时间丢掉了，
        // 只要调用方传的是本地/未指定时间，就永远按「现在」判断深夜。改成用它自己的小时。
        int hour = now.Kind == DateTimeKind.Utc ? now.ToLocalTime().Hour : now.Hour;
        bool late = hour >= 0 && hour < 5;      // 深夜
        bool dawn = hour >= 5 && hour < 8;      // 刚起
        if (late) return random.Next(5) switch { 0 => "打瞌睡", 1 => "打哈欠", 2 => "犯困", _ => "心虚" };
        if (dawn) return random.Next(6) switch { 0 => "打哈欠", 1 => "犯困", _ => "心虚" };
        return "";
    }

    private string ClickMood(string zone) => Persona switch
    {
        "人妻" => zone switch
        {
            "head" => random.Next(3) switch { 0 => "摸头", 1 => "摸后腻歪", _ => "摸头" },
            "face" => random.Next(4) switch { 0 => "亲吻", 1 => "满足", 2 => "摸后腻歪", _ => "害羞" },
            "hand" => random.Next(5) switch { 0 => "摸手", 1 => "摸手", 2 => "爱心", 3 => "满足", _ => "摸后腻歪" },
            "chest" => random.Next(4) switch { 0 => "爱心", 1 => "娇羞", 2 => "摸后腻歪", _ => "害羞" },
            "skirt" => random.Next(3) switch { 0 => "害羞", 1 => "娇羞", _ => "护裙" },
            "foot" => random.Next(4) switch { 0 => "被踩", 1 => "害羞前倾", 2 => "跺脚", _ => "害羞" },
            "leg" => random.Next(5) switch { 0 => "摸腿", 1 => "摸腿", 2 => "害羞", 3 => "娇羞", _ => "被踩" },
            "tail" => random.Next(3) switch { 0 => "害羞", 1 => "委屈", _ => "叹气" },
            _ => random.Next(4) switch { 0 => "爱心", 1 => "满足", 2 => "歪头", _ => "害羞" },
        },
        "童真" => zone switch
        {
            "head" => random.Next(3) switch { 0 => "摸头", 1 => "开心", _ => "满足" },
            "face" => random.Next(4) switch { 0 => "震惊", 1 => "戳脸", 2 => "委屈", _ => "歪头" },
            "hand" => random.Next(5) switch { 0 => "摸手", 1 => "摸手", 2 => "满足", 3 => "开心", _ => "张望" },
            "chest" => random.Next(4) switch { 0 => "震惊", 1 => "害羞", 2 => "歪头", _ => "开心" },
            "skirt" => random.Next(3) switch { 0 => "护裙", 1 => "委屈", _ => "震惊" },
            "foot" => random.Next(4) switch { 0 => "被踩", 1 => "跺脚", 2 => "害羞前倾", _ => "张望" },
            "leg" => random.Next(5) switch { 0 => "摸腿", 1 => "摸腿", 2 => "娇羞", 3 => "害羞", _ => "跺脚" },
            "tail" => random.Next(3) switch { 0 => "震惊", 1 => "委屈", _ => "张望" },
            _ => random.Next(4) switch { 0 => "开心", 1 => "震惊", 2 => "哼歌", _ => "害羞" },
        },
        "病娇" => zone switch
        {
            "head" => random.Next(3) switch { 0 => "摸头", 1 => "爱心", _ => "摸后腻歪" },
            "face" => random.Next(4) switch { 0 => "病娇", 1 => "亲吻", 2 => "歪头", _ => "戳脸" },
            "hand" => random.Next(5) switch { 0 => "摸手", 1 => "摸手", 2 => "爱心", 3 => "病娇", _ => "开心" },
            "chest" => random.Next(4) switch { 0 => "病娇", 1 => "爱心", 2 => "思考", _ => "娇羞" },
            "skirt" => random.Next(3) switch { 0 => "娇羞", 1 => "病娇", _ => "害羞" },
            "foot" => random.Next(4) switch { 0 => "被踩", 1 => "跺脚", 2 => "病娇", _ => "歪头" },
            "leg" => random.Next(5) switch { 0 => "摸腿", 1 => "摸腿", 2 => "病娇", 3 => "跺脚", _ => "生气" },
            "tail" => random.Next(3) switch { 0 => "病娇", 1 => "委屈", _ => "思考" },
            _ => random.Next(4) switch { 0 => "病娇", 1 => "爱心", 2 => "摸后腻歪", _ => "害羞" },
        },
        _ => DefaultMood(zone),
    };

    private string DefaultMood(string zone) => zone switch
    {
        "head" => "摸头", "face" => FaceMood(), "hand" => HandMood(), "skirt" => PrickMood(3), "chest" => ChestMood(), "leg" => random.Next(4) switch { 0 => "摸腿", 1 => "摸腿", 2 => "害羞", _ => "娇羞" }, "foot" => random.Next(3) switch { 0 => "被踩", 1 => "跺脚", _ => "害羞" }, "tail" => random.Next(3) == 0 ? "委屈" : "震惊", _ => "心虚",
    };

    // 亲密度到了最高一级：戳裙子时换成这三种之一，概率平分（本机加，2026-11-08）。
    // 本机改（2026-11-20）：第一支从「娇羞」换成「爱心」——满级点裙子该有心眼和抚胸（这三种是爱意场合，
    // 「娇羞」自己只是脸红，测试要求的 exp 爱心眼 永远出不来）。
    internal static readonly string[] SmittenMoods = ["爱心", "娇羞前倾", "娇羞抚胸"];
    internal Func<bool>? Smitten { get; set; }
    // 本机加（2026-11-15）：亲密度等级（1 初识 … 5 羁绊），点她时按等级给不同的反应概率
    internal Func<int>? LevelOf { get; set; }
    private int Bond => LevelOf?.Invoke() ?? 1;
    // 本机加（2026-11-15）：当前人设（人妻 / 傲娇 / 童真 / 病娇），点她的反应按人设走
    internal Func<string>? PersonaOf { get; set; }
    private string Persona => PersonaOf?.Invoke() ?? "傲娇";
    // 专注模式（番茄钟）：待机只做安静的小动作，不嘀咕、不演小剧场、不报时（本机加，2026-11-08）。
    internal bool Quiet { get; set; }
    private static readonly string[] QuietMoods = ["思考", "发呆", "歪头"];

    // 每个区自己的连续触摸计数（本机加，2026-11-16）：同一区 4 秒内接着点，次数往上走
    private readonly Dictionary<string, (int Count, DateTime At)> zoneTaps = [];

    // 碧蓝航线式的触摸递进：1～2 次是平常反应，3～4 次害羞，5～6 次生气，7 次起是她这个人专属的彩蛋
    internal void Click(string zone, DateTime now, int side = 1)
    {
        clicks.RemoveAll(time => now - time > ClickSpan);
        clicks.Add(now);
        if (clicks.Count >= ClicksForBasin) { clicks.Clear(); zoneTaps.Clear(); Show("挨打", now, force: true); return; }

        int count = zoneTaps.TryGetValue(zone, out var seen) && (now - seen.At).TotalSeconds < 4 ? seen.Count + 1 : 1;
        zoneTaps[zone] = (count, now);
        var clock = count <= 2 ? ClockMood(now) : "";
        var mood = clock.Length > 0 ? clock : count switch
        {
            <= 2 => ClickMood(zone),
            3 or 4 => "害羞",
            5 or 6 => "生气",
            _ => "彩蛋",
        };
        Show(mood, now, side);
    }

    internal void DragStart()
    {
        EndMood();
        dragging = true;
        send("exp 圈圈眼 1");
        foreach (var (id, value) in Hanging) send(FormattableString.Invariant($"set {id} {value}"));
        // 拎着时的晃动不再放固定循环，交给 PetWindow.Touch 按拖动速度实时驱动（惯性、头晕）。
    }

    // The dizzy eyes stay on through the landing and go with a blink.
    // dizzy：转晕了，不播“落地”，头晕那一段由 PetWindow.Touch 接手（圈圈眼也由它收）。
    internal void DragEnd(DateTime now, bool dizzy = false)
    {
        if (!dragging) return;
        dragging = false;
        send("animstop");
        foreach (var (id, _) in Hanging) send($"clear {id}");
        if (dizzy) { send("exp 圈圈眼 1"); return; }
        Show("落地", now, force: true);
    }

    // While she is reading, watching or writing something (any kind the agent names) she wears her glasses,
    // however long it takes, until it says she is done.
    internal void Activity(string kind, bool active)
    {
        if (active) activities.Add(kind); else activities.Remove(kind);
        UpdateGlasses();
    }

    internal void ClearActivities()
    {
        if (activities.Count == 0) return;
        activities.Clear();
        UpdateGlasses();
    }

    // Glasses worn by choice (dress) count: she does not stack a second pair, and those stay on after her work ends.
    internal void UpdateGlasses()
    {
        var keep = worn();
        bool wanted = activities.Count > 0 && !keep.Contains(Glasses) && !keep.Contains(Monocle);
        if (wanted == wearingGlasses) return;
        wearingGlasses = wanted;
        if (wanted || !keep.Contains(Glasses)) send($"exp {Glasses} {(wanted ? 1 : 0)}");
    }

    // 台词和小剧场的“条件”（本机加，2026-11-06，见 Conditions.cs）：(名字, 本地时间) → 权重，0 就是这次不抽。不设就一律 1。
    internal Func<string, DateTime, int>? TextWeight { get; set; }
    internal Func<string, DateTime, int>? SkitWeightOf { get; set; }
    // 整点报时：这个钟点该报的几条台词（本地时间 → 台词，没有就空）；ChimeBlocked 为真时先不报、下一帧再看。
    internal Func<DateTime, string[]>? ChimeTexts { get; set; }
    internal Func<bool>? ChimeBlocked { get; set; }
    private (DateTime Day, int Hour)? lastChime;
    // 钟点刚过几分钟内才算“整点”（她那会儿在说话、被摸，就等到闲下来；过了这几分钟就算错过，不补报）。
    internal const int ChimeMinutes = 3;
    // 小剧场：上次演完的时间；太久没演就下一次直接给它（2026-11-10 用户反馈「有时不会触发」）
    private DateTime lastSkitAt = DateTime.MinValue;
    internal void SkitPlayed(DateTime now) => lastSkitAt = now;
    internal void SkitMissed(DateTime now) { if (now > lastSkitAt) nextIdle = now.AddSeconds(45); }

    internal void Tick(DateTime now, bool speaking, int intervalSeconds)
    {
        if (mood != null && now >= moodUntil) EndMood();
        if (speaking || dragging || Touching || mood != null) return;
        var local = now.ToLocalTime();
        // 整点报时：每个钟点最多一次，不受“隔多久做一个小动作”的限制。
        if (!Quiet && local.Minute < ChimeMinutes && lastChime != (local.Date, local.Hour) && ChimeTexts is { } chimes && chimes(local) is { Length: > 0 } hourly && ChimeBlocked?.Invoke() != true)
        {
            lastChime = (local.Date, local.Hour);
            nextIdle = now.AddSeconds(intervalSeconds);
            TextIdle?.Invoke(hourly[random.Next(hourly.Length)]);
            return;
        }
        if (now < nextIdle) return;
        nextIdle = now.AddSeconds(intervalSeconds);
        // Never the same one twice in a row.
        int hour = local.Hour;
        var actions = (Quiet ? QuietMoods : IdleMoods).Where(name => name != lastIdle).ToArray();
        // 三类（待机小动作 / 固定嘀咕 / 小剧场）先平分：从有内容的几类里等概率挑一类，再在这一类里挑一条。
        // （本机改，2026-11-06；原来是每条权重 1 混在一起抽，条数多的那类占大头。）
        // 类内：小动作夜里犯困的权重高；嘀咕、小剧场按条件加权（不符合条件的这次不抽）；类里一条都抽不了就不算这一类。
        var texts = Quiet ? [] : Weighted(TextIdleMoods, TextWeight, local);
        var plays = Quiet ? [] : Weighted(SkitIdleMoods, SkitWeightOf, local);
        // 三类的比例（用户 2026-11-14 改）：待机小动作 35、固定嘀咕 35、小剧场 30，也就是前两类合起来约 7 成、小剧场约 3 成。
        // 某一类这次抽不了就不参加，剩下的按各自的比例分（比如没有小剧场可抽，小动作和嘀咕各一半）。
        // 原来“超过 12 分钟没演小剧场就直接演”的规则去掉了；小剧场里按日期、时段、亲密度的加权不变。
        var kinds = new List<(int Kind, int Weight)>();
        if (actions.Length > 0) kinds.Add((0, 35));
        if (texts.Length > 0) kinds.Add((1, 35));
        if (plays.Length > 0) kinds.Add((2, 30));
        if (kinds.Count == 0) return;
        int roll = random.Next(kinds.Sum(kind => kind.Weight)), chosen = kinds[^1].Kind;
        foreach (var (kind, weight) in kinds) { if (roll < weight) { chosen = kind; break; } roll -= weight; }
        switch (chosen)
        {
            case 0:
                int pick = random.Next(actions.Sum(name => IdleWeight(name, hour)));
                foreach (var name in actions)
                {
                    pick -= IdleWeight(name, hour);
                    if (pick >= 0) continue;
                    lastIdle = name;
                    Show(name, now, random.Next(2) == 0 ? 1 : -1);
                    IdleStarted?.Invoke(name);
                    return;
                }
                return;
            case 1:
                TextIdle?.Invoke(PickWeighted(texts));
                return;
            default:
                SkitIdle?.Invoke(PickWeighted(plays));
                return;
        }
    }

    private static (string Name, int Weight)[] Weighted(string[] names, Func<string, DateTime, int>? weight, DateTime local) =>
        names.Select(name => (Name: name, Weight: weight?.Invoke(name, local) ?? 1)).Where(item => item.Weight > 0).ToArray();

    private string PickWeighted((string Name, int Weight)[] items)
    {
        int pick = random.Next(items.Sum(item => item.Weight));
        foreach (var (name, weight) in items)
        {
            pick -= weight;
            if (pick < 0) return name;
        }
        return items[^1].Name;
    }
}

