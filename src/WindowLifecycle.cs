namespace MiniView.WebView2App
{
    // UI-thread owned operation stamp. Async completions must not mutate a newer window state.
    internal sealed class WindowLifecycle
    {
        internal long Generation { get; private set; }
        internal bool Visible { get; private set; }
        internal bool Closed { get; private set; }
        internal WindowLifecycle() { Visible = true; }
        internal long SetVisible(bool visible) { Visible = visible; return Refresh(); }
        internal long Refresh() { return ++Generation; }
        internal bool IsCurrent(long generation) { return !Closed && Generation == generation; }
        internal void Close() { Closed = true; Refresh(); }
    }
}
