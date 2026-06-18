"""
Campaign VFS-MASTERY Phase-D IDB annotation applier.
doida.exe SHA 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
MODE: set below — "dry-run" or "apply"
Idempotent: re-run yields noop for already-applied entries.
"""
import idc, idaapi, ida_funcs, ida_bytes, ida_name, ida_nalt
import json, hashlib, os

# === CONFIG ===
MODE = "apply"   # "dry-run" or "apply"
CLUSTER = "campaign8-phase-d"
EXPECTED_SHA = "263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee"

# CRT/runtime skip patterns (never touch these)
CRT_PREFIXES = ("__", "_imp_", "j_", "?", "??", "_RTC_", "__security",
                "mainCRTStartup", "std::", "_acmdln", "_CxxThrow",
                "nullsub_", "unknown_libname_")

def is_runtime(name):
    if not name:
        return False
    for p in CRT_PREFIXES:
        if name.startswith(p):
            return True
    return False

# ============================================================
# MANIFEST: address -> {name, comment, overwrite}
# name=None means comment-only
# comment=None means name-only
# overwrite=True required for MISLABELED-PRIOR renames
# ============================================================

MANIFEST = {}

# ---- PART 1: define_func + rename ----
MANIFEST["0x608C70"] = {
    "name": "Render_SubmitDrawBatch",
    "comment": "Graphics render-submission helper (draw-batch submission). NOT DiskFile I/O — the prior 'DiskFile gap' hypothesis at this EA was refuted. NOISE with respect to the VFS subsystem.",
    "define_func": (0x608C70, 0x608E96),
    "overwrite": False,
}

# ---- PART 2: DiskFile primitive renames ----
MANIFEST["0x608F8F"] = {
    "name": "DiskFile_ReadBytes",
    "comment": "DiskFile primitive: reads N bytes from the open file handle into caller buffer (VFS in-place or OS read by mode flag). Body-confirmed on C7 build.",
    "overwrite": False,
}
MANIFEST["0x6090EC"] = {
    "name": "DiskFile_Close",
    "comment": "DiskFile primitive: closes the open file handle and resets the stream state.",
    "overwrite": True,  # was DiskFile_CloseAndReset
}
MANIFEST["0x609369"] = {
    "name": "DiskFile_ReadByte",
    "comment": "DiskFile primitive: reads a single byte from the open file handle.",
    "overwrite": False,
}
MANIFEST["0x6094E2"] = {
    "name": "DiskFile_GetSize",
    "comment": "DiskFile primitive: returns the byte length of the open file.",
    "overwrite": False,
}
MANIFEST["0x6094BE"] = {
    "name": "DiskFile_IsGood",
    "comment": "DiskFile primitive: stream-validity / good-bit predicate (true while readable).",
    "overwrite": False,
}

# ---- PART 3: mislabeled-prior renames ----
MANIFEST["0x42F2CA"] = {
    "name": "CoreMot_LoadHeader",
    "comment": ".mot Stage-1 header loader (NOT char-actor pose): u32 id_a ->+68; u32 id_b group/set load key ->+72; LenStr name discarded; u32 frame_count -> duration = frame_count*0.1 (10 fps) ->+76.",
    "overwrite": False,
}
MANIFEST["0x42F839"] = {
    "name": "CoreMot_LoadFullData",
    "comment": ".mot Stage-2 full parse: re-read header, then u32 track_count; per track u32 descriptor (low byte = bone_id, upper 3 discarded) + u32 key_count; per keyframe 7 floats = 28B (translation XYZ + quaternion XYZW, no scale). Track stride = 8 + key_count*28. Interpolation is runtime-only.",
    "overwrite": False,
}
MANIFEST["0x5F92E3"] = {
    "name": "SoundTable_IndexEntry",
    "comment": "Generic keyed-id-list insert; skips zero key (null sentinel). On the sound path: arg2 = record +0x00 sound key, arg3 = category (0 bgm/bge, 6 eff), arg5 = record index. NOT skill-specific.",
    "overwrite": True,  # was SkillGidList_AddEntry
}
MANIFEST["0x456A35"] = {
    "name": "SoundTable_LoadFiveTables",
    "comment": "Multi-file sound-table loader: opens several soundtable files by sprintf-formatted name via DiskFile, with per-file open/read error handling. NOT a region-table variant.",
    "overwrite": False,
}
MANIFEST["0x4571BC"] = {
    "name": "NpcArr_FindRecordById",
    "comment": "npc.arr record scanner: linear walk of the npc.arr record array at 28-byte (0x1C) stride, matching a u16 id at record+0x1C against arg0; returns the matching record pointer. This is the npc.arr scanner, NOT a region table.",
    "overwrite": True,  # was RegionTable_FindRowById
}

# ---- PART 4: per-family manifests ----

# --- EFFECTS ---
MANIFEST["0x4AC336"] = {
    "name": None,  # already EFF_LoadParticleEmitter
    "comment": "EFF_LoadParticleEmitter: variable-length loop while (fileSize-cursor)>0x1C: 28B header(new 0x1C)+ReadBytes; num_frames=hdr[+4], 0->stop; ReadBytes num_frames*52 subrec; ReadBytes 64B texname; resolve; map-insert by entry_id(+0).",
    "overwrite": False,
}
MANIFEST["0x4AC252"] = {
    "name": None,  # already ParticleEmitterMap_Insert
    "comment": "Parses and inserts an emitter entry into the EffectManager map, keyed on entry_id (+0x00).",
    "overwrite": False,
}
MANIFEST["0x4AB5E3"] = {
    "name": None,  # already ParticleSubRecord_DefaultCtor
    "comment": "Zero-initializes the 52-byte particle sub-record before bulk read (eh-vector constructor).",
    "overwrite": False,
}
MANIFEST["0x4965F1"] = {
    "name": "EffectTextureName_Lookup",
    "comment": "EffectTextureName_Lookup: binary-search strcmp over EffectManager bmplist-derived texture-name pool (this+140/141); returns slot or 0.",
    "overwrite": False,
}
# 0x4A2B25: xdb-do takes priority: EffectScaleXdb_LookupScale
MANIFEST["0x4A2B25"] = {
    "name": "EffectScaleXdb_LookupScale",
    "comment": "effectscale lookup: effect_key -> scale_factor(+4); 0 if absent. Backed by g_EffectScaleXdb_Map at 0x84D7C0.",
    "overwrite": False,
}
MANIFEST["0x49F5E9"] = {
    "name": "XeffElement_SetResIdAndAnimFlag",
    "comment": "Stores resource_id (+4) and anim_flag bool (+8) into the in-memory .xeff element.",
    "overwrite": False,
}
# 0x4A07F0 already named XEffect_EulerToBillboardDir - noop
# 0x609369 covered in Part 2/3 already
# Inline/non-entry comments for effects:
# These are mid-function addresses - set as line comments, no rename
MANIFEST["0x4A57CE"] = {
    "name": None,
    "comment": ".xeff track header (9B): ReadByte anim_loop; then ReadU32 anim_stride, ReadU32 anim_base_time. Track header is 9 bytes in BOTH paths.",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A57D9"] = {
    "name": None,
    "comment": "anim_stride (u32, ms) -> element[20]",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A57DC"] = {
    "name": None,
    "comment": "anim_base_time (u32, ms)",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A57F9"] = {
    "name": None,
    "comment": "branch on anim_loop != 0: animated keyframe array (40B/kf) else single static-state entry",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A595E"] = {
    "name": None,
    "comment": "animated keyframe loop: per kf = ReadU32 index + 6 ReadFloat (vel xyz, size xyz) + 3 ReadFloat*pi/180 (Euler). 40 bytes EACH keyframe incl. keyframe 0.",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A5804"] = {
    "name": None,
    "comment": "static-state branch (anim_loop==0): ReadFloat x6 (vel Vec3 + size Vec3) = 24B",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A587E"] = {
    "name": None,
    "comment": "static branch: if emitter_type==2 read 3 extra ReadFloat*pi/180 (Euler) = +12B (36B total). Only emitter_type-dependent size diff.",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A5572"] = {
    "name": None,
    "comment": "element read order: emitter_type, resource_id, anim_flag(bool), field_unknown_a(+0x0C), element_dword2(+0x10 written first in mem), tex_count",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4A55D5"] = {
    "name": None,
    "comment": "name table loop: tex_count x 64-byte bare stems; full path = 'data/effect/texture/' + stem + '.tga' built here",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4AC419"] = {
    "name": None,
    "comment": "num_frames = entry header +0x04; if 0 -> break (terminator). Other terminator = <=28 bytes remain.",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4AC482"] = {
    "name": None,
    "comment": "bulk ReadBytes of num_frames*52-byte sub-records (NOT field-decoded at load; +0x0C..+0x33 typed only by sim/render path).",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4AC490"] = {
    "name": None,
    "comment": "ReadBytes 64-byte texture name VERBATIM (full data/effect/texture/... path as stored)",
    "overwrite": False,
    "line_comment": True,
}
MANIFEST["0x4AC49E"] = {
    "name": None,
    "comment": "EffectTextureName_Lookup(name): exact strcmp binary-search vs texture-name pool; full path stored & matched verbatim. Resolved handle -> entry +0x14; fallback default on miss.",
    "overwrite": False,
    "line_comment": True,
}

