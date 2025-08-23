using PurrNet;
using PurrNet.Logging;
using UnityEngine;

/// <summary>
/// Networked time source that samples the host's time and exposes an authoritative clock to all peers.
/// The server/host periodically records its Time.time along with the synchronized tick so clients can
/// reconstruct the same time deterministically.
/// </summary>
public class NetworkClock : NetworkBehaviour
{
    /// <summary>
    /// Number of Update frames between server-side time samples. 0 = sample every Update.
    /// </summary>
    [SerializeField] private int tickEveryUpdate = 0;

    /// <summary>
    /// Latest server Time.time captured and replicated to clients.
    /// </summary>
    public SyncVar<float> HostTime = new SyncVar<float>();

    /// <summary>
    /// Server tick value captured at the same moment as <see cref="HostTime"/>.
    /// </summary>
    public SyncVar<uint> ServerTickAtSync = new SyncVar<uint>();

    private int currentTick;

    /// <summary>
    /// Registers this instance so static accessors can resolve the active NetworkClock.
    /// </summary>
    private void Awake()
    {
        InstanceHandler.RegisterInstance(this);
    }

    /// <summary>
    /// Unregisters the instance when destroyed.
    /// </summary>
    protected override void OnDestroy()
    {
        InstanceHandler.UnregisterInstance<NetworkClock>();
    }

    /// <summary>
    /// Safe accessor for the authoritative time in seconds.
    /// </summary>
    public static float TryGetTime()
    {
        if (!InstanceHandler.TryGetInstance(out NetworkClock manager))
        {
            PurrLogger.LogError($"No instance was found for {nameof(NetworkClock)}. Please ensure it is subscribed");
            return 0f;
        }

        return manager.GetHostTime();
    }

    /// <summary>
    /// Server loop: ticks a counter and samples the server time at the configured cadence.
    /// </summary>
    private void Update()
    {
        if (!isServer)
            return;

        currentTick++;

        if (currentTick >= tickEveryUpdate)
        {
            currentTick = 0;
            EvaluateTick();
        }
    }

    /// <summary>
    /// Captures the current server time and the corresponding synchronized tick.
    /// </summary>
    private void EvaluateTick()
    {
        HostTime.value = Time.time;               // Snapshot of the host's Time.time
        ServerTickAtSync.value = GetSyncedTick(); // Tick value at the same instant
    }

    /// <summary>
    /// Returns the current globally synchronized tick from PurrNet.
    /// </summary>
    private uint GetSyncedTick()
    {
        return NetworkManager.main.tickModule.syncedTick;
    }

    /// <summary>
    /// Returns the network tick rate (ticks per second).
    /// </summary>
    private int GetTickRate()
    {
        return NetworkManager.main.tickModule.tickRate;
    }

    /// <summary>
    /// Gets the authoritative time in seconds.
    /// On the server: returns <see cref="Time.time"/>.
    /// On clients: reconstructs host time using the last sampled host time and the number
    /// of ticks elapsed since that sample.
    /// </summary>
    private float GetHostTime()
    {
        // Host/Server uses its local Time.time directly.
        if (NetworkManager.main.isHost)
            return Time.time;

        // Clients derive host time from the last snapshot + elapsed ticks.
        uint srvTick = ServerTickAtSync.value;

        // No snapshot yet: fall back to last replicated host time.
        if (srvTick <= 0)
            return HostTime.value;

        uint tickNow = GetSyncedTick();
        float tickRate = GetTickRate();

        // If tick rate is invalid, fall back to the last snapshot.
        if (tickRate <= 0f)
            return HostTime.value;

        // Elapsed seconds since the snapshot, computed from ticks.
        float timePassed = (tickNow - srvTick) / tickRate;

        return HostTime.value + timePassed;
    }
}

/// <summary>
/// Convenience accessor mirroring Unity's Time API style:
/// read the authoritative server/host time as a property.
/// </summary>
public static class NetTime
{
    /// <summary>
    /// Returns the server/host time in seconds.
    /// </summary>
    public static float ServerTime => NetworkClock.TryGetTime();
}
