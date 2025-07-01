namespace Piglet
{
    /// <summary>
    /// <para>
    /// Piglet uses this type to indicate the current execution state
    /// of a coroutine. `Yield.Continue` is the normal case, whereas
    /// `Yield.Blocked` indicates that the coroutine is blocked on
    /// a task that won't progress until control is returned to the
    /// main Unity thread.
    /// </para>
    /// <para>
    /// GltfImportTask checks for this return type to avoid
    /// "busy-waiting" on UnityWebRequest and UnityWebRequestTexture
    /// requests, since those tasks are executed by Unity's game loop.
    /// </para>
    /// </summary>
    public enum YieldType
    {
        /// <summary>
        /// Indicates that the coroutine is not blocked and further
        /// calls to MoveNext() will do productive work.
        /// </summary>
        Continue,
        /// <summary>
        /// Indicates that the coroutine is blocked on a task that
        /// can't until control is returned to Unity (e.g. UnityWebRequest).
        /// In other words, further calls MoveNext() in this frame
        /// will just "busy-wait" and will *not* do any productive work.
        /// </summary>
        Blocked
    }
}
