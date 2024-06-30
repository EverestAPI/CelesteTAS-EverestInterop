using System.Numerics;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class NumberInputDialog<T> : Dialog<T> where T : INumber<T> {
    private NumberInputDialog(string title, T input, T minValue, T maxValue, T step) {
        var stepper = new NumericStepper {
            Value = double.CreateChecked(input),
            MinValue = double.CreateChecked(minValue),
            MaxValue = double.CreateChecked(maxValue),
            Increment = double.CreateChecked(step),
            Width = 200,
        };
        
        if (input is int) {
            stepper.DecimalPlaces = stepper.MaximumDecimalPlaces = 0;
        } else {
            stepper.DecimalPlaces = 2;
        }
        
        Title = title;
        Content = new StackLayout {
            Padding = 10,
            Items = { stepper },
        };
        
        DefaultButton = new Button((_, _) => Close(T.CreateChecked(stepper.Value))) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(input)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
    }
    
    public static T Show<T>(string title, T input, T minValue, T maxValue, T step) where T : INumber<T> {
        return new NumberInputDialog<T>(title, input, minValue, maxValue, step).ShowModal();
    }
}