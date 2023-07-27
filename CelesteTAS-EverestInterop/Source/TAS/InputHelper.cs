using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Input;
using TAS.Input.Commands;

namespace TAS;

public static class InputHelper {
    public static void FeedInputs(InputFrame input) {
        GamePadDPad pad = default;
        GamePadThumbSticks sticks = default;
        GamePadState gamePadState = default;
        if (input.HasActions(Actions.Feather)) {
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
        if (input.HasActions(Actions.Confirm)) {
            keys.Add(BindingHelper.Confirm2);
        }

        if (input.HasActions(Actions.LeftMoveOnly)) {
            keys.Add(BindingHelper.LeftMoveOnly);
        }

        if (input.HasActions(Actions.RightMoveOnly)) {
            keys.Add(BindingHelper.RightMoveOnly);
        }

        if (input.HasActions(Actions.UpMoveOnly)) {
            keys.Add(BindingHelper.UpMoveOnly);
        }

        if (input.HasActions(Actions.DownMoveOnly)) {
            keys.Add(BindingHelper.DownMoveOnly);
        }

        keys.UnionWith(input.PressedKeys);

        MInput.Keyboard.CurrentState = new KeyboardState(keys.ToArray());
    }

    private static void SetFeather(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        sticks = new GamePadThumbSticks(input.AngleVector2, input.DashOnlyVector2);
    }

    private static void SetDPad(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(
            input.HasActions(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
        );
        sticks = new GamePadThumbSticks(new Vector2(0, 0), input.DashOnlyVector2);
    }

    private static void SetGamePadState(InputFrame input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        state = new GamePadState(
            sticks,
            new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
            new GamePadButtons(
                (input.HasActions(Actions.Jump) ? BindingHelper.JumpAndConfirm : 0)
                | (input.HasActions(Actions.Jump2) ? BindingHelper.Jump2 : 0)
                | (input.HasActions(Actions.DemoDash) ? BindingHelper.DemoDash : 0)
                | (input.HasActions(Actions.DemoDash2) ? BindingHelper.DemoDash2 : 0)
                | (input.HasActions(Actions.Dash) ? BindingHelper.DashAndTalkAndCancel : 0)
                | (input.HasActions(Actions.Dash2) ? BindingHelper.Dash2AndCancel : 0)
                | (input.HasActions(Actions.Grab) ? BindingHelper.Grab : 0)
                | (input.HasActions(Actions.Grab2) ? BindingHelper.Grab2 : 0)
                | (input.HasActions(Actions.Start) ? BindingHelper.Pause : 0)
                | (input.HasActions(Actions.Restart) ? BindingHelper.QuickRestart : 0)
                | (input.HasActions(Actions.Up) ? BindingHelper.Up : 0)
                | (input.HasActions(Actions.Down) ? BindingHelper.Down : 0)
                | (input.HasActions(Actions.Left) ? BindingHelper.Left : 0)
                | (input.HasActions(Actions.Right) ? BindingHelper.Right : 0)
                | (input.HasActions(Actions.Journal) ? BindingHelper.JournalAndTalk : 0)
            ),
            pad
        );
    }
}