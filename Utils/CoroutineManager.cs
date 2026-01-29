using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Manages coroutines to prevent memory leaks and crashes.
    /// Limits concurrent coroutines and provides cleanup on mod unload.
    /// Uses wrapper pattern to auto-remove completed coroutines from tracking.
    /// </summary>
    public static class CoroutineManager
    {
        private static readonly List<object> activeCoroutines = new List<object>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        /// <summary>
        /// Cleanup all active coroutines.
        /// </summary>
        public static void CleanupAll()
        {
            lock (coroutineLock)
            {
                if (activeCoroutines.Count > 0)
                {
                    MelonLogger.Msg($"Cleaning up {activeCoroutines.Count} active coroutines");
                    foreach (var coroutine in activeCoroutines)
                    {
                        try
                        {
                            MelonCoroutines.Stop(coroutine);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"Error stopping coroutine: {ex.Message}");
                        }
                    }
                    activeCoroutines.Clear();
                }
            }
        }

        /// <summary>
        /// Start an untracked coroutine (not tracked in the active list).
        /// Use for short-lived coroutines that don't need cleanup management.
        /// </summary>
        public static void StartUntracked(IEnumerator coroutine)
        {
            try
            {
                MelonCoroutines.Start(coroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error starting untracked coroutine: {ex.Message}");
            }
        }

        /// <summary>
        /// Start a managed coroutine with automatic cleanup and limit enforcement.
        /// Uses wrapper pattern to auto-remove from tracking when coroutine completes.
        /// </summary>
        public static void StartManaged(IEnumerator coroutine)
        {
            lock (coroutineLock)
            {
                // If we're at the limit, stop and remove the oldest coroutine
                if (activeCoroutines.Count >= maxConcurrentCoroutines)
                {
                    var oldest = activeCoroutines[0];
                    try
                    {
                        MelonCoroutines.Stop(oldest);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error stopping oldest coroutine: {ex.Message}");
                    }
                    activeCoroutines.RemoveAt(0);
                    MelonLogger.Warning("Stopped oldest coroutine due to limit");
                }

                // Start the wrapped coroutine that auto-removes on completion
                try
                {
                    var wrapper = TrackingWrapper(coroutine);
                    var handle = MelonCoroutines.Start(wrapper);
                    activeCoroutines.Add(handle);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Wrapper coroutine that auto-removes from tracking when inner coroutine completes.
        /// </summary>
        private static IEnumerator TrackingWrapper(IEnumerator inner)
        {
            object handle = null;

            // Store the handle for removal (set after MelonCoroutines.Start returns)
            lock (coroutineLock)
            {
                if (activeCoroutines.Count > 0)
                {
                    handle = activeCoroutines[activeCoroutines.Count - 1];
                }
            }

            try
            {
                while (inner.MoveNext())
                {
                    yield return inner.Current;
                }
            }
            finally
            {
                // Remove from tracking when complete
                if (handle != null)
                {
                    lock (coroutineLock)
                    {
                        activeCoroutines.Remove(handle);
                    }
                }
            }
        }
    }
}
