using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS;

public static class InputHelper {
    public static void FeedInputs(InputFrame input) {
        GamePadDPad pad = default;
        GamePadThumbSticks sticks = default;
        GamePadState gamePadState = default;
        if (input.Actions.Has(Actions.Feather)) {
            SetFeather(input, ref pad, ref sticks);
        } else {
            SetDPad(input, ref pad, ref sticks);
        }

        SetGamePadState(input, ref gamePadState, ref pad, ref sticks);

        MInput.GamePadData gamePadData = MInput.GamePads[Celeste.Input.Gamepad];
        gamePadData.PreviousState = gamePadData.CurrentState;
        gamePadData.CurrentState = gamePadState;

        MouseCommand.SetMouseState();
        SetKeyboardState(input);

        MInput.UpdateVirtualInputs();
    }

    private static void SetKeyboardState(InputFrame input) {
        MInput.Keyboard.PreviousState = MInput.Keyboard.CurrentState;

        HashSet<Keys> keys = PressCommand.GetKeys();
        if (input.Actions.Has(Actions.Confirm)) {
            keys.Add(BindingHelper.Confirm2);
        }

        if (input.Actions.Has(Actions.LeftMoveOnly)) {
            keys.Add(BindingHelper.LeftMoveOnly);
        }

        if (input.Actions.Has(Actions.RightMoveOnly)) {
            keys.Add(BindingHelper.RightMoveOnly);
        }

        if (input.Actions.Has(Actions.UpMoveOnly)) {
            keys.Add(BindingHelper.UpMoveOnly);
        }

        if (input.Actions.Has(Actions.DownMoveOnly)) {
            keys.Add(BindingHelper.DownMoveOnly);
        }

        keys.UnionWith(input.PressedKeys);

        MInput.Keyboard.CurrentState = new KeyboardState(keys.ToArray());
    }

    private static void SetFeather(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        sticks = new GamePadThumbSticks(input.StickPosition, input.DashOnlyStickPosition);
    }

    private static void SetDPad(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(
            input.Actions.Has(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
            input.Actions.Has(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
            input.Actions.Has(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
            input.Actions.Has(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
        );
        sticks = new GamePadThumbSticks(new Vector2(0, 0), input.DashOnlyStickPosition);
    }

    private static void SetGamePadState(InputFrame input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        state = new GamePadState(
            sticks,
            new GamePadTriggers(input.Actions.Has(Actions.Journal) ? 1f : 0f, 0),
            new GamePadButtons(
                (input.Actions.Has(Actions.Jump) ? BindingHelper.JumpAndConfirm : 0)
                | (input.Actions.Has(Actions.Jump2) ? BindingHelper.Jump2 : 0)
                | (input.Actions.Has(Actions.DemoDash) ? BindingHelper.DemoDash : 0)
                | (input.Actions.Has(Actions.DemoDash2) ? BindingHelper.DemoDash2 : 0)
                | (input.Actions.Has(Actions.Dash) ? BindingHelper.DashAndTalkAndCancel : 0)
                | (input.Actions.Has(Actions.Dash2) ? BindingHelper.Dash2AndCancel : 0)
                | (input.Actions.Has(Actions.Grab) ? BindingHelper.Grab : 0)
                | (input.Actions.Has(Actions.Grab2) ? BindingHelper.Grab2 : 0)
                | (input.Actions.Has(Actions.Start) ? BindingHelper.Pause : 0)
                | (input.Actions.Has(Actions.Restart) ? BindingHelper.QuickRestart : 0)
                | (input.Actions.Has(Actions.Up) ? BindingHelper.Up : 0)
                | (input.Actions.Has(Actions.Down) ? BindingHelper.Down : 0)
                | (input.Actions.Has(Actions.Left) ? BindingHelper.Left : 0)
                | (input.Actions.Has(Actions.Right) ? BindingHelper.Right : 0)
                | (input.Actions.Has(Actions.Journal) ? BindingHelper.JournalAndTalk : 0)
            ),
            pad
        );
    }
}