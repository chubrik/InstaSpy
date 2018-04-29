using Kit.Mail;
using System.IO;

namespace InstaSpy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //ConsoleClient.Setup(minLevel: LogLevel.Log);
            //HttpClient.Setup(cache: CacheMode.WriteOnly);

            var instagramCredentials = File.ReadAllLines("instagram-credentials.txt");
            var mailCredentials = File.ReadAllLines("mail-credentials.txt");

            MailClient.Setup(
                host: mailCredentials[0],
                port: int.Parse(mailCredentials[1]),
                userName: mailCredentials[2],
                password: mailCredentials[3],
                from: mailCredentials[4],
                to: mailCredentials[5]
            );

            var userName = instagramCredentials[0];
            var password = instagramCredentials[1];

            Kit.Kit.Execute(() => new Spy(userName, password).Run());
        }
    }
}
