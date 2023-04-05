using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

// ReSharper disable once UnusedType.Global
public static class SetCommand {
    private static bool consolePrintLog;
    private const string logPrefix = "Set Command Failed: ";

    [Monocle.Command("set", "Set settings/level/session/entity field. eg set DashMode Infinite; set Player.Speed 325 -52.5 (CelesteTAS)")]
    private static void ConsoleSet(string arg1, string arg2, string arg3, string arg4, string arg5, string arg6, string arg7, string arg8,
        string arg9) {
        string[] args = {arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9};
        consolePrintLog = true;
        Set(args.TakeWhile(arg => arg != null).ToArray());
        consolePrintLog = false;
    }

    // Set, Setting, Value
    // Set, Mod.Setting, Value
    // Set, Entity.Field, Value
    [TasCommand("Set", LegalInMainGame = false)]
    private static void Set(string[] args) {
        if (args.Length < 2) {
            return;
        }

        try {
            if (args[0].Contains(".")) {
                string[] parameters = args.Skip(1).ToArray();
                if (TrySetModSetting(args[0], parameters)) {
                    return;
                }

                if (InfoCustom.TryParseMemberNames(args[0], out string typeText, out List<string> memberNames, out string errorMessage)
                    && InfoCustom.TryParseType(typeText, out Type type, out string entityId, out errorMessage)) {
                    FindObjectAndSetMember(type, entityId, memberNames, parameters);
                } else {
                    errorMessage.Log(consolePrintLog, LogLevel.Warn);
                }
            } else {
                SetGameSetting(args);
            }
        } catch (Exception e) {
            e.Log(consolePrintLog, LogLevel.Warn);
        }
    }

    private static void SetGameSetting(string[] args) {
        object settings = null;
        string settingName = args[0];
        string[] parameters = args.Skip(1).ToArray();

        FieldInfo field;
        if ((field = typeof(Settings).GetField(settingName)) != null) {
            settings = Settings.Instance;
        } else if ((field = typeof(SaveData).GetField(settingName)) != null) {
            settings = SaveData.Instance;
        } else if ((field = typeof(Assists).GetField(settingName)) != null) {
            settings = SaveData.Instance.Assists;
        }

        if (settings == null) {
            return;
        }

        object value = ConvertType(parameters, field.FieldType);

        if (!SettingsSpecialCases(settingName, value)) {
            field.SetValue(settings, value);

            if (settings is Assists assists) {
                SaveData.Instance.Assists = assists;
            }
        }

        if (settings is Assists variantAssists && !Equals(variantAssists, Assists.Default)) {
            SaveData.Instance.VariantMode = true;
            SaveData.Instance.AssistMode = false;
        }
    }

    private static bool TrySetModSetting(string moduleSetting, string[] values) {
        int index = moduleSetting.IndexOf(".", StringComparison.Ordinal);
        string moduleName = moduleSetting.Substring(0, index);
        string settingName = moduleSetting.Substring(index + 1);
        foreach (EverestModule module in Everest.Modules) {
            if (module.Metadata.Name == moduleName && module.SettingsType is { } settingsType) {
                TrySetMember(settingsType, module._Settings, settingName, values);
                return true;
            }
        }

        return false;
    }

