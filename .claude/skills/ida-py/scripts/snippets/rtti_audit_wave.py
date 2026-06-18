#!/usr/bin/env python3
"""ida-script-runner snippet: adversarial audit of RTTI placement wave output.

For a stratified sample of HIGH entries:
- vfunc: confirm vtable[slot] == claimed EA
- ctor: confirm claimed EA contains a DataRef or immediate to the class vtable_ea

READ-ONLY.

=== CONFIG ===
"""
# === CONFIG ===
CLASSES_JSON = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/rtti/classes.json"
OUT_AUDIT    = "C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/campaign7/anchor/audit_wave_a1.json"
# Paste your 30-entry sample list here (ea, name, evidence)
SAMPLES = []  # populate from the wave output
SAMPLE_SEED = 42
# === END CONFIG ===

import idaapi, idc, idautils, ida_bytes
import json, re

with open(CLASSES_JSON) as f:
    by_class = {c['class']: c for c in json.load(f)}

def parse_name(name):
    parts = name.split('__')
    if len(parts) < 2: return None
    cn = parts[0].replace('Diamond_', 'Diamond::')
    member = '__'.join(parts[1:])
    m = re.match(r'VFunc_(\d+)', member)
    if m: return {'class': cn, 'role': 'vfunc', 'slot': int(m.group(1))}
    m = re.match(r'ctor(?:_(\d+))?$', member)
    if m: return {'class': cn, 'role': 'ctor'}
    return {'class': cn, 'role': 'semantic'}

def ea_to_hex(ea): return "0x%08X" % ea

def verify_vfunc(s, cls):
    vtable_ea = int(cls['vtable_ea'], 16)
    slot = parse_name(s['name'])['slot']
    target = ida_bytes.get_wide_dword(vtable_ea + slot * 4)
    fn = idaapi.get_func(target)
    target_fn = fn.start_ea if fn else target
    claimed = int(s['ea'], 16)
    match = (target_fn == claimed)
    return {"ea": s['ea'], "name": s['name'], "role": "vfunc", "slot": slot,
            "vtable_target": ea_to_hex(target_fn), "match": match,
            "verdict": "PASS" if match else "FAIL"}

def verify_ctor(s, cls):
    vtable_ea = int(cls['vtable_ea'], 16)
    claimed = int(s['ea'], 16)
    fn = idaapi.get_func(claimed)
    if fn is None:
        return {"ea": s['ea'], "name": s['name'], "role": "ctor", "verdict": "FAIL", "reason": "no func"}
    insn = idaapi.insn_t()
    ea = fn.start_ea
    found = False
    write_ea = None
    for _ in range(80):
        length = idaapi.decode_insn(insn, ea)
        if not length: break
        for op in insn.ops:
            if op.type == idaapi.o_imm and op.value == vtable_ea:
                found, write_ea = True, ea; break
        if not found:
            for xr in idautils.DataRefsFrom(ea):
                if xr == vtable_ea:
                    found, write_ea = True, ea; break
        if found: break
        ea += length
    return {"ea": s['ea'], "name": s['name'], "role": "ctor",
            "vtable_write_found": found, "vtable_write_ea": ea_to_hex(write_ea) if write_ea else None,
            "verdict": "PASS" if found else "FAIL"}

results = []
for s in SAMPLES:
    p = parse_name(s['name'])
    if not p or p['class'] not in by_class:
        results.append({"ea": s['ea'], "name": s['name'], "verdict": "SKIP"}); continue
    cls = by_class[p['class']]
    if p['role'] == 'vfunc':
        results.append(verify_vfunc(s, cls))
    elif p['role'] == 'ctor':
        results.append(verify_ctor(s, cls))
    else:
        results.append({"ea": s['ea'], "name": s['name'], "verdict": "SKIP", "reason": "semantic"})

passes = sum(1 for r in results if r['verdict'] == 'PASS')
fails  = sum(1 for r in results if r['verdict'] == 'FAIL')
total  = passes + fails
fp_rate = fails / total * 100 if total else 0.0

print(f"PASS {passes}/{total}  FP rate {fp_rate:.1f}%")
print("| # | EA | Name | Role | Verdict |")
print("|---|---|---|---|---|")
for i, r in enumerate(results):
    print(f"| {i+1} | {r['ea']} | `{r['name']}` | {r.get('role','?')} | {r['verdict']} |")

with open(OUT_AUDIT, 'w', encoding='utf-8') as f:
    json.dump({"results": results, "passes": passes, "fails": fails, "fp_rate": fp_rate}, f, indent=2)
print(f"Audit -> {OUT_AUDIT}")
