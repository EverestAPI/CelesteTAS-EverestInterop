using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace TAS {
	[AttributeUsage(AttributeTargets.Method)]
	public class TASCommandAttribute : Attribute {
		public bool executeAtStart;
		public bool illegalInMaingame;

		public string[] args;
	}
}
