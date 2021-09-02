using System;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class ConsoleEnhancements {
        private static string clickedEntityInfo = string.Empty;
        private static MouseState lastMouseState;

        public static void Load() {
            IL.Monocle.Commands.Render += Commands_Render;
        }

        public static void Unload() {
            IL.Monocle.Commands.Render -= Commands_Render;
        }

        private static void Commands_Render(ILContext il) {
            // Hijack string.Format("\n level:       {0}, {1}", xObj, yObj)
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdstr("\n level:       {0}, {1}"),
                i => i.MatchCall(typeof(string), "Format")
            );
            ILCursor c = found[1];
            c.Remove();
            c.EmitDelegate<Func<string, object, object, string>>((text, xObj, yObj) => {
                Level level = Engine.Scene as Level;
                int x = (int) xObj;
                int y = (int) yObj;
                int worldX = (int) Math.Round(x + level.LevelOffset.X);
                int worldY = (int) Math.Round(y + level.LevelOffset.Y);

                MouseState mouseState = Mouse.GetState();

                if (Engine.Instance.IsActive) {
                    if (mouseState.LeftButton == ButtonState.Pressed && lastMouseState.LeftButton == ButtonState.Released) {
                        Entity clickedEntity = InfoWatchEntity.FindClickedEntity(mouseState);
                        if (clickedEntity != null) {
                            Type type = clickedEntity.GetType();
                            clickedEntityInfo = "\n entity type: ";
                            if (type.Assembly == typeof(Celeste.Celeste).Assembly) {
                                clickedEntityInfo += type.Name;
                            } else {
                                // StartExport uses a comma as a separator, so we can't use comma,
                                // use @ to place it and replace it back with a comma when looking for the type
                                clickedEntityInfo += type.FullName + "@" + type.Assembly.GetName().Name;
                            }

                            if (clickedEntity.LoadEntityData() is { } entityData) {
                                clickedEntityInfo += $"\n entity name: {entityData.Name}";
                                clickedEntityInfo += $"\n entity id  : {entityData.ToEntityId()}";
                            }

                            ("Info of clicked entity: " + clickedEntityInfo).Log();
                        } else {
                            clickedEntityInfo = string.Empty;
                        }
                    } else if (mouseState.RightButton == ButtonState.Pressed && lastMouseState.RightButton == ButtonState.Released) {
                        clickedEntityInfo = string.Empty;
                    }
                }

                lastMouseState = mouseState;

                return
                    (string.IsNullOrEmpty(clickedEntityInfo) ? string.Empty : clickedEntityInfo) +
                    $"\n world:       {worldX}, {worldY}" +
                    $"\n level:       {x}, {y}";
            });
        }
    }
}