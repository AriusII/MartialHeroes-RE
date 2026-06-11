# Architectural Specification & Preservation Blueprint: Martial Heroes Restoration

This document establishes the comprehensive legal, philosophical, and technical framework for the *Martial Heroes* (originally *D.O. Online*) digital preservation initiative. Operating in the year **2026**, nearly two decades after the game’s official European services ceased operations in late **2008**, this project serves as a digital archaeology effort.

The primary objective is to analyze the legacy 2004 client binaries using modern reverse-engineering tools (**IDA Pro 9.3** paired with a centralized **Metadata Collaboration Platform / MCP Server**) to document historical systems and reconstruct a completely clean-room, open-source client using **.NET 10**, **C# 14**, and **Godot 4.x+**.

---

## 1. Executive Summary & Project Context

### Historical Context
*Martial Heroes* was an influential Asian martial arts MMORPG that ran commercially from 2004 until its termination in December 2008. When the official servers were shut down, the software became completely non-functional due to its hard dependency on a server-side handshake. Over the last 18 years, the original source code, database schemas, and server distributions have either been lost or remained trapped within defunct corporate entities.

### Project Goals
1. **Digital Preservation:** Safeguard the unique asset layouts, design paradigms, and netcode patterns of a mid-2000s MMORPG before they disappear from internet history.
2. **Academic & Engineering Analysis:** Study the constraints of a 2004-era game engine built for low-end hardware, single-core CPUs, and fixed-function rendering pipelines.
3. **Clean-Room Reconstruction:** Document the protocol and asset formats to build a brand-new engine, ensuring no copyrighted compiled code from the original executables is reused or redistributed.

---

## 2. Legal Framework & Compliance

Operating a software preservation project requires strict compliance with intellectual property laws, user privacy standards, and reverse-engineering exceptions.

### Market Reality & Fair Use Considerations
* **Defunct Commercial Footprint:** There has been zero commercial exploitation, active licensing, or server availability of *Martial Heroes* anywhere in Europe or globally since late 2008.
* **Zero Market Subversion:** Because there is no active commercial product or marketplace for this intellectual property, a non-profit preservation project inflicts zero financial harm or market cannibalization on the original rights holders.

### Decompilation Rights for Interoperability (EU Law)
For developers operating within Europe, the legal basis for analyzing the legacy binary relies on the **EU Software Directive (Directive 2009/24/EC)**, specifically **Article 6 (Decompilation)**.

> [!IMPORTANT]
> Under Article 6 of Directive 2009/24/EC, decompilation and reverse engineering of an executable do not require right holder authorization if performed exclusively to achieve the interoperability of an independently created computer program with other programs, provided that:
> 1. The actions are performed by a licensee or a person having a right to use a copy of the program.
> 2. The information necessary to achieve interoperability has not previously been readily available.
> 3. The modifications are strictly confined to the parts of the original program required to achieve interoperability.

This initiative fulfills these statutory conditions:
* **Interoperability Focus:** The analysis of `Main.exe` is conducted solely to map network payload structures (opcodes, packet layouts) and asset archive structures. This documentation allows a newly engineered application (the server emulator and the new client) to interoperate.
* **Non-Redistribution Policy:** This project does not distribute copyrighted game files, archives (`.pak`), or binaries (`.exe`, `.dll`). Users must provide their own historical assets to use the extraction pipelines.

---

## 3. The Philosophy of Digital Game Preservation

Video games are complex, multi-disciplinary cultural artifacts. Unlike physical media, online-only software faces absolute destruction upon server termination.

### Cultural Imperative
Reconstructing *Martial Heroes* preserves a specific milestone in the evolution of multiplayer online games, documenting:
* **Vintage Network Optimization:** Understanding how early MMORPGs achieved state synchronization across low-bandwidth dial-up and early broadband connections.
* **Low-Poly Asset Paradigms:** Showcasing how massive worlds were packed into tight archive sizes using customized vertex layouts and heavily compressed texture sets.

---

## 4. Modern Reverse Engineering Toolchain

To execute static and dynamic analysis with 2026 precision, the engineering workflow integrates modern industry-standard toolsets.

```text
+-----------------------+      Sync Metadata      +-------------------------+
|     IDA Pro 9.3       | <=====================> |   MCP Server (Shared)   |
|  (Static Decompilation)|                         | (Collaborative Database)|
+-----------------------+                         +-------------------------+
            │
            │ Export Enums, Struct Layouts, and Opcodes
            ▼
+---------------------------------------------------------------------------+
|                        MartialHeroes C# Ecosystem                         |
+---------------------------------------------------------------------------+
```

### IDA Pro 9.3 (Static Analysis)
The legacy client is a native 32-bit (x86) Windows PE executable compiled via early versions of Microsoft Visual C++. IDA Pro 9.3 acts as the foundational static analysis layer due to:
* **FLIRT Signatures:** Instantly identifies and filters out compiler-generated standard library routines, leaving only the unique game engine logic exposed.
* **Microcode Decompilation:** Reconstructs optimized assembly sequences into high-fidelity C++ pseudo-code, significantly accelerating object model discovery.
* **Object VTable Reconstruction:** Resolves legacy `__thiscall` calling conventions, mapping object instances passed via the `ECX` register back to their conceptual class structures.

