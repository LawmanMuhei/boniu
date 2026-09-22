using System;
using Microsoft.Web.WebView2.WinForms;

namespace MiniView.WebView2App
{
    internal sealed class VisibilityAwareWebView : WebView2
    {
        internal void SynchronizeVisibility()
        {
            // WinForms can coalesce visibility events when a parent and child are both hidden.
            // Re-run the SDK's protected visibility hook; never access private controller fields.
            base.OnVisibleChanged(EventArgs.Empty);
        }
    }
}
