using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DalamudACT;

internal enum DotDebugOutputTarget : byte
{
    Chat = 0,
    DalamudLog = 1,
    File = 2,
}

internal static class DotDebugOutput
{
    private static readonly object FileGate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private const string FileName = "dot-debug.log";
    private const string BackupFileName = "dot-debug.old.log";
    private const long MaxFileBytes = 5L * 1024L * 1024L;

    internal static string GetFilePath()
    {
        try
        {
            var configDir = DalamudApi.PluginInterface.GetPluginConfigDirectory();
            if (!string.IsNullOrWhiteSpace(configDir))
                return Path.Combine(configDir, FileName);
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    internal static void WriteLines(DotDebugOutputTarget target, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;

        switch (target)
        {
            case DotDebugOutputTarget.Chat:
                foreach (var line in lines)
                    DalamudApi.ChatGui.Print(line);
                break;
            case DotDebugOutputTarget.DalamudLog:
                foreach (var line in lines)
                    DalamudApi.Log.Information(line);
                break;
            case DotDebugOutputTarget.File:
                AppendLinesToFile(lines);
                break;
        }
    }

    internal static void WriteLine(DotDebugOutputTarget target, string line)
        => WriteLines(target, new[] { line });

    private static void AppendLinesToFile(IReadOnlyList<string> lines)
    {
        var path = GetFilePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            DalamudApi.Log.Warning("[DalamudACT] DotDebug: GetPluginConfigDirectory 为空，无法写入 dot-debug.log。");
            return;
        }

        try
        {
            lock (FileGate)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                RotateIfNeeded(path);

                File.AppendAllLines(path, lines, Utf8NoBom);
            }
        }
        catch (Exception e)
        {
            DalamudApi.Log.Error(e, "[DalamudACT] DotDebug: 写入 dot-debug.log 失败。");
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxFileBytes) return;

            var dir = info.DirectoryName ?? string.Empty;
            var backup = Path.Combine(dir, BackupFileName);

            if (File.Exists(backup))
                File.Delete(backup);

            File.Move(path, backup);
        }
        catch
        {
            // ignore rotation errors; worst-case we keep appending
        }
    }
}

