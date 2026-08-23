extends Node2D

const BODY_DISTANCE := 34.0
const BODY_RADIUS := 17.0
const TAIL_SEGMENTS := 8
const TAIL_SEGMENT_LENGTH := 13.0
const CONSTRAINT_ITERATIONS := 8
const GRAVITY := Vector2(0.0, 1050.0)
const AIR_DAMPING := 0.992
const FLOOR_MARGIN := 34.0
const THROW_MULTIPLIER := 1.08

const BODY_COLOR := Color("f4f7fb")
const SHADE_COLOR := Color("dce9f3")
const OUTLINE_COLOR := Color("1c2733")
const EYE_COLOR := Color("17202a")

var body_positions: Array[Vector2] = []
var body_velocities: Array[Vector2] = []
var tail_positions: Array[Vector2] = []
var tail_previous: Array[Vector2] = []

var grabbed_body_index := -1
var last_mouse_position := Vector2.ZERO
var sampled_mouse_velocity := Vector2.ZERO


func _ready() -> void:
	get_viewport().transparent_bg = true
	_reset_rig()
	_update_mouse_passthrough()
	queue_redraw()


func _reset_rig() -> void:
	var viewport_size := get_viewport_rect().size
	var center := Vector2(viewport_size.x * 0.5, viewport_size.y * 0.42)
	body_positions = [center + Vector2(0.0, -BODY_DISTANCE), center]
	body_velocities = [Vector2.ZERO, Vector2.ZERO]
	tail_positions.clear()
	tail_previous.clear()
	for index in TAIL_SEGMENTS:
		var point := center + Vector2(index * TAIL_SEGMENT_LENGTH, 5.0 + index * 2.0)
		tail_positions.append(point)
		tail_previous.append(point)
	last_mouse_position = get_local_mouse_position()


func _physics_process(delta: float) -> void:
	_update_mouse_sample(delta)
	_integrate_body(delta)
	_integrate_tail(delta)

	for _iteration in CONSTRAINT_ITERATIONS:
		_solve_body_constraint()
		_solve_tail_constraints()

	_resolve_body_bounds()
	_resolve_tail_floor()
	tail_positions[0] = body_positions[1]

	if grabbed_body_index < 0:
		_update_mouse_passthrough()
	queue_redraw()


func _update_mouse_sample(delta: float) -> void:
	var mouse_position := get_local_mouse_position()
	if delta > 0.0:
		var instantaneous := (mouse_position - last_mouse_position) / delta
		sampled_mouse_velocity = sampled_mouse_velocity.lerp(instantaneous, 0.35)
	last_mouse_position = mouse_position


func _integrate_body(delta: float) -> void:
	for index in body_positions.size():
		if index == grabbed_body_index:
			body_positions[index] = body_positions[index].lerp(get_local_mouse_position(), minf(1.0, delta * 30.0))
			body_velocities[index] = sampled_mouse_velocity
			continue

		body_velocities[index] += GRAVITY * delta
		body_velocities[index] *= pow(AIR_DAMPING, delta * 60.0)
		body_positions[index] += body_velocities[index] * delta


func _integrate_tail(delta: float) -> void:
	tail_positions[0] = body_positions[1]
	tail_previous[0] = tail_positions[0]
	for index in range(1, tail_positions.size()):
		var current := tail_positions[index]
		var velocity := (current - tail_previous[index]) * 0.985
		tail_previous[index] = current
		tail_positions[index] = current + velocity + GRAVITY * delta * delta * 0.55


func _solve_body_constraint() -> void:
	var delta := body_positions[1] - body_positions[0]
	var distance := delta.length()
	if distance < 0.001:
		return

	var correction := delta * ((distance - BODY_DISTANCE) / distance)
	if grabbed_body_index == 0:
		body_positions[1] -= correction
	elif grabbed_body_index == 1:
		body_positions[0] += correction
	else:
		body_positions[0] += correction * 0.5
		body_positions[1] -= correction * 0.5


func _solve_tail_constraints() -> void:
	tail_positions[0] = body_positions[1]
	for index in range(1, tail_positions.size()):
		var delta := tail_positions[index] - tail_positions[index - 1]
		var distance := delta.length()
		if distance < 0.001:
			continue

		var correction := delta * ((distance - TAIL_SEGMENT_LENGTH) / distance)
		if index == 1:
			tail_positions[index] -= correction
		else:
			tail_positions[index - 1] += correction * 0.5
			tail_positions[index] -= correction * 0.5


func _resolve_body_bounds() -> void:
	var viewport_size := get_viewport_rect().size
	var floor_y := viewport_size.y - FLOOR_MARGIN
	for index in body_positions.size():
		var position := body_positions[index]
		if position.y > floor_y:
			position.y = floor_y
			if body_velocities[index].y > 0.0:
				body_velocities[index].y *= -0.22
			body_velocities[index].x *= 0.72
		if position.x < BODY_RADIUS:
			position.x = BODY_RADIUS
			body_velocities[index].x = absf(body_velocities[index].x) * 0.35
		elif position.x > viewport_size.x - BODY_RADIUS:
			position.x = viewport_size.x - BODY_RADIUS
			body_velocities[index].x = -absf(body_velocities[index].x) * 0.35
		body_positions[index] = position


func _resolve_tail_floor() -> void:
	var floor_y := get_viewport_rect().size.y - FLOOR_MARGIN + 8.0
	for index in range(1, tail_positions.size()):
		if tail_positions[index].y > floor_y:
			tail_positions[index].y = floor_y


