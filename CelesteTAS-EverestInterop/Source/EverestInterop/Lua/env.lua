local LuaHelpers = require("#TAS.EverestInterop.Lua.LuaHelpers")

--- log message
function log(message, tag)
    Celeste.Mod.Logger.Log(Celeste.Mod.LogLevel.Info, tag or "CelesteTAS", tostring(message))
end

--- getEntity("Player") or getEntity("Celeste.Player")
function getEntity(entityTypeName)
    return LuaHelpers.GetEntity(entityTypeName)
end

--- getEntities("Player") or getEntities("Celeste.Player")
function getEntities(entityTypeName)
    return LuaHelpers.GetEntities(entityTypeName)
end

--- get field or property value
function getValue(instanceOrTypeName, memberName)
    return LuaHelpers.GetValue(instanceOrTypeName, memberName)
end

--- parameters = {parameter1, parameter2, ...}
function invokeMethod(instanceOrTypeName, methodName, parameters)
    return LuaHelpers.InvokeMethod(instanceOrTypeName, methodName, parameters or {})
end

--- get enum value
function getEnum(enumTypeName, value)
    return LuaHelpers.GetEnum(enumTypeName, value)
end

local Monocle = require("#Monocle")
local Celeste = require("#Celeste")
local TAS = require("#TAS")
local Vector2 = require("#Microsoft.Xna.Framework.Vector2")

local scene = Monocle.Engine.Scene
local level = scene or scene.Level
local session = level.Session
local player = getEntity("Player")