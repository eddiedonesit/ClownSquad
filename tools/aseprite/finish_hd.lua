-- Version A: a pixel-art finishing pass over a generated HD sprite, in Aseprite.
--   1. Snap every colour onto hand-picked ramps (light to dark, hue-shifted):
--      kills the muddy in-between tones the generator's blending leaves.
--   2. Selective outline: the silhouette's outline takes the darkest tone of
--      the colour it borders, except along the bottom and right (away from the
--      light), which keep the near-black for weight.
--   3. Rim light: edges facing the light (up and left) step one tone lighter.
--   4. Clean up lone pixels that match none of their four neighbours.
-- The result is a new layer "finished" over the original, saved as .aseprite,
-- and exported as a sheet + JSON for tools/aseprite_import.py.
--   Aseprite.exe -b --script-param in=X.aseprite --script-param out=Y.aseprite
--                --script-param sheet=Y.png --script-param data=Y.json --script finish_hd.lua

local p = app.params
local spr = app.open(p["in"])

-- Ramps, light to dark. Shadows lean warm-red (skin, nose) or cool-violet (the rest).
local ramps = {
  skin   = { "ffefe0", "fadac0", "eab49a", "c98b7f", "8f5a62" },
  hair   = { "ffd2dc", "fa8cae", "e2608e", "b23e74", "7a2654" },
  nose   = { "ff9c8c", "e8343c", "b01f3a", "6b1026" },
  yellow = { "fff2a8", "fad25a", "dda040", "a66a2c", "6e4224" },
  teal   = { "c4faf6", "6fe2e7", "50bed2", "337d93", "1f4d61" },
  purple = { "d898ea", "b862d1", "9646be", "6c2e8e", "45205e" },
  white  = { "ffffff", "ececf6", "c6c4d8", "9290a8", "5e5a72" },
}
local INK = "1a1222"

local function rgb(hex)
  return tonumber(hex:sub(1, 2), 16), tonumber(hex:sub(3, 4), 16), tonumber(hex:sub(5, 6), 16)
end

-- Every ramp entry, with which ramp and step it is.
local entries = {}
for name, list in pairs(ramps) do
  for i, hex in ipairs(list) do
    local r, g, b = rgb(hex)
    entries[#entries + 1] = { r = r, g = g, b = b, ramp = name, step = i }
  end
end
local ir, ig, ib = rgb(INK)
entries[#entries + 1] = { r = ir, g = ig, b = ib, ramp = "ink", step = 1 }

local function nearest(r, g, b)
  local best, bd = nil, math.huge
  for _, e in ipairs(entries) do
    -- weighted, roughly perceptual
    local dr, dg, db = r - e.r, g - e.g, b - e.b
    local d = 2 * dr * dr + 4 * dg * dg + 3 * db * db
    if d < bd then best, bd = e, d end
  end
  return best
end

local function px(e) return app.pixelColor.rgba(e.r, e.g, e.b, 255) end
local function entry(ramp, step)
  local list = ramps[ramp]
  step = math.max(1, math.min(#list, step))
  local r, g, b = rgb(list[step])
  return { r = r, g = g, b = b, ramp = ramp, step = step }
end

local base = spr.layers[1]
local out = spr:newLayer()
out.name = "finished"
base.isVisible = false

for _, frame in ipairs(spr.frames) do
  local cel = base:cel(frame.frameNumber)
  if cel then
    local src = cel.image
    local w, h = src.width, src.height
    -- 1. snap to ramps
    local grid = {}
    for y = 0, h - 1 do
      grid[y] = {}
      for x = 0, w - 1 do
        local c = src:getPixel(x, y)
        if app.pixelColor.rgbaA(c) > 0 then
          grid[y][x] = nearest(app.pixelColor.rgbaR(c), app.pixelColor.rgbaG(c), app.pixelColor.rgbaB(c))
        end
      end
    end
    local function at(x, y) return grid[y] and grid[y][x] end

    -- 4. lone pixels take the most common neighbouring colour
    for y = 0, h - 1 do
      for x = 0, w - 1 do
        local e = at(x, y)
        if e and e.ramp ~= "ink" then
          local n = { at(x - 1, y), at(x + 1, y), at(x, y - 1), at(x, y + 1) }
          local same, counts, top, topn = false, {}, nil, 0
          for _, q in ipairs(n) do
            if q then
              if q.ramp == e.ramp and q.step == e.step then same = true end
              local k = q.ramp .. q.step
              counts[k] = (counts[k] or 0) + 1
              if counts[k] > topn and q.ramp ~= "ink" then top, topn = q, counts[k] end
            end
          end
          if not same and top and topn >= 3 then grid[y][x] = top end
        end
      end
    end

    -- 2 and 3, on a copy so changes do not feed each other
    local final = {}
    for y = 0, h - 1 do
      final[y] = {}
      for x = 0, w - 1 do
        local e = at(x, y)
        if e then
          if e.ramp == "ink" then
            -- an outline pixel on the silhouette: which side is empty?
            local emptyRight = not at(x + 1, y)
            local emptyBelow = not at(x, y + 1)
            local inner
            for _, q in ipairs({ at(x + 1, y), at(x - 1, y), at(x, y + 1), at(x, y - 1) }) do
              if q and q.ramp ~= "ink" then inner = q; break end
            end
            local onEdge = not at(x - 1, y) or not at(x, y - 1) or emptyRight or emptyBelow
            if inner and onEdge and not emptyRight and not emptyBelow then
              e = entry(inner.ramp, #ramps[inner.ramp])     -- sel-out: darkest tone of what it borders
            end
          else
            -- facing the light: the pixel above or to the left is outline or empty
            local up, left = at(x, y - 1), at(x - 1, y)
            if (not up or up.ramp == "ink") or (not left or left.ramp == "ink") then
              e = entry(e.ramp, e.step - 1)
            end
          end
          final[y][x] = e
        end
      end
    end

    local img = Image(w, h, ColorMode.RGB)
    for y = 0, h - 1 do
      for x = 0, w - 1 do
        local e = final[y][x]
        if e then img:drawPixel(x, y, px(e)) end
      end
    end
    spr:newCel(out, frame, img, cel.position)
  end
end

spr:saveAs(p.out)
app.command.ExportSpriteSheet{
  ui = false, askOverwrite = false, type = SpriteSheetType.HORIZONTAL,
  textureFilename = p.sheet, dataFilename = p.data, dataFormat = SpriteSheetDataFormat.JSON_ARRAY,
  layer = "finished", listTags = true, trim = false,
}
print("finished " .. #spr.frames .. " frames -> " .. p.out)
