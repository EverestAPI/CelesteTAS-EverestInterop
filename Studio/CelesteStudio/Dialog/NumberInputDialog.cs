using System;
using System.Numerics;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class NumberInputDialog<T> : Dialog<T> where T : INumber<T> {
    private NumberInputDialog(string title, T input, T minValue, T maxValue, T step) {
        var stepper = new NumericStepper {
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
        
        stepper.Value = double.CreateChecked(input);
        
        Title = title;
        Content = new StackLayout {
            Padding = 10,
            Items = { stepper },
        };
        Icon = Assets.AppIcon;
        
        DefaultButton = new Button((_, _) => Close(T.CreateChecked(stepper.Value))) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(input)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
    
    public static T Show(string title, T input, T minValue, T maxValue, T step) {
        return new NumberInputDialog<T>(title, input, minValue, maxValue, step).ShowModal();
    }
}