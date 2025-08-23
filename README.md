# PurrNet Tools (Unity)

Small, focused utilities for building multiplayer projects with **PurrNet** in Unity.  
Each tool is self-contained so you can copy only what you need.

> **Note:** PurrNet is a dependency and is **not** included in this repository.

---

## Overview
This repository aggregates independent tools that solve common networking needs when using PurrNet (e.g., host-time access, simple diagnostics, UI glue).  
Tools are lightweight and modular; import them folder-by-folder.

**Current tools**
- **Network Clock** — Authoritative host/server time exposed to all peers for synchronized effects (timers, fades, VFX/SFX).  
  See **[Assets/Network Clock/README.md](./Assets/Network%20Clock/README.md)**.

---

## Requirements
- **Unity:** 2023.3+ (Unity 6 recommended)  
- **.NET:** C# 8 / .NET Standard 2.1  
- **PurrNet:** installed in your project (not bundled here)

---

## Installation
Choose one:

### Copy-paste (simplest)
Copy the folder of the tool you want into your project’s `Assets/`.

Example: Assets/Network Clock/NetworkClock.cs

### Git submodule (keep updates simple)
Add this repository as a submodule inside your Unity project (any folder under `Assets` is fine):

```bash
# from your Unity project root
mkdir -p Assets/External
git submodule add https://github.com/OverGast/PurrNet-Tools.git Assets/External/PurrNet-Tools
```

Then reference only the tool folders you need (e.g., Assets/External/PurrNet-Tools/Assets/Network Clock).

## Using the tools
Each tool ships with its own README containing setup instructions and notes.
Start with Assets/Network Clock/README.md
