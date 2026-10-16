-- The seltzer bottle's spray, drawn in Aseprite: what comes out of the nozzle.
-- The nozzle is at the left edge, halfway down, facing right.
--   squirt (3 frames): a short jet that frays into droplets, for a combo tap
--   burst  (3 frames): a wide fan of spray, for the big third hit
--   blast  (4 frames): a long hard stream down the lane, for the held SOAK
-- Pixel-art alpha in a few hard steps: white core, pale blue body, deeper blue edge.
--   Aseprite.exe -b --script-param out=<file.aseprite> --script-param sheet=<png>
--                   --script-param data=<json> --script draw_seltzer_spray.lua
local p = app.params
local W, H = 224, 96
local CY = H / 2
local spr = Sprite(W, H, ColorMode.RGB)
for i = 2, 10 do spr:newEmptyFrame() end

local function rgba(r, g, b, a) return app.pixelColor.rgba(r, g, b, a or 255) end
local CORE = rgba(250, 254, 255, 255)
local BODY = rgba(190, 232, 250, 235)
local EDGE = rgba(110, 190, 235, 190)
local INK = rgba(34, 40, 70, 255)

-- a deterministic little random, so the art is the same every run
local seed = 7
local function rnd()
  seed = (seed * 1103515245 + 12345) % 2147483648
  return seed / 2147483648
end

local function put(img, x, y, c)
  x, y = math.floor(x), math.floor(y)
  if x >= 0 and y >= 0 and x < W and y < H then img:drawPixel(x, y, c) end
end

local function disc(img, cx, cy, r, c)
  for y = math.floor(cy - r), math.ceil(cy + r) do
    for x = math.floor(cx - r), math.ceil(cx + r) do
      if (x + 0.5 - cx) ^ 2 + (y + 0.5 - cy) ^ 2 <= r * r then put(img, x, y, c) end
    end
  end
end

-- One droplet: a dark rim, a pale body, a bright glint.
local function drop(img, x, y, r)
  if r < 1.2 then put(img, x, y, BODY) return end
  disc(img, x, y, r + 0.8, EDGE)
  disc(img, x, y, r, BODY)
  put(img, x - r * 0.35, y - r * 0.35, CORE)
end

-- A jet from the nozzle: solid for `solid` pixels (tapering width w0 -> w1),
-- then breaking into droplets out to `reach`, spreading by `spread` (0..1).
local function jet(img, solid, reach, w0, w1, spread, drops, wobble)
  for x = 0, solid do
    local t = x / math.max(1, solid)
    local w = w0 + (w1 - w0) * t
    local yc = CY + math.sin(x * 0.09 + wobble) * 1.2 * t
    for y = math.floor(yc - w - 1), math.ceil(yc + w + 1) do
      local d = math.abs(y + 0.5 - yc)
      if d <= w * 0.45 then put(img, x, y, CORE)
      elseif d <= w then put(img, x, y, BODY)
      elseif d <= w + 1 then put(img, x, y, EDGE) end
    end
  end
  for i = 1, drops do
    local t = rnd()
    local x = solid + t * (reach - solid)
    local spreadAt = spread * (x / reach) * H * 0.45
    local y = CY + (rnd() * 2 - 1) * spreadAt
    drop(img, x, y, 1 + (1 - t) * 2.6 * rnd() + 0.6)
  end
end

local function cap(img)
  -- a few bubbles fizzing at the nozzle
  for i = 1, 5 do drop(img, 2 + rnd() * 10, CY + (rnd() * 2 - 1) * 7, 1.2 + rnd()) end
end

local layer = spr.layers[1]
layer.name = "spray"
local function frameImage(i, draw)
  local img = Image(W, H, ColorMode.RGB)
  draw(img)
  spr:newCel(layer, spr.frames[i], img, Point(0, 0))
end

-- squirt: out, at full reach, breaking up
frameImage(1, function(img) jet(img, 40, 70, 2.5, 3.5, 0.10, 14, 0.0); cap(img) end)
frameImage(2, function(img) jet(img, 70, 110, 3.0, 4.5, 0.14, 26, 1.0); cap(img) end)
frameImage(3, function(img) jet(img, 18, 118, 2.0, 2.5, 0.22, 34, 2.0) end)
-- burst: a fan
frameImage(4, function(img) jet(img, 30, 90, 4.0, 8.0, 0.35, 40, 0.5); jet(img, 0, 100, 0, 0, 0.6, 30, 0); cap(img) end)
frameImage(5, function(img) jet(img, 60, 140, 5.0, 10.0, 0.45, 60, 1.5); jet(img, 0, 140, 0, 0, 0.8, 40, 0); cap(img) end)
frameImage(6, function(img) jet(img, 10, 150, 2.0, 3.0, 0.8, 70, 2.5) end)
-- blast: a long hard stream
frameImage(7, function(img) jet(img, 90, 120, 4.0, 5.5, 0.08, 20, 0.0); cap(img) end)
frameImage(8, function(img) jet(img, 170, 205, 4.5, 6.5, 0.10, 36, 0.8); cap(img) end)
frameImage(9, function(img) jet(img, 200, 222, 4.5, 6.0, 0.12, 40, 1.6); cap(img) end)
frameImage(10, function(img) jet(img, 40, 222, 2.5, 2.5, 0.25, 60, 2.4) end)
for i = 1, 10 do spr.frames[i].duration = 0.05 end

local t1 = spr:newTag(1, 3); t1.name = "squirt"
local t2 = spr:newTag(4, 6); t2.name = "burst"
local t3 = spr:newTag(7, 10); t3.name = "blast"
spr:saveAs(p.out)
app.command.ExportSpriteSheet{
  ui = false, askOverwrite = false, type = SpriteSheetType.HORIZONTAL,
  textureFilename = p.sheet, dataFilename = p.data, dataFormat = SpriteSheetDataFormat.JSON_ARRAY,
  listTags = true, trim = false,
}
print("seltzer spray: 10 frames -> " .. p.out)
