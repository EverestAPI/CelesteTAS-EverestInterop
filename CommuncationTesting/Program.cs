using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using TASALT.StudioCommunication;

namespace CommunicationTesting {
	class Program {
		//this is just easier than debugging unit tests




		static void Main(string[] args) {
			StudioCommunicationServer.Run();
			StudioCommunicationClient.Run();
		}
	}
}
