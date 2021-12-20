using System;
using Celeste;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class ConsoleEnhancements {
        private static string clickedEntityInfo = string.Empty;

        [Load]
        private static void Load() {
            IL.Monocle.Commands.Render += Commands_Render;
        }

        [Unload]
        private static void Unload() {
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

                if (MouseButtons.Left.Pressed) {
                    Entity clickedEntity = InfoWatchEntity.FindClickedEntity();
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

                        if (clickedEntity.GetEntityData() is { } entityData) {
                            clickedEntityInfo += $"\n entity name: {entityData.Name}";
                            clickedEntityInfo += $"\n entity id  : {entityData.ToEntityId()}";
                        }

                        ("Info of clicked entity: " + clickedEntityInfo).Log();
                    } else {
                        clickedEntityInfo = string.Empty;
                    }
                } else if (MouseButtons.Right.Pressed) {
                    clickedEntityInfo = string.Empty;
                }

                return
                    (string.IsNullOrEmpty(clickedEntityInfo) ? string.Empty : clickedEntityInfo) +
                    $"\n world:       {worldX}, {worldY}" +
                    $"\n level:       {x}, {y}";
            });
        }
    }
}