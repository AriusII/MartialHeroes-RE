# Dev/_capture.gd — TEMPORARY screenshot helper.
# Registered as ShotCapture autoload only during capture sessions.
# NEVER committed as a permanent autoload.
extends Node

func _ready() -> void:
	_run()

func _run() -> void:
	# Wait N frames for the screen to fully render.
	var wait := int(OS.get_environment("MH_SHOT_WAIT"))
	if wait <= 0:
		wait = 60
	for _i in range(wait):
		await get_tree().process_frame

	# Optionally simulate a keypress before screenshot (e.g. to open a window).
	var sim_key := OS.get_environment("MH_SHOT_KEY")
	if sim_key != "":
		var kc := sim_key.to_int()
		var ev := InputEventKey.new()
		ev.keycode = kc
		ev.pressed = true
		Input.parse_input_event(ev)
		# Wait a few frames for the window to open.
		for _i in range(15):
			await get_tree().process_frame

	await RenderingServer.frame_post_draw

	var img: Image = get_viewport().get_texture().get_image()
	if img == null:
		push_error("[_capture] viewport image was null")
		get_tree().quit(1)
		return

	var out_path := OS.get_environment("MH_SHOT_PNG")
	if out_path == "":
		out_path = "user://_capture.png"

	var err := img.save_png(out_path)
	if err != OK:
		push_error("[_capture] save_png('%s') failed: %d" % [out_path, err])
		get_tree().quit(1)
		return

	print("[_capture] saved: %s (%dx%d)" % [out_path, img.get_width(), img.get_height()])
	get_tree().quit(0)
