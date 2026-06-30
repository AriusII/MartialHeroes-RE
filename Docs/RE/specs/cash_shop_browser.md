---
verification: CONSUMER-CONFIRMED for §5 (shop buy-toggle protocol, CYCLE 15, 2026-06-30; f61f66a9);
  §1–§4 (OLE/ActiveX hosting) remain static-hypothesis — re-verification pending
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-30
status: partial (§5 CONSUMER-CONFIRMED; §1–§4 static-hypothesis)
evidence: [static-ida, consumer-correlation]
note: |
  §1–§4 predate the CYCLE 14 re-anchor campaign; static-hypothesis, OLE/ActiveX hosting
  re-verification pending.
  CYCLE 15 (f61f66a9, 2026-06-30) — promotion lane P-cashshop: added §5 documenting the C2S 2/151
  buy-toggle selector mapping (selector 0 = gold/regular item shop, selector 200 = cash/Diamond goods
  panel) and the two S2C replies (3/8 SmsgShopPageUpdate, 4/113 SmsgItemShopPurchaseResult).
  CONSUMER-CONFIRMED via reply-consumer correlation. Server-side selector->reply binding is R-CAP
  (capture-pending, non-blocking — see §5.3).
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

---

## 5. Shop buy-toggle protocol — `CmsgProductBuy` (C2S 2/151) [CONSUMER-CONFIRMED, CYCLE 15]

Two shop subsystems share the wire opcode **C2S major 2 / minor 151** (`CmsgProductBuy`),
distinguished by a single-byte selector field. The body is 1 byte:

| offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| +0 | 1 | u8 | `select_flag` | Subsystem selector: `0` = gold/regular item shop; `200` (0xC8) = cash/Diamond goods panel. **CONFIRMED (two-selector split, CYCLE 15).** |

The two selector values correspond to two independent shop subsystems that happen to share this
opcode. They are not interchangeable paths to the same server handler.

### 5.1 Selector `0` — gold/regular item shop: `SmsgShopPageUpdate` (S2C 3/8)

Selector `0` is sent by the regular item-shop buy-confirm flow. The server replies with **S2C
major 3 / minor 8** `SmsgShopPageUpdate`, which refreshes the active shop panel's item rows
(rebuilding the shop display). *(CONSUMER-CONFIRMED: the reply consumer rebuilds the same shop
panel rows that the selector-0 send path operates on — consumer-side panel correlation.)*

### 5.2 Selector `200` (0xC8) — cash/Diamond goods panel: `SmsgItemShopPurchaseResult` (S2C 4/113)

Selector `200` (hex 0xC8) is sent by the cash/Diamond goods panel buy path; the sender sets a
panel-state flag before sending. The server replies with **S2C major 4 / minor 113**
`SmsgItemShopPurchaseResult`, a **12-byte** body carrying a purchase outcome. *(CONSUMER-CONFIRMED:
the reply consumer operates on the cash/Diamond domain matching the selector-200 sender context.)*

`SmsgItemShopPurchaseResult` body layout (12 bytes):

| payload offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| +8 | 1 | u8 | `success_flag` | `1` = success (posts a purchase-success notice); any other value = failure. **CONFIRMED.** |
| +9 | 1 | u8 | `failure_subtype` | Failure sub-code `0..4`, each mapping to a distinct localized error message. **CONFIRMED present; exact per-code meanings are RUNTIME-ONLY (R-CAP).** |
| +0..+7, +10..+11 | 10 | — | (unconsumed) | Not read by any recovered consumer; meaning is capture-only. **UNVERIFIED.** |

### 5.3 Opcode-reply matrix and refutation

`CmsgProductBuy` (2/151) and `CmsgProductConfirm` (2/153) are distinct opcodes with disjoint
reply sets:

| C2S opcode | selector | S2C reply | notes |
|---|---|---|---|
| 2/151 `CmsgProductBuy` | `0` | 3/8 `SmsgShopPageUpdate` | gold/regular shop page refresh |
| 2/151 `CmsgProductBuy` | `200` (0xC8) | 4/113 `SmsgItemShopPurchaseResult` | cash/Diamond purchase result |
| 2/153 `CmsgProductConfirm` | — (no selector) | 4/79 `SmsgCraftingResult` (52 bytes) | production commit result — see `specs/crafting.md §4` |

The hypothesis that `2/153` is answered by `3/8` or `4/113` is **REFUTED** (CYCLE 15): those are
replies to `2/151`. `2/153` is answered solely by `4/79`.

> **R-CAP (capture-pending, non-blocking):** The server-side rule "selector value X causes server
> to send reply Y" is consumer-correlated but not request-stamped in the wire frame — the client
> never embeds an expected-reply field. A live capture of one gold-shop buy (selector `0`) and one
> cash-shop buy (selector `200`) would byte-confirm the server binding. Breakpoint plan: intercept
> the S2C 3/8 and S2C 4/113 handler entry points after sending each selector variant; correlate
> request-reply pairs in the capture stream.

### 5.4 Sibling cash-shop opcodes (not analyzed in this lane)

| opcode | name | notes |
|---|---|---|
| S2C 4/114 | `SmsgCashShopActionResult` | Sibling cash-shop action result — out of scope for this promotion lane |
| S2C 4/115 | `SmsgItemShopBalanceUpdate` | Cash/item-shop balance update — out of scope for this promotion lane |
