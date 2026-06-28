# Cash Shop Browser (OLE Web Container)

## Overview
The Cash Shop Browser is implemented as an embedded Internet Explorer (ActiveX) control within the client. It consists of:
- `CWebContainer`: The main OLE container class implementing the client sites and interface sinks required for hosting an ActiveX control.
- `CWebEventSink`: An event sink implementing `IDispatch` to receive events from the hosted WebBrowser control.
- A custom window procedure (`WndProc` at `0x510448`) that handles basic window messages and manages the lifecycle of the container.

---

## 1. Class Structures and Layout

### `CWebContainer`
The `CWebContainer` class implements multiple COM interfaces for OLE hosting.
Its layout in memory is:
- **`+0x00`**: Vtable pointer for interface 0 (`IOleClientSite` / `IUnknown`)
- **`+0x04`**: Vtable pointer for interface 1 (`IOleInPlaceSite`)
- **`+0x08`**: Vtable pointer for interface 2 (`IOleInPlaceFrame`)
- **`+0x0C`**: Vtable pointer for interface 3 (`IDocHostShowUI`)
- **`+0x10`**: Vtable pointer for interface 4 (`IDispatch`)
- **`+0x14`**: Reserved / Padding (`0` initialized)
- **`+0x18`**: Parent Window Handle (`HWND`) — passed during initialization
- **`+0x1C`**: Reserved / Padding (`0` initialized)
- **`+0x20`** (to **`+0x2F`**): `RECT` structure (left, top, right, bottom) storing the container's boundaries. (Note: in the decompiled code, this starts at offset `0x20` / `this + 2` where `this` is cast to `tagRECT*`, but is conceptually defined at `+0x14` in the overall window layout).
- **`+0x30`**: Pointer to the `CWebEventSink` instance (`CWebEventSink*`).

### `CWebEventSink`
The event sink is a standalone COM object allocated by `CWebContainer` to listen to DWebBrowserEvents2.
Its layout is:
- **`+0x00`**: Vtable pointer (`&CWebEventSink::vftable`) implementing `IDispatch`
- **`+0x04`**: Volatile reference count (`LONG`) updated via `InterlockedIncrement` / `InterlockedDecrement`.

---

## 2. Window Procedure & Lifecycle (`WndProc` @ `0x510448`)

The Cash Shop browser window is managed by the window procedure `sub_510448`. It stores its state within a window wrapper structure:
- **`+0x00`**: Window Handle (`HWND` of the container window)
- **`+0x0C`**: Stored window width (`cx`)
- **`+0x10`**: Stored window height (`cy`)
- **`+0x14`** (offset 20): Pointer to `CWebContainer` (`CWebContainer*`)
- **`+0x18`** (offset 24): Pointer to the queried `IWebBrowser2` interface (`IWebBrowser2*`)

### Message Handling Switch

#### `WM_CREATE` (`case 1u`)
When the window is created:
1. Allocates `0x38` bytes of memory for `CWebContainer`.
2. Calls the constructor `CWebContainer__ctor_1` to initialize the vtables and set up the empty bounding rectangle.
3. Spawns `CWebEventSink` (allocated via `operator new(8u)`), sets its vtable and initializes reference count to 0.
4. Stores the container pointer at `this + 0x14` and increments its reference count.
5. Invokes helper `sub_510334` to:
   - Call `CoCreateInstance` with `CLSID_WebBrowser` to create the ActiveX control.
   - Set up OLE document hosting and client sites.
   - Connect the `CWebEventSink` to the browser's event source.
6. Calls `sub_50FEE9` to query the container for the `IWebBrowser2` interface (GUID `unk_734858`) and stores it at `this + 0x18`.
7. Resolves the initial URL from `CREATESTRUCT::lpszName` (passed at `lParam + 36`).
8. Calls the navigation helper `sub_50FF2A` with the resolved URL.
9. Latches the width `cx` and height `cy` from `CREATESTRUCT` (offsets `20` and `16`) into `this + 0x0C` and `this + 0x10`.

