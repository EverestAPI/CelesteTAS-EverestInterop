using System;
using System.Globalization;
using System.Text;

namespace TAS.Input {
    [Flags]
    public enum Actions {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        Jump = 1 << 4,
        Dash = 1 << 5,
        Grab = 1 << 6,
        Start = 1 << 7,
        Restart = 1 << 8,
        Feather = 1 << 9,
        Journal = 1 << 10,
        Jump2 = 1 << 11,
        Dash2 = 1 << 12,
        Confirm = 1 << 13
    }
    public class InputFrame {
        public int Frames;
        public Actions Actions;
        public float Angle;
        public int Line;

        public bool HasActions(Actions actions) =>
            (Actions & actions) != 0;
        
        public float GetX() =>
            (float)Math.Sin(Angle * Math.PI / 180.0);

        public float GetY() =>
            (float)Math.Cos(Angle * Math.PI / 180.0);


        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append(Frames);
            if (HasActions(Actions.Left))
                sb.Append(",L");
            if (HasActions(Actions.Right))
                sb.Append(",R");
            if (HasActions(Actions.Up))
                sb.Append(",U");
            if (HasActions(Actions.Down))
                sb.Append(",D");
            if (HasActions(Actions.Jump))
                sb.Append(",J");
            if (HasActions(Actions.Jump2))
                sb.Append(",K");
            if (HasActions(Actions.Dash))
                sb.Append(",X");
            if (HasActions(Actions.Dash2))
                sb.Append(",C");
            if (HasActions(Actions.Grab))
                sb.Append(",G");
            if (HasActions(Actions.Start))
                sb.Append(",S");
            if (HasActions(Actions.Restart))
                sb.Append(",Q");
            if (HasActions(Actions.Journal))
                sb.Append(",N");
            if (HasActions(Actions.Confirm))
                sb.Append(",O");
            if (HasActions(Actions.Feather))
                sb.Append(",F,").Append(Angle == 0 ? string.Empty : Angle.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public InputFrame Clone() {
            InputFrame clone = new InputFrame {
                Frames = Frames,
                Actions = Actions,
                Angle = Angle,
                Line = Line,
            };
            return clone;
        }
    }
}