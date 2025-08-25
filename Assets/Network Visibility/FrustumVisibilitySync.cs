using System;
using System.Collections.Generic;
using UnityEngine;
using PurrNet;

[DisallowMultipleComponent]
public class FrustumVisibilitySync : NetworkBehaviour
{
    [Header("Target")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;

    [Header("Update")]
    [Min(0.02f)]
    [SerializeField] private float checkInterval = 0.10f;
    [SerializeField] private bool allowUpdate = true;

    // Server-side events (invoked only when aggregate state transitions happen)
    /// <summary>
    /// Fired when <b>all</b> players start seeing the object (transition to ALL_VISIBLE).
    /// </summary>
    public event Action OnAllVisionEnter;
    /// <summary>
    /// Fired when <b>all</b> players stop seeing the object (transition to ALL_HIDDEN).
    /// </summary>
    public event Action OnAllVisionLeft;
    /// <summary>
    /// Fired when <b>at least one</b> player starts seeing the object (transition to ANY_VISIBLE).
    /// </summary>
    public event Action OnAnyVisionEnter;
    /// <summary>
    /// Fired when <b>no</b> player is seeing the object anymore (transition to ANY_HIDDEN).
    /// </summary>
    public event Action OnAnyVisionLeft;

    // ---- Client state ----
    private bool _lastLocalVisible;
    private float _nextCheckTime;

    // Preallocated frustum planes to avoid per-frame GC; GeometryUtility fills these.
    private readonly Plane[] _frustumPlanes = new Plane[6];

    // ---- Server state ----
    // Tracks per-player visibility as reported by their client.
    // NOTE: keys are PurrNet PlayerID; values are the latest visibility booleans.
    private readonly Dictionary<PlayerID, bool> _visibilityByPlayer = new Dictionary<PlayerID, bool>();

    // Aggregate visibility bitmask for quick edge detection.
    // Bit layout: ALL_VISIBLE=1, ALL_HIDDEN=2, ANY_VISIBLE=4, ANY_HIDDEN=8
    private byte _aggMask;
    private const byte ALL_VISIBLE = 1 << 0;
    private const byte ALL_HIDDEN = 1 << 1;
    private const byte ANY_VISIBLE = 1 << 2;
    private const byte ANY_HIDDEN = 1 << 3;

    // ---------------------- Lifecycle ----------------------

    /// <summary>
    /// Called when the networked object is spawned on client/server.
    /// On clients, ensures we have a renderer/camera so we can start visibility checks.
    /// </summary>
    protected override void OnSpawned()
    {
        if (isClient) EnsureRendererAndCamera();
        // If you ever need an initial aggregate recompute on server spawn, uncomment:
        // if (isServer) RecomputeAggregatesAndEmitIfChanged();
    }

    private void OnEnable()
    {
        // Stagger client checks so many objects don't evaluate on the same frame.
        // This reduces spikes when many instances are enabled.
        _nextCheckTime = Time.time + UnityEngine.Random.Range(0f, checkInterval);
    }

    private void OnDisable()
    {
        // Edge case: if the client object gets disabled while considered visible,
        // tell the server it is no longer visible to avoid "stuck visible" states.
        if (isClient && _lastLocalVisible)
        {
            _lastLocalVisible = false;
            UpdateVisibilityServerRpc(false);
        }
    }

    private void Update()
    {
        if (!allowUpdate)
            return;

        // Only clients perform frustum checks; they report changes to the server.
        if (!isClient || Time.time < _nextCheckTime) return;
        _nextCheckTime += checkInterval;

        EnsureRendererAndCamera();
        if (targetRenderer == null || targetCamera == null) return;

        // NOTE: CalculateFrustumPlanes with a preallocated array avoids GC on new Unity.
        // On older versions we copy the result to our cached array.
#if UNITY_2021_2_OR_NEWER
        GeometryUtility.CalculateFrustumPlanes(targetCamera, _frustumPlanes);
#else
        var planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        for (int i = 0; i < 6; i++) _frustumPlanes[i] = planes[i];
#endif
        // IMPORTANT: TestPlanesAABB uses Renderer.bounds (AABB in world space).
        // For SkinnedMeshRenderer this is updated by the animator; very fast but can be conservative
        // (i.e., may report visible when only the AABB is inside).
        bool nowVisible = GeometryUtility.TestPlanesAABB(_frustumPlanes, targetRenderer.bounds);
        if (nowVisible == _lastLocalVisible) return; // no state change -> no RPC spam

        _lastLocalVisible = nowVisible;
        UpdateVisibilityServerRpc(nowVisible);
    }

    // ---------------------- Client utilities ----------------------

    /// <summary>
    /// Ensures there is a Renderer to test and a Camera to build the frustum from.
    /// </summary>
    /// <remarks>
    /// Falls back to the first SkinnedMeshRenderer/MeshRenderer found in children and Camera.main.
    /// Using Camera.main has a lookup cost; if you have per-player cameras, call SetCamera externally.
    /// </remarks>
    private void EnsureRendererAndCamera()
    {
        if (targetRenderer == null)
        {
            if (!TryGetComponent(out targetRenderer))
            {
                // Prefer SkinnedMeshRenderer when available; otherwise use MeshRenderer.
                targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true)
                                 ?? (Renderer)GetComponentInChildren<MeshRenderer>(true);
            }
        }
        if (targetCamera == null) targetCamera = Camera.main; // Consider SetCamera for explicit control.
    }