# --- ENVIRONMENT ---
MANIFEST["0x4575AF"] = {
    "name": "Env_MapSetAndLoadArea",
    "comment": "Env area-activation hub. Sets g_EnvCurrentAreaId, pre-sets enable gates (844460..844470)=1, then calls map_option/region/weather/sky/wind loaders. map_option+region returns UNCHECKED (tolerant); weather/sky/wind hard-gate the area-set.",
    "overwrite": False,
}
MANIFEST["0x45735E"] = {
    "name": "MapOption_LoadBin",
    "comment": "map_option%d.bin loader: 40 bytes read directly over the 10 enable-gate globals at g_MapOptionBlock. Overwrites the hub's pre-set defaults. Returns 0 on absence but hub ignores it (gates stay defaulted ON).",
    "overwrite": False,
}
MANIFEST["0x44FCB8"] = {
    "name": "Fog_InitFromBin",
    "comment": "Fog::init. Reads start_dist(+208 f32), end_dist(+212 f32), data_load flag(u32). flag!=0 -> read literal 192B fog_colors[48] from file. flag==0 -> synthesize 48 kf from sky-LUT, per-slot blend src_high*0.75 + src_low*0.25 truncated to byte.",
    "overwrite": False,
}
MANIFEST["0x44F952"] = {
    "name": "Fog_ApplyAfterLoad",
    "comment": "Post-load fog distance/colour apply (called by Fog::init).",
    "overwrite": False,
}
MANIFEST["0x457DB0"] = {
    "name": "Material_LoadBin",
    "comment": "reads material%d.bin (9792B flat f32) into the colour table global.",
    "overwrite": False,
}
MANIFEST["0x457E86"] = {
    "name": "Material_SaveBin",
    "comment": "Editor save path for material%d.bin (mode 2). NOT a loader.",
    "overwrite": False,
}
MANIFEST["0x4491E2"] = {
    "name": "StarDome_InitFromBin",
    "comment": "StarDome::init. Self-gated on g_StardomeEnable: returns success WITHOUT reading if disabled (indoor tolerance). When enabled, reads 9216B (12kf x 192 stars x 4 BGRA) then builds per-star table.",
    "overwrite": False,
}
MANIFEST["0x449055"] = {
    "name": "StarDome_InterpolatePerStar",
    "comment": "Per-frame star interpolation. Source pointer advances 4 bytes PER STAR (one BGRA entry each) -> each of 192 stars interpolated INDIVIDUALLY between kf and kf+1. Per-star (per-instance) tint, NOT a single global tint.",
    "overwrite": False,
}
MANIFEST["0x447664"] = {
    "name": "CloudDome_InitFromBin",
    "comment": "CloudDome::init. Self-gated on g_ClouddomeEnable. Reads clouddome layer1 (11520B @ +5800), layer2 (11520B @ +17320), then cloud_cycle (70B @ +28840). Builds cloud%d.dds names from cloud_cycle ids; id 101 -> cloud101.dds (absent) = no-cloud sentinel.",
    "overwrite": False,
}
MANIFEST["0x459717"] = {
    "name": "Light_LoadBin",
    "comment": "light%d.bin loader: single 5312-byte slurp into manager+1424. No field parse here; keyframe structure imposed by the per-frame consumer (Light_PerFrameApply).",
    "overwrite": False,
}
MANIFEST["0x459F3A"] = {
    "name": "Light_SaveBin",
    "comment": "light%d.bin SAVE path (mode 2, AppendToGrowBuffer). Editor write, NOT a loader. Pair with Light_LoadBin.",
    "overwrite": False,
}
MANIFEST["0x45A4FE"] = {
    "name": "Light_LoadOrSynthDefault",
    "comment": "Light load wrapper. On MISSING light%d.bin takes else-branch: synthesizes 48-kf default colour ramp + writes fallback vector (-7,7,20) + scale. NEVER fails -> light absence fully tolerated.",
    "overwrite": False,
}
MANIFEST["0x459986"] = {
    "name": "Light_PerFrameApply",
    "comment": "Per-frame light apply. Reads keyframe color_A(+0x00)->directional/diffuse and color_B(+0x10)->secondary/specular. color_C(+0x20) is NOT read by any path -> loaded-but-unconsumed.",
    "overwrite": False,
}
MANIFEST["0x44960F"] = {
    "name": "Light_NegateVec3",
    "comment": "Negates a 3-float vector (fallback light dir sign convention).",
    "overwrite": False,
}
MANIFEST["0x4597F9"] = {
    "name": "PointLight_LoadMapData",
    "comment": "point_light%d.bin loader (LightManager::loadMapData). u32 intensity_scale@+0x00, u32 count@+0x04, then count*60B records (not decomposed at load). Inits 5 active-light slots.",
    "overwrite": False,
}
MANIFEST["0x451FE3"] = {
    "name": "Weather_InitFromBin",
    "comment": "Weather::init. Reads 240B weather%d.bin then applies 3 hardcoded quality-tier scalars (tier1=1.0; tier2~{0.7,0.7,0.825}; tier3~{0.65,0.65,0.8125}) chosen by OPTION_WEATHER. Tier scalars are NOT file fields. Hard-gates the area-set.",
    "overwrite": False,
}
MANIFEST["0x451F6E"] = {
    "name": "Weather_SelectDayRow",
    "comment": "Weather day-row select: indexes the 240B weather body by (area_time % 10) at stride 24 (10 day-pattern rows, like cloud_cycle).",
    "overwrite": False,
}
MANIFEST["0x5E1C9A"] = {
    "name": "Weather_SetQualityOption",
    "comment": "OPTION_WEATHER slider handler: writes INI, re-invokes Weather::init.",
    "overwrite": False,
}
# 0x451203 already RainSystem_ConstructAndLoad - noop name, add comment
MANIFEST["0x451203"] = {
    "name": None,
    "comment": "Rain particle system constructor. Built ENTIRELY from hardcoded constants + rand(). Loads ONLY rains.dds + rain_drop.dds textures. Reads NO weather_rain.bin -> that VFS file is unconsumed/dead.",
    "overwrite": False,
}
# 0x44857B already SkySystem_Init - noop name, add comment
MANIFEST["0x44857B"] = {
    "name": None,
    "comment": "SkySystem_Init sub-orchestrator: stardome -> clouddome(+cloud_cycle) -> material -> light(synth-default-on-absence) -> fog. Each sub-loader hard-gates (return 0 aborts).",
    "overwrite": False,
}
# Environment globals
MANIFEST["0x844A88"] = {"name": "g_EnvCurrentAreaId", "comment": "Current area id; every env loader sprintf's its filename from this.", "overwrite": False}
MANIFEST["0x844458"] = {"name": "g_MapOptionBlock", "comment": "Base of the 40B map_option block (10 enable/flag u32 words).", "overwrite": False}
MANIFEST["0x844464"] = {"name": "g_StardomeEnable", "comment": "Stardome_enable gate (read by StarDome_InitFromBin / InterpolatePerStar).", "overwrite": False}
MANIFEST["0x844468"] = {"name": "g_ClouddomeEnable", "comment": "Clouddome_enable gate (read by CloudDome_InitFromBin).", "overwrite": False}
MANIFEST["0x8C3C18"] = {"name": "g_K_ambient", "comment": "g_K_ambient ambient gate multiplier. Static 0.0 (read-only, no writer) -> per-keyframe ambient table is inert at runtime. Matches committed spec K_ambient=0.0.", "overwrite": False}
# Path format string pointer globals
MANIFEST["0x79FEB0"] = {"name": "g_PathFmt_map_option", "comment": "-> 'data/sky/dat/map_option%d.bin'", "overwrite": False}
MANIFEST["0x79FEEC"] = {"name": "g_PathFmt_stardome", "comment": "-> 'data/sky/dat/stardome%d.bin'", "overwrite": False}
MANIFEST["0x79FEF0"] = {"name": "g_PathFmt_clouddome", "comment": "-> 'data/sky/dat/clouddome%d.bin'", "overwrite": False}
MANIFEST["0x79FEF4"] = {"name": "g_PathFmt_fog", "comment": "-> 'data/sky/dat/fog%d.bin'", "overwrite": False}
MANIFEST["0x79FEF8"] = {"name": "g_PathFmt_light", "comment": "-> 'data/sky/dat/light%d.bin'", "overwrite": False}
MANIFEST["0x79FEFC"] = {"name": "g_PathFmt_material", "comment": "-> 'data/sky/dat/material%d.bin'", "overwrite": False}
MANIFEST["0x79FF00"] = {"name": "g_PathFmt_cloud_cycle", "comment": "-> 'data/sky/dat/cloud_cycle%d.bin'", "overwrite": False}
MANIFEST["0x79FF04"] = {"name": "g_PathFmt_point_light", "comment": "-> 'data/sky/dat/point_light%d.bin'", "overwrite": False}
MANIFEST["0x79FF08"] = {"name": "g_PathFmt_weather", "comment": "-> 'data/sky/dat/weather%d.bin'", "overwrite": False}
MANIFEST["0x79FF0C"] = {"name": "g_PathFmt_wind_bin", "comment": "-> 'data/sky/dat/wind%d.bin'", "overwrite": False}

