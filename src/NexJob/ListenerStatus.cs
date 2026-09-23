namespace NexJob;

/// <summary>
/// Status representing the connection and operational state of a background event listener or trigger.
/// </summary>
public enum ListenerStatus
{
    /// <summary>
    /// The listener service is initializing or binding.
    /// </summary>
    Starting,

    /// <summary>
    /// The listener is connected to the broker and actively listening for events.
    /// </summary>
    Listening,

    /// <summary>
    /// The connection was lost and the listener is actively attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The listener has encountered a terminal failure and is not consuming.
    /// </summary>
    Faulted,

    /// <summary>
    /// The listener has been stopped or gracefully shut down.
    /// </summary>
    Stopped,
}
