-- The swing trail ("smear"), drawn in Aseprite: what makes a swing read as fast.
-- A crescent sweeping from over the shoulder down to the front, brightest and
-- widest at its leading edge, in three frames (full, fading, a thin afterglow),
-- and a straight streak for a thrust in two. Pixel-art alpha: a few hard steps,
-- not a smooth fade. The pivot (the shoulder) is the canvas centre, facing right.
--   Aseprite.exe -b --script-param out=<file.aseprite> --script-param sheet=<png>
--                   --script-param data=<json> --script draw_swing_trail.lua
local p = app.params
local S = 160
local C = S / 2
local spr = Sprite(S, S, ColorMode.RGB)
for i = 2, 5 do spr:newEmptyFrame() end   -- (and six more below)

local function rgba(r, g, b, a) return app.pixelColor.rgba(r, g, b, a) end
-- three steps of light: core, body, edge
local CORE = { 255, 255, 250 }
local BODY = { 255, 246, 200 }
local EDGE = { 255, 220, 140 }

-- A crescent from angle a0 to a1 (degrees, y up, facing right), the way the
-- weapon travels: clockwise for the overhead slam, counter-clockwise for the
-- swipe and the uppercut. Radius R from the pivot.
local function arcFrameAt(img, a0, a1, clockwise, R, sweepEnd, thickness, fade)
  local span = clockwise and ((a0 - a1) % 360) or ((a1 - a0) % 360)
  for y = 0, S - 1 do
    for x = 0, S - 1 do
      local dx, dy = x + 0.5 - C, C - (y + 0.5)
      local r = math.sqrt(dx * dx + dy * dy)
      local ang = math.deg(math.atan(dy, dx))
      local d = clockwise and ((a0 - ang) % 360) or ((ang - a0) % 360)
      local t = d / span                      -- 0 at the tail, 1 at the head
      if t >= 0 and t <= sweepEnd then
        local head = t / sweepEnd
        local inner = R * (1 - thickness * (0.35 + 0.65 * head))
        if r <= R and r >= inner then
          local depth = (r - inner) / math.max(1, R - inner)    -- 0 inner edge, 1 outer edge
          local v = head * fade
          local col, alpha
          if v > 0.62 and depth > 0.35 then col, alpha = CORE, 255
          elseif v > 0.38 then col, alpha = BODY, 235
          elseif v > 0.16 then col, alpha = EDGE, 175
          end
          if col then img:drawPixel(x, y, rgba(col[1], col[2], col[3], alpha)) end
        end
      end
    end
  end
end

local function thrustFrame(img, length, fade)
  for y = 0, S - 1 do
    for x = 0, S - 1 do
      local dx, dy = x + 0.5 - C, math.abs(C - (y + 0.5))
      if dx > 0 and dx <= length then
        local t = dx / length
        local half = 2 + 5 * t                 -- widening toward the point
        if dy <= half then
          local v = t * fade
          local col, alpha
          if v > 0.6 and dy <= half * 0.45 then col, alpha = CORE, 255
          elseif v > 0.34 then col, alpha = BODY, 230
          elseif v > 0.14 then col, alpha = EDGE, 170
          end
          if col then img:drawPixel(x, y, rgba(col[1], col[2], col[3], alpha)) end
        end
      end
    end
  end
end

local layer = spr.layers[1]
layer.name = "trail"
local function frameImage(i, draw)
  local img = Image(S, S, ColorMode.RGB)
  draw(img)
  spr:newCel(layer, spr.frames[i], img, Point(0, 0))
end
-- the overhead slam (the power hit): up-and-behind round to forward-and-down
local function arcFrame(img, sweepEnd, thickness, fade) arcFrameAt(img, 150, -15, true, 70, sweepEnd, thickness, fade) end
-- hit 1, the swipe: low from behind, under, out to the front
local function swipeFrame(img, thickness, fade) arcFrameAt(img, 200, 345, false, 62, 1.0, thickness, fade) end
-- hit 2, the uppercut: from low in front, up to overhead
local function riseFrame(img, thickness, fade) arcFrameAt(img, -60, 95, false, 66, 1.0, thickness, fade) end

for i = 6, 11 do spr:newEmptyFrame() end
frameImage(1, function(img) arcFrame(img, 1.0, 0.42, 1.0) end)
frameImage(2, function(img) arcFrame(img, 1.0, 0.30, 0.75) end)
frameImage(3, function(img) arcFrame(img, 1.0, 0.14, 0.5) end)
frameImage(4, function(img) thrustFrame(img, 78, 1.0) end)
frameImage(5, function(img) thrustFrame(img, 78, 0.6) end)
frameImage(6, function(img) swipeFrame(img, 0.40, 1.0) end)
frameImage(7, function(img) swipeFrame(img, 0.28, 0.75) end)
frameImage(8, function(img) swipeFrame(img, 0.13, 0.5) end)
frameImage(9, function(img) riseFrame(img, 0.40, 1.0) end)
frameImage(10, function(img) riseFrame(img, 0.28, 0.75) end)
frameImage(11, function(img) riseFrame(img, 0.13, 0.5) end)
for i = 1, 11 do spr.frames[i].duration = 0.04 end

local arc = spr:newTag(1, 3); arc.name = "arc"
local thrust = spr:newTag(4, 5); thrust.name = "thrust"
local swipe = spr:newTag(6, 8); swipe.name = "swipe"
local rise = spr:newTag(9, 11); rise.name = "rise"
spr:saveAs(p.out)
app.command.ExportSpriteSheet{
  ui = false, askOverwrite = false, type = SpriteSheetType.HORIZONTAL,
  textureFilename = p.sheet, dataFilename = p.data, dataFormat = SpriteSheetDataFormat.JSON_ARRAY,
  listTags = true, trim = false,
}
print("swing trail: 11 frames -> " .. p.out)
