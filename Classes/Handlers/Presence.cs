using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;

#nullable disable

namespace GTAServer
{
    public class Presence
    {
        public struct TypeNameValue
        {
            public string type;
            public string name;
            public string value;
        }

        public static Task<int> GetPresenceServers(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/presence/GetPresenceServersResponse.xml");

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }

        public static Task<int> GetAttributes(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/presence/GetAttributes.xml");

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }

        public static Task<int> SetAttributes(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ClientCrypto clientCrypto = new ClientCrypto(true);
            client.requestData = clientCrypto.Decrypt(client.requestData, platform);

            int queryDataLength = client.requestData.Length - 0x14;
            byte[] queryData = new byte[queryDataLength];
            Buffer.BlockCopy(client.requestData, 0, queryData, 0, queryDataLength);

            string queryString = Encoding.UTF8.GetString(queryData);
            NameValueCollection collection = HttpUtility.ParseQueryString(queryString);

            string csvString = string.Empty;

            if (collection["typeNameValueCsv"] != string.Empty)
            {
                csvString = collection["typeNameValueCsv"];
            }

            string[] results = [];

            if (csvString != string.Empty)
            {
                results = Tools.ParseCsv(csvString);

                foreach (string result in results)
                {
                    int x = result.IndexOf(",");
                    string name = result.Substring(0, x).Replace(",", "");
                    string value = result.Substring(x).Replace(",", "");

                    if (Database.AttributeExists(name))
                    {
                        Database.SetAttribute(name, value, session_ticket);
                    }
                }
            }

            string file = File.ReadAllText("bin/presence/ServicesSuccess.xml");

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }
        public static Task<int> Query(Globals.Client client)

            {

                string session_ticket = client.request.Headers.Get("ros-SessionTicket");



                Globals.Member member = new Globals.Member();

                Database.GetMemberFromSessionTicket(ref member, session_ticket);



                ClientCrypto clientCrypto = new ClientCrypto(true);

                client.requestData = clientCrypto.Decrypt(client.requestData, member.platform_name);



                int queryDataLength = client.requestData.Length - 0x14;

                byte[] queryData = new byte[queryDataLength];

                Buffer.BlockCopy(client.requestData, 0, queryData, 0, queryDataLength);



                string queryString = Encoding.UTF8.GetString(queryData);

                NameValueCollection collection = HttpUtility.ParseQueryString(queryString);



                string queryName = collection["queryName"];



                string responseXml = File.ReadAllText("bin/OK.txt");



                // =========================

                // FRIEND / CREWMATE SESSIONS

                // =========================

                if (queryName == "CrewmateSessions")

                {

                    string[] results = Database.FindSession(session_ticket, member.platform_name);



                    responseXml = BuildSessionResponse(member.platform_name, results);

                }



                // =========================

                // REQUIRED FOR JOIN SUCCESS

                // =========================

                else if (queryName == "SessionByGamerHandle")

                {

                    string gamerHandle = collection["gamerHandle"];



                    string[] results = Database.FindSessionByHandle(gamerHandle);



                    responseXml = BuildSessionResponse(member.platform_name, results);

                }



                else if (queryName == "FindMatchedGamers")

                {

                    string gamerHandle = collection["gamerHandle"];



                    string[] results = Database.FindSessionByHandle(gamerHandle);



                    responseXml = BuildSessionResponse(member.platform_name, results);

                }



                // =========================

                // SEND RESPONSE

                // =========================

                ServerCrypto serverCrypto = new ServerCrypto(true);

                client.responseData = serverCrypto.Encrypt(

                    Encoding.UTF8.GetBytes(responseXml),

                    member.platform_name);



                client.response.StatusCode = (int)HttpStatusCode.OK;

                client.response.ContentType = "text/xml; charset=utf-8";

                client.response.ContentLength64 = client.responseData.Length;

                client.response.OutputStream.Write(client.responseData);



                return Task.FromResult(0);

            }



        // ===================================================

        // BUILDS MATCHMAKING RESPONSE (DO NOT MODIFY gsinfo)

        // ===================================================

        private static string BuildSessionResponse(string platform, string[] results)

        {

            if (results == null || results.Length < 2)

                return File.ReadAllText("bin/OK.txt");



            string Xuid = results[0];

            string Info = results[1];



            XmlDocument doc = new XmlDocument();



            XmlDeclaration declaration = doc.CreateXmlDeclaration("1.0", "utf-8", null);

            doc.AppendChild(declaration);



            // IMPORTANT: Namespace must match Rockstar format

            XmlElement responseElement = doc.CreateElement("Response");

            responseElement.SetAttribute("xmlns", "ReadStatsResponse");

            doc.AppendChild(responseElement);



            XmlElement statusElement = doc.CreateElement("Status");

            statusElement.InnerText = "1";

            responseElement.AppendChild(statusElement);



            XmlElement resultsElement = doc.CreateElement("Results");

            resultsElement.SetAttribute("Count", "1");

            responseElement.AppendChild(resultsElement);



            XmlElement rElement = doc.CreateElement("r");



            // PS3 uses raw XUID

            rElement.SetAttribute("gh", platform == "ps3" ? Xuid : $"XBL {Xuid}");



            // DO NOT MODIFY gsinfo

            rElement.InnerText = $"{{\"gsinfo\":\"{Info}\"}}";



            resultsElement.AppendChild(rElement);



            return doc.OuterXml;

        }

        public static Task<int> ReplaceAttributes(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/ServicesSuccess.xml");

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }

       
        public static Task<int> Subscribe(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/OK.txt");

            string platform = !string.IsNullOrEmpty(member.platform_name) ? member.platform_name : "xbox360";

            ServerCrypto serverCrypto = new ServerCrypto(true);
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), platform);

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return Task.FromResult(0);
        }

        public static Task<int> MultiPostMessage(Globals.Client client)
        {
            string session_ticket = client.request.Headers.Get("ros-SessionTicket");

            Globals.Member member = new Globals.Member();
            Database.GetMemberFromSessionTicket(ref member, session_ticket);

            string file = File.ReadAllText("bin/OK.txt");

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