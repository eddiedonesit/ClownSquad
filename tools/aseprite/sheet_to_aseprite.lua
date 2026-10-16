-- Turn one of the game's sprite strips into an .aseprite file: one frame per
-- frame, a tag per clip (from the .frames.json), so it can be opened and
-- worked on in Aseprite. Run with:
--   Aseprite.exe -b --script-param sheet=... --script-param out=... --script sheet_to_aseprite.lua
local p = app.params
local strip = Image{ fromFile = p.sheet }
local json = io.open(p.frames):read("a")
local fw = tonumber(json:match('"frame_width":%s*(%d+)'))
local fh = tonumber(json:match('"frame_height":%s*(%d+)'))
local count = math.floor(strip.width / fw)

local spr = Sprite(fw, fh, ColorMode.RGB)
spr.layers[1].name = "art"
for i = 1, count - 1 do spr:newEmptyFrame() end
for i = 0, count - 1 do
  local img = Image(fw, fh, ColorMode.RGB)
  img:drawImage(strip, Point(-i * fw, 0))
  spr:newCel(spr.layers[1], spr.frames[i + 1], img, Point(0, 0))
end
-- clips: "name": [a, b, c] -> a tag from the first to the last frame
for name, list in json:gmatch('"([%w_]+)":%s*%[([%d,%s]+)%]') do
  local first, last
  for n in list:gmatch("%d+") do
    n = tonumber(n)
    first = first and math.min(first, n) or n
    last = last and math.max(last, n) or n
  end
  local tag = spr:newTag(first + 1, last + 1)
  tag.name = name
end
spr:saveAs(p.out)
print("saved " .. p.out .. ": " .. count .. " frames, " .. #spr.tags .. " tags")
