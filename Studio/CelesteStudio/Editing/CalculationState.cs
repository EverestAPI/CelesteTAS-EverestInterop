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
        return c switch {
            '+' => CalculationOperator.Add,
            '-' => CalculationOperator.Sub,
            '*' => CalculationOperator.Mul,
            '/' => CalculationOperator.Div,
            '=' => CalculationOperator.Set,
            _ => null
        };
    }
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

    public static int Apply(this CalculationOperator op, int value, int operand) {
        return op switch {
            CalculationOperator.Add => value + operand,
            CalculationOperator.Sub => value - operand,
            CalculationOperator.Mul => value * operand,
            CalculationOperator.Div => value / operand,
            CalculationOperator.Set => operand,
            _ => throw new UnreachableException(),
        };
    }
    public static ActionLine Apply(this CalculationOperator op, ActionLine actionLine, int operand) {
        int newFrames = op.Apply(actionLine.FrameCount, operand);
        return actionLine with { FrameCount = Math.Clamp(newFrames, 0, ActionLine.MaxFrames) };
    }
}
