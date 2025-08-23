# Network Clock (PurrNet Tool)

Authoritative, host-driven time source for PurrNet projects in Unity.  
Clients reconstruct the host/server time locally so time-based gameplay, VFX/SFX, and UI timers stay in sync with the host.

> **Dependency:** PurrNet must be present in your project. It is **not** included in this repository.

---

## Requirements
- **Unity:** 2023.3+ (Unity 6 recommended)  
- **.NET:** C# 8 / .NET Standard 2.1  
- **PurrNet:** installed in your project

---

## Files
Assets/
Network Clock/
NetworkClock.cs
---

## Installation
Copy the `Network Clock` folder into your project’s `Assets/`.  
(If this repository is a submodule in your project, you can reference the folder directly from there.)

---

## Setup
1. Start a PurrNet session as Host/Server/Client in your usual bootstrap flow.
2. Create a GameObject (e.g., **NetworkClock**) in a bootstrap scene and add the `NetworkClock` component.
3. *(Optional)* Configure in the inspector:
   - **`tickEveryUpdate` (int)**  
     - `0` — sample every `Update` on the host/server (highest accuracy, default)  
     - `N > 0` — sample every N frames (fewer writes, lower update frequency)
4. Access the clock from anywhere in code.

---

## Public API
- `float NetTime.ServerTime` — Authoritative server/host time in seconds.  
- `float NetworkClock.TryGetTime()` — Safe getter; returns the authoritative time and logs a helpful message if the clock isn’t ready.

---

## Quick use
```csharp
// Authoritative host/server time (seconds)
float t = NetTime.ServerTime;

// Safe getter variant
float t2 = NetworkClock.TryGetTime();

Typical uses: drive fades/shader params/animation curves, or compute countdowns/timers that must match the host.

How it works (high level)

The host/server periodically snapshots its local Time.time and the synchronized network tick.

Clients reconstruct host time by adding elapsed ticks (converted to seconds via the tick rate) to the last snapshot.

Until the first snapshot arrives, clients use the last replicated value.

Troubleshooting

“No instance was found for NetworkClock” in logs
Add the component to your scene (host and clients) and ensure your PurrNet session has started.

Time does not appear to update on clients
Confirm the session is active and that the GameObject with NetworkClock exists and is not disabled.
