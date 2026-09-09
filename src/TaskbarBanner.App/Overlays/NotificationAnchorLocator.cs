using System.Windows.Automation;
using TaskbarBanner.Core.Geometry;
using TaskbarBanner.Core.Interop;

namespace TaskbarBanner.App.Overlays;

public static class NotificationAnchorLocator
{
    public static bool TryGetNotificationAreaLeftPx(IntPtr taskbarHwnd, out double leftPx, out string method)
    {
        if (TryGetTrayNotifyLeft(taskbarHwnd, out leftPx))
        {
            method = "TrayNotifyWnd";
            return true;
        }

        if (TryGetShowHiddenIconsLeftUiAutomation(taskbarHwnd, out leftPx))
        {
            method = "UI Automation (Show hidden icons)";
            return true;
        }

        leftPx = 0;
        method = string.Empty;
        return false;
    }

    private static bool TryGetTrayNotifyLeft(IntPtr taskbarHwnd, out double leftPx)
    {
        leftPx = 0;
        if (NativeMethods.TryFindDescendantRect(taskbarHwnd, NativeMethods.TrayNotifyWindowClass, out RectI rect))
        {
            leftPx = rect.Left;
            return true;
        }

        return false;
    }

    private static bool TryGetShowHiddenIconsLeftUiAutomation(IntPtr taskbarHwnd, out double leftPx)
    {
        leftPx = 0;
        try
        {
            AutomationElement? root = AutomationElement.FromHandle(taskbarHwnd);
            if (root is null)
            {
                return false;
            }

            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < 2000)
            {
                (AutomationElement element, int depth) = queue.Dequeue();
                visited++;

                AutomationElement.AutomationElementInformation info = element.Current;
                if (info.ControlType == ControlType.Button
                    && LooksLikeChevron(info.Name, info.AutomationId))
                {
                    System.Windows.Rect bounds = info.BoundingRectangle;
                    if (bounds.Width > 1 && bounds.Height > 1)
                    {
                        leftPx = bounds.Left;
                        return true;
                    }
                }

                if (depth >= 10)
                {
                    continue;
                }

                AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(element);
                while (child is not null)
                {
                    queue.Enqueue((child, depth + 1));
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool LooksLikeChevron(string? name, string? automationId)
    {
        if (!string.IsNullOrWhiteSpace(name) && name.Contains("hidden icons", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        return automationId.Contains("hidden icons", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("overflow", StringComparison.OrdinalIgnoreCase)
            || automationId.Equals("ShowHiddenIcons", StringComparison.OrdinalIgnoreCase);
    }
}