# --- MESH-ANIM ---
MANIFEST["0x423E0F"] = {
    "name": "CharManifest_LoadAll",
    "comment": "Char-asset manifest batch loader: bindlist.txt, motlist.txt, emoticon.txt, skin.txt, actormotion.txt, userjoint.txt, gmmapmove.txt (in this order). Called from boot bulk-loader.",
    "overwrite": False,
}
MANIFEST["0x423108"] = {
    "name": "BindList_LoadAndRegister",
    "comment": "bindlist.txt loader: EOF-bounded loop; per line prepend 'data/char/bind/'; register pose. Registers EVERY listed g{N}.bnd (349 entries), keyed by the parsed .bnd actor_id, NOT g1..g4 only.",
    "overwrite": False,
}
MANIFEST["0x4234F2"] = {
    "name": "MotList_LoadAndRegister",
    "comment": "motlist.txt: per line prepend 'data/char/mot/', register clip by id.",
    "overwrite": False,
}
MANIFEST["0x423942"] = {
    "name": "SkinTable_LoadFromTxt",
    "comment": "skin.txt parser: count-prefixed (line0 u32). Per row reads 6 ints; col2 (3rd int) is the OUTFIT/CLASS TAG used as an INDEX into a per-catalogue base-offset table to build the composite 9-digit id. col2 is NOT the .skn binary id_b skeleton field.",
    "overwrite": False,
}
MANIFEST["0x423B7C"] = {
    "name": "ActorMotionTable_LoadFromTxt",
    "comment": "actormotion.txt parser (33-col, 136B record).",
    "overwrite": False,
}
MANIFEST["0x42E7DA"] = {
    "name": "CharPosePool_RegisterFromBndPath",
    "comment": "Parse .bnd (via bind stub), obtain its actor_id (offset-0 header field), register CorePose in the pool keyed BY that actor_id. This is the per-skeleton registration step.",
    "overwrite": False,
}
MANIFEST["0x430A55"] = {
    "name": "BindStub_OpenAndParseBnd",
    "comment": "Opens data/char/bind/*.bnd, news CorePose, parses, inserts.",
    "overwrite": False,
}
MANIFEST["0x430A02"] = {
    "name": "CorePose_InsertReturnActorId",
    "comment": "Inserts pose into map, returns parsed actor_id (offset-0 field).",
    "overwrite": False,
}
MANIFEST["0x43009C"] = {
    "name": "BindPose_ParseFile",
    "comment": ".bnd parse: u32 actor_id -> +4; LenStr actor_name (discarded); u32 bone_count (LOW BYTE only -> +16, max 255); bone array (36B disk / 72B mem each) via BindPose_ParseBoneRecord; then build hierarchy + parent-relative->world accumulation.",
    "overwrite": False,
}
# 0x42FD00 already BindPose_ParseBoneRecord - add comment
MANIFEST["0x42FD00"] = {
    "name": None,
    "comment": ".bnd bone record (36B disk): self_id u32 (low byte ->+12), parent_id u32 (low byte ->+13), 12B local translation ->+16, 16B local rotation quaternion XYZW (W last) ->+28. CONFIRMED vs formats/mesh.md.",
    "overwrite": False,
}
MANIFEST["0x43051A"] = {
    "name": "CharPosePool_LookupById",
    "comment": "Registry lookup keyed by id (used by both pose-register and skin id_b bind).",
    "overwrite": False,
}
MANIFEST["0x434EAA"] = {
    "name": "SkinStub_OpenAndParseSkn",
    "comment": "Opens data/char/skin/g{id}.skn, news CoreSkin, parses.",
    "overwrite": False,
}
# 0x43472A already CoreSkin_LoadFromFile - add comment
MANIFEST["0x43472A"] = {
    "name": None,
    "comment": ".skn parse: u32 id_a ->+4; u32 id_b (SkinClassId / skeleton ptr) ->+8; LenStr name; face_count + 36B*N faces; vertex_count + 24B*N verts (normal-first, reordered to pos-first 32B render vertex, v->1-v); weight_count + 12B weights. Weights <0.01 skipped; per-vertex normalize to sum 1.0. Binds skeleton via registry lookup keyed by id_b (+8).",
    "overwrite": False,
}
MANIFEST["0x42E56D"] = {
    "name": "MotClip_RegisterByPath",
    "comment": "Find/create clip from path, insert into manager map keyed by clip id.",
    "overwrite": False,
}
MANIFEST["0x42FAB4"] = {
    "name": "MotClip_FindOrCreate",
    "comment": "News CoreAnimation, runs header load, registers.",
    "overwrite": False,
}
# 0x60A1E0 already AssetStream_ReadInt32Field - add comment
MANIFEST["0x60A1E0"] = {
    "name": None,
    "comment": "Asset stream typed reader: if file flag bit3(=8) set -> read line + atoi (text mode); else read 4 raw bytes as u32 LE (binary mode). .skn/.bnd/.mot use binary mode.",
    "overwrite": False,
}
MANIFEST["0x60A36F"] = {
    "name": "AssetStream_ReadLenStrToBuf",
    "comment": "LenStr (4B len + body) -> char buffer (strcpy form).",
    "overwrite": False,
}
MANIFEST["0x60A3DD"] = {
    "name": "AssetStream_ReadLenStrToString",
    "comment": "LenStr -> std::string form (used by .skn/.mot name).",
    "overwrite": False,
}
MANIFEST["0x60A15B"] = {
    "name": "AssetStream_ReadU32Raw",
    "comment": "Raw 4B u32 read (frame_count / LenStr length helper).",
    "overwrite": False,
}
# 0x609482 VfsEntry_CompareOrderKey - comment-only (dual role note), no rename
MANIFEST["0x609482"] = {
    "name": None,
    "comment": "In the char text-table loaders this is invoked as the DiskFile end-of-stream predicate (loop while !eof), distinct from its VFS-TOC-comparator role elsewhere.",
    "overwrite": False,
}
# mesh-anim comment-only (mid-function addresses):
MANIFEST["0x42F2CA"] # already covered in Part 3 - will get Part 3 treatment (first occurrence wins)

# --- MISC-BULK ---
MANIFEST["0x5369B7"] = {
    "name": "BootThread_LoadDataTableCorpus",
    "comment": "Boot-thread data-table loader: opens ~47 VFS tables in order (system_control.scr, events.scr, playtime_reward.scr, items.scr, skills.scr, skillicon.txt, musajung.do, skillcategory.scr, users.scr, products.scr, productcollect.scr, productrandname.scr, helps.scr, npc.scr, npcs.scr, items_extra.do, mobs.scr, repair.scr, upgradeitems.scr, quests.scr, emoticon.do, textcommand.do, chivalry.scr, letters.scr, nicktofame.scr, guildcrest.scr, discript.sc, tiphelp.scr, setitemname.scr, oblist.scr, citems.scr, Tutor.scr, warstoneinfo.scr, statue.scr, skillneedset.scr, viplevels.scr, itemscale.scr, itemeffect.scr, UiTex.txt, skinlist.txt, char manifest batch, sameemoticon.txt, guildicon pool, effectscale.xdb, creature_item.xdb, vehicle.xdb, buff_icon_position.xdb), then Sleep(500)+endthreadex.",
    "overwrite": False,
}
MANIFEST["0x49473A"] = {
    "name": "DiscriptSc_LoadMenuLabelTable",
    "comment": "Loader for data/script/discript.sc: opens (read), reads 68-byte (0x44) records, new+register each into an id-keyed menu-label map. discript.sc = UI context-menu label table (CONFIRMED, not district/zone).",
    "overwrite": False,
}
MANIFEST["0x4946CD"] = {
    "name": "DiscriptRecord_InsertIntoMap",
    "comment": "discript.sc per-record ctor: qmemcpy 68-byte record, insert into map keyed by record_id @+0x00 (menu item id -> label record).",
    "overwrite": False,
}
# misc-bulk line comments for DiskFile corrections (these are function addresses so set as func comments)
# (already covered in Parts 2+3, those take priority)

