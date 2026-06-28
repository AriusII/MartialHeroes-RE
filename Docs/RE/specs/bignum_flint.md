---
verification: confirmed
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
---

# FLINT++ Bignum Engine Specification

This document details the reverse-engineered specification of the FLINT/LINT multi-precision integer (bignum) library incorporated in the client. This library is critical for client-server authentication, particularly the RSA handshake wrapper.

---

## 1. LINT Grand Number Structure

The `LINT` class wraps a C-style `CLINT` structure consisting of an error status and a pointer to a little-endian digit array.

### 1a. Object Field Layout
At the C++ object level, the `LINT` structure has a memory footprint of **8 bytes**:

| Offset | Type | Role |
|---|---|---|
| **+0x00** | `unsigned short *` | Pointer to the digit buffer (`n_l` array). |
| **+0x04** | `int` | **Error Status** (0 = OK, 512 = uninitialized, 128 = overflow). |

### 1b. Digit Buffer Layout (`n_l`)
The digit buffer is an array of 16-bit words (`unsigned short`), ordered **little-endian** (Least Significant Digit first) with a leading length word:

- **`n_l[0]` (offset 0):** The **length word** (16-bit). Specifies the number of active 16-bit digits (words) currently representing the grand number.
- **`n_l[1]` (offset 2):** The **Least Significant Digit (LSD)**.
- **`n_l[2..n_l[0]]`:** Remaining digits in ascending order of significance.
- **Buffer Sizing:** For a standard 4096-bit maximum capacity, `CLINTMAXDIGIT = 256` words. The buffer allocation size is `257` words (**514 bytes**), accommodating `n_l[0]` through `n_l[256]`.
- **Odd Modulus Test:** Since Montgomery arithmetic requires an odd modulus, the oddness test performs `*n_l && (n_l[1] & 1)` — validating that the length word is non-zero and the least significant bit of the LSD is set.

---

## 2. Mathematical Primitives & Underlying Algorithms

All FLINT++ arithmetic operations perform strict argument validation and raise runtime exceptions (§3) if uninitialized inputs are encountered.

### 2a. Basic Primitives

#### `Flint_Mul` (0x65320f)
Multiplies two grand numbers.
- Checks if the input operands have error status `512` (uninitialized).
- If multiplying an object by itself, redirects to the squaring helper `Flint_BignumHelper_64BCF7`.
- Otherwise, delegates to the multiplication helper `Flint_BignumHelper_64BC25`.
- If the helper returns `-2` (overflow), sets the object error status to `128`. Other non-zero errors raise a `0x2000` runtime exception.

#### `Flint_DivRem` (0x653300)
Divides two grand numbers, returning the quotient and remainder.
- Gated by uninitialized checks (status `512`).
- Calls the core division helper `Flint_BignumHelper_64A2EA`.
- If the divisor is zero, the helper returns `-1`, raising runtime error `32` (Division by Zero).

#### `Flint_ModMul` (0x6535f3)
Modular multiplication: $R = (A \times B) \pmod M$.
- Gated by uninitialized checks.
- If $A == B$, calls the modular squaring helper `Flint_BignumHelper_64C34A`.
- Otherwise, calls the modular multiplication helper `Flint_BignumHelper_64C288`.
- If the modulus $M$ is invalid or inverse calculation fails, the helper returns `-1`, raising runtime error `32`.

#### `Flint_ModSqr` (0x6536b5)
Modular squaring: $R = A^2 \pmod M$.
- Delegates directly to `Flint_BignumHelper_64C34A` with identical base arguments.

---

### 2b. Modular Exponentiation: `Flint_ModExp2` (0x653962)
Computes $R = A^{e} \pmod M$, representing the core cryptographic operation for RSA authentication.

#### Execution Pipeline
1. Gated by status `512` uninitialized checks on base and modulus.
2. Invokes the core sliding-window Montgomery exponentiation helper: `Flint_BignumHelper_64DC82` (implemented at `0x64d605` / `0x64dc82`).
3. If successful, clears the error status of the result.

#### The `Flint_BignumHelper_64DC82` Algorithm
The helper implements **Sliding-Window Exponentiation** combined with **Montgomery Reduction**:

1. **Modulus Validation:** Verifies that modulus $M$ is odd and non-zero. If even, returns `-7` (handled as `Even Modulus` exception).
2. **Montgomery Setup:** Calculates the 16-bit Montgomery constant $n_0' = -M^{-1} \pmod{2^{16}}$ using `Flint_BignumHelper_649B2F`.
3. **Window Size Selector:** Selects the optimal window size $k$ (ranging from 1 to 8) based on the bit-length of the exponent:
   $$\frac{(k-1) \cdot k \cdot 2^{2(k-1)}}{2^k - k - 1} < \text{bitLength} - 1$$
4. **Precomputation Table:**
   - Allocates a contiguous buffer for $2^{k-1} - 1$ entries.
   - Computes base powers in Montgomery representation: $base, base^3, base^5, \dots, base^{2^k - 1} \pmod M$.
   - Uses Montgomery Squaring (`Flint_BignumHelper_64C569`) and Multiplication (`Flint_BignumHelper_64C3E1`).
