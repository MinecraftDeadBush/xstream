using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XboxWebApi.Authentication;
using XboxWebApi.Authentication.Model;

namespace xstream
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AllocConsole();

            Console.Write("tokenFilePath: ");
            string tokenFilePath = Console.ReadLine();

            AuthenticationService auth = null;

            if (!File.Exists(tokenFilePath))
            {
                Shell.WriteLine("Warning: '{0}' file not found.\n", tokenFilePath);

                string reqURL = AuthenticationService.GetWindowsLiveAuthenticationUrl();

                Console.WriteLine("1) Open following URL in your WebBrowser:\n\n{0}\n\n" +
                                        "2) Authenticate with your Microsoft Account\n" +
                                        "3) Paste returned URL from addressbar: \n", reqURL);

                // Call requestUrl via WebWidget or manually and authenticate

                string url = Console.ReadLine();
                WindowsLiveResponse rep = AuthenticationService.ParseWindowsLiveResponse(url);
                auth = new AuthenticationService(rep);

                if (!auth.Authenticate())
                    throw new Exception("Authentication failed!");

                Console.WriteLine(auth.XToken);
                Console.WriteLine(auth.UserInformation);

                // Save token to JSON

                FileStream tokenOutputFile = new FileStream(tokenFilePath, FileMode.Create);
                auth.DumpToFile(tokenOutputFile);
                tokenOutputFile.Close();

                Console.WriteLine("Storing tokens to file \'{0}\' on successful auth",
                        tokenOutputFile.Name);
            }
            else
            {
                // Load token from JSON

                FileStream fs = new FileStream(tokenFilePath, FileMode.Open);
                auth = AuthenticationService.LoadFromFile(fs);
                fs.Close();
            }

            Discover().Wait();

            Shell.PressAnyKeyToContinue();
            FreeConsole();

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Xstream(auth));
        }

        async static Task<int> Discover()
        {
            Console.WriteLine("Name (HardwareId) Address LiveId");

            IEnumerable<SmartGlass.Device> devices = await SmartGlass.Device.DiscoverAsync();
            foreach (SmartGlass.Device device in devices)
            {
                Console.WriteLine($"{device.Name} ({device.HardwareId}) {device.Address} {device.LiveId}");
            }

            return 0;
        }

        [DllImport("kernel32")]
        static extern bool AllocConsole();
        [DllImport("kernel32")]
        static extern bool FreeConsole();
    }
}
