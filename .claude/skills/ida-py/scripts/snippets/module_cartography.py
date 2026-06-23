"""
module_cartography.py  --  Binary Module Map for doida.exe
Reusable IDAPython snippet: classify all named functions into subsystems,
report counts, address ranges, and representative entry-points.
Output goes to stdout as Markdown. Zero IDB mutations.

Usage (via MCP exec tool):
    Paste the full script content into py_eval.
    Optionally set OUTPUT_PATH to a writable path and the script will also
    write the Markdown table there.

# === CONFIG ===
"""
OUTPUT_PATH = None          # e.g. r"C:/tmp/module_cartography.md"  or None for stdout-only
MIN_NAMED   = 1             # skip subsystems with fewer than this many named fns
SHOW_SAMPLES = 3            # number of representative functions to show per subsystem

# === LOGIC ===
import idc, idautils


def classify_name(name):
    """Return the subsystem tag for a named function."""
    CRT_PREFIXES = [
        '__', '_strlen', '_memcpy', '_memset', '_memcmp', '_malloc', '_free',
        '_calloc', '_realloc', '_sprintf', '_printf', '_strcmp', '_strcpy',
        '_strcat', '_strtok', 'start_', '_abs', '_fabs', '_sin', '_cos',
        '_acos', '_tan', '_atan', '_log', '_exp', '_pow', '_floor', '_ceil',
        '_sqrt', '_fmod', '_isalpha', '_isupper', '_islower', '_isdigit',
        '_isspace', '_rand', '_srand', '_fopen', '_fclose', '_fread',
        '_fwrite', '_fseek', '_ftell',
    ]
    if any(name.startswith(p) for p in CRT_PREFIXES):
        return 'CRT'

    RULES = [
        ('Lua',           ['lua_', 'luaL_', 'LuaConfig_', 'LuaTinker_', 'lua_cpp_load']),
        ('NetHandler',    ['NetHandler_']),
        ('Network',       ['NetClient_', 'NetConn_', 'NetSocket_', 'NetPacketDeque_',
                           'NetPacketQueue_', 'Net_', 'PacketBuf_', 'AuthSession_',
                           'CIPList_', 'WSAGetLastError', 'connect', 'socket', 'recv',
                           'htons', 'inet_addr', 'WSAStartup', 'closesocket',
                           'timeGetTime', 'InternetCloseHandle', 'InternetOpenUrlA',
                           'InternetOpenA', 'EnumerateLoadedModules', 'StackWalk',
                           'GetTimestampForLoadedLibrary']),
        ('Crypto',        ['Cipher_', 'ObfuscatedString_', 'LZ4_']),
        ('AntiCheat',     ['AntiCheat_', 'CheatLog_', 'TopLevelException',
                           'BugTrap_', 'EnumLoadedModulesCallback', 'XTrap']),
        ('Sound',         ['SoundManager_', 'SoundTable_', 'DSBuffer_', 'Audio_',
                           'GSoundThread_', 'GSound_', 'Diamond_GSoundOGG_']),
        ('IME',           ['IME_']),
        ('Render',        ['Renderer_', 'Cel_', 'HUD_', 'Diamond_GPipeline_',
                           'Diamond_GRegularPipeline_', 'Diamond_GCull',
                           'Diamond_GView', 'Diamond_GScene_', 'Diamond_GTraverser_',
                           'Diamond_GDrawable', 'Diamond_GDrawItem_',
                           'Diamond_GDrawablePair_', 'Diamond_GGeometry_',
                           'Diamond_GGeode_', 'Diamond_GGroup_', 'Diamond_GNode_',
                           'Diamond_GSwitch_', 'Diamond_GRS', 'Diamond_AABB_',
                           'Diamond_GStats_', 'Diamond_GSeperatedPipeline_',
                           'Diamond_GMultiplePipeline_', 'Diamond_GVector_',
                           'Diamond_GObject_', 'Diamond_GHandle_', 'GHTex_',
                           'Texture_', 'Font_']),
        ('Terrain',       ['Terrain_', 'TileTerrain__', 'MassTerrain__', 'BUD_',
                           'BUDParser_', 'TED_', 'SOD_', 'MUD_',
                           'Fx1Terrain', 'Fx2Terrain', 'Fx3Terrain', 'Fx4Terrain',
                           'Fx5Terrain', 'Fx6Terrain', 'Fx7Terrain',
                           'Map_', 'MapOption_', 'TerrainPool_', 'ShadowManager_',
                           'SkyBox_', 'CloudDome_', 'StarDome_', 'Sun_', 'MapTime_',
                           'LightManager_', 'PointLight_', 'Fog_', 'LensFlare_',
                           'Weather_', 'Wind_', 'EditorTool_', 'Material_']),
        ('Actor',         ['Actor_', 'ActorManager_', 'ActorAnimationMixer_',
                           'ActorAnimMixer_', 'ActorMap_', 'AnimCatalog_',
                           'AnimMixer_', 'AnimTrack__', 'CoreActor_',
                           'CoreActorManager_', 'CoreSkin', 'CoreAnimation_',
                           'CoreAnimManager_', 'CoreMot', 'CoreMotManager_',
                           'CorePoseManager_', 'BindList_', 'BindPose',
                           'BindPosePool_', 'Bone__', 'Skin__', 'StaticSkin__',
                           'Pose__', 'WeightEntry__', 'MobTemplateMap_',
                           'NpcTemplateMap_', 'TextureListTxt_', 'Char_LoadAll',
                           'MotionCache_', 'Visual_', 'DiskFile_']),
        ('Effects',       ['XEffect_', 'UserXEffect_', 'JointXEffect_', 'MapXEffect_',
                           'EffectManager_', 'EffectCache_', 'EffectMap_',
                           'XEffectManager_', 'XObj', 'ParticleEffect_',
                           'ParticleEffectManager_', 'EFF_', 'GHTexManager_']),
        ('UI',            ['Diamond_GU', 'Diamond_ChatPanel_', 'Diamond_MapPanel_',
                           'Diamond_PartyPanel_', 'Diamond_NpcQuestPanel_',
                           'Diamond_CharacterBillboard', 'Diamond_CommunityPanel_',
                           'Diamond_LinkComboPanel_', 'Diamond_ItemPanel_',
                           'Diamond_TradePanel_', 'Diamond_ItemConfirmPanel_',
                           'Diamond_ActorStatePanel_', 'TutorPanel_',
                           'PartyPanel_', 'RankProgressPanel_', 'ShopWindow_',
                           'SkillIcon_']),
        ('SceneLifecycle',['Diamond_MainHandler_', 'Diamond_MainWindow_',
                           'Diamond_LoginWindow_', 'Diamond_CommonLoginWindow_',
                           'Diamond_COpeningWindow_', 'Diamond_SelectWindow_',
                           'Diamond_LoadHandler_', 'BulkAssetLoader_',
                           'GameState_', 'Engine_', 'WinMain', 'DialogFunc']),
        ('Input',         ['Diamond_InputManager_', 'Input_DInput']),
        ('ScriptData',    ['ScrLoader_', 'DOFile_', 'XdbLoader_', 'Option_',
                           'GuildCrest_', 'BillingState_', 'Screenshot_',
                           'RankProgress_', 'PartyState_', 'SkillIcon_',
                           'Skill_', 'MailInbox_']),
        ('VFS',           ['VFS_', 'Diamond_CVFSManager_', 'Diamond_DiskFile_',
                           'Diamond_File_', 'Diamond_ScriptFile_',
                           'Diamond_ConfigScriptFile_']),
        ('Threading',     ['ThreadSlot_', 'DiamondEventScheduler_',
                           'DiamondEventSubscriber_', 'FrameTickScheduler_',
                           'TickSubscriber_', 'Diamond_EventOutlet_']),
        ('Math',          ['Vec3__', 'Quat_', 'Quat__', '_logf_', 'math_',
                           'Diamond_GTransform_', 'Diamond_GRangeObject_',
                           'Diamond_GPerspectiveCamera_', 'Diamond_GCamera_',
                           'Diamond_GPositionalLight_', 'Diamond_GDirectionalLight_',
                           'Diamond_GLight_', 'Diamond_GPick_', 'Diamond_GProbe_',
                           'Diamond_GParticleBuffer_', 'GParticleBuffer__',
                           'Diamond_GFrustum_', 'Diamond_GPolytope_', 'Camera_']),
        ('Camera',        ['Diamond_EventCameraManipulator_',
                           'Diamond_FirstCameraManipulator_',
                           'Diamond_GambleCameraManipulator_',
                           'Diamond_SelectCameraManipulator_',
                           'Diamond_StaticCameraManipulator_',
                           'Diamond_ThirdCameraManipulator_']),
    ]
    for subsys, prefixes in RULES:
        if any(name.startswith(p) for p in prefixes):
            return subsys
    return 'Unknown'