### Metadata Collaboration Platform (MCP Server)
Because reversing an entire MMO client involves thousands of subroutines, team collaboration is unified via an MCP Server:
* **Real-Time Database Synchronization:** When an analyst renames a variable, refactors a struct layout, or documents a packet opcode in IDA Pro 9.3, the metadata synchronizes globally in real-time.
* **Atomic Analysis Locks:** Prevents multi-developer conflicts by locking specific function nodes while an analyst steps through their execution path.
* **Type Pipeline Automation:** Streamlines exporting C-style headers from the collaborative database directly into format files that feed the C# Source Generators.

---

## 5. Architectural Specification: `MartialHeroes.slnx`

The modern client architecture enforces strict decoupling using a multi-project solution structure designed with **.NET 10** and **C# 14**.

### Architectural Solution Diagram
```text
MartialHeroes.slnx
├── 📁 01.Infrastructure.Shared/
│   ├── MartialHeroes.Shared.Kernel            (Primitives, Global Enums, Strongly Typed IDs)
│   └── MartialHeroes.Shared.Diagnostics       (OpenTelemetry, High-Performance Logging)
├── 📁 02.Network.Layer/
│   ├── MartialHeroes.Network.Abstractions     (Transport Contracts, Session Interfaces)
│   ├── MartialHeroes.Network.Protocol         (Opcodes, Sequenced Packed Layouts, Inline Arrays)
│   ├── MartialHeroes.Network.Crypto           (In-Place Sliding XOR/Stream Decryption)
│   └── MartialHeroes.Network.Transport.Pipe   (System.IO.Pipelines Socket Implementation)
├── 📁 03.Storage.Assets/
│   ├── MartialHeroes.Assets.Vfs               (Virtual File System, .PAK Multi-Mapping Memory)
│   ├── MartialHeroes.Assets.Parsers           (Binary Stream Decoders for Mesh/Terrain/Animations)
│   └── MartialHeroes.Assets.Mapping           (Runtime Model Transformers to GLTF/PNG/JSON)
├── 📁 04.Client.Core/
│   ├── MartialHeroes.Client.Domain            (Pure Entities, Stats Formulae, State Machines)
│   ├── MartialHeroes.Client.Application       (Use Cases, Network Packet Handlers, Feature Services)
│   └── MartialHeroes.Client.Infrastructure    (Local Configuration, SQLite Offline States)
└── 📁 05.Presentation/
    └── MartialHeroes.Client.Godot             (Godot 4.x Rendering Engine, Controls, UI Canvas)
```

---

## 6. Granular Project Breakdowns

### 📁 01. Infrastructure.Shared Layer

#### `MartialHeroes.Shared.Kernel`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** None
* **Technical Mandate:** Defines cross-cutting primitive constraints, core enums (`CharacterClass`, `ItemType`), and global game constants. All domain identifiers utilize C# 14 `readonly record struct` definitions acting as strongly typed IDs to ensure type safety across memory models:
  ```csharp
  public readonly record struct PlayerId(Guid Value);
  ```

#### `MartialHeroes.Shared.Diagnostics`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `Microsoft.Extensions.Logging.Abstractions`, `System.Diagnostics.DiagnosticSource`
* **Technical Mandate:** Implements zero-allocation structured tracing and telemetry. Utilizes compile-time source generators to eliminate string formatting memory allocations during high-frequency telemetry loops:
  ```csharp
  [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Actor {ActorId} moved to {Position}")]
  public static partial void LogActorMovement(ILogger logger, int actorId, string position);
  ```

---

### 📁 02. Network.Layer

#### `MartialHeroes.Network.Abstractions`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Shared.Kernel`
* **Technical Mandate:** Declares contracts for stream boundaries, session contexts, and I/O handlers. It remains completely transport-agnostic, allowing the system to run on top of TCP, reliable UDP, or local offline simulation streams without affecting upstream code.

#### `MartialHeroes.Network.Crypto`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Shared.Kernel`
* **Technical Mandate:** Encapsulates the network transformation algorithms discovered through reverse engineering. Methods operate purely on memory slices using `Span<byte>` and `ReadOnlySpan<byte>` to execute in-place mutation, avoiding Heap allocations entirely:
  ```csharp
  public static void DecryptInPlace(Span<byte> packetData, uint rollingKey);
  ```

#### `MartialHeroes.Network.Protocol`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Shared.Kernel`
* **Technical Mandate:** Defines the structural memory layouts of network packets exactly as expected by the legacy application.
    * Packets implement rigid data sequence alignments via `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.
    * Fixed-size character or item buffers are declared via C# **Inline Arrays** to maintain deterministic memory layout size bounds without invoking managed string instantiations:
      ```csharp
      [InlineArray(32)]
      public struct CharacterNameBuffer
      {
          private byte _element0;
      }
      ```
    * Utilizes Roslyn **Source Generators** to map network opcodes directly to their packet processing handlers at compile-time, completely avoiding the overhead of runtime reflection.

#### `MartialHeroes.Network.Transport.Pipe`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Network.Abstractions`, `System.IO.Pipelines`
* **Technical Mandate:** Handles high-throughput asynchronous network stream parsing. Uses `PipeReader` and `PipeWriter` to read bytes directly from Windows or Linux sockets, managing backpressure automatically and outputting clean, framed byte windows to the protocol layers.

