using System.Diagnostics;
using CelesteStudio.Util;

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
}

public static class CalculationExtensions {
    public static CalculationOperator? TryParse(char c) {
        return c switch {
            '+' => CalculationOperator.Add,
            '-' => CalculationOperator.Sub,
            '*' => CalculationOperator.Mul,
            '/' => CalculationOperator.Div,
            _ => null
        };
    }
    public static char Char(this CalculationOperator op) {
        return op switch {
            CalculationOperator.Add => '+',
            CalculationOperator.Sub => '-',
            CalculationOperator.Mul => '*',
            CalculationOperator.Div => '/',
            _ => throw new UnreachableException(),
        };
    }
    
    public static CalculationOperator Inverse(this CalculationOperator op) {
        return op switch {
            CalculationOperator.Add => CalculationOperator.Sub,
            CalculationOperator.Sub => CalculationOperator.Add,
            CalculationOperator.Mul => CalculationOperator.Div,
            CalculationOperator.Div => CalculationOperator.Mul,
            _ => throw new UnreachableException(),
        };
    }

    public static int Apply(this CalculationOperator op, int value, int operand) {
        return op switch {
            CalculationOperator.Add => value + operand,
            CalculationOperator.Sub => value - operand,
            CalculationOperator.Mul => value * operand,
            CalculationOperator.Div => value / operand,
            _ => throw new UnreachableException(),
        };
    }
    public static ActionLine Apply(this CalculationOperator op, ActionLine actionLine, int operand) {
        return actionLine with { Frames = op.Apply(actionLine.Frames, operand) };
    }
}