# --- REGION ---
MANIFEST["0x456E70"] = {
    "name": "Map_LoadAreaBinaries",
    "comment": "Map area loader. Opens 4 files per map: [1] map%s.bin fixed 0x208B -> unk_844250; [2] regiontable%s.bin FIXED 0x600=1536B = 32 records x 48 bytes -> g_RegionTable(0x844488); [3] region%s.bin = width,height,grid(WxH u8),originX,originZ (Layout A); [4] npc%s.arr = (size/28+1) records x 28B.",
    "overwrite": False,
}
# 0x456A35 already covered in Part 3 (SoundTable_LoadFiveTables) - noop in region
MANIFEST["0x457583"] = {
    "name": "MapSetting_GetRecordByMapId",
    "comment": "Ordered-map lookup into g_MapSettingTableRoot by map id -> mapsetting.scr record ptr (stored to g_CurrentMapSettingRecord).",
    "overwrite": False,
}
MANIFEST["0x45779B"] = {
    "name": "MapSetting_GetFlag37",
    "comment": "Returns current map record byte @+0x25 (37).",
    "overwrite": False,
}
MANIFEST["0x479FF1"] = {
    "name": "MapSetting_ParseScrFile",
    "comment": "mapsetting.scr parser: loops read(0x54=84B) per record, qmemcpy into keyed container.",
    "overwrite": False,
}
MANIFEST["0x479F84"] = {
    "name": "MapSetting_InsertRecord",
    "comment": "qmemcpy(node, block, 0x54) into ordered map g_MapSettingTableRoot (proves 84-byte stride).",
    "overwrite": False,
}
# 0x4572E0 already RegionGrid_LookupIdByWorldXZ - add comment
MANIFEST["0x4572E0"] = {
    "name": None,
    "comment": "RegionGrid index = (X-originX)/256 + (Z-originZ)/256 * width. Signed subtract + signed /256 -> originX/Z are i32. 256-unit cell stride. Row-major.",
    "overwrite": False,
}
# 0x4289F8 already RegionTable_GetRecord - add comment
MANIFEST["0x4289F8"] = {
    "name": None,
    "comment": "RegionTable_GetRecord: bound id < 0x20 (32) and stride imul 0x30 (48) -> record = g_RegionTable + 48*id. Definitive stride 48.",
    "overwrite": False,
}
# 0x4CC1BC already RegionTable_GetRecord_Minimap - add comment
MANIFEST["0x4CC1BC"] = {
    "name": None,
    "comment": "Byte-identical 0x30*id stride-48 indexer (minimap caption path).",
    "overwrite": False,
}
MANIFEST["0x429204"] = {
    "name": "Region_ResolveCombatMode",
    "comment": "Reads zoneType @+0x28 (dword idx10) ==2 -> movement-restricted.",
    "overwrite": False,
}
# Region globals
MANIFEST["0x844488"] = {"name": "g_RegionTable", "comment": "Fixed 1536-byte block = 32 records x 48 bytes; base for both indexers.", "overwrite": False}
MANIFEST["0x844240"] = {"name": "g_RegionGridWidth", "comment": "u32 width (region.bin front).", "overwrite": False}
MANIFEST["0x844244"] = {"name": "g_RegionGridHeight", "comment": "u32 height (region.bin front).", "overwrite": False}
MANIFEST["0x844248"] = {"name": "g_RegionGridCellCount", "comment": "width*height (bounds check).", "overwrite": False}
MANIFEST["0x84424C"] = {"name": "g_RegionGridBuffer", "comment": "u8[width*height] grid body ptr.", "overwrite": False}
MANIFEST["0x844238"] = {"name": "g_RegionGridOriginX", "comment": "i32 originX (region.bin trailing).", "overwrite": False}
MANIFEST["0x84423C"] = {"name": "g_RegionGridOriginZ", "comment": "i32 originZ (region.bin trailing).", "overwrite": False}
MANIFEST["0x844A90"] = {"name": "g_CurrentMapSettingRecord", "comment": "mapsetting.scr record ptr for active map (84-byte record).", "overwrite": False}
MANIFEST["0x844A94"] = {"name": "g_NpcSpawnArray", "comment": "npc.arr 28-byte record array base (the black-box's mis-attributed 'stride-32 regiontable').", "overwrite": False}
MANIFEST["0x84CC28"] = {"name": "g_MapSettingTableRoot", "comment": "Ordered-map root keyed by map id, value = 84-byte mapsetting record.", "overwrite": False}
# Region inline comments
MANIFEST["0x456F86"] = {"name": None, "comment": "read regiontable%s.bin: SINGLE fixed-length read of 0x600 (1536) bytes = 32 x 48. No stride division, no loop. Proves record stride = 48, count = 32.", "overwrite": False, "line_comment": True}
MANIFEST["0x457039"] = {"name": None, "comment": "region grid alloc: operator new(width*height) for region%s.bin body.", "overwrite": False, "line_comment": True}
MANIFEST["0x457087"] = {"name": None, "comment": "read trailing originX (g_RegionGridOriginX) then originZ (g_RegionGridOriginZ) AFTER the grid body -> Layout A (origins trail).", "overwrite": False, "line_comment": True}
MANIFEST["0x4570EF"] = {"name": None, "comment": "npc%s.arr: GetSize/0x1C (28) + 1 = record count. 28-byte records.", "overwrite": False, "line_comment": True}
MANIFEST["0x457164"] = {"name": None, "comment": "npc.arr read loop: read(buf + i*28, 0x1C). Records [1..count); record[0] memset-zero sentinel; index array[i]=i.", "overwrite": False, "line_comment": True}
MANIFEST["0x4289FC"] = {"name": None, "comment": "RegionTable_GetRecord: bound id < 0x20 (32) and stride imul 0x30 (48) -> record = g_RegionTable + 48*id.", "overwrite": False, "line_comment": True}
MANIFEST["0x4CC1C8"] = {"name": None, "comment": "RegionTable_GetRecord_Minimap: byte-identical 0x30*id stride-48 indexer (minimap caption path).", "overwrite": False, "line_comment": True}
MANIFEST["0x429261"] = {"name": None, "comment": "zoneType read: *((u32*)RegionTable_GetRecord(id) + 10) == 2 -> dword index 10 = byte +0x28 (40). Offset only fits a 48-byte record. ==2 = movement-restricted zone.", "overwrite": False, "line_comment": True}
MANIFEST["0x457332"] = {"name": None, "comment": "RegionGrid index = (X-originX)/256 + (Z-originZ)/256 * width. Signed subtract + signed /256 -> originX/Z are i32. 256-unit cell stride. Row-major.", "overwrite": False, "line_comment": True}
MANIFEST["0x456AED"] = {"name": None, "comment": "Map_LoadAreaSoundTables: reads 5 soundtable.{bgm,bge,eff,run,wlk} fixed 0x3000 blocks. NOT a region/cell loader (corrects cartography label).", "overwrite": False, "line_comment": True}
MANIFEST["0x479FAA"] = {"name": None, "comment": "MapSetting record stride: qmemcpy(node, block, 0x54) = 84 bytes. Confirms mapsetting.scr stride 84.", "overwrite": False, "line_comment": True}
MANIFEST["0x47A04F"] = {"name": None, "comment": "mapsetting.scr parse loop: read(0x54=84B) per record into keyed container. Flat copy; inner fields decoded only by downstream consumers.", "overwrite": False, "line_comment": True}
MANIFEST["0x4575C8"] = {"name": None, "comment": "store active map id; MapSetting_GetRecordByMapId -> g_CurrentMapSettingRecord. Record fields read by consumers: +0x25 flag, +0x3C byte mode(==3), +0x50 byte name-mask(==1).", "overwrite": False, "line_comment": True}
MANIFEST["0x53AAE4"] = {"name": None, "comment": "mapsetting record +0x3C read as BYTE ==3 (special map mode). NOT a u32 boolean.", "overwrite": False, "line_comment": True}
MANIFEST["0x4CEDF9"] = {"name": None, "comment": "mapsetting record +0x50 (80) ==1 -> mask other players' names with '********'. Functional flag, NOT padding.", "overwrite": False, "line_comment": True}

# --- SCR-TABLES ---
MANIFEST["0x4902D6"] = {
    "name": "StatCurves_LoadAll",
    "comment": "StatCurves loader. users.scr read as ONE 0x1F0(496)B blob (no block stride); then userlevel.scr stride 0x3C(60), userpoint.scr 0x20(32), exp.scr 0x14(20). Builds (10/A)*B grid. Asserts users==userpoint-last==exp-last count.",
    "overwrite": False,
}
MANIFEST["0x47136C"] = {
    "name": "ItemsScr_LoadFile",
    "comment": "items.scr reader. Per record: read 0x224(548) fixed; trailing count = u8 @rec+0x220; read 8*N trailing, expand 8->12B. Dispatch: rec+0xCD->code1, +0xCE->26, +0xCF->11, +0xD0->16; discriminator rec+0xBA (==14 bypasses id-modulo). Catalogue keys rec+0x80(A)/+0x84(B).",
    "overwrite": False,
}
# 0x4712D7 already ItemsScr_LoadRecord - add comment
MANIFEST["0x4712D7"] = {
    "name": None,
    "comment": "items.scr record ctor. qmemcpy 0x224; trailing-list ptr @this+0x224 (obj=0x228). BST key = item UID @rec+0x34.",
    "overwrite": False,
}
MANIFEST["0x423A9A"] = {
    "name": "ItemAnimCatalogue_Dispatch",
    "comment": "Item anim-catalogue dispatch. arg a6 = type discriminator (rec+0xBA); !=14 runs id-modulo routing. Composes 1e9*(a6 + 100*(a5 + class)) + id key.",
    "overwrite": False,
}
MANIFEST["0x46AAAE"] = {
    "name": "CitemsScr_LoadFile",
    "comment": "citems.scr loader. Read 0x41C(1052)/record. +0 u32 = item ID (key). Billing filter: inactive->id<100000 only; active->id>=100000 only.",
    "overwrite": False,
}
MANIFEST["0x46AA53"] = {
    "name": "CitemsScr_LoadRecord",
    "comment": "citems.scr ctor. qmemcpy 0x41C. Uses +0 item ID as DENSE ARRAY INDEX (grows to 2*id) - so +0 is item_id, not a sequential slot.",
    "overwrite": False,
}
MANIFEST["0x483F33"] = {
    "name": "SkillsScr_LoadFile",
    "comment": "skills.scr reader. Per record: read 0x5E0(1504) fixed; trailing count = u8 @rec+0x5DC(+1500); read 8*N trailing, expand 8->12B (variable stride 1504+8*N, NOT flat 1504).",
    "overwrite": False,
}
MANIFEST["0x483E63"] = {
    "name": "SkillsScr_LoadRecord",
    "comment": "skills.scr ctor. qmemcpy 0x5E0; trailing ptr @this+0x5E0 (obj=0x5E4). Primary BST key +0 (skill id); secondary index on +4 (category) when non-zero.",
    "overwrite": False,
}
MANIFEST["0x47ADCE"] = {
    "name": "MobsScr_LoadFile",
    "comment": "mobs.scr loader. count=filesize/488; slurp whole file; loop advance +=488. At load: *(QWORD*)(rec+248) += 10. rec+324 u8 ==11 -> boss/elite, inserted into 2nd index. BST key = u16 @rec+0.",
    "overwrite": False,
}
MANIFEST["0x47DB06"] = {
    "name": "NpcsScr_LoadFile",
    "comment": "npcs.scr loader. Read 0x77C(1916)/record into raw byte buffer. Break on short read -> new-server 732B residual is an unconsumed partial record, no variant handling.",
    "overwrite": False,
}
MANIFEST["0x47DA20"] = {
    "name": "NpcsScr_LoadRecord",
    "comment": "npcs.scr ctor. qmemcpy 0x77C. Primary key = u16 @rec+0 (NPC id). Secondary: table @rec+128, up to 60 x 16B entries, first dword each inserted into 2nd index (referenced-id list).",
    "overwrite": False,
}
# 0x609B9C - orchestrator says apply DiskFile_ReadVirtual name+comment here
MANIFEST["0x609B9C"] = {
    "name": "DiskFile_ReadVirtual",
    "comment": "Virtual read dispatcher: reads len bytes into caller buffer (VFS in-place vs OS read by mode flag). Used by all .scr/.xdb/.bin/.bud/.ted/.sod terrain/FX decoders.",
    "overwrite": False,
}

