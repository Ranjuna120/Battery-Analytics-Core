using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace System.Windows.Forms;

// Lightweight fallback toast (balloon tip) helper without full UWP toast infra.
internal static class ToastNotificationHelper
{
    private static NotifyIcon? _shared;

    public static void ShowToast(string title, string message, int timeoutMs = 5000)
    {
        try
        {
            if (_shared == null)
            {
                _shared = new NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Information,
                    Visible = true,
                    Text = "Battery Monitor"
                };
            }
            _shared.BalloonTipTitle = title;
            _shared.BalloonTipText = message;
            _shared.ShowBalloonTip(timeoutMs);
        }
        catch { }
    }
}
