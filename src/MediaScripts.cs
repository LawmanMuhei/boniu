using System.Globalization;

namespace MiniView.WebView2App
{
    internal static class MediaScripts
    {
        internal static string Build(long generation, bool playing)
        {
            return @"(() => {
                const revision = " + generation.ToString(CultureInfo.InvariantCulture) + @";
                const visible = " + (playing ? "true" : "false") + @";
                const key = '__boniuMediaState';
                const state = window[key] || (window[key] = { revision: -1, visible: true, resume: new Set() });
                if (revision < state.revision) return 'stale';
                state.revision = revision;
                state.visible = visible;
                for (const video of state.resume) if (!video.isConnected) state.resume.delete(video);
                if (!visible) {
                    document.querySelectorAll('video').forEach(video => {
                        if (!video.paused && !video.ended) state.resume.add(video);
                    });
                    state.resume.forEach(video => { try { video.pause(); } catch (_) {} });
                } else {
                    state.resume.forEach(video => {
                        try {
                            Promise.resolve(video.play()).then(() => {
                                if (!state.visible) video.pause();
                                else state.resume.delete(video);
                            }).catch(() => {});
                        } catch (_) {}
                    });
                }
                return visible ? 'playing' : 'paused';
            })()";
        }
    }
}
