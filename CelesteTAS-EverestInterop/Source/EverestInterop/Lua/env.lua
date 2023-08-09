Monocle = require("#Monocle")
Celeste = require("#Celeste")
TAS = require("#TAS")
Vector2 = require("#Microsoft.Xna.Framework.Vector2")
LuaHelpers = require("#TAS.EverestInterop.Lua.LuaHelpers")

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
function invokeMethod(instanceOrTypeName, methodName, ...)
    return LuaHelpers.InvokeMethod(instanceOrTypeName, methodName, ...)
end

--- get enum value
function getEnum(enumTypeName, value)
    return LuaHelpers.GetEnum(enumTypeName, value)
end

function getLevel()
    return LuaHelpers.GetLevel()
end

function getSession()
    return LuaHelpers.GetSession()
end