5. **Sliding Window Loop:**
   - Traverses the bits of the exponent from MSB to LSB.
   - Groups non-zero bits into windows of size $k$ and multiplies by the corresponding precomputed odd power.
   - Performs Montgomery Squaring (`Flint_BignumHelper_64C569`) for all zero bits or step transitions.
6. **Montgomery Post-conversion:** Multiplies the accumulated result by 1 (`byte_79D408`) via `Flint_BignumHelper_64C3E1` to exit Montgomery representation.
7. **Cleanup:** Zeroes and frees the precomputation table and temp buffers before returning.

---

## 3. Error Handler & LINT Exception Hierarchy

Runtime failures in the bignum library route through a centralized handler.

### 3a. The Error Router: `Flint_RaiseRuntimeError` (0x65128d)
When a primitive encounters an invalid parameter, underflow, or arithmetic failure, it invokes this function:
```c
int __cdecl Flint_RaiseRuntimeError(int err_code, char *func_name, int arg_idx, int line_num);
```

#### Execution Logic
- If the global exception callback pointer `dword_9FF43C` is set, the router delegates to it (which translates the error code into a C++ throw).
- Otherwise, it formats a diagnostic message to the standard ostream `dword_A3F590` and calls `abort()`.

#### Error Taxonomy & Formatting Map:

| Error Code | Error Label | Exception Thrown | Formatted Message |
|---|---|---|---|
| **16 (0x10)** | File I/O Error | `LINT_File` | `"Error in file I/O, operator/function {func}, line {line}"` |
| **32 (0x20)** | Division by Zero | `LINT_DivByZero` | `"Division by zero, operator/function {func}, line {line}"` |
| **64 (0x40)** | Out of Memory | `LINT_Heap` | `"Error in new, function/operator {func}, line {line}"` |
| **128 (0x80)**| Overflow | `LINT_OFL` | `"Overflow, operator/function {func}, line {line}"` |
| **256 (0x100)**| Underflow | `LINT_UFL` | `"Underflow, operator/function {func}, line {line}"` |
| **512 (0x200)**| Uninitialized Argument| `LINT_Init` | `"Argument {arg_idx} in operator/function {func} uninitialized or has invalid value, line {line}"` |
| **1024 (0x400)**| Invalid Base | `LINT_Init` | `"Base invalid, operator/function {func}, line {line}"` |
| **2048 (0x800)**| Modulus is Even | `LINT_Emod` | `"Modulus is even, operator/function {func}, line {line}"` |
| **4096 (0x1000)**| Null Pointer | `LINT_Nullptr` | `"Argument {arg_idx} is Null-pointer in operator/function {func}, line {line}"` |
| *Other* | Unexpected Error | `LINT_Mystic` | `"Unexpected Error in operator/function {func}, line {line}"` |

---

### 3b. C++ Exception Classes Layout
The library defines a custom exception hierarchy derived from `LINT_Base`. The constructors write specific virtual table pointers and set member variables.

```
            +-------------+
            |  LINT_Base  |
            +------+------+
                   |
            +------+------+
            |  LINT_Error |
            +------+------+
                   |
     +-------------+-------------+-------------+-------------+
     |             |             |             |             |
+----+----+   +----+----+   +----+----+   +----+----+   +----+----+
|LINT_File|   |LINT_Init|   |LINT_Heap|   |LINT_OFL |   |LINT_UFL |
+---------+   +---------+   +---------+   +---------+   +---------+
     |             |             |             |             |
+----+----+   +----+----+   +----+----+   +----+----+   +----+----+
|LINT_Emod|   |LINT_Null|   |LINT_Myst|   |LINT_Div0|   |   ...   |
+---------+   +---------+   +---------+   +---------+   +---------+
```

All subclasses occupy **16 bytes** in memory, mapping to the following field layouts initialized by their constructors:

#### 1. Standard Exceptions (`LINT_File`, `LINT_Heap`, `LINT_OFL`, `LINT_UFL`, `LINT_Emod`, `LINT_DivByZero`)
- **`+0x00` (4B):** Virtual Table Pointer (e.g. `??_7LINT_File@@6B@` @ `0x732b9c`).
- **`+0x04` (4B):** `error_code` (copied from `a2`).
- **`+0x08` (4B):** Constant `0`.
- **`+0x0c` (4B):** `line_number` (copied from `a3`).

#### 2. Argument-Indexed Exceptions (`LINT_Init`, `LINT_Nullptr`, `LINT_Mystic`)
These store the index of the offending argument in the middle slot:
- **`+0x00` (4B):** Virtual Table Pointer (e.g. `??_7LINT_Init@@6B@` @ `0x732ba8`).
- **`+0x04` (4B):** `error_code` (copied from `a2`).
- **`+0x08` (4B):** `argument_index` (copied from `a3`).
- **`+0x0c` (4B):** `line_number` (copied from `a4`).
