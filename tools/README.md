# tools/

`DebugCapture.gd` — attach to a 3D scene root to dump runtime camera/node
transforms, screen-projected positions and distances, then save a framebuffer
capture to `debug/capture.png` and quit.

Use it when a scene renders wrongly and you need ground truth rather than
guesswork. It caught a transposed camera basis that aimed the camera at the sky.

To use: set `script = ExtResource(...)` pointing here on the scene root, run the
project, then detach it again. `debug/` is gitignored.
