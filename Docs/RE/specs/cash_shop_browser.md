---
verification: static-hypothesis, unverified at f61f66a9
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
status: unverified
evidence: [static-ida]
note: This spec predates the CYCLE 14 re-anchor campaign. All stated facts are static-hypothesis
      only and have not been re-confirmed against the current IDB. Re-verification is pending.
---

# Cash Shop Browser (OLE Web Container)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Client.Application` (cash-shop session state) and by
> `05.Presentation/MartialHeroes.Client.Godot` (rendering the browser overlay).

## Overview
The Cash Shop Browser is implemented as an embedded Internet Explorer (ActiveX) control within the client. It consists of:
- `CWebContainer`: The main OLE container class implementing the client sites and interface sinks required for hosting an ActiveX control.
- `CWebEventSink`: An event sink implementing `IDispatch` to receive events from the hosted WebBrowser control.
- A custom window procedure that handles basic window messages and manages the lifecycle of the container.

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
- **`+0x14`**: Reserved / Padding (zero-initialized)
- **`+0x18`**: Parent Window Handle (`HWND`) — passed during initialization
- **`+0x1C`**: Reserved / Padding (zero-initialized)
- **`+0x20`** (to **`+0x2F`**): `RECT` structure (left, top, right, bottom) storing the container's boundaries.
- **`+0x30`**: Pointer to the `CWebEventSink` instance (`CWebEventSink*`).

### `CWebEventSink`
The event sink is a standalone COM object allocated by `CWebContainer` to listen to `DWebBrowserEvents2`.
Its layout is:
- **`+0x00`**: Vtable pointer implementing `IDispatch`
- **`+0x04`**: Volatile reference count (`LONG`) updated via `InterlockedIncrement` / `InterlockedDecrement`.

---

## 2. Window Procedure and Lifecycle

The Cash Shop browser window is managed by a dedicated window procedure. It stores its state within a window wrapper structure:
- **`+0x00`**: Window Handle (`HWND` of the container window)
- **`+0x0C`**: Stored window width (`cx`)
- **`+0x10`**: Stored window height (`cy`)
- **`+0x14`**: Pointer to `CWebContainer` (`CWebContainer*`)
- **`+0x18`**: Pointer to the queried `IWebBrowser2` interface (`IWebBrowser2*`)

### Message Handling

#### `WM_CREATE`
When the window is created:
1. Allocates 0x38 bytes of memory for `CWebContainer`.
2. Calls the `CWebContainer` constructor to initialize the vtables and set up the empty bounding rectangle.
3. Allocates `CWebEventSink` (8 bytes), sets its vtable, and initializes the reference count to 0.
4. Stores the container pointer at offset `+0x14` and increments its reference count.
5. Calls `CoCreateInstance` with `CLSID_WebBrowser` to create the ActiveX control, sets up OLE document hosting and client sites, and connects `CWebEventSink` to the browser's event source.
6. Queries the container for the `IWebBrowser2` interface and stores the result at offset `+0x18`.
7. Resolves the initial URL from `CREATESTRUCT::lpszName` (passed via `lParam`).
8. Navigates the browser to the resolved URL.
9. Latches the width (`cx`) and height (`cy`) from `CREATESTRUCT` into offsets `+0x0C` and `+0x10`.

#### `WM_DESTROY`
When the window is destroyed:
1. If the `IWebBrowser2` interface (at offset `+0x18`) is valid, calls `Release()` on it.
2. If the `CWebContainer` instance (at offset `+0x14`) is valid, deactivates and closes the embedded OLE object, then calls `Release()` on the container.

#### `WM_SIZE`
Resizes the browser control:
1. Passes the `CWebContainer` pointer, origin `(0, 0)`, and the latched width and height to the resize helper.
2. The resize helper updates the `RECT` at offset `+0x20` of the container, retrieves the `IOleInPlaceSite` interface, and calls `SetObjectRects` to resize the ActiveX window.

#### `WM_SETTEXT`
Allows manual navigation by sending `WM_SETTEXT` to the window:
1. Calls the navigation function with `lParam` as the URL string.

#### `WM_PARENTNOTIFY`
1. If the `IWebBrowser2` pointer is valid, queries or logs the current state.

---

## 3. Navigation Function

The navigation function performs browser navigation to a given ANSI URL string:
1. **Validation:** Checks that the URL string is valid and non-empty.
2. **String Conversion:** Calculates the character length; allocates a wide-character buffer; converts the URL from ANSI to UTF-16 using `MultiByteToWideChar` with code page `CP_ACP`.
3. **BSTR Allocation:** Allocates a `BSTR` from the wide string via `SysAllocString`.
4. **Variant Setup:** Initializes a `VARIANTARG` as `VT_BSTR` pointing to the `BSTR`.
5. **Navigate:** Invokes `IWebBrowser2::Navigate` (public COM vtable slot 208 / `0xD0`) on the stored `IWebBrowser2` interface, passing the URL variant and four empty parameters.
6. **Cleanup:** Frees the temporary wide-character buffer and calls `VariantClear` on the variant.

---

## 4. `CWebEventSink` COM Interface Slots

`CWebEventSink` implements the `IDispatch` interface to receive event callbacks.

### `QueryInterface`
Queries the sink for supported interfaces:
- Checks if the requested IID matches `IID_IUnknown` or `IID_IDispatch`.
- On match: sets the output pointer to `this`, calls `AddRef`, and returns `S_OK` (0).
- On mismatch: sets the output pointer to null and returns `E_NOINTERFACE` (`0x80004002`).

### `AddRef`
Increments the reference count:
- Performs an atomic increment on the reference count field at `+0x04` using `InterlockedIncrement`.
- Returns the new reference count.

### `Release`
Decrements the reference count:
- Performs an atomic decrement on the reference count field at `+0x04` using `InterlockedDecrement`.
- If the count reaches 0, frees the sink's memory.
- Returns the remaining reference count.

### `IDispatch` Standard Slots (Stubs)
- **`GetTypeInfoCount`**: Returns `E_NOTIMPL` (`0x80004001`).
- **`GetTypeInfo`**: Returns `E_NOTIMPL` (`0x80004001`).
- **`GetIDsOfNames`**: Sets the output argument to `-1` and returns `DISP_E_MEMBERNOTFOUND` (`0x80020006`).
- **`Invoke`**: Event notification receiver. Returns `S_OK` (0) to acknowledge browser events.
