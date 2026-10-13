extends Node3D
# TEMPORARY diagnostic: dumps runtime transforms, captures the framebuffer, quits.

func _ready() -> void:
	var cam: Camera3D = $Camera3D
	print("=== RUNTIME DIAGNOSTICS ===")
	print("viewport size: ", get_viewport().get_visible_rect().size)
	print("camera global_position: ", cam.global_position)
	print("camera rotation_degrees: ", cam.rotation_degrees)
	print("camera fov: ", cam.fov, "  near: ", cam.near, "  far: ", cam.far)
	print("camera forward (-Z): ", -cam.global_transform.basis.z)
	print("camera current: ", cam.current)
	var env := $WorldEnvironment as WorldEnvironment
	print("world env camera_attributes: ", env.camera_attributes)
	print("camera attributes: ", cam.attributes)
	print("--- node distances from camera ---")
	for n in get_children():
		if n is Node3D and n != cam:
			var d: float = cam.global_position.distance_to((n as Node3D).global_position)
			print("  %-18s pos=%s dist=%.2f" % [n.name, str((n as Node3D).global_position), d])
	print("--- is anything in front of the camera? ---")
	for n in get_children():
		if n is Node3D and n != cam:
			var p: Vector2 = cam.unproject_position((n as Node3D).global_position)
			var behind: bool = cam.is_position_behind((n as Node3D).global_position)
			print("  %-18s screen=%s behind=%s" % [n.name, str(p), str(behind)])
	await RenderingServer.frame_post_draw
	await get_tree().create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	var img: Image = get_viewport().get_texture().get_image()
	img.save_png("C:/Claude/ClownSquad/debug/capture.png")
	print("=== captured to debug/capture.png ===")
	get_tree().quit()