# --- SOUND ---
MANIFEST["0x5F9348"] = {
    "name": "SoundTable_LoadArea",
    "comment": "Sound-table loader. Opens soundtable<aaa>.bgm/.bge/.eff (3 of the 5 sound exts) and reads exactly 0x3000=12288 B from each into 3 parallel buffers. On-disk record stride = 0x30 (48) bytes, count = 0x100 (256); 256*48=12288. The trailing 1024 B of the 13312-B file are NEVER read.",
    "overwrite": False,
}
# 0x5F92E3 already covered in Part 3 (SoundTable_IndexEntry) - noop in sound
# Sound inline comments
MANIFEST["0x5F9592"] = {"name": None, "comment": "Record-cursor advance = 0x30 (48 B) per iteration. DEFINITIVE record stride = 48 (refutes prior 52). cmp 0x100 below = 256 records.", "overwrite": False, "line_comment": True}
MANIFEST["0x5F9595"] = {"name": None, "comment": "Loop bound 0x100 = 256 records walked.", "overwrite": False, "line_comment": True}
MANIFEST["0x5F9550"] = {"name": None, "comment": "Per-record loop: reads ONLY the +0x00 dword (sound_entry_id) of each of the 3 parallel arrays; passes it to the keyed-id insert. hour_schedule(+0x04), positions(+0x20/+0x24/+0x28), radius(+0x2C) are NOT read here.", "overwrite": False, "line_comment": True}

# --- SPAWNS-MI ---
# 0x456E70 already covered in REGION (Map_LoadAreaBinaries) - spawns-mi proposes Area_LoadGeometryAndNpcArr
# Per idempotency: apply region name (first occurrence), spawns noop. BUT add comment from spawns-mi
MANIFEST["0x456DD4"] = {
    "name": "Area_FreeGeometryAndNpcArr",
    "comment": "Area reset/free: releases g_NpcArrRecords(+index) and region buffer; zeroes map.bin/regiontable.bin/region.bin blocks. No mob.arr buffer (none is loaded at runtime).",
    "overwrite": False,
}
# 0x4575AF already covered in env (Env_MapSetAndLoadArea) - spawns-mi proposes Area_SetActive, noop
# 0x4571BC already covered in Part 3 (NpcArr_FindRecordById) with allow_overwrite - noop in spawns
MANIFEST["0x51768D"] = {
    "name": "NpcArr_FindRecordById_Alt",
    "comment": "Linear scan of g_NpcArrRecords (28B stride) matching id@+0 -> record pointer.",
    "overwrite": False,
}
MANIFEST["0x5B4BE0"] = {
    "name": "NpcArr_GetIdByIndex",
    "comment": "Returns u16 id@+0 of npc.arr record[a1] (bounds-checked).",
    "overwrite": False,
}
MANIFEST["0x4C665F"] = {
    "name": "NpcArr_GetRecordByIndex",
    "comment": "npc.arr accessor: index -> record ptr (base + 28*i). Used by every facing(+12)/spawn_type(+16) consumer.",
    "overwrite": False,
}
MANIFEST["0x4C77E3"] = {
    "name": "NpcArr_FindActorBySpawnGroup",
    "comment": "Reads npc.arr rec+16 (spawn-group id); finds a live actor whose field+520 == that id. Confirms +16 = spawn-group link.",
    "overwrite": False,
}
MANIFEST["0x56CEE0"] = {
    "name": "SpawnType_EliteMultiplierA",
    "comment": "npc.arr rec+16 == 7 (elite/boss) && map-id==1 && dword_844480==12 -> 110 (else 100): +10% combat modifier.",
    "overwrite": False,
}
MANIFEST["0x571E77"] = {
    "name": "SpawnType_EliteMultiplierB",
    "comment": "npc.arr rec+16 == 7 (elite/boss) -> sub_4AFFBD(85)+100 (else 100): variable elite bonus.",
    "overwrite": False,
}
# spawns-mi globals (some overlap with region g_NpcSpawnArray = 0x844A94)
MANIFEST["0x844A98"] = {"name": "g_NpcArrCount", "comment": "Record count = GetSize/0x1C + 1.", "overwrite": False}
MANIFEST["0x844A9C"] = {"name": "g_NpcArrIndex", "comment": "Parallel (n)*4 identity index array.", "overwrite": False}
# spawns-mi inline comments
MANIFEST["0x5EF5EF"] = {"name": None, "comment": "S2C 4/4 area entity snapshot. kind==3 spawn: reads npc.arr facing via NpcArr_GetRecordByIndex -> actor yaw = (pi/2 - *(f32*)(rec+12)). Mob/npc actors come from THIS packet (+ mobs.scr by id), not from a mob.arr file.", "overwrite": False, "line_comment": True}

# --- TERRAIN-CELL ---
# 0x43D9E9 already Map_ParseDescriptor - add comment
MANIFEST["0x43D9E9"] = {
    "name": None,
    "comment": ".map text descriptor parser. Per section keyword reads DATAFILE token, opens via DiskFile_Open_ByValue, dispatches: TERRAIN->Ted_LoadGeometryBlob(.ted) EXTRA_TERRAIN->Exd_DecodeTriangles(.exd) UP_TERRAIN->Up_DecodeTriangles(.up) BUILDING->Bud_LoadBuildingBlob(.bud) FX1..FX7->Fx1..7_DecodeGroups SOLID->Sod_LoadCollisionBlob(.sod).",
    "overwrite": False,
}
MANIFEST["0x43F626"] = {
    "name": "Map_ParseDescriptor_LooseDisk",
    "comment": "Secondary/loose-disk .map parser (same grammar, non-VFS DiskFile path).",
    "overwrite": False,
}
MANIFEST["0x440CCE"] = {
    "name": "Map_LoadCellDescriptor",
    "comment": ".map load (VFS vs loose branch) + cell finalize tail (calls Ted_ResolvePatchTextures + subsystem build).",
    "overwrite": False,
}
MANIFEST["0x440F47"] = {
    "name": "Terrain_LoadCellFiles",
    "comment": "Per-cell streaming: bounds=(map-10000)*1024; base path 'data/map%d%d%d/dat/d%d%d%dx%dz%d'; loads %s.mud, %s.gad, %s.map in order.",
    "overwrite": False,
}
MANIFEST["0x44191E"] = {
    "name": "Terrain_AcquireSlotAndLoadCell",
    "comment": "Cell critical-section + slot pick -> Terrain_LoadCellFiles.",
    "overwrite": False,
}
MANIFEST["0x44ADE2"] = {
    "name": "Ted_LoadGeometryBlob",
    "comment": ".ted loader: 5 fixed-length block reads, no header. 0x4204 heightmap(f32 65x65) + 0x3183 normals(i8x3) + 0x100 texidx(16x16 u8) + 0x100 dirmap(16x16 u8) + 0x4204 diffuse(u8x4 65x65) = 46987 B total. Normals /127.0; diffuse R/G/B *0.5 on read; dir byte &1=mirror S/U, &2=mirror T/V.",
    "overwrite": False,
}
MANIFEST["0x44B267"] = {
    "name": "TileTerrain_SetTextureId",
    "comment": "Register per-cell TERRAIN texture id (slot order, cap 128). Per-cell TERRAIN texture list write: list base = this+73473 (dword slots), filled in .map order from slot 0, cap 128.",
    "overwrite": False,
}
MANIFEST["0x44B296"] = {
    "name": "Ted_ResolvePatchTextures",
    "comment": "Per-cell finalize: clamp grid byte (<1 ->1, >count ->1) then resolve perCellTexList[byte-1] (THE idx-1 site). IDX-1 SITE: byte = *(this + byte + 73472) = perCellTexList[byte-1]. The -1 is baked into the 73472 base.",
    "overwrite": False,
}
MANIFEST["0x44E23F"] = {
    "name": "Bud_LoadBuildingBlob",
    "comment": ".bud reader: objectCount + per-object {type_byte(1)@52, tex_id(u32)@0, vertex_count(u32)@8, then vertices 32Bx vtxCount; vtxCount>3072 -> WARN-ONLY-CONTINUE; then index_count(u32)@16 + idx 2xidxCount}. In-memory BudObject stride 0x74=116.",
    "overwrite": False,
}
MANIFEST["0x44DD00"] = {
    "name": "BuildingSection_AddTextureId",
    "comment": "Register per-cell BUILDING texture id (BUILDING TEXTURES list).",
    "overwrite": False,
}
MANIFEST["0x458F13"] = {
    "name": "Sod_LoadCollisionBlob",
    "comment": ".sod reader: u32 solidCount; then 108 x solidCount flat SolidRecord array in ONE pass; per-solid: read u32 quadCount, alloc 48 x quadCount quad array, read 48 x quadCount QuadRecord bytes. File = 4 + sum(108 + 4 + 48*quadCount).",
    "overwrite": False,
}
MANIFEST["0x659B03"] = {
    "name": "Sod_AllocSolidQuadArray",
    "comment": "Store stream quadCount into SolidRecord+60; alloc 48xquadCount quad array at SolidRecord+64 (overwrites on-disk embedded-count/authoring-ptr slots).",
    "overwrite": False,
}
MANIFEST["0x65A655"] = {
    "name": "Sod_BuildSolidQuadtree",
    "comment": "Recursive quadtree partition over a solid; reads SolidRecord AABB(+44/+48); does NOT read any QuadRecord scalar at +32..+47.",
    "overwrite": False,
}
MANIFEST["0x65A256"] = {
    "name": "Sod_QuadtreeLeafAppend",
    "comment": "Append solid/quad ref into a quadtree leaf bucket.",
    "overwrite": False,
}
MANIFEST["0x441D18"] = {
    "name": "SmallU32ArrayLoader_4575AF",
    "comment": "Count-prefixed u32-array reader (caller Env_MapSetAndLoadArea). NOT a terrain-cell .ted/.bud/.sod loader — scope clarification.",
    "overwrite": False,
}
# terrain-cell inline comments
MANIFEST["0x44AE5C"] = {"name": None, "comment": "Read #3: 0x100 (256) bytes = texture-index grid (16x16 u8); stored raw, resolved later in Ted_ResolvePatchTextures.", "overwrite": False, "line_comment": True}
MANIFEST["0x44B289"] = {"name": None, "comment": "Per-cell TERRAIN texture list write: list base = this+73473 (dword slots), filled in .map order from slot 0, cap 128.", "overwrite": False, "line_comment": True}
MANIFEST["0x44B2AA"] = {"name": None, "comment": "Value-0 handling: if patch texture byte < 1 it is clamped to 1 (no no-texture sentinel; 0 renders texture slot 1).", "overwrite": False, "line_comment": True}
MANIFEST["0x44B2B6"] = {"name": None, "comment": "Out-of-range handling: if byte > registered per-cell texture count it is clamped to 1.", "overwrite": False, "line_comment": True}
MANIFEST["0x44B2C3"] = {"name": None, "comment": "IDX-1 SITE: byte = *(this + byte + 73472) = perCellTexList[byte-1]. The -1 is baked into the 73472 base (list base 73473 minus 1). Resolves grid byte to global-pool intTexId.", "overwrite": False, "line_comment": True}
MANIFEST["0x44E267"] = {"name": None, "comment": ".bud read #1: u32 objectCount. In-memory BudObject stride 0x74=116 (NOT on-disk size).", "overwrite": False, "line_comment": True}
MANIFEST["0x44E2E7"] = {"name": None, "comment": ".bud per-object on-disk read order: type_byte(1)@52, tex_id(u32)@0, vertex_count(u32)@8, then vertices. type_byte read (1 byte) and retained; no branch on it in this loader.", "overwrite": False, "line_comment": True}
MANIFEST["0x44E385"] = {"name": None, "comment": "Read vertex array: 32 bytes x vertex_count (vertex stride 0x20=32). Alloc+read use FULL on-disk vertex_count.", "overwrite": False, "line_comment": True}
MANIFEST["0x44E398"] = {"name": None, "comment": "VERTEX-CAP: if vertex_count > 0xC00 (3072) -> LOG ONLY ('mass object vertex_count(%d) over') then CONTINUE. No clamp/truncate/skip/realloc; full count already allocated+read above. Legacy = warn-and-continue.", "overwrite": False, "line_comment": True}
MANIFEST["0x44E3C7"] = {"name": None, "comment": ".bud read index_count (u32)@16 then index array 2 x index_count (u16 triangle list).", "overwrite": False, "line_comment": True}
MANIFEST["0x458F47"] = {"name": None, "comment": ".sod read u32 solidCount; then 108 x solidCount flat SolidRecord array (in-memory stride 0x6C=108) in ONE pass.", "overwrite": False, "line_comment": True}
MANIFEST["0x458FC9"] = {"name": None, "comment": ".sod per-solid: read u32 quadCount, alloc 48 x quadCount quad array (Sod_AllocSolidQuadArray), read 48 x quadCount QuadRecord bytes.", "overwrite": False, "line_comment": True}
MANIFEST["0x458FF3"] = {"name": None, "comment": "QuadRecord slurped as opaque 48-B blocks; +32..+47 (incl edge_pad0 at +36) never field-sub-read. Consumer reads SolidRecord AABB + quad corners only.", "overwrite": False, "line_comment": True}
MANIFEST["0x659B20"] = {"name": None, "comment": "Stream quadCount stored into SolidRecord+60; heap quad-array ptr stored into SolidRecord+64 (overwrites on-disk embedded-count/authoring-ptr slots).", "overwrite": False, "line_comment": True}

