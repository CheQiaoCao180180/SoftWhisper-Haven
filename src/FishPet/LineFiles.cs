// 本机重写（2026-11-17）：台词文件的位置。
//
// 以前台词散在插件根目录（touch.txt / stroke.txt / …），加了多语言之后又多了个 lang 目录，
// 两套路径混在一起很难看。现在统一成：
//
//   lines\zh\touch.txt     中文（原来的根目录那套搬过来）
//   lines\en\touch.txt     英文
//   lines\ja\touch.txt     日文
//
// 找文件的规矩：先看对应语言有没有，没有就退回中文；中文也没有，再看一眼根目录（兼容旧布局）。

using System;
using System.IO;

namespace FishPet;

internal static class LineFiles
{
    internal const string Default = "zh";

    /// <summary>她能听懂的几种语言。key 存进 settings.Language。</summary>
    internal static readonly string[] Languages = ["zh", "en", "ja"];

    internal static string Folder(string language) =>
        Path.Combine(Places.Root, "lines", Languages.Length > 0 && Array.IndexOf(Languages, language) >= 0 ? language : Default);

    /// <summary>
    /// 找这个名字的台词文件：<paramref name="language"/> 有就用它，没有退回中文，
    /// 中文也没有就看一眼插件根目录（旧版本把文件放那儿）。
    /// </summary>
    internal static string Pick(string language, string name)
    {
        var localized = Path.Combine(Folder(language), name);
        if (File.Exists(localized)) return localized;

        var fallback = Path.Combine(Folder(Default), name);
        if (File.Exists(fallback)) return fallback;

        var legacy = Path.Combine(Places.Root, name);
        return File.Exists(legacy) ? legacy : fallback;
    }

    /// <summary>热重载时要知道哪些目录要盯着。</summary>
    internal static string[] WatchedFolders()
    {
        var root = Places.Root;
        return
        [
            root,
            Path.Combine(root, "lines"),
            Path.Combine(root, "lines", "zh"),
            Path.Combine(root, "lines", "en"),
            Path.Combine(root, "lines", "ja"),
        ];
    }
}
