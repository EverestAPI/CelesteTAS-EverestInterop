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
    public static class SetHandler {
        private static readonly FieldInfo MovementCounter = typeof(Actor).GetFieldInfo("movementCounter");

        // Set, Setting, Value
        // Set, Mod.Setting, Value
        [TasCommand(LegalInMainGame = false, Name = "Set")]
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
                    if (moduleName == "player" && Engine.Scene.Tracker.GetEntity<Player>() is Player player) {
                        SetPlayer(player, settingName, args.Skip(1).ToArray());
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

                    if (SettingsSpecialCases(settingName, value)) {
                        return;
                    }

                    field.SetValue(settings, value);

                    if (settings is Assists assists) {
                        SaveData.Instance.Assists = assists;
                    }
                }
            } catch (Exception e) {
                e.Log();
            }
        }

        private static void SetPlayer(Player player, string name, string[] values) {
            if (values.Length == 0) {
                return;
            }

            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            if (typeof(Player).GetProperty(name, bindingFlags) is PropertyInfo property && property.GetSetMethod(true) != null) {
                object value = ConvertType(values, property.PropertyType);
                property.SetValue(player, value);
            } else if (typeof(Player).GetField(name, bindingFlags) is FieldInfo field) {
                if (name == "Position" && values.Length == 2) {
                    double.TryParse(values[0], out double x);
                    double.TryParse(values[1], out double y);
                    Vector2 position = new Vector2((int) Math.Round(x), (int) Math.Round(y));
                    Vector2 remainder = new Vector2((float) (x - Math.Truncate(x) + (int) x - (int) Math.Round(x)),
                        (float) (y - Math.Truncate(y) + (int) y - (int) Math.Round(y)));
                    player.Position = position;
                    MovementCounter.SetValue(player, remainder);
                } else {
                    object value = ConvertType(values, field.FieldType);
                    field.SetValue(player, value);
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

        private static bool SettingsSpecialCases(string setting, object value) {
            Player player;
            switch (setting) {
                case "GameSpeed":
                    SaveData.Instance.Assists.GameSpeed = (int) value;
                    Engine.TimeRateB = SaveData.Instance.Assists.GameSpeed / 10f;
                    break;
                case "MirrorMode":
                    SaveData.Instance.Assists.MirrorMode = (bool) value;
                    Celeste.Input.MoveX.Inverted = Celeste.Input.Aim.InvertedX = (bool) value;
                    break;
                case "PlayAsBadeline":
                    SaveData.Instance.Assists.PlayAsBadeline = (bool) value;
                    player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
                    if (player != null) {
                        PlayerSpriteMode mode = SaveData.Instance.Assists.PlayAsBadeline
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
                    SaveData.Instance.Assists.DashMode = (Assists.DashModes) value;
                    player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
                    if (player != null) {
                        player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
                    }

                    break;
                case "DashAssist":
                    break;
                case "SpeedrunClock":
                    Settings.Instance.SpeedrunClock = (SpeedrunType) value;
                    break;
                case "VariantMode":
                    SaveData.Instance.VariantMode = (bool) value;
                    SaveData.Instance.AssistMode = false;
                    break;
                case "AssistMode":
                    SaveData.Instance.AssistMode = (bool) value;
                    SaveData.Instance.VariantMode = false;
                    break;
                default:
                    return false;
            }

            return true;
        }
    }
}