# --- TERRAIN-OVERLAYS ---
# 0x44191E: conflict terrain-cell vs overlays - terrain-cell name Terrain_AcquireSlotAndLoadCell wins (already set)
# overlays also proposes Terrain_LoadCellLocked - skip (noop)
MANIFEST["0x456D5B"] = {
    "name": "Mud_LoadGrid",
    "comment": ".mud loader: free old, reload.",
    "overwrite": False,
}
MANIFEST["0x456C84"] = {
    "name": "Mud_ReadBlob",
    "comment": ".mud loader: opens file, operator new(0x8000), DiskFile_ReadVirtual(blob, 0x8000). Fixed 32768B (64*64*8) ambient-sound zone grid. No field parse at load.",
    "overwrite": False,
}
MANIFEST["0x456D0C"] = {
    "name": "Mud_TileAt",
    "comment": ".mud tile indexer: tile = blob + 8*((floor(x*0.0625)&0x3F) + ((floor(z*0.0625)&0x3F)<<6)). 16-unit tiles, 64 cols, 8B stride. Null blob -> default tile unk_835228.",
    "overwrite": False,
}
MANIFEST["0x452CC6"] = {
    "name": "Mud_TileAtLocal",
    "comment": "Subtracts cell origin then -> Mud_TileAt.",
    "overwrite": False,
}
MANIFEST["0x452CFF"] = {
    "name": "Mud_TileAtFromTerrainMgr",
    "comment": "Null-guarded tile fetch via terrain mgr.",
    "overwrite": False,
}
MANIFEST["0x455EF0"] = {
    "name": "SoundMgr_UpdateAmbientFromMudTile",
    "comment": "SOLE .mud tile consumer (per-frame ambient sound). Tile ptr v35: byte2=BGM, byte3/4=BGE, byte5/6/7=EFF in a 3-iter loop. Bytes 0/1 are NOT read by any located path in this build.",
    "overwrite": False,
}
MANIFEST["0x45D01F"] = {
    "name": "Fx1_DecodeGroups",
    "comment": ".fx1 decoder. Reads u32 groupCount; per group reads 20B header (vertexCount@+0x0C, indexCount@+0x10), then vertexCount*36 (VF_36) verts + indexCount*2 u16 indices. Leading u32 is GROUP COUNT, not a type_tag.",
    "overwrite": False,
}
MANIFEST["0x45ED5B"] = {
    "name": "Fx2_DecodeGroups",
    "comment": ".fx2 decoder. Same flat-group-array model as .fx1 but vertex stride 44 (VF_44). u32 groupCount + per-group(20B hdr vc@+0x0C/ic@+0x10, vc*44, ic*2).",
    "overwrite": False,
}
MANIFEST["0x460B6F"] = {
    "name": "Fx3_DecodeGroups",
    "comment": ".fx3 decoder. u32 groupCount + per-group(44B hdr: vertexCount@+0x24, indexCount@+0x28; vc*36 VF_36; ic*2). Group rec 0x70.",
    "overwrite": False,
}
MANIFEST["0x4629BD"] = {
    "name": "Fx4_DecodeGroups",
    "comment": ".fx4 decoder. u32 groupCount + per-group(48B hdr: vertexCount@+0x28, indexCount@+0x2C; vc*44 VF_44; ic*2). Group rec 0x74.",
    "overwrite": False,
}
MANIFEST["0x46483D"] = {
    "name": "Fx5_DecodeGroups",
    "comment": ".fx5 decoder. u32 groupCount + per-group(48B hdr: vertexCount@+0x28, indexCount@+0x2C; vc*36 VF_36; ic*2). Group rec 0x78.",
    "overwrite": False,
}
MANIFEST["0x466616"] = {
    "name": "Fx6_DecodeGroups",
    "comment": ".fx6 decoder. u32 groupCount + per-group(36B hdr: vertexCount@+0x1C, indexCount@+0x20; vc*32 VF_32; ic*2). Group rec 0x70.",
    "overwrite": False,
}
MANIFEST["0x468596"] = {
    "name": "Fx7_DecodeGroups",
    "comment": ".fx7 decoder. u32 groupCount + per-group(48B hdr: vertexCount@+0x28, indexCount@+0x2C; vc*32 VF_32; ic*2). Group rec 0x70.",
    "overwrite": False,
}
MANIFEST["0x45B018"] = {
    "name": "Exd_DecodeTriangles",
    "comment": ".exd (EXTRA_TERRAIN) collision-triangle decoder.",
    "overwrite": False,
}
MANIFEST["0x45BB55"] = {
    "name": "Up_DecodeTriangles",
    "comment": ".up (UP_TERRAIN) overhang-triangle decoder.",
    "overwrite": False,
}
MANIFEST["0x4521A9"] = {
    "name": "Wind_LoadMapData",
    "comment": "wind%d.bin loader. u32 count@+0x00, u32 flag2@+0x04, then count*24B records. If flag2!=0: WindObject_SetTexCount(count) then per record WindObject_SetRecordTexId(rec+0x14) -> wind-object texture id.",
    "overwrite": False,
}
MANIFEST["0x452991"] = {
    "name": "WindObject_SetRecordTexId",
    "comment": "Per-record consumer: sets wind-object texture from record +0x14.",
    "overwrite": False,
}
# 0x4525B1 already WindObject_SetTexCount - add comment
MANIFEST["0x4525B1"] = {
    "name": None,
    "comment": "Allocates wind-object texture slots.",
    "overwrite": False,
}
# terrain-overlays inline comments
MANIFEST["0x456AED"] # already set in region section