---

### 📁 03. Storage.Assets Layer

#### `MartialHeroes.Assets.Vfs`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** None
* **Technical Mandate:** Virtual File System implementation that indexes internal directory offsets inside the game's native `.pak` or `.dat` archive blocks. Exposes files safely through memory-mapped architectures using `ReadOnlyMemory<byte>`, ensuring massive data archives can be read instantly without loading entire gigabyte files into RAM.

#### `MartialHeroes.Assets.Parsers`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Assets.Vfs`
* **Technical Mandate:** Decodes binary streams into unified structured data sets. Houses procedural logic to read custom vertex layouts, skeleton rig nodes, skin weights, map tile configurations, and legacy texture headers (e.g., custom `.tga` configurations). It contains zero rendering dependencies.

#### `MartialHeroes.Assets.Mapping`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Assets.Parsers`
* **Technical Mandate:** Bridges raw parsed assets with modern industry ecosystems. Transforms internal proprietary meshes into memory-compliant `.gltf` serialization streams and uncompressed legacy textures into standard raw `.png` structures at runtime.

---

### 📁 04. Client.Core Layer

#### `MartialHeroes.Client.Domain`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Shared.Kernel`
* **Technical Mandate:** Represents the absolute mathematical and logical truth of the game world. Tracks state trees for entities (`Player`, `Npc`, `Monster`), inventory placement rules, and combat statistics formulas. Written in 100% deterministic C# with no dependencies on outside platforms.

#### `MartialHeroes.Client.Application`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Client.Domain`, `MartialHeroes.Network.Abstractions`
* **Technical Mandate:** Orchestrates macro-level features and application use cases. Intercepts incoming network structures, updates the underlying `Client.Domain` engine state, and dispatches UI notifications over fast, unmanaged event buses powered by `System.Threading.Channels`.

#### `MartialHeroes.Client.Infrastructure`
* **System Type:** Class Library (`net10.0`)
* **Dependencies:** `MartialHeroes.Client.Application`
* **Technical Mandate:** Manages host machine dependencies, local SQLite configuration databases, localized client settings caching, and local macro parsing.

---

### 📁 05. Presentation Layer

#### `MartialHeroes.Client.Godot`
* **System Type:** Godot 4.x Engine Assembly (C# Enabled)
* **Dependencies:** `MartialHeroes.Client.Application`, `MartialHeroes.Assets.Mapping`
* **Technical Mandate:** Acts as a strictly visual, passive presentation container. It maintains zero authority over game business rules.
    * Translates input captures directly into execution triggers (e.g., hotbar slot activation directly signals `IApplicationUseCases.ExecuteSkill(slotId)`).
    * Monopolizes event channels exposed by the application layer, using them to update position frames on visual `Node3D` configurations or redraw health bars on user interface canvas spaces.

---

## 7. High-Performance Zero-Allocation Pipeline

By structuring data pathways exclusively around modern .NET memory primitives, network data flows seamlessly from the kernel level down to the frame presentation without triggering Garbage Collector allocations.

```text
[Operating System Socket Buffer]
                │
                │ Pushes raw binary byte stream via PipeReader
                ▼
[MartialHeroes.Network.Transport.Pipe]
                │
                │ Frames data windows and isolates complete message packets
                ▼
[MartialHeroes.Network.Crypto]
                │
                │ Mutates framed buffer in-place using Span<byte> (No Allocations)
                ▼
[MartialHeroes.Network.Protocol]
                │
                │ Compile-time structural type cast via Source Generated Router
                ▼
[MartialHeroes.Client.Application]
                │
                │ Validates structure timing data and fires transactional handler
                ▼
[MartialHeroes.Client.Domain]
                │
                │ Updates structural vectors of targeted Entity states
                ▼
[MartialHeroes.Client.Godot]
                (Updates the spatial transforms of the associated Node3D on the next frame)
```

---

## 8. Architectural Advantages & Core Benefits

1. **Absolute Decoupling:** The entire client state, parsing pipeline, and network engine are fully operational without a graphics engine. Game systems can be developed, simulated, and tested via automated test suites (`xUnit`) without ever opening the Godot Engine.
2. **Zero GC Stutter:** Leveraging `Span<T>` and fixed-size layout memory mapping prevents the runtime memory fragmentation that typically plagues managed game implementations during massive multiplayer operations.
3. **Dual-Core Utility for Server Creation:** Because the network, cryptography, asset parser, and kernel projects are completely free of client rendering context, they can be directly referenced by the server project (`MartialHeroes.Server.Console`). This completely removes data structure code duplication between client and server architectures.