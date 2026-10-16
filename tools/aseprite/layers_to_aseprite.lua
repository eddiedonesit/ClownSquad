-- Build a layered .aseprite from one PNG per layer (all the same size).
--   Aseprite.exe -b --script-param dir=<folder of PNGs> --script-param layers=a,b,c
--                   --script-param out=<file.aseprite> --script layers_to_aseprite.lua
local p = app.params
local spr
for name in p.layers:gmatch("[^,]+") do
  local img = Image{ fromFile = p.dir .. "/" .. name .. ".png" }
  if not spr then
    spr = Sprite(img.width, img.height, ColorMode.RGB)
    spr.layers[1].name = name
    spr:newCel(spr.layers[1], spr.frames[1], img, Point(0, 0))
  else
    local layer = spr:newLayer()
    layer.name = name
    spr:newCel(layer, spr.frames[1], img, Point(0, 0))
  end
end
spr:saveAs(p.out)
print("saved " .. p.out .. " with " .. #spr.layers .. " layers")
