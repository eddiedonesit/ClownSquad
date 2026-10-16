-- Version B: the same character at half the pixel density, as classic pixel
-- art (played back at 2x, so it is the same size on screen, chunkier).
--   1. Halve each frame: each 2x2 block takes its most common colour, ignoring
--      the outline, so shapes survive and lines do not smear.
--   2. Snap to a small palette: 3-4 hue-shifted tones per colour.
--   3. Throw the old outline away and draw a clean 1 px one around the new
--      silhouette: dark tone of the colour it borders on the lit top-left,
--      near-black on the shadow side.
--   4. Hand corrections from a patch file (x y colour per line, per frame),
--      written after looking at the result zoomed in.
-- Writes a new .aseprite (layers "base" and "hand") and a sheet + JSON.
--   Aseprite.exe -b --script-param in=X.aseprite --script-param out=Y.aseprite
--      --script-param sheet=Y.png --script-param data=Y.json [--script-param patch=P.txt] --script classic_b.lua

local p = app.params
local src = app.open(p["in"])
local W, H = math.floor(src.width / 2), math.floor(src.height / 2)

local pal = {
  K = "1a1222",
  s = "ffefe0", S = "fadac0", d = "e2a690", D = "b8766e",
  h = "ffd2dc", H = "fa8cae", j = "d65488", J = "8e2f5c",
  r = "ffb0a0", R = "e8343c", q = "a01c38",
  y = "fff2a8", Y = "fad25a", o = "d0903a", O = "8a5228",
  t = "c4faf6", T = "6fe2e7", u = "45b0c8", U = "236479",
  p = "d898ea", P = "b862d1", m = "8a3cb4", M = "55246f",
  w = "ffffff", W = "e6e6f2", g = "b0aec8", G = "6e6a86",
  l = "c4243a", k = "5a0c22",
}
-- the darkest tone of each family, for selective outlines
local family = { s = "D", S = "D", d = "D", D = "D", h = "J", H = "J", j = "J", J = "J", r = "q", R = "q", q = "q",
  y = "O", Y = "O", o = "O", O = "O", t = "U", T = "U", u = "U", U = "U", p = "M", P = "M", m = "M", M = "M",
  w = "G", W = "G", g = "G", G = "G", l = "k", k = "k" }

