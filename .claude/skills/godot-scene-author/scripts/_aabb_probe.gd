# _aabb_probe.gd — TEMPORARY global-AABB probe autoload for the Martial Heroes client.
#
# Purpose: catch coordinate-convention bugs by reporting WHERE a placed node actually ended up.
# Prints the node's GLOBAL AABB (centre/min/max/size in world space) so it can be compared to the
# expected world position computed from the asset's cell/legacy coordinates (expected_pos.py).
#
# Why global (not local): a local AABB ignores the placement transform — the very thing being
# validated. We accumulate each MeshInstance3D's get_aabb() transformed by its GLOBAL transform.
#
# Lifecycle (managed by the godot-coordinate-check skill):
#   1. Copied to res://Dev/_aabb_probe.gd.
#   2. Registered in project.godot as:   AabbProbe="*res://Dev/_aabb_probe.gd"
#   3. Set env MH_PROBE_NODE to a node path (/root/World/BudSceneNode) or a bare name to search for.
#   4. Run headless (godot-run-headless, ~150 frames). Read the AABB-PROBE line.
#   5. *** REMOVED from project.godot AND deleted afterwards. *** It quits the tree on every run.
#
# Output (greppable on "AABB-PROBE:"):
#   AABB-PROBE: <node path>  center=(x,y,z)  min=(x,y,z)  max=(x,y,z)  size=(x,y,z)

extends Node


func _ready() -> void:
	_probe()


func _probe() -> void:
	# Wait a few frames so async placement / terrain streaming settles before measuring.
	for _i in range(120):
		await get_tree().process_frame

	var target := OS.get_environment("MH_PROBE_NODE")
	if target == "":
		push_error("[aabb] MH_PROBE_NODE not set.")
		get_tree().quit(1)
		return

	var node := _resolve(target)
	if node == null:
		push_error("[aabb] node not found: %s" % target)
		get_tree().quit(1)
		return

	var aabb := _global_aabb(node)
	if aabb == null:
		push_error("[aabb] no MeshInstance3D geometry under '%s' to measure." % target)
		get_tree().quit(1)
		return

	var box: AABB = aabb
	var c := box.get_center()
	var mn := box.position
	var mx := box.position + box.size
	print("AABB-PROBE: %s  center=(%.2f,%.2f,%.2f)  min=(%.2f,%.2f,%.2f)  max=(%.2f,%.2f,%.2f)  size=(%.2f,%.2f,%.2f)"
		% [str(node.get_path()),
		   c.x, c.y, c.z, mn.x, mn.y, mn.z, mx.x, mx.y, mx.z, box.size.x, box.size.y, box.size.z])

	get_tree().quit(0)


func _resolve(target: String) -> Node:
	# Absolute / relative path first.
	if target.begins_with("/"):
		var n := get_node_or_null(target)
		if n != null:
			return n
	# Otherwise search the whole tree by node name.
	var root := get_tree().root
	return _find_by_name(root, target)


func _find_by_name(node: Node, name: String) -> Node:
	if node.name == name:
		return node
	for child in node.get_children():
		var found := _find_by_name(child, name)
		if found != null:
			return found
	return null


func _global_aabb(node: Node):
	# Accumulate global-space AABBs of every MeshInstance3D in the subtree.
	var acc = null
	for mi in _iter_mesh_instances(node):
		if mi.mesh == null:
			continue
		var world: AABB = mi.global_transform * mi.get_aabb()
		acc = world if acc == null else (acc as AABB).merge(world)
	return acc


func _iter_mesh_instances(node: Node) -> Array:
	var out: Array = []
	if node is MeshInstance3D:
		out.append(node)
	for child in node.get_children():
		out.append_array(_iter_mesh_instances(child))
	return out
