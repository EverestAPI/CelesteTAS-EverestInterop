using System;
using System.Numerics;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class DialogUtil
{
    public static T ShowNumberInputDialog<T>(string title, T input, T minValue, T maxValue, T step) where T : INumber<T> {
        var stepper = new NumericStepper {
            Value = double.CreateChecked(input),
            MinValue = double.CreateChecked(minValue),
            MaxValue = double.CreateChecked(maxValue),
            Increment = double.CreateChecked(step),
            Width = 200,
        };

        if (input is int)
            stepper.DecimalPlaces = stepper.MaximumDecimalPlaces = 0;
        else
            stepper.DecimalPlaces = 2;

        var dialog = new Dialog<T> {
            Title = title,
            Content = new StackLayout {
                Padding = 10,
                Items = { stepper },
            }
        };

        dialog.DefaultButton = new Button((_, _) => dialog.Close(T.CreateChecked(stepper.Value))) { Text = "&OK" };
        dialog.AbortButton = new Button((_, _) => dialog.Close()) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        dialog.Result = input;
            
        return dialog.ShowModal();
    }
    
    public static void ShowRecordDialog() {
        var textBox = new TextBox { Text = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", Width = 200 };
        
        var dialog = new Dialog<bool> {
            Title = "Record TAS",
            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                VerticalContentAlignment = VerticalAlignment.Center, 
                Orientation = Orientation.Horizontal,
                Items = { new Label { Text = "File Name" }, textBox },
            }
        };
        
        dialog.DefaultButton = new Button((_, _) => dialog.Close(true)) { Text = "&Record" };
        dialog.AbortButton = new Button((_, _) => dialog.Close(false)) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        
        if (!dialog.ShowModal())
            return;
        
        if (string.IsNullOrWhiteSpace(textBox.Text)) {
            MessageBox.Show("An empty file name is not valid!", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        Studio.CommunicationWrapper.Server.RecordTAS(textBox.Text);
    }
}