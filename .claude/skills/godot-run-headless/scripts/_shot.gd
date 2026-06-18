# _shot.gd — TEMPORARY screenshot autoload for the Martial Heroes Godot client.
#
# Purpose: capture one PNG of the running, WINDOWED scene after a short warmup, then quit.
# This is the most reliable in-engine probe — a GDScript autoload loads before any scene
# script and needs no C# rebuild.
#
# Lifecycle (managed by the godot-screenshot skill):
#   1. Copied to res://Dev/_shot.gd.
#   2. Registered in project.godot as:   ShotCapture="*res://Dev/_shot.gd"
#   3. Run windowed via screenshot.ps1 (which sets MH_SHOT_PNG / MH_SHOT_FRAMES).
#   4. *** REMOVED from project.godot AND deleted again afterwards. ***
#      It calls quit() on every run; if left registered, every editor/game launch self-quits.
#
# Config via environment variables (with safe defaults):
#   MH_SHOT_PNG     absolute output path for the PNG  (default: user://_shot.png)
#   MH_SHOT_FRAMES  frames to wait before capturing   (default: 180)
#
# Waiting N frames (not a wall-clock timer) lets the async terrain / NPC streaming populate the
# scene before the grab. Bump MH_SHOT_FRAMES if geometry streams in late and the shot is empty.

extends Node


func _ready() -> void:
	# Run the capture coroutine without blocking _ready.
	_capture()


func _capture() -> void:
	var frames := 180
	var env_frames := OS.get_environment("MH_SHOT_FRAMES")
	if env_frames != "" and env_frames.is_valid_int():
		frames = maxi(1, env_frames.to_int())

	var out_path := OS.get_environment("MH_SHOT_PNG")
	if out_path == "":
		out_path = "user://_shot.png"

	# Wait the requested number of rendered frames so streamed content is present.
	for _i in range(frames):
		await get_tree().process_frame

	# One extra frame after any late work, then grab the viewport's rendered texture.
	await RenderingServer.frame_post_draw

	var img: Image = get_viewport().get_texture().get_image()
	if img == null:
		push_error("[_shot] viewport image was null — is this a WINDOWED (non-headless) run?")
		get_tree().quit(1)
		return

	var err := img.save_png(out_path)
	if err != OK:
		push_error("[_shot] save_png('%s') failed: %d" % [out_path, err])
		get_tree().quit(1)
		return

	print("[_shot] saved screenshot: %s (%dx%d)" % [out_path, img.get_width(), img.get_height()])
	get_tree().quit(0)
