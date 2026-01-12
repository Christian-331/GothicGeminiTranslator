using System;
using Avalonia.Controls;

namespace GothicTranslator;

public static class Logger
{
    private static TextBox? _txtLog;
    private static MainWindow? _window;

    public static void SetBoxes(TextBox txtLog, MainWindow mainWindow)
    {
        _txtLog = txtLog;
        _window = mainWindow;
    }

    public static void Log(string message, int? count = null)
    {
        if (_txtLog is null
            || (count.HasValue && count != ApiHandler.Count)) return;

        _txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _txtLog.CaretIndex = _txtLog.Text.Length;
    }

    public static void Debug(string message, int? count = null)
    {
        if (_window?.Debug != true) return;

        Log(message, count);
    }
}
