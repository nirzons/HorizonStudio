using System;
using System.Threading.Tasks;

namespace NirZonshine.NINA.HorizonStudio.Services {
    /// <summary>
    /// Provides robust, null-safe thread marshaling helpers for WPF UI dispatcher interactions.
    /// Handles Application.Current nullability during plugin unloading, assembly testing, and shutdown.
    /// </summary>
    public static class ThreadHelper {
        /// <summary>
        /// Safely executes an action on the WPF UI thread, checking for access and guarding against null Application instance.
        /// </summary>
        public static void RunOnUI(Action action) {
            var app = System.Windows.Application.Current;
            if (app != null) {
                if (app.Dispatcher.CheckAccess()) {
                    action();
                } else {
                    app.Dispatcher.BeginInvoke(action);
                }
            } else {
                action(); // Inline execution fallback for testing or shutdown
            }
        }

        /// <summary>
        /// Safely executes an action asynchronously on the WPF UI thread.
        /// </summary>
        public static async Task RunOnUIAsync(Action action) {
            var app = System.Windows.Application.Current;
            if (app != null) {
                await app.Dispatcher.InvokeAsync(action);
            } else {
                action();
            }
        }
    }
}
