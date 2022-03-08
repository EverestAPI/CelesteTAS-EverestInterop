using System.Collections;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.Entities;

[Tracked]
internal class Toast : Entity {
    private const int Padding = 25;
    private const float DefaultDuration = 1.5f;
    private readonly string message;
    private readonly float duration;
    private float alpha;
    private float unEasedAlpha;

    private Toast(string message, float duration = DefaultDuration) {
        this.message = message;
        this.duration = duration;
        Vector2 messageSize = ActiveFont.Measure(message);
        Position = new(Padding, Engine.Height - messageSize.Y - Padding / 2f);
        Tag = Tags.HUD | Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;
        Depth = Depths.Top;
        Add(new Coroutine(Show()));
        Add(new IgnoreSaveLoadComponent());
    }

    private IEnumerator Show() {
        while (alpha < 1f) {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 1f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineOut(unEasedAlpha);
            yield return null;
        }

        yield return Dismiss();
    }

    private IEnumerator Dismiss() {
        yield return duration;
        while (alpha > 0f) {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 0f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineIn(unEasedAlpha);
            yield return null;
        }

        RemoveSelf();
    }

    public override void Render() {
        base.Render();
        ActiveFont.DrawOutline(message, Position, Vector2.Zero, Vector2.One, Color.White * alpha, 2,
            Color.Black * alpha * alpha * alpha);
    }

    public static void Show(string message, float duration = DefaultDuration) {
        if (Engine.Scene == null) {
            return;
        }

        Engine.Scene.Tracker.GetEntities<Toast>().ForEach(entity => entity.RemoveSelf());
        Engine.Scene.Add(new Toast(message, duration));
    }
}