-- Each clown's own face details, added as layers to art/aseprite/clown_head.aseprite
-- (same canvas and anchor as the shared head: centre at 56,24, facing right;
-- the eye sits at about x 62-70, y 14-24). The generator paints a clown's layer
-- over the shared hand-drawn face (gen_sprite_hd.hand_face):
--   lashes_poppy   three curled lashes over the eye
--   brow_bingo     a thick, cocked eyebrow (BINGO shouts)
--   freckles_dot   freckles across the cheek and nose, and a second glint in the eye
-- Re-running replaces these layers; the shared ones are left alone.
--   Aseprite.exe -b --script-param file=<clown_head.aseprite> --script draw_clown_faces.lua
local p = app.params
local spr = app.open(p.file)
local function rgba(r, g, b) return app.pixelColor.rgba(r, g, b, 255) end
local INK = rgba(34, 22, 40)
local BROW = rgba(52, 30, 40)
local FRECKLE = rgba(170, 82, 64)
local GLINT = rgba(255, 255, 255)

local function layer(name, pixels)
  for i = #spr.layers, 1, -1 do
    if spr.layers[i].name == name then spr:deleteLayer(spr.layers[i]) end
  end
  local l = spr:newLayer()
  l.name = name
  local img = Image(spr.width, spr.height, ColorMode.RGB)
  for _, px in ipairs(pixels) do img:drawPixel(px[1], px[2], px[3]) end
  spr:newCel(l, spr.frames[1], img, Point(0, 0))
end

-- Poppy: lashes flicking out from the front corner of the eye (the hat brim covers its top)
layer("lashes_poppy", {
  { 70, 14, INK }, { 71, 13, INK }, { 72, 12, INK },
  { 71, 16, INK }, { 72, 15, INK }, { 73, 15, INK },
  { 71, 18, INK }, { 72, 18, INK },
})

-- Bingo: a heavy brow just under the brim, low at the nose end and cocked up at the back
local brow = {}
for x = 61, 71 do
  local y = (x < 66) and 13 or 12
  brow[#brow + 1] = { x, y, BROW }
  brow[#brow + 1] = { x, y + 1, BROW }
end
layer("brow_bingo", brow)

-- Dot: freckles across the cheek and the bridge of the nose, and a second glint
layer("freckles_dot", {
  { 60, 27, FRECKLE }, { 61, 27, FRECKLE }, { 63, 26, FRECKLE }, { 64, 26, FRECKLE },
  { 66, 27, FRECKLE }, { 61, 30, FRECKLE }, { 62, 30, FRECKLE }, { 64, 29, FRECKLE },
  { 58, 29, FRECKLE }, { 59, 29, FRECKLE }, { 68, 26, FRECKLE },
  { 68, 21, GLINT },
})

spr:saveAs(p.file)
print("clown faces: lashes_poppy, brow_bingo, freckles_dot -> " .. p.file)