# --- TEXTURES-UI ---
MANIFEST["0x495214"] = {
    "name": "UiTex_ParseManifest",
    "comment": "UiTex.txt parser. Skip '#' comments; match UI_TEXTURE '{'; sub-blocks DDS & MSK. Entry = <int tex_id> '<path>' (2 fields, same in DDS+MSK). Loop reads rows until '}'/ EOF: NO fixed count -> 37 rows in real file. char-34 = quote strip.",
    "overwrite": False,
}
MANIFEST["0x52540D"] = {
    "name": "SkillIcon_ParseManifest",
    "comment": "skillicon.txt parser. Match SKILL '{' (no nesting). Entry = exactly 4 cols: int skill_id, int job_id (low byte, 1..4), int kind_id (low byte, 1..3), quoted icon_sheet_path. SkillIcon_RegisterEntry registers the 4-tuple. Loop until '}'/ EOF.",
    "overwrite": False,
}
MANIFEST["0x5252F0"] = {
    "name": "SkillIcon_RegisterEntry",
    "comment": "Register (skill_id, job_id, kind_id, icon_path) 4-tuple into skill-icon table.",
    "overwrite": False,
}
MANIFEST["0x46CEDB"] = {
    "name": "GuildCrest_ParseListAndLoad",
    "comment": "crestlist.txt parser (NOT braced; line-list). while(!EOF) read line; filename {region}_{type}_{guild_id}_{server_id}_{name}.dds split RIGHT-to-left. atol two numeric fields; one GATED vs singleton@0x7B0790 (region/server filter). Load each via GuildCrest_LoadOneTexture. Row count EOF-driven -> 1952 lines (spec's ~1350 is a wrong byte-avg estimate).",
    "overwrite": False,
}
MANIFEST["0x46CDAA"] = {
    "name": "GuildCrest_LoadOneTexture",
    "comment": "Load one guild-crest DDS texture (name, FourCC='DXT2'=844388420, w=23, h=23) from pool dir.",
    "overwrite": False,
}
# 0x61451B already Texture_LoadFromVfsOrDisk - add comment
MANIFEST["0x61451B"] = {
    "name": None,
    "comment": "Generic UI/item/face texture loader. Mounted: VFS_FindAndReadEntry slurp -> D3DXCreateTextureFromFileInMemoryEx (auto-detects dds/tga/bmp/png/jpg from blob header). Unmounted: -> Texture_D3DXCreateFromDiskOrVfs. No dedup.",
    "overwrite": False,
}
MANIFEST["0x605563"] = {
    "name": "Texture_D3DXCreateFromDiskOrVfs",
    "comment": "D3DX texture create. Mounted: DiskFile slurp -> D3DXCreateTextureFromFileInMemoryEx. Unmounted: D3DXCreateTextureFromFileExA from disk path. ALL raster formats share one D3DX9 auto-detect entry.",
    "overwrite": False,
}
MANIFEST["0x6481A3"] = {
    "name": "DiskToken_ParseInt",
    "comment": "atol() of current whitespace-delimited token. Returns the int (0 if non-numeric); the '>=0' guard at callers is effectively always-true.",
    "overwrite": False,
}
# 0x616F57 already Icon_LoadFileVFSorDisk - add comment
MANIFEST["0x616F57"] = {
    "name": None,
    "comment": "Byte-slurp file into re-tokenizable buffer object (not a parser itself). Mounted: VFS slurp; unmounted: disk read.",
    "overwrite": False,
}
# 0x60B222 already Diamond_GHTex__ctor - add comment
MANIFEST["0x60B222"] = {
    "name": None,
    "comment": "GHTex handle ctor; stores path@+4, FourCC hint@+36, pool link@+52/+72.",
    "overwrite": False,
}
# textures-ui inline comments
MANIFEST["0x49551d"] = {"name": None, "comment": "Build GHTex handle with FourCC hint: render-mode 0->DXT5(894720068), 1->DXT3(861165636), else->DXT2(844388420).", "overwrite": False, "line_comment": True}
MANIFEST["0x5255a5"] = {"name": None, "comment": "col1 skill_id = atol(token); next tokens = job_id, kind_id (LOBYTE), then quoted path.", "overwrite": False, "line_comment": True}
MANIFEST["0x46d3c3"] = {"name": None, "comment": "After list: always load built-in guildbasic.dds as 72x48 'basic' default crest (the non-704B pool exception).", "overwrite": False, "line_comment": True}
MANIFEST["0x46d2bb"] = {"name": None, "comment": "region/server runtime GATE: only crests whose key == active singleton value are loaded (list line count > runtime-loaded count).", "overwrite": False, "line_comment": True}

# --- XDB-DO ---
MANIFEST["0x536441"] = {
    "name": "EffectScaleXdb_Load",
    "comment": "effectscale.xdb loader: stride 8 (size>>3); flat buffer @g_EffectScaleXdb_Records; map @g_EffectScaleXdb_Map keyed by effect_key(+0).",
    "overwrite": False,
}
MANIFEST["0x53662A"] = {
    "name": "CreatureItemXdb_Load",
    "comment": "creature_item.xdb loader: stride 48 (size/0x30); flat buffer @g_CreatureItemXdb_Records; map @g_CreatureItemXdb_Map keyed by creature_key(+0).",
    "overwrite": False,
}
MANIFEST["0x5366FE"] = {
    "name": "VehicleXdb_Load",
    "comment": "vehicle.xdb loader: stride 52 (size/0x34); flat buffer @g_VehicleXdb_Records; map @g_VehicleXdb_Map keyed by vehicle_id(+0).",
    "overwrite": False,
}
MANIFEST["0x5367D2"] = {
    "name": "BuffIconPositionXdb_Load",
    "comment": "buff_icon_position.xdb loader: stride 12 (size/0xC); flat buffer @g_BuffIconPositionXdb_Records; map @g_BuffIconPositionXdb_Map keyed by buff_id(+0).",
    "overwrite": False,
}
# 0x4A2B25 already set above as EffectScaleXdb_LookupScale
MANIFEST["0x4117CF"] = {
    "name": "VehicleXdb_LookupRecord",
    "comment": "vehicle lookup: vehicle_id -> 52B record ptr; 0 if absent.",
    "overwrite": False,
}
MANIFEST["0x4117F9"] = {
    "name": "CreatureItemXdb_LookupRecord",
    "comment": "creature_item lookup: creature_key -> 48B record ptr; 0 if absent.",
    "overwrite": False,
}
MANIFEST["0x4AFF76"] = {
    "name": "BuffIconPositionXdb_LookupRecord",
    "comment": "buff_icon_position lookup: buff_id -> 12B record ptr; 0 if absent.",
    "overwrite": False,
}
MANIFEST["0x4AFFA0"] = {
    "name": "BuffIconPositionXdb_GetSpriteXY",
    "comment": "buff_icon: buff_id -> sprite_x in EAX, sprite_y in EDX (record +4, +8).",
    "overwrite": False,
}
MANIFEST["0x4289D6"] = {
    "name": "VehicleXdb_GetFacingYOffset",
    "comment": "vehicle per-facing Y offset: dir 1..4 selects record float[dir+8] = param_4..8 (+32..+48); else 0.0.",
    "overwrite": False,
}
MANIFEST["0x428E7D"] = {
    "name": "Vehicle_ApplyMountYHeight",
    "comment": "vehicle mount: samples terrain Y then ADDS vehicle per-facing Y offset to rider height. Only +0/+4/+16/+32..+48 used; tag_a(+8)/tag_b(+12) ignored.",
    "overwrite": False,
}
MANIFEST["0x40D313"] = {
    "name": "Vehicle_SpawnMountVisual",
    "comment": "mount spawn: copies vehicle_id(+0) + item_id(+4) to descriptor; param_0(+16) sets visual SCALE when non-zero. tag_a/tag_b skipped.",
    "overwrite": False,
}
MANIFEST["0x41205A"] = {
    "name": "Actor_UpdateMountAttachment",
    "comment": "mount/attachment dispatch; vehicle lookup keyed by actor byte +542.",
    "overwrite": False,
}
MANIFEST["0x411DE9"] = {
    "name": "CreatureItem_SpawnAttachment",
    "comment": "creature_item attach spawn: item_id(+4) visual; offset vectors (f0,0,f1)=(+8,+12), then state-branch (f2,0,f3)=(+16,+20) or (f4,0,f5)=(+24,+28); Y forced 0, rotated into facing. +36 = visual scale.",
    "overwrite": False,
}
MANIFEST["0x42BD51"] = {
    "name": "CreatureItem_TickEffectGate",
    "comment": "creature_item tick gate: flags +40..+43 gate branches; +44 used as ms tick INTERVAL (not a percent).",
    "overwrite": False,
}
MANIFEST["0x484AE9"] = {
    "name": "StanceDoTable_LoadStreaming",
    "comment": ".do stance loader: streams 116B (0x74) records to EOF; floor-div ignores short tail. Per-record StanceDoTable_ParseRecord.",
    "overwrite": False,
}
MANIFEST["0x484A2D"] = {
    "name": "StanceDoTable_ParseRecord",
    "comment": ".do record parse: copies 116B; inserts into g_StanceDoTable_ByInstanceKey by instanceKey(+0) AND g_StanceDoTable_BySlotIndex by slotIndex(+8).",
    "overwrite": False,
}
# xdb-do globals
MANIFEST["0x84d7c0"] = {"name": "g_EffectScaleXdb_Map", "comment": "std::map effect_key -> record (effectscale).", "overwrite": False}
MANIFEST["0x84d7d0"] = {"name": "g_EffectScaleXdb_Records", "comment": "Flat record buffer (effectscale).", "overwrite": False}
MANIFEST["0x7a8ff4"] = {"name": "g_CreatureItemXdb_Map", "comment": "std::map creature_key -> record.", "overwrite": False}
MANIFEST["0x84df04"] = {"name": "g_CreatureItemXdb_Records", "comment": "Flat record buffer (creature_item).", "overwrite": False}
MANIFEST["0x7a9000"] = {"name": "g_VehicleXdb_Map", "comment": "std::map vehicle_id -> record.", "overwrite": False}
MANIFEST["0x84def4"] = {"name": "g_VehicleXdb_Records", "comment": "Flat record buffer (vehicle).", "overwrite": False}
MANIFEST["0x84d8b0"] = {"name": "g_BuffIconPositionXdb_Map", "comment": "std::map buff_id -> record.", "overwrite": False}
MANIFEST["0x84dee4"] = {"name": "g_BuffIconPositionXdb_Records", "comment": "Flat record buffer (buff_icon_position).", "overwrite": False}
MANIFEST["0x84cdfc"] = {"name": "g_StanceDoTable_ByInstanceKey", "comment": "std::map instanceKey(+0) -> .do record.", "overwrite": False}
MANIFEST["0x84ce00"] = {"name": "g_StanceDoTable_BySlotIndex", "comment": "std::map slotIndex(+8) -> .do record.", "overwrite": False}
MANIFEST["0x7a0094"] = {"name": "g_Path_EffectScaleXdb", "comment": "std::string 'data/script/effectscale.xdb'.", "overwrite": False}
MANIFEST["0x7a0098"] = {"name": "g_Path_CreatureItemXdb", "comment": "std::string 'data/script/creature_item.xdb'.", "overwrite": False}
MANIFEST["0x7a009c"] = {"name": "g_Path_VehicleXdb", "comment": "std::string 'data/script/vehicle.xdb'.", "overwrite": False}
MANIFEST["0x7a00a4"] = {"name": "g_Path_BuffIconPositionXdb", "comment": "std::string 'data/script/buff_icon_position.xdb'.", "overwrite": False}
MANIFEST["0x7a00a0"] = {"name": "g_Path_ActorSizeXdb_UNUSED", "comment": "std::string 'data/script/actor_size.xdb' — constructed by static init but NEVER opened in C7 (no loader/consumer; zero refs). Table is DEAD in this build.", "overwrite": False}
# xdb-do inline comments
MANIFEST["0x7a00a0_dead"] = None  # skip placeholder

