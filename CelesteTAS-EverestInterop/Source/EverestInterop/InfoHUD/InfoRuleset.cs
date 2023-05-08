using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static class InfoRuleset {

    [Load]
    private static void Load() {
        On.Celeste.Level.Render += LevelOnRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Render -= LevelOnRender;
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        CheckMouseButtons();
    }

    public static void CheckMouseButtons() {
        if (MouseButtons.Right.Pressed && InfoWatchEntity.FindClickedEntity() is { } entity) {
            Type type = entity.GetType();
            FieldInfo[] fields = type.GetAllFieldInfos().ToArray();
            PropertyInfo[] properties = type.GetAllProperties().ToArray();
            List<MemberInfo> memberInfos = fields.Cast<MemberInfo>().ToList();
            List<MemberInfo> propertyInfos = properties.Cast<MemberInfo>().ToList();
            memberInfos.AddRange(propertyInfos);
            string separator = "\t";
            List<string> values = memberInfos.Select(info => {
                object value;
               // try {
                    value = info switch {
                        FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                        PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                        _ => null
                    };
               // } catch {
               //     value = string.Empty;
               // }

                //if (value is float floatValue) {
                //    if (info.Name.EndsWith("Timer")) {
                //        value = GameInfo.ConvertToFrames(floatValue);
                //    } else {
                //        value = floatValue.ToFormattedString(decimals);
                //    }
                //} else if (value is Vector2 vector2) {
                //    value = vector2.ToSimpleString(decimals);
                //}

                //if (separator == "\t" && value != null) {
                //    value = value.ToString().ReplaceLineBreak(" ");
                //}

                return $"{type.Name}.{info.Name}: {value}";
            }).ToList();
            LogUtil.ConsoleLog(string.Join(separator, values));
        }
    }

    public static List<object> GetAllFields(object obj) {
        return new List<object>();
    }
}

   /* 
    * 
    * i call this feature: ruleset info
    * unfortunately, there may be some conflict with rectangle selection info
    * if right click on info hud, create options to: a) clear WatchEntities, b) clear temp ruleset info; c) open ruleset creator menu for all fields; d) open ruleset editor menu
    * else if right click on entity, create ruleset creator menu for all fields of this entity
    * else, create menu for all fields of level
    * 
    * ruleset creator menu:
    * constructor data = list of roots (e.g. Level, UniqueEntityId, ...)
    * to show:
    *      apply existing rulesets
    *      a search engine, including current path, list of properties/methods/fields in current path
    *      emm, maybe allow binding flags in search engine, like BindingFlags.Instance | BindingFlags.DeclaredOnly
    *      allow add postfix for each selected field, e.g. ":", "toFrame()"
    *      maybe allow do arithmetic in subsequent updates?
    *      some menu options in bottom like: save this ruleset, options of this ruleset
    *      and in the most bottom line, an apply button.
    * current path means: you can look for fields of fields of entity, e.g. Player.Position.X, just like what custom info should do
    * 
    * ruleset editor menu:
    * including temp rulesets
    * add/delete rulesets, and change some options of ruleset
    * convert ruleset to custom info contents
    * ruleset package = a list of rulesets, so various types are scoped
    * 
    * ruleset:
    * list of fields of a specific type of entity
    * option1: on/off
    * option2: auto apply to (watched/all entities of same/child type)
    * option3: saved or not. If saved then will persist across games
    * scope: those in option2 and those specified to apply this ruleset (i.e. from ruleset info menu), call the latter part temp entities, these are editable
    * description in editor menu: add by user
    * description in ruleset info: add by user
    * 
    * info hud will be like:
    * custom info + ruleset info + temp ruleset info + watched entities
    * 
    * temp ruleset info:
    * either the ruleset, or the scoped entity, is temp
    * 
    * clear temp ruleset info:
    * clear all temp rulesets, and clear all temp entity from scope of saved rulesets
    * 
    */