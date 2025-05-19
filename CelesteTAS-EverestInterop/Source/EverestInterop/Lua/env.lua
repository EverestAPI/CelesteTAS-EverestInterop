local Monocle = require("#Monocle")
local Celeste = require("#Celeste")
local TAS = require("#TAS")
local Vector2 = require("#Microsoft.Xna.Framework.Vector2")
local LuaHelpers = require("#TAS.EverestInterop.Lua.LuaHelpers")

--- Use nullValue instead of nil when using setValue / invokeMethod
local nullValue = LuaHelpers.NullValue

--- Logs a message
local function log(message, tag)
    Celeste.Mod.Logger.Log(Celeste.Mod.LogLevel.Info, tag or "CelesteTAS/Lua", tostring(message))
end

--- Gets all members matching the specified target-query
local function get(query)
    return LuaHelpers.Get(query)
end

--- Sets all members matching the specified target-query to the value
local function set(query, arguments)
    LuaHelpers.Set(query, arguments)
end

--- Invokes all members matching the specified target-query with the arguments
local function invoke(query, arguments)
    LuaHelpers.Set(query, arguments)
end

--- Resolves the first entity which matches the specified target-query
--- Example: getEntity("Player"), getEntity("Celeste.Player"), getEntity("DustStaticSpinner[s1:12]")
local function getEntity(entityTypeName)
    return LuaHelpers.GetEntity(entityTypeName)
end

--- Resolves all entities which match the specified target-query, e.g. "Player" or "Celeste.Player"
--- Example: getEntities("Player"), getEntities("Celeste.Player"), getEntities("CustomSpinner@VivHelper")
local function getEntities(entityTypeName)
    return LuaHelpers.GetEntities(entityTypeName)
end

--- Gets the value of a (private) field / property
local function getValue(instanceOrTypeName, memberName)
    return LuaHelpers.GetValue(instanceOrTypeName, memberName)
end

--- Sets the value of a (private) field / property
--- Use nullValue instead of nil if you want to pass null to C#
local function setValue(instanceOrTypeName, memberName, value)
    return LuaHelpers.SetValue(instanceOrTypeName, memberName, value)
end

--- Invokes a (private) method
--- Use nullValue instead of nil if you want to pass null to C#
local function invokeMethod(instanceOrTypeName, methodName, ...)
    return LuaHelpers.InvokeMethod(instanceOrTypeName, methodName, ...)
end

--- Resolves the enum value for an ordinal or name
--- Example: getEnum('Facings', 'Right') or getEnum('Facings', 1)
local function getEnum(enumTypeName, value)
    return LuaHelpers.GetEnum(enumTypeName, value)
end

--- Returns the current level
local function getLevel()
    return LuaHelpers.GetLevel()
end

--- Returns the current session
local function getSession()
    return LuaHelpers.GetSession()
end

--- Casts the value to an int, for usage with setValue / invokeMethod
local function toInt(longValue)
    return LuaHelpers.ToInt(longValue)
end

--- Casts the value to a float, for usage with setValue / invokeMethod
--- Example: invokeMethod("ExtendedVariants.UI.ModOptionsEntries", "SetVariantValue", getEnum("Variant", "Gravity"), toFloat(0.1))
local function toFloat(doubleValue)
    return LuaHelpers.ToFloat(doubleValue)
end

local scene = Monocle.Engine.Scene
local level = getLevel()
local session = getSession()
local player = getEntity("Player")