    private static void FindObjectAndSetMember(Type type, string entityId, List<string> memberNames, string[] values, object structObj = null) {
        if (memberNames.IsEmpty() || values.IsEmpty() && structObj == null) {
            return;
        }

        string lastMemberName = memberNames.Last();
        memberNames = memberNames.SkipLast().ToList();

        Type objType;
        object obj = null;
        if (memberNames.IsEmpty() &&
            (type.GetGetMethod(lastMemberName) is {IsStatic: true} || type.GetFieldInfo(lastMemberName) is {IsStatic: true})) {
            objType = type;
        } else if (memberNames.IsNotEmpty() &&
                   (type.GetGetMethod(memberNames.First()) is {IsStatic: true} ||
                    type.GetFieldInfo(memberNames.First()) is {IsStatic: true})) {
            obj = InfoCustom.GetMemberValue(type, null, memberNames, true);
            if (TryPrintErrorLog()) {
                return;
            }

            objType = obj.GetType();
        } else {
            obj = FindSpecialObject(type, entityId);
            if (obj == null) {
                Log($"{type.FullName}{entityId} object is not found");
                return;
            } else {
                if (type.IsSameOrSubclassOf(typeof(Entity)) && obj is List<Entity> entities) {
                    if (entities.IsEmpty()) {
                        Log($"{type.FullName}{entityId} entity is not found");
                        return;
                    } else {
                        List<object> memberValues = new();
                        foreach (Entity entity in entities) {
                            object memberValue = InfoCustom.GetMemberValue(type, entity, memberNames, true);
                            if (TryPrintErrorLog()) {
                                return;
                            }

                            if (memberValue != null) {
                                memberValues.Add(memberValue);
                            }
                        }

                        if (memberValues.IsEmpty()) {
                            return;
                        }

                        obj = memberValues;
                        objType = memberValues.First().GetType();
                    }
                } else {
                    obj = InfoCustom.GetMemberValue(type, obj, memberNames, true);
                    if (TryPrintErrorLog()) {
                        return;
                    }

                    objType = obj.GetType();
                }
            }
        }

        if (type.IsSameOrSubclassOf(typeof(Entity)) && obj is List<object> objects) {
            objects.ForEach(SetMember);
        } else {
            SetMember(obj);
        }

        void SetMember(object @object) {
            if (!TrySetMember(objType, @object, lastMemberName, values, structObj)) {
                return;
            }

            // after modifying the struct
            // we also need to update the object own the struct
            if (memberNames.IsNotEmpty() && objType.IsStructType()) {
                string[] position = @object switch {
                    Vector2 vector2 => new[] {vector2.X.ToString(CultureInfo.InvariantCulture), vector2.Y.ToString(CultureInfo.InvariantCulture)},
                    Vector2Double vector2Double => new[] {
                        vector2Double.X.ToString(CultureInfo.InvariantCulture), vector2Double.Y.ToString(CultureInfo.InvariantCulture)
                    },
                    _ => new string[] { }
                };

                FindObjectAndSetMember(type, entityId, memberNames, position, position.IsEmpty() ? @object : null);
            }
        }

        bool TryPrintErrorLog() {
            if (obj == null) {
                Log($"{type.FullName} member value is null");
                return true;
            } else if (obj is string errorMsg && errorMsg.EndsWith(" not found")) {
                Log(errorMsg);
                return true;
            }

            return false;
        }
    }