local function rgb(hex) return tonumber(hex:sub(1, 2), 16), tonumber(hex:sub(3, 4), 16), tonumber(hex:sub(5, 6), 16) end
local keys, cols = {}, {}
for k, hex in pairs(pal) do keys[#keys + 1] = k; cols[k] = { rgb(hex) } end

local function nearest(r, g, b)
  local best, bd = nil, math.huge
  for _, k in ipairs(keys) do
    if k ~= "K" then
      local c = cols[k]
      local d = 2 * (r - c[1]) ^ 2 + 4 * (g - c[2]) ^ 2 + 3 * (b - c[3]) ^ 2
      if d < bd then best, bd = k, d end
    end
  end
  return best
end

local function isInk(r, g, b) return 0.299 * r + 0.587 * g + 0.114 * b < 55 end

-- patch: "frame x y key" per line; frame "*" = every frame
local patches = {}
if p.patch and p.patch ~= "" then
  local f = io.open(p.patch)
  if f then
    for line in f:lines() do
      local fr, x, y, k = line:match("^%s*([%*%d]+)%s+(%d+)%s+(%d+)%s+(%S)")
      if fr then patches[#patches + 1] = { fr = fr, x = tonumber(x), y = tonumber(y), k = k } end
    end
    f:close()
  end
end

local spr = Sprite(W, H, ColorMode.RGB)
spr.layers[1].name = "base"
local hand = spr:newLayer()
hand.name = "hand"
for i = 2, #src.frames do spr:newEmptyFrame() end

for fi, frame in ipairs(src.frames) do
  local cel = src.layers[1]:cel(fi)
  local img = Image(src.width, src.height, ColorMode.RGB)
  if cel then img:drawImage(cel.image, cel.position) end
  -- 1 + 2: halve, majority colour per block, snapped to the palette
  local g = {}
  for y = 0, H - 1 do
    g[y] = {}
    for x = 0, W - 1 do
      local votes, best, bn, opaque, ink = {}, nil, 0, 0, 0
      for dy = 0, 1 do
        for dx = 0, 1 do
          local c = img:getPixel(x * 2 + dx, y * 2 + dy)
          local a = app.pixelColor.rgbaA(c)
          if a > 0 then
            opaque = opaque + 1
            local r, gg, b = app.pixelColor.rgbaR(c), app.pixelColor.rgbaG(c), app.pixelColor.rgbaB(c)
            if isInk(r, gg, b) then ink = ink + 1 end
            if not isInk(r, gg, b) then
              local k = nearest(r, gg, b)
              votes[k] = (votes[k] or 0) + 1
              if votes[k] > bn then best, bn = k, votes[k] end
            end
          end
        end
      end
      -- Inner lines (arm against chest, the leg split) survive when half the block is line
      -- and the block is inside the figure; the silhouette's own outline is redrawn below.
      if opaque == 4 and ink >= 2 then g[y][x] = "K"
      elseif opaque >= 2 and best then g[y][x] = best
      elseif opaque >= 3 then g[y][x] = "K" end
    end
  end
  -- 3: a fresh outline around the silhouette
  local o = {}
  for y = 0, H - 1 do
    o[y] = {}
    for x = 0, W - 1 do
      o[y][x] = g[y][x]
      if not g[y][x] then
        local inner, side
        for _, n in ipairs({ { 1, 0, "L" }, { -1, 0, "R" }, { 0, 1, "T" }, { 0, -1, "B" } }) do
          local q = g[y + n[2]] and g[y + n[2]][x + n[1]]
          if q and q ~= "K" then inner = inner or q; side = side or n[3] end
        end
        if inner then
          -- lit side (outline above or left of the figure): the family's dark tone; shadow side: ink
          o[y][x] = (side == "L" or side == "T") and (family[inner] or "K") or "K"
        end
      end
    end
  end
  local base = Image(W, H, ColorMode.RGB)
  for y = 0, H - 1 do
    for x = 0, W - 1 do
      local k = o[y][x]
      if k then local c = cols[k]; base:drawPixel(x, y, app.pixelColor.rgba(c[1], c[2], c[3], 255)) end
    end
  end
  spr:newCel(spr.layers[1], spr.frames[fi], base, Point(0, 0))
  -- 4: hand corrections on their own layer
  local fix = Image(W, H, ColorMode.RGB)
  local any = false
  for _, pt in ipairs(patches) do
    if pt.fr == "*" or tonumber(pt.fr) == fi - 1 then
      if pt.k == "." then
        -- "." erases: punch through by painting nothing; handled by flattening below
        base:drawPixel(pt.x, pt.y, app.pixelColor.rgba(0, 0, 0, 0))
        spr.layers[1]:cel(fi).image = base
      else
        local c = cols[pt.k]
        if c then fix:drawPixel(pt.x, pt.y, app.pixelColor.rgba(c[1], c[2], c[3], 255)); any = true end
      end
    end
  end
  if any then spr:newCel(hand, spr.frames[fi], fix, Point(0, 0)) end
end

-- Hand-drawn parts stamped onto frames: a character grid (same palette keys,
-- "." = leave alone) and a list of "frame x y" placements. Under the stamp's
-- box, the base's head colours (skin, hair, nose, hat) are cleared first, so
-- nothing of the rough downscale pokes out around the drawing; arms, gloves
-- and pins passing in front are kept.
if p.stamp and p.stamp ~= "" and p.stampat and p.stampat ~= "" then
  local function loadGrid(path)
    local grid = {}
    for line in io.lines(path) do if #line > 0 then grid[#grid + 1] = line end end
    return grid
  end
  local dir = p.stamp:gsub("[^/]*$", "")
  local default = loadGrid(p.stamp)
  local headKeys = "sSdDhHjJrRqyYoOtTuUk"
  -- "frame x y [@other_grid.txt] [front x0 y0 x1 y1]": another drawing for that
  -- frame (an expression), and/or a box where the arm passes in front of the head:
  -- there the original frame's arm, glove and pin are painted back over the drawing.
  for line in io.lines(p.stampat) do
    local fr, sx, sy = line:match("^(%d+)%s+(%-?%d+)%s+(%-?%d+)")
    if fr then
      fr, sx, sy = tonumber(fr) + 1, tonumber(sx), tonumber(sy)
      local other = line:match("@(%S+)")
      local grid = other and loadGrid(dir .. other) or default
      local fx0, fy0, fx1, fy1 = line:match("front%s+(%d+)%s+(%d+)%s+(%d+)%s+(%d+)")
      local baseCel = spr.layers[1]:cel(fr)
      local before = baseCel.image:clone()
      local img = baseCel.image:clone()
      for gy, row in ipairs(grid) do
        for gx = 1, #row do
          local x, y = sx + gx - 1, sy + gy - 1
          local c = img:getPixel(x, y)
          if app.pixelColor.rgbaA(c) > 0 then
            -- which palette key is this base pixel?
            local r, g2, b = app.pixelColor.rgbaR(c), app.pixelColor.rgbaG(c), app.pixelColor.rgbaB(c)
            for key, col in pairs(cols) do
              if col[1] == r and col[2] == g2 and col[3] == b then
                -- the hat zone (top 20 rows) is cleared entirely: nothing passes in front of it
                if gy <= 20 or headKeys:find(key, 1, true) or (key == "K" and row:sub(gx, gx) == ".") then
                  img:drawPixel(x, y, app.pixelColor.rgba(0, 0, 0, 0))
                end
                break
              end
            end
          end
          local k = row:sub(gx, gx)
          if k ~= "." and cols[k] then
            local col = cols[k]
            img:drawPixel(x, y, app.pixelColor.rgba(col[1], col[2], col[3], 255))
          end
        end
      end
      if fx0 then
        fx0, fy0, fx1, fy1 = tonumber(fx0), tonumber(fy0), tonumber(fx1), tonumber(fy1)
        for y = fy0, fy1 do
          for x = fx0, fx1 do
            local c = before:getPixel(x, y)
            if app.pixelColor.rgbaA(c) > 0 then
              local r, g2, b = app.pixelColor.rgbaR(c), app.pixelColor.rgbaG(c), app.pixelColor.rgbaB(c)
              for key, col in pairs(cols) do
                if col[1] == r and col[2] == g2 and col[3] == b then
                  if not headKeys:find(key, 1, true) then img:drawPixel(x, y, c) end
                  break
                end
              end
            end
          end
        end
      end
      baseCel.image = img
    end
  end
end

for _, t in ipairs(src.tags) do
  local tag = spr:newTag(t.fromFrame.frameNumber, t.toFrame.frameNumber)
  tag.name = t.name
end
spr:saveAs(p.out)
app.command.ExportSpriteSheet{
  ui = false, askOverwrite = false, type = SpriteSheetType.HORIZONTAL,
  textureFilename = p.sheet, dataFilename = p.data, dataFormat = SpriteSheetDataFormat.JSON_ARRAY,
  listTags = true, trim = false,
}
print("classic: " .. #spr.frames .. " frames at " .. W .. "x" .. H .. ", " .. #patches .. " hand fixes -> " .. p.out)
