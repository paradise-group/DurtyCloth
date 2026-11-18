local root = assert(arg[1], 'fixture directory required')
local ids = dofile(root .. '/ids.lua')
local registered, document, current, applied, offset = {}, nil, {}, 0, 100
function GetCurrentResourceName() return 'idprobe' end
function LoadResourceFile() return 'fixture' end
json = { decode = function() return document end }
function exports(name, callback) registered[name] = callback end
function PlayerPedId() return 1 end
function DoesEntityExist(ped) return ped == 1 or ped == 2 end
function joaat(model) return model == 'mp_m_freemode_01' and 11 or 22 end
function GetEntityModel(ped) return ped == 1 and 11 or 22 end
local function lookup(kind, slot, collection, index)
    for _, entry in ipairs(document.entries) do
        if entry.available and entry.kind == kind and entry.slot == slot and entry.collection == collection and entry.index == index then return entry end
    end
end
local function global(kind, ped, slot, collection, index)
    return lookup(kind, slot, collection, index) and offset + index or -1
end
function GetPedDrawableGlobalIndexFromCollection(...) return global('component', ...) end
function GetPedPropGlobalIndexFromCollection(...) return global('prop', ...) end
local function textureCount(kind, ped, slot, collection, index)
    local entry = lookup(kind, slot, collection, index)
    if not entry then return 0 end
    if entry.textures[1] and entry.textures[1].inherited then return 4 end -- Actual game metadata has four slots, regardless of map candidates.
    local count = 0
    for _, texture in ipairs(entry.textures) do count = math.max(count, texture.index + 1) end
    return count
end
function GetNumberOfPedCollectionTextureVariations(...) return textureCount('component', ...) end
function GetNumberOfPedCollectionPropTextureVariations(...) return textureCount('prop', ...) end
local function set(kind, ped, slot, collection, index, texture)
    applied = applied + 1
    current[kind .. slot] = { collection = collection, index = index, texture = texture }
end
function SetPedCollectionComponentVariation(...) set('component', ...) end
function SetPedCollectionPropIndex(...) set('prop', ...) end
local function equipped(kind, slot) return current[kind .. slot] end
function GetPedDrawableVariation(ped, slot) local e = equipped('component', slot); return e and offset + e.index or 0 end
function GetPedTextureVariation(ped, slot) local e = equipped('component', slot); return e and e.texture or 0 end
function GetPedPropIndex(ped, slot) local e = equipped('prop', slot); return e and offset + e.index or -1 end
function GetPedPropTextureIndex(ped, slot) local e = equipped('prop', slot); return e and e.texture or 0 end
function GetPedCollectionNameFromDrawable(ped, slot) local e = equipped('component', slot); return e and e.collection or '' end
function GetPedCollectionLocalIndexFromDrawable(ped, slot) local e = equipped('component', slot); return e and e.index or 0 end
function GetPedCollectionNameFromProp(ped, slot) local e = equipped('prop', slot); return e and e.collection or '' end
function GetPedCollectionLocalIndexFromProp(ped, slot) local e = equipped('prop', slot); return e and e.index or 0 end
local function load(name)
    document = dofile(root .. '/' .. name .. '.lua')
    dofile(root .. '/idprobe/clothing-ids.client.lua')
end
load('before')
assert(registered.applyClothingId(ids.itemId, ids.textureId))
local original = registered.resolveClothingId(ids.itemId, ids.textureId)
assert(original.index == 0 and original.texture == 0)
local captured = registered.captureClothingIds()
assert(captured['component:11'].itemId == ids.itemId and captured['component:11'].textureId == ids.textureId)
load('after')
offset = 800 -- A DLC inserted before this collection shifts global indices.
assert(registered.applyClothingId(ids.itemId, ids.textureId))
local updated = registered.resolveClothingId(ids.itemId, ids.textureId)
assert(updated.index == 1 and updated.texture == 1 and updated.item == 801)
assert(current.component11.index == 1 and current.component11.texture == 1)
local active
for _, entry in ipairs(document.entries) do if entry.id == ids.itemId then active = entry end end
table.insert(document.entries, 1, { id = 'unavailable-shadow', available = false,
    model = active.model, kind = active.kind, slot = active.slot, collection = active.collection, index = active.index,
    textures = { { id = 'unavailable-shadow-texture', index = 1, available = true } } })
dofile(root .. '/idprobe/clothing-ids.client.lua')
assert(registered.captureClothingIds()['component:11'].itemId == ids.itemId, 'Unavailable source entry shadowed an active identity')
assert(registered.applyClothingId(ids.watchId, ids.watchTextureId))
assert(current.prop6.collection == 'mp_m_idprobe')
local function refused(id, texture, ped, reason)
    local count = applied
    local ok, actual = registered.applyClothingId(id, texture, ped)
    assert(ok == false and actual == reason, tostring(actual))
    assert(applied == count, 'Failed resolution modified the ped')
end
refused(ids.itemId, ids.textureId, 2, 'ped_model_mismatch')
refused('missing', ids.textureId, 1, 'item_missing')
refused(ids.itemId, 'missing', 1, 'texture_missing')
local converter = GetPedDrawableGlobalIndexFromCollection
GetPedDrawableGlobalIndexFromCollection = function() return -1 end
refused(ids.itemId, ids.textureId, 1, 'collection_item_missing')
GetPedDrawableGlobalIndexFromCollection = nil
refused(ids.itemId, ids.textureId, 1, 'collection_natives_unavailable')
GetPedDrawableGlobalIndexFromCollection = converter
load('removed-texture')
refused(ids.itemId, ids.textureId, 1, 'texture_missing')
load('redirected')
local moved = assert(registered.resolveClothingId(ids.itemId, ids.textureId))
assert(moved.itemId ~= ids.itemId and moved.textureId ~= ids.textureId, 'Saved ID pair did not redirect to the separate drawable')
assert(registered.applyClothingId(ids.itemId, ids.textureId))
assert(current.component11.index == moved.index and current.component11.texture == moved.texture)
assert(registered.captureClothingIds()['component:11'].itemId == moved.itemId)
refused(ids.itemId, ids.textureId, 2, 'ped_model_mismatch')
load('removed-item')
refused(ids.itemId, ids.textureId, 1, 'item_missing')
load('original-source')
assert(registered.applyClothingId(ids.sourceItemId, ids.sourceTextureId))
assert(current.component11.collection == 'source_one' and current.component11.index == 4 and current.component11.texture == 0)
assert(registered.applyClothingId(ids.sourceAliasId, ids.sourceAliasTextureId))
assert(registered.captureClothingIds()['component:11'].itemId == ids.sourceItemId, 'Source model alias was not captured canonically')
assert(registered.applyClothingId(ids.inheritedId, ids.inheritedTexture3))
assert(current.component11.collection == 'source_two' and current.component11.index == 17 and current.component11.texture == 3)
assert(registered.captureClothingIds()['component:11'].textureId == ids.inheritedTexture3)
refused(ids.inheritedId, ids.inheritedTexture4, 1, 'collection_texture_missing')
print('PASS: Lua adapter captures/applies IDs across reordered items and textures and shifted global indices; component/prop anchors; missing items/textures/collections/natives and wrong peds never apply another item.')
print('PASS: original collections/indices, explicit source aliases and inherited variants; native texture counts reject unavailable candidates.')
