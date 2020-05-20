using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TAS.StudioCommunication;
using TASALT.StudioCommunication;

namespace StudioCommunicationTests {
	[TestClass]
	public class StudioCommunicationTests {
		[TestMethod]
		public void Yikes() {
			StudioCommunicationServer.Run();
			StudioCommunicationClient.Run();
			int i = 0;
			while (!StudioCommunicationServer.Initialized || !StudioCommunicationClient.Initialized) {
				if (i > 1000)
					break;
				i++;
				Thread.Sleep(5);
			}
			Assert.IsTrue(StudioCommunicationServer.Initialized);
			Assert.IsTrue(StudioCommunicationClient.Initialized);
		}
	}
}
