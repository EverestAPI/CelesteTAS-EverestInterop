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
    public static char Char(this CalculationOperator op) {
        return op switch {
            CalculationOperator.Add => '+',
            CalculationOperator.Sub => '-',
            CalculationOperator.Mul => '*',
            CalculationOperator.Div => '/',
            CalculationOperator.Set => '=',
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
