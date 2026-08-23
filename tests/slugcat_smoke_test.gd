extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed_scene := load("res://scenes/main.tscn") as PackedScene
	assert(packed_scene != null, "Main scene must load")
	var main := packed_scene.instantiate()
	root.add_child(main)
	await process_frame

	assert(main.body_positions.size() == 2, "Rig must contain two body points")
	assert(main.body_velocities.size() == 2, "Each body point must have velocity")
	assert(main.tail_positions.size() == main.TAIL_SEGMENTS, "Tail point count must match configuration")
	assert(main.tail_previous.size() == main.TAIL_SEGMENTS, "Tail history must match tail point count")
	assert(
		is_equal_approx(main.body_positions[0].distance_to(main.body_positions[1]), main.BODY_DISTANCE),
		"Initial body constraint must have the configured length",
	)

	print("Slugcat prototype smoke test passed")
	quit(0)
