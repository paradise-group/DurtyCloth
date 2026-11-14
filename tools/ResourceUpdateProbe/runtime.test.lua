local resource = assert(arg[1], "resource directory required")
local document = dofile(assert(arg[2], "identity fixture required"))
local registered = {}
local offset = 272
local worn = document.entries[1]
local applied

function exports(name, callback) registered[name] = callback end
function GetCurrentResourceName() return "high-clothes-s3" end
function LoadResourceFile() return "fixture" end
json = { decode = function() return document end }
function PlayerPedId() return 1 end
function DoesEntityExist() return true end
function joaat(model) return model == "mp_m_freemode_01" and 111 or 222 end
function GetEntityModel() return 111 end
local function find(collection, index)
    for _, entry in ipairs(document.entries) do
        if entry.collection == collection and entry.index == index then return entry end
    end
end
function GetPedDrawableGlobalIndexFromCollection(_, _, collection, index)
    local entry = find(collection, index)
    return entry and offset + index + (entry == document.entries[1] and 0 or 50) or -1
end
GetPedPropGlobalIndexFromCollection = GetPedDrawableGlobalIndexFromCollection
function GetNumberOfPedCollectionTextureVariations(_, _, collection, index)
    local entry = find(collection, index)
    local count = 0
    for _, texture in ipairs(entry and entry.textures or {}) do count = math.max(count, texture.index + 1) end
    return count
end
GetNumberOfPedCollectionPropTextureVariations = GetNumberOfPedCollectionTextureVariations
function SetPedCollectionComponentVariation(_, slot, collection, index, texture)
    applied = { slot = slot, collection = collection, index = index, texture = texture }
end
SetPedCollectionPropIndex = SetPedCollectionComponentVariation
function GetPedDrawableVariation(_, slot)
    return slot == worn.slot and GetPedDrawableGlobalIndexFromCollection(1, slot, worn.collection, worn.index) or 0
end
function GetPedTextureVariation() return 0 end
function GetPedPropIndex() return -1 end
function GetPedPropTextureIndex() return 0 end
function GetPedCollectionNameFromDrawable() return worn.collection end
function GetPedCollectionLocalIndexFromDrawable() return worn.index end
GetPedCollectionNameFromProp = GetPedCollectionNameFromDrawable
GetPedCollectionLocalIndexFromProp = GetPedCollectionLocalIndexFromDrawable

dofile(resource .. "/clothing-ids.client.lua")
local old, added = document.entries[1], document.entries[2]
local itemId = "d6c55f77-12a7-8d35-68dc-d7385fb5309b"
local textureId = "c25ecaa7-fdbb-d86b-b422-5699197bd9d6"
local initial = assert(registered.resolveClothingId(itemId, textureId))
assert(initial.item == 273 and initial.collection == old.collection)
offset = offset + 200
local shifted = assert(registered.resolveClothingId(itemId, textureId))
assert(shifted.item == 473 and shifted.itemId == itemId and shifted.textureId == textureId)
assert(registered.applyClothingId(itemId, textureId))
assert(applied.slot == 11 and applied.collection == old.collection and applied.index == old.index)
assert(registered.captureClothingIds()["component:11"].itemId == itemId)
assert(registered.resolveClothingId(itemId, added.textures[1].id) == nil)
worn = added
assert(registered.applyClothingId(added.id, added.textures[1].id))
assert(applied.collection == added.collection and applied.index == added.index)
assert(registered.captureClothingIds()["component:11"].itemId == added.id)
print("PASS: actual saved UUID pair resolves/captures/applies through the combined export, even after a global-index shift; added clothing also resolves correctly.")
