-- Export every layer of an .aseprite as its own PNG (full canvas, so layers
-- keep their alignment): <out>/<prefix>_<layer>.png. The sprite tools read these.
--   Aseprite.exe -b --script-param in=<file.aseprite> --script-param out=<folder>
--                   --script-param prefix=<name> --script export_layers.lua
local p = app.params
local spr = app.open(p["in"])
local count = 0
for _, layer in ipairs(spr.layers) do
  local img = Image(spr.width, spr.height, spr.colorMode)
  local cel = layer:cel(1)
  if cel then img:drawImage(cel.image, cel.position) end
  img:saveAs(p.out .. "/" .. p.prefix .. "_" .. layer.name .. ".png")
  count = count + 1
end
print("exported " .. count .. " layers of " .. p["in"])
