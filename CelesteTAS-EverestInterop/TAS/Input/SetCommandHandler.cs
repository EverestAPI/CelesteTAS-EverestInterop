using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input {
    // ReSharper disable once UnusedType.Global
    public static class SetCommandHandler {
        private static readonly FieldInfo ActorMovementCounter = typeof(Actor).GetFieldInfo("movementCounter");
        private static readonly FieldInfo InputFeather = typeof(Celeste.Input).GetFieldInfo("Feather");

        // Set, Setting, Value
        // Set, Mod.Setting, Value
        // Set, Entity.Field, Value
        [TasCommand("Set", LegalInMainGame = false)]
        private static void SetCommand(string[] args) {
            if (args.Length < 2) {
                return;
            }

            try {
                object settings = null;
                if (args[0].Contains(".")) {
                    string[] parameters = args.Skip(1).ToArray();
                    if (TrySetModSetting(args[0], parameters)) {
                        return;
                    }

                    if (InfoCustom.TryParseMemberNames(args[0], out string typeText, out List<string> memberNames, out string errorMessage)
                        && InfoCustom.TryParseType(typeText, out Type type, out string entityId, out errorMessage)) {
                        SetObject(type, entityId, memberNames, parameters);
                    } else {
                        errorMessage.Log();
                    }
                } else {
                    SetGameSetting(args);
                }
            } catch (Exception e) {
                e.Log();
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

        private static bool TrySetModSetting(string moduleSetting, string[] value) {
            int index = moduleSetting.IndexOf(".", StringComparison.Ordinal);
            string moduleName = moduleSetting.Substring(0, index);
            string settingName = moduleSetting.Substring(index + 1);
            foreach (EverestModule module in Everest.Modules) {
                if (module.Metadata.Name == moduleName && module.SettingsType is { } settingsType) {
                    PropertyInfo property = settingsType.GetProperty(settingName);
                    if (property != null) {
                        property.SetValue(module._Settings, ConvertType(value, property.PropertyType));
                    }

                    return true;
                }
            }

            return false;
        }

        private static void SetObject(Type type, string entityId, List<string> memberNames, string[] values) {
            if (memberNames.IsEmpty() || values.IsEmpty()) {
                return;
            }

            string lastMemberName = memberNames.Last();
            memberNames = memberNames.SkipLast().ToList();

            Type memberType = null;
            object obj;
            if (memberNames.IsNotEmpty() &&
                (InfoCustom.GetGetMethod(type, memberNames.First()) is {IsStatic: true} ||
                 InfoCustom.GetFieldInfo(type, memberNames.First()) is {IsStatic: true})) {
                obj = InfoCustom.GetMemberValue(type, null, memberNames);
            } else {
                obj = FindObject(type, entityId);
                obj = InfoCustom.GetMemberValue(type, obj, memberNames);
                if (obj == null) {
                    $"Set Command Failed: {type.FullName}{entityId} object is not found".Log();
                    return;
                } else {
                    memberType = obj.GetType();
                }
            }

            if (memberType.GetPropertyInfo(lastMemberName, true) is { } property && property.GetSetMethod(true) is { } setMethod) {
                if (obj is Actor actor && lastMemberName is "X" or "Y") {
                    double.TryParse(values[0], out double value);
                    Vector2 remainder = actor.PositionRemainder;
                    if (lastMemberName == "X") {
                        actor.Position.X = (int) Math.Round(value);
                        remainder.X = (float) (value - actor.Position.X);
                    } else {
                        actor.Position.Y = (int) Math.Round(value);
                        remainder.Y = (float) (value - actor.Position.Y);
                    }

                    ActorMovementCounter.SetValue(obj, remainder);
                } else {
                    object value = ConvertType(values, property.PropertyType);
                    setMethod.Invoke(obj, new[] {value});
                }
            } else if (memberType.GetFieldInfo(lastMemberName, true) is { } field) {
                if (obj is Actor actor && lastMemberName == "Position" && values.Length == 2) {
                    double.TryParse(values[0], out double x);
                    double.TryParse(values[1], out double y);
                    Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                    Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                    actor.Position = position;
                    ActorMovementCounter.SetValue(obj, remainder);
                } else {
                    object value = ConvertType(values, field.FieldType);
                    if (lastMemberName.Equals("Speed", StringComparison.OrdinalIgnoreCase) && value is Vector2 speed &&
                        Math.Abs(Engine.TimeRateB - 1f) > 1e-10) {
                        field.SetValue(obj, speed / Engine.TimeRateB);
                    } else {
                        field.SetValue(obj, value);
                    }
                }
            }

            // after modifying the struct
            // we also need to update the object own the struct, here only the Vector2 type is handled
            if (memberNames.IsNotEmpty()) {
                string[] position = obj switch {
                    Vector2 vector2 => new[] {vector2.X.ToString(CultureInfo.InvariantCulture), vector2.Y.ToString(CultureInfo.InvariantCulture)},
                    Vector2Double vector2Double => new[] {
                        vector2Double.X.ToString(CultureInfo.InvariantCulture), vector2Double.Y.ToString(CultureInfo.InvariantCulture)
                    },
                    _ => new string[] { }
                };

                SetObject(
                    type,
                    entityId,
                    memberNames,
                    position
                );
            }
        }

        private static object FindObject(Type type, string entityId) {
            if (type.IsSameOrSubclassOf(typeof(Entity))) {
                return InfoCustom.FindEntities(type, entityId).FirstOrDefault();
            } else if (type == typeof(Level)) {
                return Engine.Scene.GetLevel();
            } else if (type == typeof(Session)) {
                return Engine.Scene.GetSession();
            } else {
                return null;
            }
        }

        private static object ConvertType(string value, Type type) {
            try {
                return type.IsEnum ? Enum.Parse(type, value, true) : Convert.ChangeType(value, type);
            } catch {
                return value;
            }
        }

        private static object ConvertType(string[] values, Type type) {
            if (values.Length == 2 && type == typeof(Vector2)) {
                float.TryParse(values[0], out float x);
                float.TryParse(values[1], out float y);
                return new Vector2(x, y);
            } else if (values.Length == 1) {
                return ConvertType(values[0], type);
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
                    if (InputFeather?.GetValue(null) is VirtualJoystick featherJoystick) {
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
                    typeof(Celeste.Celeste).InvokeMethod("ResetGrab", null);
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
}