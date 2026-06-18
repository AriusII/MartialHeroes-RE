#!/usr/bin/env python3
"""ida-script-runner snippet: deterministic RTTI placement for unplaced class names.

For each unplaced name of the form <Class>__VFunc_N or <Class>__ctor[_N]:
- VFunc_N  -> read vtable[N] pointer from the class vtable to find the target EA.
- ctor/dtor -> scan DataRefsTo(vtable_ea) to find functions that write the class vtable ptr.

READ-ONLY: produces proposal YAML only; does not rename/patch the IDB.

=== CONFIG ===
"""
# === CONFIG ===
UNPLACED_JSON = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/anchor/unplaced.json"
CLASSES_JSON  = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/rtti/classes.json"
OUT_HIGH      = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/glossary/reanchor_wave_a1.yaml"
OUT_MED       = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/glossary/reanchor_wave_a1_med.yaml"
OUT_RPT       = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/anchor/wave_a1_report.md"
# Filter to only this cluster tag (set to None for all)
TARGET_CLUSTER = "rtti-class-core"
# Binary SHA pinned in output
SHA = "263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee"
# === END CONFIG ===

import idaapi, idc, idautils, ida_bytes, ida_funcs
import json, re, os

def ea_to_hex(ea): return "0x%08X" % ea

def read_dword(ea): return ida_bytes.get_wide_dword(ea)

def is_sub_or_unnamed(ea):
    name = idc.get_func_name(ea)
    if not name: return True
    return bool(re.match(r'^(sub_|j_|nullsub_|loc_|off_)', name))

def get_func_at(ea):
    fn = ida_funcs.get_func(ea)
    return fn.start_ea if fn else None

def is_library(ea):
    flags = idc.get_func_attr(ea, idc.FUNCATTR_FLAGS)
    return bool(flags & 4)

def get_vtable_slot(vtable_ea, slot_n, ptr_size=4):
    target = read_dword(vtable_ea + slot_n * ptr_size)
    if not target or target == 0xFFFFFFFF: return None
    return get_func_at(target) or target

def find_ctor_by_vtable_write(vtable_ea, max_results=4):
    results = []
    for xref in idautils.DataRefsTo(vtable_ea):
        fn = get_func_at(xref)
        if fn is None or is_library(fn): continue
        results.append((fn, xref, idc.generate_disasm_line(xref, 0) or ""))
        if len(results) >= max_results: break
    return results

def parse_name(name):
    parts = name.split('__')
    if len(parts) < 2: return None
    cn = parts[0].replace('Diamond_', 'Diamond::')
    member = '__'.join(parts[1:])
    m = re.match(r'VFunc_(\d+)', member)
    if m: return {'class': cn, 'role': 'vfunc', 'slot': int(m.group(1)), 'member': member}
    m = re.match(r'ctor(?:_(\d+))?$', member)
    if m: return {'class': cn, 'role': 'ctor', 'variant': int(m.group(1)) if m.group(1) else 0, 'member': member}
    m = re.match(r'dtor(?:_(\d+))?$', member)
    if m: return {'class': cn, 'role': 'dtor', 'member': member}
    return {'class': cn, 'role': 'semantic', 'member': member}

# Load inputs
with open(UNPLACED_JSON) as f: unplaced = json.load(f)
with open(CLASSES_JSON) as f: by_class = {c['class']: c for c in json.load(f)}

scope = [x for x in unplaced if TARGET_CLUSTER is None or x['cluster'] == TARGET_CLUSTER]
print(f"Scope: {len(scope)} entries (cluster={TARGET_CLUSTER})")

# Pre-group ctors per class to assign variants in order
ctor_groups = {}
for x in scope:
    p = parse_name(x['name'])
    if p and p['role'] in ('ctor', 'dtor'):
        cn = p['class']
        ctor_groups.setdefault(cn, []).append((p.get('variant', 0), x['name']))
for cn in ctor_groups:
    ctor_groups[cn].sort(key=lambda t: t[0])

high, med, unrecoverable = [], [], []
used_eas = set()

for x in scope:
    name, p = x['name'], parse_name(x['name'])
    if not p: unrecoverable.append((name, "unparseable")); continue
    cn = p['class']
    if cn not in by_class: unrecoverable.append((name, f"class {cn} not in classes.json")); continue
    cls = by_class[cn]
    try: vtable_ea = int(cls['vtable_ea'], 16)
    except: unrecoverable.append((name, "bad vtable_ea")); continue

    if p['role'] == 'vfunc':
        slot = p['slot']
        if slot >= cls.get('slot_count', 999):
            unrecoverable.append((name, f"slot {slot} OOB")); continue
        target = get_vtable_slot(vtable_ea, slot)
        if target is None: unrecoverable.append((name, "null vtable slot")); continue
        if target in used_eas or not is_sub_or_unnamed(target) or is_library(target):
            existing = idc.get_func_name(target)
            med.append((name, target, f"rtti:vtable[{slot}]", f"{cn} vtable slot {slot}; existing={existing}. [rtti-class-core]"))
            continue
        used_eas.add(target)
        high.append((name, target, f"rtti:vtable[{slot}]", f"{cn} vtable slot {slot}. [rtti-class-core]"))

    elif p['role'] in ('ctor', 'dtor'):
        candidates = find_ctor_by_vtable_write(vtable_ea)
        valid = [(fn, xr, d) for fn, xr, d in candidates if is_sub_or_unnamed(fn) and not is_library(fn) and fn not in used_eas]
        if not valid: unrecoverable.append((name, "no unnamed vtable-write candidates")); continue
        group = ctor_groups.get(cn, [])
        pos = next((i for i, (v, n) in enumerate(group) if n == name), -1)
        if pos < 0 or pos >= len(valid):
            fn, xr, _ = valid[0]
            med.append((name, fn, f"rtti:ctor ambiguous pos={pos}", f"{cn} ctor (ambiguous). [rtti-class-core]"))
        else:
            fn, xr, _ = valid[pos]
            if fn in used_eas:
                med.append((name, fn, "ctor EA collision", f"{cn} ctor (collision). [rtti-class-core]"))
            else:
                used_eas.add(fn)
                high.append((name, fn, f"rtti:ctor vtable-write pos={pos}", f"{cn} constructor (vtable-write @{ea_to_hex(xr)}). [rtti-class-core]"))
    else:
        unrecoverable.append((name, "semantic — needs manual placement"))

print(f"HIGH={len(high)} MED={len(med)} UNRECOVERABLE={len(unrecoverable)}")

# Write YAML
HDR = f"""# CAMPAIGN 7 Wave A1 -- RTTI deterministic placement
# Shape: /ida-annotate-batch
binary:
  name: doida.exe
  sha256: {SHA}
functions:
"""
def write_yaml(entries, path, hdr):
    lines = [hdr]
    for name, ea, ev, cm in entries:
        lines += [f"  '{ea_to_hex(ea)}':", f"    name: {name}",
                  f"    comment: '{cm.replace(chr(39), chr(34))}'",
                  f"    confidence: high", f"    cluster: C7-rtti-wave-a1",
                  f"    evidence: '{ev}'"]
    open(path, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    print(f"Written {len(entries)} -> {path}")

write_yaml(high, OUT_HIGH, HDR)
write_yaml(med,  OUT_MED,  HDR.replace("HIGH", "MED"))
