# _diag_scene.gd — TEMPORARY scene-tree diagnostic autoload for the Martial Heroes client.
#
# Purpose: catch the silent gray-screen bug. In Godot 4 a node's script must be a PROPERTY LINE
# under the [node] header (script = ExtResource("id")), NOT a header attribute. Put it in the
# header and it is silently ignored — the node loads with NO script, _Ready never runs, and you
# get a gray screen with zero errors. This autoload walks the live scene tree and prints, per node,
# whether it actually carries a script — so you can SEE which nodes are missing one.
#
# Lifecycle (managed by the godot-scene-author skill):
#   1. Copied to res://Dev/_diag_scene.gd.
#   2. Registered in project.godot as:   SceneDiag="*res://Dev/_diag_scene.gd"
#   3. Run headless (godot-run-headless) for ~60 frames; lines go to stdout.
#   4. *** REMOVED from project.godot AND deleted afterwards. *** It quits the tree on every run.
#
# Output (one line per node), greppable on the "SCENE-DIAG:" prefix:
#   SCENE-DIAG: <node path>  type=<ClassName>  script=<res://path.cs | NONE>
# Any node you EXPECTED to be scripted that prints script=NONE is the gray-screen bug.

extends Node


func _ready() -> void:
	# Let the main scene instantiate first, then dump after a couple of frames.
	_dump()


func _dump() -> void:
	# Wait a couple of frames so the current scene is fully attached.
	await get_tree().process_frame
	await get_tree().process_frame

	var root := get_tree().current_scene
	if root == null:
		# Fall back to the tree root's children (current_scene can be null very early).
		print("SCENE-DIAG: current_scene is null — dumping tree root children instead.")
		root = get_tree().root

	print("SCENE-DIAG: ==== begin scene dump (root=%s) ====" % root.name)
	_walk(root, "")
	print("SCENE-DIAG: ==== end scene dump ====")

	# This is a one-shot diagnostic — quit so a headless run terminates promptly.
	get_tree().quit(0)


func _walk(node: Node, prefix: String) -> void:
	var script_res := node.get_script()
	var script_desc := "NONE"
	if script_res != null and script_res.resource_path != "":
		script_desc = script_res.resource_path

	var path := prefix + "/" + node.name if prefix != "" else node.name
	print("SCENE-DIAG: %-48s type=%-20s script=%s" % [path, node.get_class(), script_desc])

	for child in node.get_children():
		_walk(child, path)
