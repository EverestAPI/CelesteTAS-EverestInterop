using TAS.StudioCommunication;
#if DEBUG
using TASALT.StudioCommunication;

#endif

namespace CommunicationTesting {
class Program {
    //this is just easier than debugging unit tests


    static void Main(string[] args) {
#if DEBUG
        StudioCommunicationServer.Run();
        StudioCommunicationClient.Run();
#endif
    }
}
}