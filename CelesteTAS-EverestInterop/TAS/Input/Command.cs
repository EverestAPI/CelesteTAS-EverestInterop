using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TAS.Input {
    public class Command {
        public int frame;
        public Action commandCall;
        public int studioLine;
        public Command(int frame, Action commandCall, int studioLine) {
            this.frame = frame;
            this.commandCall = commandCall;
            this.studioLine = studioLine;
        }

        public void Invoke() => commandCall.Invoke();

        public Command Clone() => new Command(frame, commandCall, studioLine);
    }
}
