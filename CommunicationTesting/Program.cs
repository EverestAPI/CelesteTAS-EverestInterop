using TAS.Communication;
#if DEBUG

#endif

namespace CommunicationTesting {
    class Program {
        //this is just easier than debugging unit tests

        static void Main(string[] args) {
#if DEBUG
            StudioCommunicationTestServer.Run();
            StudioCommunicationClient.Run();
#endif
        }
    }
}