func _input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	if event.button_index != MOUSE_BUTTON_LEFT:
		return

	if event.pressed:
		grabbed_body_index = _pick_body_point(event.position)
		if grabbed_body_index >= 0:
			last_mouse_position = event.position
			sampled_mouse_velocity = Vector2.ZERO
			# Capture the full window while dragging so a fast cursor cannot leave
			# the small interactive polygon before the release event arrives.
			DisplayServer.window_set_mouse_passthrough(PackedVector2Array())
	else:
		if grabbed_body_index >= 0:
			body_velocities[grabbed_body_index] = sampled_mouse_velocity * THROW_MULTIPLIER
		grabbed_body_index = -1
		_update_mouse_passthrough()


func _pick_body_point(mouse_position: Vector2) -> int:
	var best_index := -1
	var best_distance := BODY_RADIUS * 2.2
	for index in body_positions.size():
		var distance := mouse_position.distance_to(body_positions[index])
		if distance < best_distance:
			best_distance = distance
			best_index = index
	return best_index


func _update_mouse_passthrough() -> void:
	if body_positions.is_empty() or tail_positions.is_empty():
		return
	var minimum := body_positions[0]
	var maximum := body_positions[0]
	for point in body_positions + tail_positions:
		minimum = minimum.min(point)
		maximum = maximum.max(point)
	# Include the procedural head and ears, which extend beyond the chest point.
	var margin := Vector2(40.0, 56.0)
	minimum -= margin
	maximum += margin
	var region := PackedVector2Array([
		Vector2(minimum.x, minimum.y),
		Vector2(maximum.x, minimum.y),
		Vector2(maximum.x, maximum.y),
		Vector2(minimum.x, maximum.y),
	])
	DisplayServer.window_set_mouse_passthrough(region)


func _draw() -> void:
	if body_positions.size() < 2 or tail_positions.is_empty():
		return

	_draw_tail()
	_draw_limbs()
	_draw_body()
	_draw_head()


func _draw_tail() -> void:
	for index in range(tail_positions.size() - 1):
		var width := lerpf(18.0, 3.0, float(index) / float(tail_positions.size() - 1))
		draw_line(tail_positions[index], tail_positions[index + 1], OUTLINE_COLOR, width + 5.0, true)
		draw_line(tail_positions[index], tail_positions[index + 1], SHADE_COLOR, width, true)


func _draw_limbs() -> void:
	var chest := body_positions[0]
	var hips := body_positions[1]
	var floor_y := get_viewport_rect().size.y - FLOOR_MARGIN
	var body_right := (hips - chest).normalized().orthogonal()
	if body_right.length_squared() < 0.5:
		body_right = Vector2.RIGHT

	var left_hand := chest - body_right * 19.0 + Vector2(-3.0, 23.0)
	var right_hand := chest + body_right * 19.0 + Vector2(3.0, 23.0)
	var left_foot := hips - body_right * 14.0 + Vector2(-5.0, 28.0)
	var right_foot := hips + body_right * 14.0 + Vector2(5.0, 28.0)
	left_foot.y = minf(left_foot.y, floor_y)
	right_foot.y = minf(right_foot.y, floor_y)

	for limb in [[chest, left_hand], [chest, right_hand], [hips, left_foot], [hips, right_foot]]:
		draw_line(limb[0], limb[1], OUTLINE_COLOR, 9.0, true)
		draw_line(limb[0], limb[1], BODY_COLOR, 5.0, true)
		draw_circle(limb[1], 3.5, BODY_COLOR)


func _draw_body() -> void:
	var chest := body_positions[0]
	var hips := body_positions[1]
	draw_line(chest, hips, OUTLINE_COLOR, BODY_RADIUS * 2.0 + 7.0, true)
	draw_circle(chest, BODY_RADIUS + 3.5, OUTLINE_COLOR)
	draw_circle(hips, BODY_RADIUS + 4.0, OUTLINE_COLOR)
	draw_line(chest, hips, BODY_COLOR, BODY_RADIUS * 2.0, true)
	draw_circle(chest, BODY_RADIUS, BODY_COLOR)
	draw_circle(hips, BODY_RADIUS + 0.5, SHADE_COLOR)


func _draw_head() -> void:
	var chest := body_positions[0]
	var hips := body_positions[1]
	var up := (chest - hips).normalized()
	if up.length_squared() < 0.5:
		up = Vector2.UP
	var right := up.orthogonal()
	var head := chest + up * 22.0
	var ear_left := PackedVector2Array([
		head - right * 11.0 + up * 6.0,
		head - right * 17.0 + up * 22.0,
		head - right * 2.0 + up * 14.0,
	])
	var ear_right := PackedVector2Array([
		head + right * 11.0 + up * 6.0,
		head + right * 17.0 + up * 22.0,
		head + right * 2.0 + up * 14.0,
	])
	draw_colored_polygon(ear_left, OUTLINE_COLOR)
	draw_colored_polygon(ear_right, OUTLINE_COLOR)
	draw_circle(head, 20.0, OUTLINE_COLOR)
	draw_colored_polygon(ear_left, BODY_COLOR)
	draw_colored_polygon(ear_right, BODY_COLOR)
	draw_circle(head, 16.5, BODY_COLOR)

	var look_direction := (get_local_mouse_position() - head).normalized()
	var eye_center := head + look_direction * 2.0 + up * 1.0
	draw_circle(eye_center - right * 5.5, 1.8, EYE_COLOR)
	draw_circle(eye_center + right * 5.5, 1.8, EYE_COLOR)
