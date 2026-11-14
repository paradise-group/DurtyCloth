local resource = assert(arg[1], "resource directory required")
local fixture = dofile(assert(arg[2], "native insertion fixture required"))
local document
local registered
local base = fixture.saved.item - fixture.before.index
local wornIndex = fixture.before.index

function exports(name, callback) registered[name] = callback end
function GetCurrentResourceName() return "high-clothes-s3" end
function LoadResourceFile() return "fixture" end
json = { decode = function() return document end }
function PlayerPedId() return 1 end
function DoesEntityExist() return true end
function joaat() return 111 end
function GetEntityModel() return 111 end
function GetPedDrawableGlobalIndexFromCollection(_, slot, collection, index)
    for _, entry in ipairs(document.entries) do
        if entry.slot == slot and entry.collection == collection and entry.index == index then return base + index end
    end
    return -1
end
GetPedPropGlobalIndexFromCollection = GetPedDrawableGlobalIndexFromCollection
function GetNumberOfPedCollectionTextureVariations(_, slot, collection, index)
    for _, entry in ipairs(document.entries) do
        if entry.slot == slot and entry.collection == collection and entry.index == index then
            local count = 0
            for _, texture in ipairs(entry.textures) do count = math.max(count, texture.index + 1) end
            return count
        end
    end
    return 0
end
GetNumberOfPedCollectionPropTextureVariations = GetNumberOfPedCollectionTextureVariations
function SetPedCollectionComponentVariation(_, slot, collection, index, texture)
    assert(slot == 11 and collection == fixture.before.collection and texture == 0)
    wornIndex = index
end
SetPedCollectionPropIndex = SetPedCollectionComponentVariation
function GetPedDrawableVariation() return base + wornIndex end
function GetPedTextureVariation() return 0 end
function GetPedPropIndex() return -1 end
function GetPedPropTextureIndex() return 0 end
function GetPedCollectionNameFromDrawable() return fixture.before.collection end
function GetPedCollectionLocalIndexFromDrawable() return wornIndex end
GetPedCollectionNameFromProp = GetPedCollectionNameFromDrawable
GetPedCollectionLocalIndexFromProp = GetPedCollectionLocalIndexFromDrawable

local function load(entries)
    document = { formatVersion = 1, preserveSourceLayout = true, entries = entries }
    registered = {}
    dofile(resource .. "/clothing-ids.client.lua")
end

load({ fixture.before })
local previous = assert(registered.resolveClothingId(fixture.saved.itemId, fixture.saved.textureId))
assert(previous.item == fixture.saved.item and previous.index == 101)
load(fixture.after)
local current = assert(registered.resolveClothingId(fixture.saved.itemId, fixture.saved.textureId))
assert(current.item == fixture.saved.item + 1 and current.index == 102)
assert(current.itemId == fixture.saved.itemId and current.textureId == fixture.saved.textureId)
assert(registered.applyClothingId(fixture.saved.itemId, fixture.saved.textureId))
assert(wornIndex == 102 and registered.captureClothingIds()["component:11"].itemId == fixture.saved.itemId)
local added = fixture.after[2]
assert(registered.applyClothingId(added.id, added.textures[1].id))
assert(wornIndex == 101 and registered.captureClothingIds()["component:11"].itemId == added.id)
assert(fixture.saved.item == 773 and fixture.saved.index == 101)
print("PASS: unchanged database UUID pair selects original shirt after real collection insertion: local 101 -> 102, global 773 -> 774; new clothing occupies local 101/global 773.")
