-- The High Striker, drawn in Aseprite: the carnival strength tester that pops
-- up when Big Bertha pounds the ground. A striped pole on a pedestal with a
-- scale running green to red, a brass bell on top, and the puck that rides
-- up the pole. Three frames, one size: the tower, the tower with its bell
-- ringing, and the puck alone (centred, so the game can slide it up the pole).
--   Aseprite.exe -b --script-param out=<file.aseprite> --script-param sheet=<png>
--                   --script-param data=<json> --script draw_high_striker.lua
local p = app.params
local W, H = 56, 208
local spr = Sprite(W, H, ColorMode.RGB)
spr:newEmptyFrame()
spr:newEmptyFrame()

local function rgba(r, g, b, a) return app.pixelColor.rgba(r, g, b, a or 255) end
local INK = rgba(34, 22, 40)
local function put(img, x, y, c) if x >= 0 and y >= 0 and x < W and y < H then img:drawPixel(x, y, c) end end
local function rect(img, x0, y0, x1, y1, c) for y = y0, y1 do for x = x0, x1 do put(img, x, y, c) end end end
local function disc(img, cx, cy, rx, ry, c)
  for y = math.floor(cy - ry), math.ceil(cy + ry) do
    for x = math.floor(cx - rx), math.ceil(cx + rx) do
      local dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
      if dx * dx + dy * dy <= 1 then put(img, x, y, c) end
    end
  end
end
-- one dark pixel round every opaque pixel that touches empty space
local function outline(img)
  local edge = {}
  for y = 0, H - 1 do
    for x = 0, W - 1 do
      if app.pixelColor.rgbaA(img:getPixel(x, y)) == 0 then
        for _, d in ipairs({ { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } }) do
          local nx, ny = x + d[1], y + d[2]
          if nx >= 0 and ny >= 0 and nx < W and ny < H and app.pixelColor.rgbaA(img:getPixel(nx, ny)) > 0 then
            edge[#edge + 1] = { x, y }; break
          end
        end
      end
    end
  end
  for _, e in ipairs(edge) do img:drawPixel(e[1], e[2], INK) end
end

local CX = W // 2
local TOP, BASE = 30, 186          -- the pole's top and bottom

local function tower(img, ringing)
  -- pedestal: a red box with a gold band and a star
  rect(img, CX - 20, BASE, CX + 19, H - 4, rgba(196, 40, 58))
  rect(img, CX - 20, BASE, CX + 19, BASE + 2, rgba(252, 204, 72))
  rect(img, CX - 20, H - 7, CX + 19, H - 5, rgba(150, 26, 46))
  disc(img, CX, BASE + 10, 5, 5, rgba(252, 236, 140))
  -- the strike pad
  rect(img, CX - 12, BASE - 4, CX + 11, BASE - 1, rgba(120, 110, 130))
  rect(img, CX - 12, BASE - 4, CX + 11, BASE - 4, rgba(200, 196, 214))
  -- the pole: cream with a scale of coloured bands, green at the bottom to red at the top
  rect(img, CX - 4, TOP, CX + 3, BASE - 5, rgba(250, 240, 214))
  rect(img, CX + 2, TOP, CX + 3, BASE - 5, rgba(214, 196, 166))     -- its shaded side
  local stops = { { 90, 200, 90 }, { 180, 220, 70 }, { 250, 210, 60 }, { 250, 150, 50 }, { 230, 60, 60 } }
  local span = BASE - 5 - TOP
  for i = 0, 9 do
    local y = BASE - 8 - math.floor(i * span / 10)
    local s = stops[math.min(5, 1 + i // 2)]
    rect(img, CX - 7, y - 1, CX + 6, y, rgba(s[1], s[2], s[3]))
  end
  -- the bell on its bracket
  rect(img, CX - 2, TOP - 6, CX + 1, TOP, rgba(110, 90, 70))
  local bell = ringing and rgba(255, 246, 170) or rgba(252, 204, 72)
  disc(img, CX, TOP - 14, 11, 9, bell)
  rect(img, CX - 12, TOP - 10, CX + 11, TOP - 7, bell)
  disc(img, CX - 4, TOP - 17, 3, 2, rgba(255, 250, 220))              -- its shine
  disc(img, CX, TOP - 6, 2.5, 2, rgba(160, 110, 40))                 -- the clapper
  if ringing then
    -- ring lines either side
    for _, s in ipairs({ -1, 1 }) do
      for i = 0, 2 do
        local x = CX + s * (16 + i * 4)
        rect(img, x, TOP - 22 + i * 3, x, TOP - 16 + i * 3, rgba(255, 250, 200))
      end
    end
  end
  outline(img)
end

local function puck(img)
  local y = H // 2
  rect(img, CX - 9, y - 4, CX + 8, y + 3, rgba(220, 50, 64))
  rect(img, CX - 9, y - 4, CX + 8, y - 3, rgba(255, 130, 130))
  rect(img, CX - 9, y + 2, CX + 8, y + 3, rgba(150, 26, 46))
  outline(img)
end

local layer = spr.layers[1]
layer.name = "striker"
local function frameImage(i, draw)
  local img = Image(W, H, ColorMode.RGB)
  draw(img)
  spr:newCel(layer, spr.frames[i], img, Point(0, 0))
end
frameImage(1, function(img) tower(img, false) end)
frameImage(2, function(img) tower(img, true) end)
frameImage(3, puck)
for i = 1, 3 do spr.frames[i].duration = 0.1 end
local t = spr:newTag(1, 1); t.name = "tower"
local r = spr:newTag(2, 2); r.name = "ring"
local k = spr:newTag(3, 3); k.name = "puck"
spr:saveAs(p.out)
app.command.ExportSpriteSheet{
  ui = false, askOverwrite = false, type = SpriteSheetType.HORIZONTAL,
  textureFilename = p.sheet, dataFilename = p.data, dataFormat = SpriteSheetDataFormat.JSON_ARRAY,
  listTags = true, trim = false,
}
print("high striker: 3 frames -> " .. p.out)
