using System;
using System.Diagnostics;
using StudioCommunication;

namespace CelesteStudio.Editing;

public class CalculationState(CalculationOperator op, int row) {
    public readonly CalculationOperator Operator = op;
    public readonly int Row = row;

    public string Operand = string.Empty;
}

public enum CalculationOperator {
    Add,
    Sub,
    Mul,
    Div,
    Set,
}

public static class CalculationExtensions {
    public static CalculationOperator? TryParse(char c) {
        if (c == char.MaxValue) {
            return null;
        }

        if (c == Settings.Instance.AddFrameOperationChar) {
            return CalculationOperator.Add;
        }
        if (c == Settings.Instance.SubFrameOperationChar) {
            return CalculationOperator.Sub;
        }
        if (c == Settings.Instance.MulFrameOperationChar) {
            return CalculationOperator.Mul;
        }
        if (c == Settings.Instance.DivFrameOperationChar) {
            return CalculationOperator.Div;
        }
        if (c == Settings.Instance.SetFrameOperationChar) {
            return CalculationOperator.Set;
        }

        return null;
    }
    public static char Char(this CalculationOperator op) {
        return op switch {
            CalculationOperator.Add => Settings.Instance.AddFrameOperationChar,
            CalculationOperator.Sub => Settings.Instance.SubFrameOperationChar,
            CalculationOperator.Mul => Settings.Instance.MulFrameOperationChar,
            CalculationOperator.Div => Settings.Instance.DivFrameOperationChar,
            CalculationOperator.Set => Settings.Instance.SetFrameOperationChar,
            _ => throw new UnreachableException(),
        };
    }

    public static ActionLine Apply(this CalculationOperator op, ActionLine actionLine, int operand) {
        int newFrames = op switch {
            CalculationOperator.Add => actionLine.FrameCount + operand,
            CalculationOperator.Sub => actionLine.FrameCount - operand,
            CalculationOperator.Mul => actionLine.FrameCount * operand,
            CalculationOperator.Div => actionLine.FrameCount / operand,
            CalculationOperator.Set => operand,
            _ => throw new UnreachableException(),
        };

        return actionLine with { FrameCount = Math.Clamp(newFrames, 0, ActionLine.MaxFrames) };
    }
}