# ============================================================
# APPLIER ENGINE
# ============================================================

def get_effective_name(ea):
    """Get the current name at an EA, stripping IDA-generated prefixes."""
    name = idc.get_func_name(ea)
    if not name:
        name = idc.get_name(ea)
    return name or ""

def set_function_comment(ea, comment, repeatable=True):
    """Set a function comment (repeatable so it shows in decompiler)."""
    idc.set_func_cmt(ea, comment, repeatable)

def set_line_comment(ea, comment, repeatable=False):
    """Set a regular line/anterior comment."""
    idc.set_cmt(ea, comment, repeatable)

results = []
define_func_result = None

# === SHA-256 CHECK ===
bin_path = idaapi.get_input_file_path()
try:
    with open(bin_path, 'rb') as f:
        actual_sha = hashlib.sha256(f.read()).hexdigest()
    if actual_sha != EXPECTED_SHA:
        print(f"RESULT_JSON: {{\"error\": \"SHA MISMATCH: got {actual_sha}, expected {EXPECTED_SHA}\"}}")
        raise SystemExit("SHA mismatch")
    sha_ok = True
except FileNotFoundError:
    sha_ok = False

# === PHASE 1: define_func if needed ===
df_ea_start = 0x608C70
df_ea_end = 0x608E96

fn = ida_funcs.get_func(df_ea_start)
if fn is not None and fn.start_ea == df_ea_start:
    define_func_result = "noop"
else:
    if MODE == "apply":
        ok = idc.add_func(df_ea_start, df_ea_end)
        define_func_result = "applied" if ok else "failed"
    else:
        define_func_result = "would-define"

# === PHASE 2: process manifest ===
for addr_str, entry in MANIFEST.items():
    if entry is None:
        continue
    if addr_str.endswith("_dead"):
        continue

    try:
        ea = int(addr_str, 16)
    except ValueError:
        results.append({"addr": addr_str, "verdict": "error", "reason": "bad address"})
        continue

    desired_name = entry.get("name")
    desired_comment = entry.get("comment")
    overwrite = entry.get("overwrite", False)
    is_line_comment = entry.get("line_comment", False)

    current_name = get_effective_name(ea)

    # Skip runtime symbols
    if is_runtime(current_name):
        results.append({"addr": addr_str, "current_name": current_name, "desired_name": desired_name, "verdict": "skip-runtime"})
        continue

    # Validate EA resolves (has some data)
    flags = idc.get_full_flags(ea)
    if flags == 0xFFFFFFFF:
        results.append({"addr": addr_str, "verdict": "skip-missing", "reason": "invalid EA"})
        continue

    verdict = "noop"
    rename_applied = False
    comment_applied = False

    # --- NAME ---
    if desired_name is not None:
        # Generate the "base" auto-name pattern for this EA
        is_sub = current_name.startswith("sub_") or current_name.startswith("loc_") or current_name.startswith("dword_") or current_name.startswith("off_") or current_name.startswith("unk_") or current_name.startswith("flt_") or current_name.startswith("byte_") or current_name.startswith("word_") or current_name.startswith("stru_")
        already_has_desired = (current_name == desired_name)
        has_different_user_name = not is_sub and not already_has_desired and current_name != ""

        if already_has_desired:
            # noop for name
            pass
        elif has_different_user_name and not overwrite:
            results.append({
                "addr": addr_str,
                "current_name": current_name,
                "desired_name": desired_name,
                "verdict": "conflict",
                "reason": f"existing user name '{current_name}' != desired '{desired_name}', overwrite=False"
            })
            # Still try to apply comment below
        else:
            if MODE == "apply":
                flags_name = ida_name.SN_FORCE if overwrite else 0
                ok = idc.set_name(ea, desired_name, flags_name | ida_name.SN_NOCHECK)
                verdict = "applied" if ok else "failed"
                rename_applied = ok
            else:
                verdict = "would-apply"
            # Mark for results
            results.append({
                "addr": addr_str,
                "current_name": current_name,
                "desired_name": desired_name,
                "verdict": verdict,
            })
            continue  # comment handled below after this block

    # --- COMMENT ---
    if desired_comment is not None:
        fn = ida_funcs.get_func(ea)
        is_func_start = fn is not None and fn.start_ea == ea

        if is_line_comment or not is_func_start:
            # Line comment at this address
            existing = idc.get_cmt(ea, 0) or ""
            if desired_comment in existing:
                comment_verdict = "noop"
            else:
                if MODE == "apply":
                    idc.set_cmt(ea, desired_comment, 0)
                    comment_verdict = "comment-applied"
                else:
                    comment_verdict = "would-comment"
        else:
            # Function comment (repeatable)
            existing = idc.get_func_cmt(ea, 1) or ""
            if desired_comment in existing:
                comment_verdict = "noop"
            else:
                if MODE == "apply":
                    idc.set_func_cmt(ea, desired_comment, 1)
                    comment_verdict = "comment-applied"
                else:
                    comment_verdict = "would-comment"

        name_verdict = "noop" if desired_name is None else verdict
        results.append({
            "addr": addr_str,
            "current_name": current_name,
            "desired_name": desired_name,
            "verdict": name_verdict,
            "comment_verdict": comment_verdict,
        })
    elif desired_name is not None:
        # Already handled above but didn't continue if it was noop
        if not any(r.get("addr") == addr_str for r in results):
            results.append({
                "addr": addr_str,
                "current_name": current_name,
                "desired_name": desired_name,
                "verdict": "noop",
            })

# Post-pass: handle entries where we did continue early but still need comment
# (This happens when name was applied via the rename branch and we continued)
# Re-process comment for those entries
for addr_str, entry in MANIFEST.items():
    if entry is None or addr_str.endswith("_dead"):
        continue
    # Find if this addr had a "would-apply" or "applied" name verdict already
    existing_result = next((r for r in results if r.get("addr") == addr_str), None)
    if existing_result and existing_result.get("verdict") in ("applied", "would-apply") and "comment_verdict" not in existing_result:
        desired_comment = entry.get("comment")
        if desired_comment is None:
            continue
        try:
            ea = int(addr_str, 16)
        except ValueError:
            continue
        is_line_comment = entry.get("line_comment", False)
        fn = ida_funcs.get_func(ea)
        is_func_start = fn is not None and fn.start_ea == ea

        if is_line_comment or not is_func_start:
            existing = idc.get_cmt(ea, 0) or ""
            if desired_comment in existing:
                existing_result["comment_verdict"] = "noop"
            else:
                if MODE == "apply":
                    idc.set_cmt(ea, desired_comment, 0)
                    existing_result["comment_verdict"] = "comment-applied"
                else:
                    existing_result["comment_verdict"] = "would-comment"
        else:
            existing = idc.get_func_cmt(ea, 1) or ""
            if desired_comment in existing:
                existing_result["comment_verdict"] = "noop"
            else:
                if MODE == "apply":
                    idc.set_func_cmt(ea, desired_comment, 1)
                    existing_result["comment_verdict"] = "comment-applied"
                else:
                    existing_result["comment_verdict"] = "would-comment"

# Count verdicts
counts = {}
for r in results:
    v = r.get("verdict", "?")
    counts[v] = counts.get(v, 0) + 1
    cv = r.get("comment_verdict")
    if cv:
        counts[cv] = counts.get(cv, 0) + 1

summary = {
    "mode": MODE,
    "sha_ok": sha_ok,
    "cluster": CLUSTER,
    "define_func": define_func_result,
    "counts": counts,
    "conflicts": [r for r in results if r.get("verdict") == "conflict"],
    "failures": [r for r in results if r.get("verdict") == "failed"],
    "results": results,
}

print("RESULT_JSON: " + json.dumps(summary))
