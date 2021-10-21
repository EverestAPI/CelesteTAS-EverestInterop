using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.Input {
    // ReSharper disable once UnusedType.Global
    public static class SetCommandHandler {
        private static readonly FieldInfo ActorMovementCounter = typeof(Actor).GetFieldInfo("movementCounter");
        private static readonly FieldInfo InputFeather = typeof(Celeste.Input).GetFieldInfo("Feather");

        // Set, Setting, Value
        // Set, Mod.Setting, Value
        // Set, Player.Field, Value
        [TasCommand("Set", LegalInMainGame = false)]
        private static void SetCommand(string[] args) {
            if (args.Length < 2) {
                return;
            }

            try {
                object settings = null;
                string settingName;
                int index = args[0].IndexOf(".", StringComparison.Ordinal);
                if (index != -1) {
                    string moduleName = args[0].Substring(0, index);
                    settingName = args[0].Substring(index + 1);
                    string[] parameters = args.Skip(1).ToArray();
                    // TODO Use the same syntax as custom info
                    if (moduleName == "Player" && Engine.Scene.GetPlayer() is { } player) {
                        SetObject(player, settingName, parameters);
                    } else if (moduleName is "Theo" or "TheoCrystal" && Engine.Scene.Tracker.GetEntity<TheoCrystal>() is { } theo) {
                        SetObject(theo, settingName, parameters);
                    } else if (moduleName == "Level" && Engine.Scene is Level level) {
                        SetObject(level, settingName, parameters);
                    } else if (moduleName == "Session" && Engine.Scene.GetSession() is { } session) {
                        SetObject(session, settingName, parameters);
                    } else {
                        foreach (EverestModule module in Everest.Modules) {
                            if (module.Metadata.Name == moduleName) {
                                Type settingsType = module.SettingsType;
                                settings = module._Settings;
                                PropertyInfo property = settingsType.GetProperty(settingName);
                                if (property != null) {
                                    property.SetValue(settings, ConvertType(args[1], property.PropertyType));
                                }

                                return;
                            }
                        }
                    }
                } else {
                    settingName = args[0];

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

                    object value = ConvertType(args[1], field.FieldType);

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
            } catch (Exception e) {
                e.Log();
            }
        }

        private static void SetObject(object obj, string name, string[] values) {
            if (values.Length == 0) {
                return;
            }

            Type type = obj.GetType();

            if (type.GetPropertyInfo(name, true) is { } property && property.GetSetMethod(true) is {IsStatic: false} setMethod) {
                object value = ConvertType(values, property.PropertyType);
                setMethod.Invoke(obj, new[] {value});
            } else if (type.GetFieldInfo(name, true) is {IsStatic: false} field) {
                if (obj is Actor actor && name == "Position" && values.Length == 2) {
                    double.TryParse(values[0], out double x);
                    double.TryParse(values[1], out double y);
                    Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                    Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));
                    actor.Position = position;
                    ActorMovementCounter.SetValue(obj, remainder);
                } else {
                    object value = ConvertType(values, field.FieldType);
                    field.SetValue(obj, value);
                }
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