# --- sweep all functions ---
subsys_addrs   = {}   # subsys -> [ea, ...]
subsys_samples = {}   # subsys -> [(ea, name), ...]

AUTO_PREFIXES = ('sub_', 'nullsub_', 'loc_', 'j_', 'unknown_', '?')

for func_ea in idautils.Functions():
    name = idc.get_func_name(func_ea)
    if not name or any(name.startswith(p) for p in AUTO_PREFIXES):
        continue
    s = classify_name(name)
    subsys_addrs.setdefault(s, []).append(func_ea)
    if len(subsys_samples.get(s, [])) < SHOW_SAMPLES:
        subsys_samples.setdefault(s, []).append((func_ea, name))

# --- format output ---
lines = []
lines.append("# doida.exe — Binary Module Map")
lines.append("")
lines.append("| Subsystem | Named fns | Addr lo | Addr hi | Representative anchors |")
lines.append("|---|---|---|---|---|")

ORDER = [
    'Math', 'Camera', 'Render', 'Sound', 'IME', 'Terrain', 'Actor',
    'Effects', 'UI', 'SceneLifecycle', 'Input', 'ScriptData',
    'Network', 'NetHandler', 'Crypto', 'AntiCheat', 'VFS',
    'Threading', 'Lua', 'CRT', 'Unknown',
]

for s in ORDER:
    addrs = subsys_addrs.get(s, [])
    if len(addrs) < MIN_NAMED:
        continue
    lo   = min(addrs)
    hi   = max(addrs)
    samp = '; '.join(n for _, n in subsys_samples.get(s, [])[:2])
    lines.append(f"| {s} | {len(addrs)} | 0x{lo:08x} | 0x{hi:08x} | {samp} |")

output = '\n'.join(lines)
print(output)

if OUTPUT_PATH:
    with open(OUTPUT_PATH, 'w', encoding='utf-8') as f:
        f.write(output)
    print(f"\n[written to {OUTPUT_PATH}]")
