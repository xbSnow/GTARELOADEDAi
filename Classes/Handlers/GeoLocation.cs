using System.Net;
using System.Text;

#nullable disable

namespace GTAServer
{
    public class GeoLocation
    {
        public static Task<int> GetLocationInfoFromIP(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/Geolocation/RegionBucketLookUpResponse.xml");

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }
    }
}