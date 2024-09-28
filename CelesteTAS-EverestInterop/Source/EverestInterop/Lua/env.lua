local Monocle = require("#Monocle")
local Celeste = require("#Celeste")
local TAS = require("#TAS")
local Vector2 = require("#Microsoft.Xna.Framework.Vector2")
local LuaHelpers = require("#TAS.EverestInterop.Lua.LuaHelpers")

--- use nullValue instead of nil when using setValue/invokeMethod
local nullValue = LuaHelpers.NullValue

--- log message
local function log(message, tag)
    Celeste.Mod.Logger.Log(Celeste.Mod.LogLevel.Info, tag or "CelesteTAS", tostring(message))
end

--- getEntity("Player") or getEntity("Celeste.Player")
--- can specify entityId, like getEntity("DustStaticSpinner[s1:12]")
local function getEntity(entityTypeName)
    return LuaHelpers.GetEntity(entityTypeName)
end

--- getEntities("Player") or getEntities("Celeste.Player")
local function getEntities(entityTypeName)
    return LuaHelpers.GetEntities(entityTypeName)
end

--- get field or property value
local function getValue(instanceOrTypeName, memberName)
    return LuaHelpers.GetValue(instanceOrTypeName, memberName)
end

--- set field or property value
--- use nullValue instead of nil if you want to pass null to c#
local function setValue(instanceOrTypeName, memberName, value)
    return LuaHelpers.SetValue(instanceOrTypeName, memberName, value)
end

--- parameters = parameter1, parameter2, ...
--- use nullValue instead of nil if you want to pass null to c#
local function invokeMethod(instanceOrTypeName, methodName, ...)
    return LuaHelpers.InvokeMethod(instanceOrTypeName, methodName, ...)
end

--- cast double value to float in c# side when calling invokeMethod() or setValue()
--- e.g. invokeMethod("ExtendedVariants.UI.ModOptionsEntries", "SetVariantValue", getEnum("Variant", "Gravity"), toFloat(0.1))
local function toFloat(doubleValue)
    return LuaHelpers.ToFloat(doubleValue)
end

--- get enum value
--- getEnum('Facings', 'Right') or getEnum('Facings', 1) 
local function getEnum(enumTypeName, value)
    return LuaHelpers.GetEnum(enumTypeName, value)
end

local function getLevel()
    return LuaHelpers.GetLevel()
end

local function getSession()
    return LuaHelpers.GetSession()
end

local scene = Monocle.Engine.Scene
local level = getLevel()
local session = getSession()
local player = getEntity("Player")