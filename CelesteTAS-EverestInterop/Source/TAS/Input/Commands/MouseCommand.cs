using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class MouseCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"Mouse{CommandInfo.Separator}[0;X]{CommandInfo.Separator}[1;Y]";
        public bool HasArguments => true;
    }

    public static MouseState CurrentState;

    [Load]
    private static void Load() {
        // make Mouse.GetState() return fake data if tas is running
        typeof(Mouse).GetMethodInfo(nameof(Mouse.GetState))!.IlHook((cursor, _) => {
            Instruction start = cursor.Next!;
            cursor.EmitDelegate(IsRunningTas);
            cursor.Emit(OpCodes.Brfalse, start)
                .Emit(OpCodes.Call, typeof(MInput).GetMethodInfo("get_Mouse")!)
                .Emit(OpCodes.Ldfld, typeof(MInput.MouseData).GetFieldInfo(nameof(MInput.MouseData.CurrentState))!)
                .Emit(OpCodes.Ret);
        });
    }

    private static bool IsRunningTas() {
        return Manager.Running && !MouseInput.Updating;
    }

    // "Mouse, X, Y, [ScrollWheel]"
    // "Mouse, [L], [R], [M], [X1], [X2]"
    // "Mouse, X, Y, [ScrollWheel], [L], [R], [M], [X1], [X2]"
    // "Mouse, [L], [R], [M], [X1], [X2], X, Y, [ScrollWheel]"
    [TasCommand("Mouse", MetaDataProvider = typeof(Meta))]
    private static void MouseControl(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.IsEmpty()) {
            return;
        }

        int? x = null;
        int? y = null;
        int? scrollWheel = null;

        foreach (string arg in args) {
            if (int.TryParse(arg, out int n)) {
                if (x == null) {
                    x = n;
                    if (x is > 319 or < 0) {
                        AbortTas($"{x} is not a valid X position");
                        return;
                    }
                } else if (y == null) {
                    y = n;
                    if (y is > 179 or < 0) {
                        AbortTas($"{y} is not a valid Y position");
                        return;
                    }
                } else if (scrollWheel == null) {
                    scrollWheel = n;
                }
            } else {
                string button = arg.ToUpperInvariant();
                switch (button) {
                    case "L":
                        CurrentState.Set(leftButton: ButtonState.Pressed);
                        break;
                    case "M":
                        CurrentState.Set(middleButton: ButtonState.Pressed);
                        break;
                    case "R":
                        CurrentState.Set(rightButton: ButtonState.Pressed);
                        break;
                    case "X1":
                        CurrentState.Set(xButton1: ButtonState.Pressed);
                        break;
                    case "X2":
                        CurrentState.Set(xButton2: ButtonState.Pressed);
                        break;
                }
            }
        }

        Rectangle win = Engine.Instance.Window.ClientBounds;
        if (win.Width % 320 != 0 || win.Height % 180 != 0) {
            AbortTas("Window size isn't an integer multiple of 320x180");
            return;
        }

        if (x != null && y == null) {
            AbortTas("No Y mouse coordinate was provided");
        } else if (y != null) {
            int scale = win.Width / 320;
            CurrentState.Set(x * scale, y * scale);
        }

        if (scrollWheel != null) {
            CurrentState.Set(scrollWheel: scrollWheel);
        }
    }

    public static void SetMouseState() {
        MInput.Mouse.PreviousState = MInput.Mouse.CurrentState;
        MInput.Mouse.CurrentState = CurrentState;
        if (Manager.Controller.Current != Manager.Controller.Next) {
            ReleaseButtons();
        }
    }

    private static void ReleaseButtons() {
        CurrentState = new MouseState(
            CurrentState.X,
            CurrentState.Y,
            0,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
    }

    [DisableRun]
    private static void ClearState() {
        CurrentState = default;
    }

    private static void Set(this ref MouseState mouseState,
        int? x = null,
        int? y = null,
        int? scrollWheel = null,
        ButtonState? leftButton = null,
        ButtonState? middleButton = null,
        ButtonState? rightButton = null,
        ButtonState? xButton1 = null,
        ButtonState? xButton2 = null
    ) {
        mouseState = new MouseState(
            x ?? mouseState.X,
            y ?? mouseState.Y,
            scrollWheel ?? mouseState.ScrollWheelValue,
            leftButton ?? mouseState.LeftButton,
            middleButton ?? mouseState.MiddleButton,
            rightButton ?? mouseState.RightButton,
            xButton1 ?? mouseState.XButton1,
            xButton2 ?? mouseState.XButton2
        );
    }
}

internal enum MButtons {
    Left,
    Right,
    Middle,
    X1,
    X2
}