    // ---------------------- Server ----------------------

    /// <summary>
    /// Client → Server: updates this client's visibility state for the object.
    /// </summary>
    /// <param name="isVisible">True if this client currently sees the object.</param>
    /// <param name="info">PurrNet RPC context (used for sender identification).</param>
    [ServerRpc]
    private void UpdateVisibilityServerRpc(bool isVisible, RPCInfo info = default)
    {
        // Use sender ID as the key; add/update in a single line to avoid branching.
        // PurrNet guarantees server-side execution on the server thread.
        _visibilityByPlayer[info.sender] = isVisible;
        RecomputeAggregatesAndEmitIfChanged();
    }

    /// <summary>
    /// Recomputes the aggregate visibility across all connected players and
    /// fires events only on <b>positive edge</b> transitions (bit goes from 0 → 1).
    /// </summary>
    /// <remarks>
    /// - Ensures a dictionary entry exists for every known player (defaults to false).
    /// - Aggregates are encoded as bits for O(1) edge detection against previous mask.
    /// - If players can join/leave dynamically, this method naturally adapts as it
    ///   iterates networkManager.players each time.
    /// </remarks>
    private void RecomputeAggregatesAndEmitIfChanged()
    {
        int playerCount = 0, visibleCount = 0;

        // Ensure a slot for each current player; count visible ones.
        foreach (var id in networkManager.players)
        {
            playerCount++;
            if (_visibilityByPlayer.TryGetValue(id, out bool v))
            {
                if (v) visibleCount++;
            }
            else
            {
                // Default newly seen players to "not seeing" until their client reports.
                _visibilityByPlayer[id] = false;
            }
        }

        // Build new aggregate bitmask.
        byte newMask = 0;
        if (playerCount > 0)
        {
            if (visibleCount == playerCount) newMask |= ALL_VISIBLE;   // everyone sees it
            if (visibleCount == 0) newMask |= ALL_HIDDEN;    // no one sees it
            if (visibleCount > 0) newMask |= ANY_VISIBLE;   // at least one sees it
            if (visibleCount < playerCount) newMask |= ANY_HIDDEN;    // at least one does not see it
        }

        // Edge detection: we only emit when a bit flips from 0 → 1 to avoid event spam.
        byte added = (byte)(newMask & ~_aggMask);
        _aggMask = newMask;

        if ((added & ALL_VISIBLE) != 0)
        {
            OnAllVisionEnter?.Invoke();
        }
        if ((added & ALL_HIDDEN) != 0)
        {
            OnAllVisionLeft?.Invoke();
        }
        if ((added & ANY_VISIBLE) != 0)
        {
            OnAnyVisionEnter?.Invoke();
        }
        if ((added & ANY_HIDDEN) != 0)
        {
            OnAnyVisionLeft?.Invoke();
        }
    }

    // ---------------------- Public API ----------------------

    /// <summary>
    /// Sets the camera used for frustum checks on this client.
    /// Call this if you have per-player cameras instead of relying on Camera.main.
    /// </summary>
    public void SetCamera(Camera cam) => targetCamera = cam;

    /// <summary>
    /// Returns the current aggregate visibility flags as booleans.
    /// </summary>
    /// <remarks>
    /// This is a snapshot of the last computed mask on the server.
    /// </remarks>
    public (bool allVisible, bool allHidden, bool anyVisible, bool anyHidden) GetAggregates()
    {
        bool allVisible = (_aggMask & ALL_VISIBLE) != 0;
        bool allHidden = (_aggMask & ALL_HIDDEN) != 0;
        bool anyVisible = (_aggMask & ANY_VISIBLE) != 0;
        bool anyHidden = (_aggMask & ANY_HIDDEN) != 0;
        return (allVisible, allHidden, anyVisible, anyHidden);
    }
}