    private static bool TrySetMember(Type objType, object obj, string lastMemberName, string[] values, object structObj = null) {
        if (objType.GetPropertyInfo(lastMemberName) is { } property && property.GetSetMethod(true) is { } setMethod) {
            if (obj is Actor actor && lastMemberName is "X" or "Y") {
                double.TryParse(values[0], out double value);
                Vector2 remainder = actor.movementCounter;
                if (lastMemberName == "X") {
                    actor.Position.X = (int) Math.Round(value);
                    remainder.X = (float) (value - actor.Position.X);
                } else {
                    actor.Position.Y = (int) Math.Round(value);
                    remainder.Y = (float) (value - actor.Position.Y);
                }

                actor.movementCounter = remainder;
            } else if (obj is Platform platform && lastMemberName is "X" or "Y") {
                double.TryParse(values[0], out double value);
                Vector2 remainder = platform.movementCounter;
                if (lastMemberName == "X") {
                    platform.Position.X = (int) Math.Round(value);
                    remainder.X = (float) (value - platform.Position.X);
                } else {
                    platform.Position.Y = (int) Math.Round(value);
                    remainder.Y = (float) (value - platform.Position.Y);
                }

                platform.movementCounter = remainder;
            } else if (property.PropertyType == typeof(ButtonBinding) && property.GetValue(obj) is ButtonBinding buttonBinding) {
                HashSet<Keys> keys = new();
                HashSet<MButtons> mButtons = new();
                IList mouseButtons = buttonBinding.Button.GetFieldValue<object>("Binding")?.GetFieldValue<IList>("Mouse");
                foreach (string str in values) {
                    // parse mouse first, so Mouse.Left is not parsed as Keys.Left
                    if (Enum.TryParse(str, true, out MButtons mButton)) {
                        if (mouseButtons == null && mButton is MButtons.X1 or MButtons.X2) {
                            AbortTas("X1 and X2 are not supported before Everest adding mouse support");
                            return false;
                        }

                        mButtons.Add(mButton);
                    } else if (Enum.TryParse(str, true, out Keys key)) {
                        keys.Add(key);
                    } else {
                        AbortTas($"{str} is not a valid key");
                        return false;
                    }
                }

                List<VirtualButton.Node> nodes = buttonBinding.Button.Nodes;

                if (keys.IsNotEmpty()) {
                    foreach (VirtualButton.Node node in nodes.ToList()) {
                        if (node is VirtualButton.KeyboardKey) {
                            nodes.Remove(node);
                        }
                    }

                    nodes.AddRange(keys.Select(key => new VirtualButton.KeyboardKey(key)));
                }

                if (mButtons.IsNotEmpty()) {
                    foreach (VirtualButton.Node node in nodes.ToList()) {
                        switch (node) {
                            case VirtualButton.MouseLeftButton:
                            case VirtualButton.MouseRightButton:
                            case VirtualButton.MouseMiddleButton:
                                nodes.Remove(node);
                                break;
                        }
                    }

                    if (mouseButtons != null) {
                        mouseButtons.Clear();
                        foreach (MButtons mButton in mButtons) {
                            mouseButtons.Add(mButton);
                        }
                    } else {
                        foreach (MButtons mButton in mButtons) {
                            if (mButton == MButtons.Left) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseLeftButton()));
                            } else if (mButton == MButtons.Right) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseRightButton()));
                            } else if (mButton == MButtons.Middle) {
                                nodes.AddRange(keys.Select(key => new VirtualButton.MouseMiddleButton()));
                            }
                        }
                    }
                }
            } else {
                object value = structObj ?? ConvertType(values, property.PropertyType);
                setMethod.Invoke(obj, new[] {value});
            }
        } else if (objType.GetFieldInfo(lastMemberName) is { } field) {
            if (obj is Actor actor && lastMemberName == "Position" && values.Length == 2) {
                double.TryParse(values[0], out double x);
                double.TryParse(values[1], out double y);
                Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                actor.Position = position;
                actor.movementCounter = remainder;
            } else if (obj is Platform platform && lastMemberName == "Position" && values.Length == 2) {
                double.TryParse(values[0], out double x);
                double.TryParse(values[1], out double y);
                Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                platform.Position = position;
                platform.movementCounter = remainder;
            } else {
                object value = structObj ?? ConvertType(values, field.FieldType);
                if (lastMemberName.Equals("Speed", StringComparison.OrdinalIgnoreCase) && value is Vector2 speed &&
                    Math.Abs(Engine.TimeRateB - 1f) > 1e-10) {
                    field.SetValue(obj, speed / Engine.TimeRateB);
                } else {
                    field.SetValue(obj, value);
                }
            }
        } else {
            Log($"{objType.FullName}.{lastMemberName} member not found");
            return false;
        }

        return true;
    }

    public static object FindSpecialObject(Type type, string entityId) {
        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            return InfoCustom.FindEntities(type, entityId);
        } else if (type == typeof(Level)) {
            return Engine.Scene.GetLevel();
        } else if (type == typeof(Session)) {
            return Engine.Scene.GetSession();
        } else {
            return null;
        }
    }

    private static void Log(string text) {
        if (!consolePrintLog) {
            text = $"{logPrefix}{text}";
        }

        text.Log(consolePrintLog, LogLevel.Warn);
    }

    public static object Convert(object value, Type type) {
        try {
            if (value is null or string and ("" or "null")) {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            } else if (type == typeof(string) && value is "\"\"") {
                return string.Empty;
            } else {
                return type.IsEnum ? Enum.Parse(type, (string) value, true) : System.Convert.ChangeType(value, type);
            }
        } catch {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private static object ConvertType(string[] values, Type type) {
        Type nullableType = type;
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (values.Length == 2 && type == typeof(Vector2)) {
            float.TryParse(values[0], out float x);
            float.TryParse(values[1], out float y);
            return new Vector2(x, y);
        } else if (values.Length == 1) {
            if (type == typeof(Random) && int.TryParse(values[0], out int seed)) {
                return new Random(seed);
            } else {
                return Convert(values[0], nullableType);
            }
        } else if (values.Length >= 2) {
            object instance = Activator.CreateInstance(type);
            MemberInfo[] members = type.GetMembers().Where(info => (info.MemberType & (MemberTypes.Field | MemberTypes.Property)) != 0).ToArray();
            for (int i = 0; i < members.Length && i < values.Length; i++) {
                string memberName = members[i].Name;
                if (type.GetField(memberName) is { } fieldInfo) {
                    fieldInfo.SetValue(instance, Convert(values[i], fieldInfo.FieldType));
                } else if (type.GetProperty(memberName) is { } propertyInfo) {
                    propertyInfo.SetValue(instance, Convert(values[i], propertyInfo.PropertyType));
                }
            }

            return instance;
        }

        return default;
    }

    private static bool SettingsSpecialCases(string settingName, object value) {
        Player player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
        SaveData saveData = SaveData.Instance;
        Settings settings = Settings.Instance;
        switch (settingName) {
            // Assists
            case "GameSpeed":
                saveData.Assists.GameSpeed = (int) value;
                Engine.TimeRateB = saveData.Assists.GameSpeed / 10f;
                break;
            case "MirrorMode":
                saveData.Assists.MirrorMode = (bool) value;
                Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = (bool) value;
                if (typeof(Celeste.Input).GetFieldValue<VirtualJoystick>("Feather") is { } featherJoystick) {
                    featherJoystick.InvertedX = (bool) value;
                }

                break;
            case "PlayAsBadeline":
                saveData.Assists.PlayAsBadeline = (bool) value;
                if (player != null) {
                    PlayerSpriteMode mode = saveData.Assists.PlayAsBadeline
                        ? PlayerSpriteMode.MadelineAsBadeline
                        : player.DefaultSpriteMode;
                    if (player.Active) {
                        player.ResetSpriteNextFrame(mode);
                    } else {
                        player.ResetSprite(mode);
                    }
                }

                break;
            case "DashMode":
                saveData.Assists.DashMode = (Assists.DashModes) value;
                if (player != null) {
                    player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                }

                break;

            // SaveData
            case "VariantMode":
                saveData.VariantMode = (bool) value;
                saveData.AssistMode = false;
                if (!saveData.VariantMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }

                break;
            case "AssistMode":
                saveData.AssistMode = (bool) value;
                saveData.VariantMode = false;
                if (!saveData.AssistMode) {
                    Assists assists = default;
                    assists.GameSpeed = 10;
                    ResetVariants(assists);
                }

                break;

            // Settings
            case "Rumble":
                settings.Rumble = (RumbleAmount) value;
                Celeste.Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                break;
            case "GrabMode":
                settings.SetFieldValue("GrabMode", value);
                typeof(Celeste.Celeste).InvokeMethod("ResetGrab");
                break;
            // case "Fullscreen":
            // game get stuck when toggle fullscreen
            // typeof(MenuOptions).InvokeMethod("SetFullscreen", value);
            // break;
            case "WindowScale":
                typeof(MenuOptions).InvokeMethod("SetWindow", value);
                break;
            case "VSync":
                typeof(MenuOptions).InvokeMethod("SetVSync", value);
                break;
            case "MusicVolume":
                typeof(MenuOptions).InvokeMethod("SetMusic", value);
                break;
            case "SFXVolume":
                typeof(MenuOptions).InvokeMethod("SetSfx", value);
                break;
            default:
                return false;
        }

        return true;
    }

    public static void ResetVariants(Assists assists) {
        SaveData.Instance.Assists = assists;
        SettingsSpecialCases("DashMode", assists.DashMode);
        SettingsSpecialCases("GameSpeed", assists.GameSpeed);
        SettingsSpecialCases("MirrorMode", assists.MirrorMode);
        SettingsSpecialCases("PlayAsBadeline", assists.PlayAsBadeline);
    }
}