using System.Collections;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.Entities;

[Tracked]
internal class Toast : Entity {
    private const int Padding = 25;
    private const float DefaultDuration = 2f;
    private string message;
    private readonly float duration;
    private float alpha;
    private float unEasedAlpha;

    private Toast(string message, float duration = DefaultDuration) {
        this.duration = duration;
        UpdateMessage(message);
        Tag = Tags.HUD | Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;
        Depth = Depths.Top;
        Add(new Coroutine(Show()));
    }

    private void UpdateMessage(string newMessage) {
        message = newMessage;
        Vector2 messageSize = ActiveFont.Measure(message);
        Position = new(Padding, Engine.Height - messageSize.Y - Padding / 2f);
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
        if (Engine.Scene.Entities.adding.FirstOrDefault(entity => entity is Toast) is Toast toast) {
            toast.UpdateMessage(toast.message + "\n" + message);
        } else {
            Engine.Scene.Add(new Toast(message, duration));
        }
    }

    public static void ShowAndLog(string message, float duration = DefaultDuration, LogLevel logLevel = LogLevel.Warn) {
        Show(message, duration);
        message.Log(logLevel);
    }
}