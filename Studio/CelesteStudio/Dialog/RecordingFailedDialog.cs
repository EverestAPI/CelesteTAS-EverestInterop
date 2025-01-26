using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;
using System;

namespace CelesteStudio.Dialog;

public class RecordingFailedDialog : Eto.Forms.Dialog {
    private RecordingFailedDialog(RecordingFailedReason reason) {
        Title = "Recording Failed";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new Label {
                    Text = reason switch {
                        RecordingFailedReason.TASRecorderNotInstalled => "TAS Recorder is not installed! Please install it to record your TAS.",
                        RecordingFailedReason.FFmpegNotInstalled => "FFmpeg isn't properly installed! Please make sure it's properly installed, to record your TAS.",
                        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
                    }
                },
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = {
                        new Button((_, _) => {
                            var proc = ProcessHelper.OpenInDefaultApp("https://maddie480.ovh/celeste/dl?id=TASRecorder&twoclick=1");
                            proc?.WaitForExit(TimeSpan.FromSeconds(10));
                            Environment.Exit(1);
                        }) { Text = "&Install TAS Recorder" },

                        new Button((_, _) => {
                            var proc = ProcessHelper.OpenInDefaultApp("https://github.com/psyGamer/TASRecorder#installation");
                            proc?.WaitForExit(TimeSpan.FromSeconds(10));
                            Environment.Exit(1);
                        }) { Text = "&Open Install instructions" },
                    }
                },
            },
        };

        Studio.RegisterDialog(this);
    }

    public static void Show(RecordingFailedReason reason) {
        new RecordingFailedDialog(reason).ShowModal();
    }
}