#### `WM_DESTROY` (`case 2u`)
When the window is destroyed:
1. If the `IWebBrowser2` interface (`this + 0x18`) is valid, calls `Release()`.
2. If the `CWebContainer` instance (`this + 0x14`) is valid, calls helper `sub_5103DA` to deactivate and close the embedded OLE object, then calls `Release()` on the container.

#### `WM_SIZE` (`case 5u`)
Resizes the browser control:
1. Invokes helper `sub_50FDC2` passing the `CWebContainer` pointer, `(0, 0)` for origin, and the latched width (`this + 0x0C`) and height (`this + 0x10`).
2. Inside `sub_50FDC2`, updates the RECT at offset `0x20` of the container, retrieves the in-place site interface (GUID `unk_734508`), and calls its `SetObjectRects` method to resize the ActiveX window.

#### `WM_SETTEXT` (`case 0x0C`)
Allows manual navigation by sending `WM_SETTEXT` to the window:
1. Calls navigation function `sub_50FF2A` with `lParam` (the URL string).

#### `WM_PARENTNOTIFY` (`case 0x210`)
1. If the `IWebBrowser2*` is valid, queries current state or logs parameters.

---

## 3. Navigation Function (`sub_50FF2A`)

The function `sub_50FF2A` performs browser navigation to a given ANSI URL string:
```cpp
LPCSTR __thiscall sub_50FF2A(_DWORD **this, LPCSTR lpString)
```
1. **Validation:** Checks if `lpString` is valid and not empty.
2. **String Conversion:**
   - Calculates the character length.
   - Allocates wide-char buffer memory (`operator new`).
   - Converts the URL from ANSI to UTF-16 using `MultiByteToWideChar` with code page `CP_ACP` (0).
3. **BSTR Allocation:** Allocates a BSTR from the wide string via `SysAllocString`.
4. **Variant Setup:** Initializes `VARIANTARG pvarg` as `VT_BSTR` (8) pointing to the BSTR.
5. **Call Navigate:** Invokes `IWebBrowser2::Navigate` (vtable offset `208` / `0xD0`) on the WebBrowser interface stored at `this + 24` (or `*(this + 6)` which is `this + 24`), passing the URL Variant and four empty/null parameters.
6. **Cleanup:** Frees the temporary wide-char buffer and calls `VariantClear(&pvarg)`.

---

## 4. `CWebEventSink` COM Interface Slots

`CWebEventSink` implements the `IDispatch` interface to receive event callbacks.

### `QueryInterface` (`CWebEventSink__VFunc_00` @ `0x510254`)
Queries the sink for supported interfaces:
- Checks if the requested IID (`Buf1`) matches `IID_IUnknown` or `IID_IDispatch` (represented by `unk_7349F8` and `riid`).
- On match: sets output pointer `*a3 = this`, calls `AddRef` via its vtable slot, and returns `S_OK` (0).
- On mismatch: sets `*a3 = 0` and returns `E_NOINTERFACE` (`0x80004002` / `-2147467262`).

### `AddRef` (`CWebEventSink__VFunc_01` @ `0x5101fb`)
Increments the reference count:
- Performs an atomic increment on the reference count field at `this + 0x04` using `InterlockedIncrement`.
- Returns the new reference count.

### `Release` (`CWebEventSink__VFunc_02` @ `0x51020c`)
Decrements the reference count:
- Performs an atomic decrement on the reference count field at `this + 0x04` using `InterlockedDecrement`.
- If the count reaches 0, frees the sink's memory using `j__free`.
- Returns the remaining reference count.

### `IDispatch` Standard Slots (Stubs)
- **`GetTypeInfoCount` (`VFunc_03` @ `0x510247`)**: Returns `E_NOTIMPL` (`0x80004001`).
- **`GetTypeInfo` (`VFunc_04` @ `0x51023f`)**: Returns `E_NOTIMPL` (`0x80004001`).
- **`GetIDsOfNames` (`VFunc_05` @ `0x510230`)**: Sets output argument to `-1` and returns `DISP_E_MEMBERNOTFOUND` (`0x80020006`).
- **`Invoke` (`sub_51024F` @ `0x51024f`)**: Event notification receiver. Returns `S_OK` (0) to swallow or acknowledge browser events.
